using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Horizon.Stealth.Services;

public enum VpnRelayState
{
    Stopped,
    Direct,
    Connecting,
    Connected,
    Fallback,
    Blocked
}

public static class VpnRelayService
{
    private sealed class UpstreamTargetException : Exception
    {
        public UpstreamTargetException(string message) : base(message) { }
    }

    private sealed class BlockedException : Exception
    {
        public BlockedException(string message) : base(message) { }
    }

    private enum RoutePath { Direct, Upstream, Refuse }

    private const string ExitIpHost = "api.ipify.org";
    private const int ConnectTimeoutMs = 10000;
    private const int ProbeTimeoutMs = 5000;
    private const int HealthIntervalMs = 10000;
    private const int FailureThreshold = 2;

    private static readonly object _lock = new();
    private static readonly HashSet<string> _droppedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Proxy-Connection", "Proxy-Authorization", "Connection", "Keep-Alive"
    };

    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static VpnProfile? _profile;
    private static VpnRelayState _state = VpnRelayState.Stopped;
    private static string _lastMessage = "";
    private static string? _exitIp;
    private static int _failures;

    public static event Action<VpnRelayState, string>? StateChanged;
    public static event Action<string>? FallbackNotice;

    public static int Port { get; private set; }

    public static VpnRelayState State
    {
        get { lock (_lock) return _state; }
    }

    public static string LastMessage
    {
        get { lock (_lock) return _lastMessage; }
    }

    public static string? ExitIp
    {
        get { lock (_lock) return _exitIp; }
    }

    public static VpnProfile? ActiveProfile
    {
        get { lock (_lock) return _profile == null ? null : CloneProfile(_profile); }
    }

    public static bool Start()
    {
        lock (_lock)
        {
            if (_listener != null) return true;
            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
            catch (Exception ex)
            {
                try { _listener?.Stop(); } catch { }
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                LogService.RecordCrash(ex, "VpnRelayService.Start");
                return false;
            }

            var token = _cts.Token;
            var listener = _listener;
            _ = Task.Run(() => AcceptLoopAsync(listener, token));
            _ = Task.Run(() => HealthLoopAsync(token));
        }

        SetState(VpnRelayState.Direct, $"Relay listening on 127.0.0.1:{Port}");
        return true;
    }

    public static void Stop()
    {
        lock (_lock)
        {
            if (_listener == null) return;
            try { _cts?.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            _profile = null;
            _exitIp = null;
            _failures = 0;
        }
        SetState(VpnRelayState.Stopped, "Relay stopped");
    }

    public static async Task<(bool Ok, string Message)> ConnectAsync(VpnProfile profile, CancellationToken ct = default)
    {
        if (!Start()) return (false, "Relay failed to start. See log.");
        if (!VpnProfileStore.Validate(profile, out string error)) return (false, error);

        var snapshot = CloneProfile(profile);
        lock (_lock)
        {
            _profile = snapshot;
            _failures = 0;
            _exitIp = null;
        }
        SetState(VpnRelayState.Connecting, $"Connecting via {snapshot.Name}");

        var (ok, info) = await TestProfileAsync(snapshot, ct);

        bool superseded;
        lock (_lock) superseded = !ReferenceEquals(_profile, snapshot);
        if (superseded) return (false, "Superseded by a newer connect request.");

        if (!ok)
        {
            lock (_lock) _profile = null;
            SetState(VpnRelayState.Direct, $"Connect failed: {info}");
            return (false, info);
        }

        lock (_lock) _exitIp = info;
        SetState(VpnRelayState.Connected, $"Connected via {snapshot.Name}, exit IP {info}");
        return (true, info);
    }

    public static void Disconnect()
    {
        lock (_lock)
        {
            _profile = null;
            _exitIp = null;
            _failures = 0;
        }
        if (State != VpnRelayState.Stopped)
            SetState(VpnRelayState.Direct, "VPN disconnected");
    }

    public static async Task<(bool Ok, string Info)> TestProfileAsync(VpnProfile profile, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(20000);
        TcpClient? tc = null;
        try
        {
            var result = await ConnectViaUpstreamAsync(profile, ExitIpHost, 443, false, linked.Token);
            tc = result.Client;

            using var ssl = new SslStream(tc.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = ExitIpHost }, linked.Token);

            var req = Encoding.ASCII.GetBytes(
                $"GET / HTTP/1.1\r\nHost: {ExitIpHost}\r\nConnection: close\r\nUser-Agent: Horizon\r\n\r\n");
            await ssl.WriteAsync(req, linked.Token);
            await ssl.FlushAsync(linked.Token);

            using var reader = new StreamReader(ssl, Encoding.ASCII);
            var response = await reader.ReadToEndAsync(linked.Token);
            int split = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var body = split >= 0 ? response[(split + 4)..] : response;

            foreach (var token in body.Split(new[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if ((token.Contains('.') || token.Contains(':')) && IPAddress.TryParse(token, out var ip))
                {
                    LogService.Write("VPN", $"Test OK via {profile.Name}: exit IP {ip}");
                    return (true, ip.ToString());
                }
            }

            LogService.Write("VPN", $"Test via {profile.Name}: unexpected response from exit-IP service");
            return (false, "Tunnel opened but exit-IP check returned an unexpected response.");
        }
        catch (UpstreamTargetException ex)
        {
            LogService.Write("VPN", $"Test via {profile.Name} failed: {ex.Message}");
            return (false, ex.Message);
        }
        catch (OperationCanceledException)
        {
            LogService.Write("VPN", $"Test via {profile.Name} timed out");
            return (false, "Timed out while connecting through the server.");
        }
        catch (Exception ex)
        {
            LogService.Write("VPN", $"Test via {profile.Name} failed: {ex.GetType().Name}: {ex.Message}");
            return (false, ex.Message);
        }
        finally
        {
            tc?.Dispose();
        }
    }

    private static void SetState(VpnRelayState state, string message)
    {
        lock (_lock)
        {
            _state = state;
            _lastMessage = message;
        }
        LogService.Write("VPN", $"State -> {state}: {message}");
        try { StateChanged?.Invoke(state, message); }
        catch (Exception ex) { LogService.RecordCrash(ex, "VpnRelayService.StateChanged"); }
    }

    private static void RaiseNotice(string message)
    {
        if (!SettingsService.Current.VpnNotifyOnFallback) return;
        try { FallbackNotice?.Invoke(message); }
        catch (Exception ex) { LogService.RecordCrash(ex, "VpnRelayService.FallbackNotice"); }
    }

    private static async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                LogService.Write("VPN", $"Accept error: {ex.Message}");
                try { await Task.Delay(100, ct); } catch { break; }
                continue;
            }
            _ = Task.Run(() => HandleClientAsync(client, ct));
        }
    }

    private static async Task HealthLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HealthIntervalMs, ct); }
            catch { break; }

            VpnProfile? profile;
            VpnRelayState state;
            lock (_lock)
            {
                profile = _profile;
                state = _state;
            }
            if (profile == null) continue;
            if (state != VpnRelayState.Connected && state != VpnRelayState.Fallback && state != VpnRelayState.Blocked) continue;

            bool ok = await ProbeAsync(profile, ct);
            if (ok) ReportUpstreamSuccess();
            else ReportUpstreamFailure(new IOException("Health probe could not reach the upstream server."));
        }
    }

    private static async Task<bool> ProbeAsync(VpnProfile profile, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(ProbeTimeoutMs);
        try
        {
            using var tc = new TcpClient();
            await tc.ConnectAsync(profile.Host, profile.Port, linked.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ReportUpstreamSuccess()
    {
        bool recovered;
        lock (_lock)
        {
            _failures = 0;
            recovered = _state == VpnRelayState.Fallback || _state == VpnRelayState.Blocked;
        }
        if (!recovered) return;
        SetState(VpnRelayState.Connected, "Upstream recovered; traffic routed through the VPN again.");
        RaiseNotice("VPN upstream recovered. Traffic is routed through the VPN again.");
    }

    private static void ReportUpstreamFailure(Exception ex)
    {
        bool escalate;
        int count;
        lock (_lock)
        {
            _failures++;
            count = _failures;
            escalate = _failures >= FailureThreshold && _state == VpnRelayState.Connected;
        }
        LogService.Write("VPN", $"Upstream failure #{count}: {ex.GetType().Name}: {ex.Message}");
        if (!escalate) return;

        bool killSwitch = SettingsService.Current.VpnKillSwitchEnabled;
        if (killSwitch)
        {
            const string msg = "VPN upstream is unreachable. Kill switch is ON: browser traffic is blocked.";
            SetState(VpnRelayState.Blocked, msg);
            RaiseNotice(msg);
        }
        else
        {
            const string msg = "VPN upstream is unreachable. Kill switch is OFF: traffic now goes direct and exposes your real IP.";
            SetState(VpnRelayState.Fallback, msg);
            RaiseNotice(msg);
        }
    }

    private static RoutePath Decide(string host, out VpnProfile? profile)
    {
        VpnRelayState state;
        lock (_lock)
        {
            profile = _profile;
            state = _state;
        }
        if (profile == null) return RoutePath.Direct;
        if (IsBypassed(host)) return RoutePath.Direct;
        return state switch
        {
            VpnRelayState.Connected => RoutePath.Upstream,
            VpnRelayState.Connecting => RoutePath.Upstream,
            VpnRelayState.Blocked => RoutePath.Refuse,
            _ => RoutePath.Direct
        };
    }

    private static bool IsBypassed(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                if (b[0] == 10) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                if (b[0] == 169 && b[1] == 254) return true;
            }
            else if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                return true;
            }
        }

        var list = SettingsService.Current.VpnBypassList ?? "";
        var h = host.ToLowerInvariant();
        foreach (var raw in list.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = raw.ToLowerInvariant();
            if (entry == "<local>")
            {
                if (!h.Contains('.')) return true;
                continue;
            }
            if (entry.StartsWith("*.")) entry = entry[2..];
            else if (entry.StartsWith(".")) entry = entry[1..];
            if (h == entry || h.EndsWith("." + entry)) return true;
        }
        return false;
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var clientHolder = client;
        TcpClient? target = null;
        bool piping = false;
        NetworkStream? cs = null;
        try
        {
            client.NoDelay = true;
            cs = client.GetStream();

            var (head, leftover) = await ReadHeadAsync(cs, ct);
            var text = Encoding.Latin1.GetString(head);
            int eol = text.IndexOf("\r\n", StringComparison.Ordinal);
            var requestLine = eol >= 0 ? text[..eol] : text;
            var parts = requestLine.Split(' ');
            if (parts.Length < 3)
            {
                await WriteStatusAsync(cs, 400, "Bad Request", "Malformed request line.", ct);
                return;
            }

            string method = parts[0];
            string targetStr = parts[1];
            string version = parts[2];

            if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                if (!TrySplitHostPort(targetStr, 443, out string host, out int port))
                {
                    await WriteStatusAsync(cs, 400, "Bad Request", "Invalid CONNECT target.", ct);
                    return;
                }

                var connected = await ConnectTargetAsync(host, port, false, ct);
                target = connected.Client;
                var ts = target.GetStream();

                var ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                await cs.WriteAsync(ok, ct);
                if (leftover.Length > 0) await ts.WriteAsync(leftover, ct);

                piping = true;
                await PipeAsync(cs, ts);
            }
            else
            {
                if (!Uri.TryCreate(targetStr, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
                {
                    await WriteStatusAsync(cs, 400, "Bad Request", "Only absolute http:// targets are supported for non-CONNECT requests.", ct);
                    return;
                }

                var connected = await ConnectTargetAsync(uri.DnsSafeHost, uri.Port, true, ct);
                target = connected.Client;
                var ts = target.GetStream();

                string newLine = connected.Absolute ? requestLine : $"{method} {uri.PathAndQuery} {version}";
                var rebuilt = BuildHead(text, newLine, connected.AuthHeader);
                await ts.WriteAsync(rebuilt, ct);
                if (leftover.Length > 0) await ts.WriteAsync(leftover, ct);

                piping = true;
                await PipeAsync(cs, ts);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (BlockedException ex)
        {
            LogService.Write("VPN", $"Blocked connection: {ex.Message}");
            if (cs != null && !piping) await WriteStatusAsync(cs, 503, "Service Unavailable", ex.Message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (!piping)
            {
                LogService.Write("VPN", $"Connection error: {ex.GetType().Name}: {ex.Message}");
                if (cs != null) await WriteStatusAsync(cs, 502, "Bad Gateway", ex.Message, CancellationToken.None);
            }
        }
        finally
        {
            target?.Dispose();
        }
    }

    private static async Task<(TcpClient Client, bool Absolute, string? AuthHeader)> ConnectTargetAsync(
        string host, int port, bool plainHttp, CancellationToken ct)
    {
        var path = Decide(host, out var profile);
        if (path == RoutePath.Refuse)
            throw new BlockedException("VPN kill switch is active: upstream unavailable, traffic blocked.");

        if (path == RoutePath.Upstream && profile != null)
        {
            try
            {
                var result = await ConnectViaUpstreamAsync(profile, host, port, plainHttp, ct);
                ReportUpstreamSuccess();
                return result;
            }
            catch (UpstreamTargetException)
            {
                ReportUpstreamSuccess();
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportUpstreamFailure(ex);
                if (SettingsService.Current.VpnKillSwitchEnabled)
                    throw new BlockedException("VPN upstream failed and the kill switch is ON: connection blocked.");
            }
        }

        var direct = await ConnectDirectAsync(host, port, ct);
        return (direct, false, null);
    }

    private static async Task<TcpClient> ConnectDirectAsync(string host, int port, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(ConnectTimeoutMs);
        var tc = new TcpClient { NoDelay = true };
        try
        {
            await tc.ConnectAsync(host, port, linked.Token);
            return tc;
        }
        catch
        {
            tc.Dispose();
            throw;
        }
    }

    private static async Task<(TcpClient Client, bool Absolute, string? AuthHeader)> ConnectViaUpstreamAsync(
        VpnProfile profile, string host, int port, bool plainHttp, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(ConnectTimeoutMs);
        var tc = new TcpClient { NoDelay = true };
        try
        {
            await tc.ConnectAsync(profile.Host, profile.Port, linked.Token);
            var stream = tc.GetStream();

            if (profile.Type == VpnUpstreamType.Socks5)
            {
                await Socks5HandshakeAsync(stream, profile, host, port, linked.Token);
                return (tc, false, null);
            }

            if (plainHttp) return (tc, true, ProxyAuthHeader(profile));

            await HttpConnectHandshakeAsync(stream, profile, host, port, linked.Token);
            return (tc, false, null);
        }
        catch
        {
            tc.Dispose();
            throw;
        }
    }

    private static string? ProxyAuthHeader(VpnProfile profile)
    {
        if (!profile.HasCredentials) return null;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{profile.Username}:{profile.Password}"));
        return $"Proxy-Authorization: Basic {token}";
    }

    private static async Task Socks5HandshakeAsync(NetworkStream s, VpnProfile p, string host, int port, CancellationToken ct)
    {
        bool useAuth = p.HasCredentials;
        await s.WriteAsync(useAuth ? new byte[] { 5, 2, 0, 2 } : new byte[] { 5, 1, 0 }, ct);

        var choice = await ReadExactAsync(s, 2, ct);
        if (choice[0] != 5) throw new IOException("Upstream is not a SOCKS5 server.");
        if (choice[1] == 0xFF) throw new IOException("SOCKS5 server accepts none of the offered auth methods.");

        if (choice[1] == 2)
        {
            var user = Encoding.UTF8.GetBytes(p.Username);
            var pass = Encoding.UTF8.GetBytes(p.Password);
            if (user.Length > 255 || pass.Length > 255) throw new IOException("SOCKS5 credentials exceed 255 bytes.");

            var auth = new byte[3 + user.Length + pass.Length];
            auth[0] = 1;
            auth[1] = (byte)user.Length;
            Buffer.BlockCopy(user, 0, auth, 2, user.Length);
            auth[2 + user.Length] = (byte)pass.Length;
            Buffer.BlockCopy(pass, 0, auth, 3 + user.Length, pass.Length);
            await s.WriteAsync(auth, ct);

            var authReply = await ReadExactAsync(s, 2, ct);
            if (authReply[1] != 0) throw new IOException("SOCKS5 authentication failed (wrong username or password).");
        }
        else if (choice[1] != 0)
        {
            throw new IOException($"SOCKS5 server chose unsupported auth method {choice[1]}.");
        }

        var req = new List<byte> { 5, 1, 0 };
        if (IPAddress.TryParse(host, out var ip))
        {
            req.Add(ip.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)4);
            req.AddRange(ip.GetAddressBytes());
        }
        else
        {
            var hostBytes = Encoding.ASCII.GetBytes(host);
            if (hostBytes.Length == 0 || hostBytes.Length > 255) throw new UpstreamTargetException("Target host name length is invalid.");
            req.Add(3);
            req.Add((byte)hostBytes.Length);
            req.AddRange(hostBytes);
        }
        req.Add((byte)(port >> 8));
        req.Add((byte)(port & 0xFF));
        await s.WriteAsync(req.ToArray(), ct);

        var reply = await ReadExactAsync(s, 4, ct);
        if (reply[0] != 5) throw new IOException("Malformed SOCKS5 reply.");
        if (reply[1] != 0) throw new UpstreamTargetException($"SOCKS5 connect to {host}:{port} failed: {Socks5ReplyText(reply[1])}.");

        int addrLen;
        switch (reply[3])
        {
            case 1: addrLen = 4; break;
            case 4: addrLen = 16; break;
            case 3: addrLen = (await ReadExactAsync(s, 1, ct))[0]; break;
            default: throw new IOException("Malformed SOCKS5 reply address type.");
        }
        await ReadExactAsync(s, addrLen + 2, ct);
    }

    private static string Socks5ReplyText(byte code) => code switch
    {
        1 => "general failure",
        2 => "not allowed by ruleset",
        3 => "network unreachable",
        4 => "host unreachable",
        5 => "connection refused",
        6 => "TTL expired",
        7 => "command not supported",
        8 => "address type not supported",
        _ => $"code {code}"
    };

    private static async Task HttpConnectHandshakeAsync(NetworkStream s, VpnProfile p, string host, int port, CancellationToken ct)
    {
        string authority = host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
        var sb = new StringBuilder();
        sb.Append($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n");
        var auth = ProxyAuthHeader(p);
        if (auth != null) sb.Append(auth).Append("\r\n");
        sb.Append("\r\n");
        await s.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);

        var (head, _) = await ReadHeadAsync(s, ct);
        var text = Encoding.Latin1.GetString(head);
        int eol = text.IndexOf("\r\n", StringComparison.Ordinal);
        var statusLine = eol >= 0 ? text[..eol] : text;
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out int code))
            throw new IOException($"Malformed upstream proxy reply: {statusLine}");

        if (code == 200) return;
        if (code == 407) throw new IOException("Upstream proxy rejected the credentials (407).");
        throw new UpstreamTargetException($"Upstream proxy refused CONNECT to {authority}: {statusLine}");
    }

    private static byte[] BuildHead(string originalHead, string requestLine, string? extraHeader)
    {
        var lines = originalHead.Split("\r\n");
        var sb = new StringBuilder();
        sb.Append(requestLine).Append("\r\n");
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            var name = colon > 0 ? line[..colon] : "";
            if (name.Length > 0 && _droppedHeaders.Contains(name)) continue;
            sb.Append(line).Append("\r\n");
        }
        sb.Append("Connection: close\r\n");
        if (!string.IsNullOrEmpty(extraHeader)) sb.Append(extraHeader).Append("\r\n");
        sb.Append("\r\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static bool TrySplitHostPort(string value, int defaultPort, out string host, out int port)
    {
        host = "";
        port = defaultPort;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (value.StartsWith("["))
        {
            int close = value.IndexOf(']');
            if (close < 0) return false;
            host = value.Substring(1, close - 1);
            if (close + 1 < value.Length)
            {
                if (value[close + 1] != ':') return false;
                if (!int.TryParse(value[(close + 2)..], out port)) return false;
            }
            return host.Length > 0 && port > 0 && port <= 65535;
        }

        int idx = value.LastIndexOf(':');
        if (idx < 0)
        {
            host = value;
            return host.Length > 0;
        }
        host = value[..idx];
        if (!int.TryParse(value[(idx + 1)..], out port)) return false;
        return host.Length > 0 && port > 0 && port <= 65535;
    }

    private static async Task<(byte[] Head, byte[] Leftover)> ReadHeadAsync(Stream s, CancellationToken ct)
    {
        var buf = new byte[16384];
        int len = 0;
        while (true)
        {
            if (len == buf.Length)
            {
                if (buf.Length >= 65536) throw new IOException("Request header too large.");
                Array.Resize(ref buf, buf.Length * 2);
            }

            int n = await s.ReadAsync(buf.AsMemory(len, buf.Length - len), ct);
            if (n == 0) throw new IOException("Connection closed before the header ended.");

            int scanFrom = Math.Max(0, len - 3);
            len += n;

            int end = -1;
            for (int i = scanFrom; i <= len - 4; i++)
            {
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                {
                    end = i + 4;
                    break;
                }
            }
            if (end < 0) continue;

            var head = new byte[end];
            Buffer.BlockCopy(buf, 0, head, 0, end);
            var leftover = new byte[len - end];
            Buffer.BlockCopy(buf, end, leftover, 0, len - end);
            return (head, leftover);
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream s, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
            if (n == 0) throw new IOException("Connection closed during handshake.");
            read += n;
        }
        return buf;
    }

    private static async Task WriteStatusAsync(NetworkStream s, int code, string reason, string body, CancellationToken ct)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(body);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {code} {reason}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await s.WriteAsync(head, ct);
            await s.WriteAsync(payload, ct);
        }
        catch
        {
        }
    }

    private static async Task PipeAsync(NetworkStream a, NetworkStream b)
    {
        var t1 = a.CopyToAsync(b, 81920);
        var t2 = b.CopyToAsync(a, 81920);
        _ = t1.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        _ = t2.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        await Task.WhenAny(t1, t2);
    }

    private static VpnProfile CloneProfile(VpnProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Type = p.Type,
        Host = p.Host,
        Port = p.Port,
        Username = p.Username,
        Password = p.Password,
        Source = p.Source
    };
}