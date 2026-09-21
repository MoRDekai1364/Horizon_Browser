// ═══════════════════════════════════════════════════════════════════════════════
//  AccountSyncService.cs  —  Horizon Browser
//
//  Provides:
//   • Google account login via OAuth 2.0 PKCE (browser-based)
//   • Microsoft account login via OAuth 2.0 PKCE (browser-based)
//   • Multi-account support (multiple logged-in accounts per provider)
//   • Google Calendar + Google Keep (Notes) sync
//   • Microsoft OneNote + Outlook Calendar sync
//
//  HOW IT WORKS
//  ───────────────────────────────────────────────────────────────────────────
//  1. User clicks "Add Google Account" or "Add Microsoft Account"
//  2. A loopback HTTP listener opens on localhost:PORT
//  3. System browser opens the OAuth consent page
//  4. On successful login the provider redirects to localhost:PORT?code=…
//  5. We exchange the code for tokens, fetch the profile, and store everything
//     encrypted in SettingsService.
//
//  SETUP (one-time developer step)
//  ───────────────────────────────────────────────────────────────────────────
//  Google:
//    1. console.cloud.google.com → New project → Enable APIs:
//       "Google Calendar API", "Keep API (v1)"
//    2. Credentials → OAuth 2.0 Client → Desktop App
//    3. Copy Client ID + Client Secret into Settings → Accounts → Google
//    Scopes requested: calendar.readonly, keep.readonly (read+write optional)
//
//  Microsoft:
//    1. portal.azure.com → App registrations → New registration
//    2. Redirect URI: http://localhost  (type: Mobile/Desktop)
//    3. API permissions: Calendars.Read, Notes.ReadWrite (Microsoft Graph)
//    4. Copy Application (Client) ID into Settings → Accounts → Microsoft
//    (Microsoft public apps do not require a client secret)
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Services;

// ── Data models ───────────────────────────────────────────────────────────────

public class SyncAccount
{
    public string Provider    { get; set; } = "";   // "Google" | "Microsoft"
    public string AccountId   { get; set; } = "";   // email / user id
    public string DisplayName { get; set; } = "";
    public string Email       { get; set; } = "";
    public string AvatarUrl   { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken{ get; set; } = "";
    public DateTime Expiry    { get; set; } = DateTime.MinValue;
    public bool IsExpired      => DateTime.UtcNow >= Expiry.ToUniversalTime();
}

public class CalendarEvent
{
    public string Id      { get; set; } = "";
    public string Title   { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End   { get; set; }
    public string Location{ get; set; } = "";
    public string Source  { get; set; } = ""; // account email
}

public class SyncNote
{
    public string Id      { get; set; } = "";
    public string Title   { get; set; } = "";
    public string Body    { get; set; } = "";
    public DateTime Updated{ get; set; }
    public string Source  { get; set; } = "";
}

// ── Main service ──────────────────────────────────────────────────────────────

public static class AccountSyncService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // ── Scopes ────────────────────────────────────────────────────────────────

    private const string GoogleCalendarScope = "https://www.googleapis.com/auth/calendar.readonly";
    private const string GoogleKeepScope     = "https://www.googleapis.com/auth/keep";
    private const string GoogleProfileScope  = "openid email profile";

    private const string MsCalendarScope = "https://graph.microsoft.com/Calendars.ReadWrite";
    private const string MsNotesScope    = "https://graph.microsoft.com/Notes.ReadWrite";
    private const string MsProfileScope  = "openid email profile offline_access";

    // ── PKCE helpers ──────────────────────────────────────────────────────────

	internal static (string verifier, string challenge) GeneratePkce()    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        string verifier = Base64UrlEncode(bytes);
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        string challenge = Base64UrlEncode(hash);
        return (verifier, challenge);
    }

    internal static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).Replace("+","-").Replace("/","_").TrimEnd('=');

    internal static string GenerateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start(); int port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        return port;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  GOOGLE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the Google OAuth consent page. Returns null on failure.
    /// </summary>
    public static async Task<SyncAccount?> AddGoogleAccountAsync(string clientId, string clientSecret)
    {
        int port = GetFreePort();
        string redirectUri = $"http://localhost:{port}/";
        string scope = Uri.EscapeDataString($"{GoogleProfileScope} {GoogleCalendarScope} {GoogleKeepScope}");
        var (verifier, challenge) = GeneratePkce();
        string state = GenerateState();

        string authUrl =
            $"https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={scope}" +
            $"&code_challenge={challenge}" +
            $"&code_challenge_method=S256" +
            $"&state={state}" +
            $"&access_type=offline" +
            $"&prompt=select_account";

        string? code = await RunLoopbackListenerAsync(redirectUri, authUrl, state);
        if (string.IsNullOrEmpty(code)) return null;

        return await ExchangeGoogleCodeAsync(code, verifier, redirectUri, clientId, clientSecret);
    }

    internal static async Task<SyncAccount?> ExchangeGoogleCodeAsync(
        string code, string verifier, string redirectUri, string clientId, string clientSecret)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"]  = redirectUri,
            ["grant_type"]    = "authorization_code",
            ["code_verifier"] = verifier,
        });

        var resp = await Http.PostAsync("https://oauth2.googleapis.com/token", body);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var atProp)) return null;

        string accessToken  = atProp.GetString() ?? "";
        string refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
        int    expiresIn    = root.TryGetProperty("expires_in",    out var ei) ? ei.GetInt32() : 3600;

        var profile = await FetchGoogleProfileAsync(accessToken);
        return new SyncAccount
        {
            Provider     = "Google",
            AccountId    = profile.email,
            DisplayName  = profile.name,
            Email        = profile.email,
            AvatarUrl    = profile.picture,
            AccessToken  = accessToken,
            RefreshToken = refreshToken,
            Expiry       = DateTime.UtcNow.AddSeconds(expiresIn),
        };
    }

    private static async Task<(string email, string name, string picture)> FetchGoogleProfileAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return (
            r.TryGetProperty("email",   out var e) ? e.GetString()??""  : "",
            r.TryGetProperty("name",    out var n) ? n.GetString()??""  : "",
            r.TryGetProperty("picture", out var p) ? p.GetString()??""  : ""
        );
    }

    public static async Task<string> RefreshGoogleTokenAsync(SyncAccount account, string clientId, string clientSecret)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = account.RefreshToken,
            ["grant_type"]    = "refresh_token",
        });
        var resp = await Http.PostAsync("https://oauth2.googleapis.com/token", body);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("access_token", out var at)) throw new Exception("Refresh failed");
        account.AccessToken = at.GetString()!;
        account.Expiry = DateTime.UtcNow.AddSeconds(r.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600);
        return account.AccessToken;
    }

    // ── Google Calendar ───────────────────────────────────────────────────────

    public static async Task<List<CalendarEvent>> FetchGoogleCalendarAsync(SyncAccount account,
        string clientId, string clientSecret, DateTime? from = null, DateTime? to = null)
    {
        if (account.IsExpired)
            await RefreshGoogleTokenAsync(account, clientId, clientSecret);

        from ??= DateTime.UtcNow;
        to   ??= DateTime.UtcNow.AddDays(30);
        string tMin = from.Value.ToString("o");
        string tMax = to  .Value.ToString("o");

        string url = $"https://www.googleapis.com/calendar/v3/calendars/primary/events" +
                     $"?timeMin={Uri.EscapeDataString(tMin)}&timeMax={Uri.EscapeDataString(tMax)}" +
                     $"&singleEvents=true&orderBy=startTime&maxResults=100";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var events = new List<CalendarEvent>();
        if (!doc.RootElement.TryGetProperty("items", out var items)) return events;
        foreach (var item in items.EnumerateArray())
        {
            string start = "", end = "";
            if (item.TryGetProperty("start", out var s))
                start = s.TryGetProperty("dateTime", out var dt) ? dt.GetString()??""
                      : s.TryGetProperty("date",     out var d ) ? d.GetString()??"" : "";
            if (item.TryGetProperty("end",   out var e))
                end   = e.TryGetProperty("dateTime", out var dt2) ? dt2.GetString()??""
                      : e.TryGetProperty("date",     out var d2)  ? d2.GetString()??"" : "";
            events.Add(new CalendarEvent
            {
                Id       = item.TryGetProperty("id",       out var id)  ? id.GetString()??"" : "",
                Title    = item.TryGetProperty("summary",  out var sum) ? sum.GetString()??"" : "(no title)",
                Location = item.TryGetProperty("location", out var loc) ? loc.GetString()??"" : "",
                Start    = DateTime.TryParse(start, out var ds) ? ds : DateTime.MinValue,
                End      = DateTime.TryParse(end,   out var de) ? de : DateTime.MinValue,
                Source   = account.Email
            });
        }
        return events;
    }

    // ── Google Keep (Notes) ───────────────────────────────────────────────────

    public static async Task<List<SyncNote>> FetchGoogleKeepAsync(SyncAccount account,
        string clientId, string clientSecret)
    {
        if (account.IsExpired)
            await RefreshGoogleTokenAsync(account, clientId, clientSecret);

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://keep.googleapis.com/v1/notes?pageSize=100");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var notes = new List<SyncNote>();
        if (!doc.RootElement.TryGetProperty("notes", out var items)) return notes;
        foreach (var item in items.EnumerateArray())
        {
            string body = "";
            if (item.TryGetProperty("body", out var b) &&
                b.TryGetProperty("text",    out var t) &&
                t.TryGetProperty("text",    out var txt))
                body = txt.GetString() ?? "";

            notes.Add(new SyncNote
            {
                Id      = item.TryGetProperty("name",    out var name) ? name.GetString()??""  : "",
                Title   = item.TryGetProperty("title",   out var ttl)  ? ttl.GetString()??"Untitled" : "Untitled",
                Body    = body,
                Updated = item.TryGetProperty("updateTime", out var ut) &&
                          DateTime.TryParse(ut.GetString(), out var udt) ? udt : DateTime.MinValue,
                Source  = account.Email
            });
        }
        return notes;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  MICROSOFT
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the Microsoft OAuth consent page. Returns null on failure.
    /// </summary>
    public static async Task<SyncAccount?> AddMicrosoftAccountAsync(string clientId)
    {
        int port = GetFreePort();
        string redirectUri = $"http://localhost:{port}/";
        string scope = Uri.EscapeDataString($"{MsProfileScope} {MsCalendarScope} {MsNotesScope}");
        var (verifier, challenge) = GeneratePkce();
        string state = GenerateState();

        string authUrl =
            $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={scope}" +
            $"&code_challenge={challenge}" +
            $"&code_challenge_method=S256" +
            $"&state={state}" +
            $"&prompt=select_account";

        string? code = await RunLoopbackListenerAsync(redirectUri, authUrl, state);
        if (string.IsNullOrEmpty(code)) return null;

        return await ExchangeMsCodeAsync(code, verifier, redirectUri, clientId);
    }

    internal static async Task<SyncAccount?> ExchangeMsCodeAsync(
        string code, string verifier, string redirectUri, string clientId)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = clientId,
            ["code"]          = code,
            ["redirect_uri"]  = redirectUri,
            ["grant_type"]    = "authorization_code",
            ["code_verifier"] = verifier,
        });

        var resp = await Http.PostAsync(
            "https://login.microsoftonline.com/common/oauth2/v2.0/token", body);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var atProp)) return null;

        string accessToken  = atProp.GetString() ?? "";
        string refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
        int    expiresIn    = root.TryGetProperty("expires_in",    out var ei) ? ei.GetInt32() : 3600;

        var profile = await FetchMsProfileAsync(accessToken);
        return new SyncAccount
        {
            Provider     = "Microsoft",
            AccountId    = profile.id,
            DisplayName  = profile.name,
            Email        = profile.email,
            AccessToken  = accessToken,
            RefreshToken = refreshToken,
            Expiry       = DateTime.UtcNow.AddSeconds(expiresIn),
        };
    }

    private static async Task<(string id, string email, string name)> FetchMsProfileAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string email = r.TryGetProperty("mail",                out var m) ? m.GetString()??"" :
                       r.TryGetProperty("userPrincipalName",   out var u) ? u.GetString()??"" : "";
        return (
            r.TryGetProperty("id",          out var id) ? id.GetString()??""   : "",
            email,
            r.TryGetProperty("displayName", out var n)  ? n.GetString()??""    : ""
        );
    }

    public static async Task<string> RefreshMicrosoftTokenAsync(SyncAccount account, string clientId)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = clientId,
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = account.RefreshToken,
        });
        var resp = await Http.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", body);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("access_token", out var at)) throw new Exception("Refresh failed");
        account.AccessToken = at.GetString()!;
        account.Expiry = DateTime.UtcNow.AddSeconds(r.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600);
        return account.AccessToken;
    }

    // ── Microsoft Calendar ────────────────────────────────────────────────────

    public static async Task<List<CalendarEvent>> FetchMicrosoftCalendarAsync(SyncAccount account,
        string clientId, DateTime? from = null, DateTime? to = null)
    {
        if (account.IsExpired) await RefreshMicrosoftTokenAsync(account, clientId);

        from ??= DateTime.UtcNow;
        to   ??= DateTime.UtcNow.AddDays(30);
        string start = from.Value.ToString("o");
        string end   = to  .Value.ToString("o");

        string url = $"https://graph.microsoft.com/v1.0/me/calendarview" +
                     $"?startDateTime={Uri.EscapeDataString(start)}&endDateTime={Uri.EscapeDataString(end)}" +
                     $"&$top=100&$select=subject,start,end,location";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        req.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");

        var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var events = new List<CalendarEvent>();
        if (!doc.RootElement.TryGetProperty("value", out var items)) return events;
        foreach (var item in items.EnumerateArray())
        {
            string startDt = "", endDt = "";
            if (item.TryGetProperty("start", out var s)) startDt = s.TryGetProperty("dateTime",out var sd)?sd.GetString()??"":"";
            if (item.TryGetProperty("end",   out var e)) endDt   = e.TryGetProperty("dateTime",out var ed)?ed.GetString()??"":"";
            events.Add(new CalendarEvent
            {
                Id       = item.TryGetProperty("id",       out var id)  ? id.GetString()??"" : "",
                Title    = item.TryGetProperty("subject",  out var sub) ? sub.GetString()??""     : "(no title)",
                Location = item.TryGetProperty("location", out var loc) &&
                           loc.TryGetProperty("displayName", out var ld) ? ld.GetString()??"" : "",
                Start    = DateTime.TryParse(startDt, out var ds) ? ds : DateTime.MinValue,
                End      = DateTime.TryParse(endDt,   out var de) ? de : DateTime.MinValue,
                Source   = account.Email
            });
        }
        return events;
    }

    // ── Microsoft OneNote ─────────────────────────────────────────────────────

    public static async Task<List<SyncNote>> FetchMicrosoftNotesAsync(SyncAccount account, string clientId)
    {
        if (account.IsExpired) await RefreshMicrosoftTokenAsync(account, clientId);

        string url = "https://graph.microsoft.com/v1.0/me/onenote/pages?$top=100&$select=id,title,lastModifiedDateTime,contentUrl";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var notes = new List<SyncNote>();
        if (!doc.RootElement.TryGetProperty("value", out var items)) return notes;
        foreach (var item in items.EnumerateArray())
        {
            notes.Add(new SyncNote
            {
                Id      = item.TryGetProperty("id",                   out var id)  ? id.GetString()??"" : "",
                Title   = item.TryGetProperty("title",                out var ttl) ? ttl.GetString()??"Untitled" : "Untitled",
                Updated = item.TryGetProperty("lastModifiedDateTime", out var mod) &&
                          DateTime.TryParse(mod.GetString(),          out var dt)  ? dt : DateTime.MinValue,
                Source  = account.Email,
                // Body requires a separate call per page; fetch lazily
            });
        }
        return notes;
    }

    public static async Task<string> FetchOneNotePageBodyAsync(SyncAccount account, string clientId, string pageId)
    {
        if (account.IsExpired) await RefreshMicrosoftTokenAsync(account, clientId);
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/me/onenote/pages/{pageId}/content?includeIDs=false");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var resp = await Http.SendAsync(req);
        // Returns HTML — strip tags for plain text
        string html = await resp.Content.ReadAsStringAsync();
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LOOPBACK LISTENER  (shared by Google + Microsoft)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the system browser, listens on redirectUri for the auth code.
    /// Returns null if user cancelled or timeout (120 s).
    /// </summary>
    private static async Task<string?> RunLoopbackListenerAsync(
        string redirectUri, string authUrl, string expectedState)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        // Open browser
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true }); }
        catch { listener.Stop(); return null; }

        // Wait for callback (120 s max)
        var getCtx = listener.GetContextAsync();
        if (await Task.WhenAny(getCtx, Task.Delay(120_000)) != getCtx)
        { listener.Stop(); return null; }

        var ctx = await getCtx;
        var qs  = ctx.Request.QueryString;
        string? code  = qs["code"];
        string? state = qs["state"];
        string? error = qs["error"];

        // Send close-tab response
        string html = string.IsNullOrEmpty(error)
            ? "<html><body style='font-family:sans-serif;background:#0b0b0b;color:#00ff00;text-align:center;padding-top:80px'><h2>✔ Logged in — you can close this tab.</h2></body></html>"
            : $"<html><body style='font-family:sans-serif;background:#0b0b0b;color:#ff4444;text-align:center;padding-top:80px'><h2>✘ {error}: {qs["error_description"]}</h2></body></html>";
        byte[] buf = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html;charset=utf-8";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf);
        ctx.Response.Close();
        listener.Stop();

        if (!string.IsNullOrEmpty(error) || state != expectedState) return null;
        return code;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  SettingsData additions  (add to SettingsData class in SettingsService.cs)
// ═══════════════════════════════════════════════════════════════════════════════
//
//  public List<SyncAccount> SyncAccounts { get; set; } = new();
//  public string GoogleClientId          { get; set; } = "";
//  public string GoogleClientSecret      { get; set; } = "";
//  public string MicrosoftClientId       { get; set; } = "";
//  public string ChatGptApiKey           { get; set; } = "";
//  public string GeminiApiKey            { get; set; } = "";
//
// ═══════════════════════════════════════════════════════════════════════════════