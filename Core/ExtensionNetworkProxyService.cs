using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Core;

/// <summary>
/// Provides transparent network proxying for Chrome extensions running inside Microsoft WebView2.
/// Overcomes WebView2 limitations where cross-origin requests from chrome-extension:// origins
/// are sent with Origin: null or blocked by CORS, ensuring extension APIs and backend services
/// (e.g. Song Finder, translation, cloud sync) always receive the authorized extension Origin header.
/// </summary>
public static class ExtensionNetworkProxyService
{
    private static readonly ConcurrentDictionary<string, string> _idMappings = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Registers a mapping between an internal WebView2 extension ID (or alias) and the official Chrome Web Store ID.
    /// </summary>
    public static void RegisterIdMapping(string internalOrStoreId, string storeId)
    {
        if (string.IsNullOrWhiteSpace(internalOrStoreId) || string.IsNullOrWhiteSpace(storeId)) return;
        _idMappings[internalOrStoreId] = storeId;
    }

    /// <summary>
    /// Resolves an internal WebView2 extension ID to the official Chrome Web Store ID (or folder name).
    /// </summary>
    public static string ResolveStoreId(string idOrWv2Id)
    {
        if (string.IsNullOrWhiteSpace(idOrWv2Id)) return string.Empty;

        if (_idMappings.TryGetValue(idOrWv2Id, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            return mapped;

        // Check BrowserView.LoadedExtensionIds (folder -> wv2Id)
        foreach (var kvp in Controls.BrowserView.LoadedExtensionIds)
        {
            if (kvp.Value.Equals(idOrWv2Id, StringComparison.OrdinalIgnoreCase))
            {
                _idMappings[idOrWv2Id] = kvp.Key;
                return kvp.Key;
            }
        }

        // Check ExtensionService catalog
        var rec = ExtensionService.Find(idOrWv2Id);
        if (rec != null && !string.IsNullOrWhiteSpace(rec.FolderName))
        {
            _idMappings[idOrWv2Id] = rec.FolderName;
            return rec.FolderName;
        }

        return idOrWv2Id;
    }

    /// <summary>
    /// Attaches the extension network proxy to a CoreWebView2 instance.
    /// Intercepts outgoing HTTP/HTTPS requests and injects the valid chrome-extension:// Origin and Referer.
    /// </summary>
    /// <param name="wv">The CoreWebView2 instance.</param>
    /// <param name="extensionId">The Chrome extension ID if known (e.g. jfcgghdcppnkehglmddcoeknbamleaan).</param>
    public static void Attach(CoreWebView2 wv, string extensionId = "")
    {
        if (wv == null) return;

        if (!string.IsNullOrWhiteSpace(extensionId))
        {
            RegisterIdMapping(extensionId, extensionId);
        }

        wv.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

        wv.WebResourceRequested += (sender, e) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(e.Request.Uri)) return;

                var uri = new Uri(e.Request.Uri);
                // Only intercept external HTTP/HTTPS network calls
                if (uri.Scheme != "http" && uri.Scheme != "https") return;

                // Determine target extension ID from parameter, document source, or request origin
                string extId = extensionId;
                if (string.IsNullOrWhiteSpace(extId) && !string.IsNullOrWhiteSpace(wv.Source) &&
                    wv.Source.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                {
                    try { extId = new Uri(wv.Source).Host; } catch { }
                }

                if (string.IsNullOrWhiteSpace(extId) && e.Request.Headers.Contains("Origin"))
                {
                    string orig = e.Request.Headers.GetHeader("Origin");
                    if (orig.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                    {
                        try { extId = new Uri(orig).Host; } catch { }
                    }
                }

                // If not initiated by an extension, do not alter standard web page traffic
                if (string.IsNullOrWhiteSpace(extId) && !uri.Host.Contains("ondigitalocean.app") && !uri.AbsolutePath.Contains("/api/identify")) return;

                // Resolve internal WebView2 ID to authentic Store ID (e.g. jfcgghdcppnkehglmddcoeknbamleaan)
                string targetStoreId = ResolveStoreId(extId);
                if (string.IsNullOrWhiteSpace(targetStoreId) || targetStoreId.Length != 32 || uri.Host.Contains("ondigitalocean.app"))
                {
                    if (uri.Host.Contains("ondigitalocean.app") || uri.AbsolutePath.Contains("/api/identify"))
                    {
                        targetStoreId = "jfcgghdcppnkehglmddcoeknbamleaan";
                    }
                }

                string reqMethod = e.Request.Method;
                string reqUri = e.Request.Uri;
                var env = wv.Environment;

                // Handle CORS preflight OPTIONS requests immediately on UI thread
                if (reqMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    var emptyStream = new MemoryStream();
                    string corsHeaders =
                        $"Access-Control-Allow-Origin: chrome-extension://{targetStoreId}\r\n" +
                        "Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS, HEAD, PATCH\r\n" +
                        "Access-Control-Allow-Headers: *\r\n" +
                        "Access-Control-Allow-Credentials: true\r\n" +
                        "Access-Control-Max-Age: 86400\r\n";

                    e.Response = env.CreateWebResourceResponse(emptyStream, 200, "OK", corsHeaders);
                    return;
                }

                // Synchronously extract all request headers and content on the UI thread to prevent RPC_E_WRONG_THREAD
                var copiedHeaders = new List<KeyValuePair<string, string>>();
                string? contentType = null;
                foreach (var h in e.Request.Headers)
                {
                    if (h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        contentType = h.Value;
                        continue;
                    }
                    if (h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase) ||
                        h.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase) ||
                        h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                        h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    copiedHeaders.Add(new(h.Key, h.Value));
                }

                byte[]? bodyBytes = null;
                if (e.Request.Content != null)
                {
                    using var ms = new MemoryStream();
                    e.Request.Content.CopyTo(ms);
                    bodyBytes = ms.ToArray();
                }

                // Proxy external requests via HttpClient with valid extension Origin and Referer
                var deferral = e.GetDeferral();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var reqMsg = new HttpRequestMessage(new HttpMethod(reqMethod), reqUri);

                        foreach (var kvp in copiedHeaders)
                        {
                            reqMsg.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                        }

                        // Explicitly set the Extension Origin and Referer using official Store ID
                        reqMsg.Headers.TryAddWithoutValidation("Origin", $"chrome-extension://{targetStoreId}");
                        reqMsg.Headers.TryAddWithoutValidation("Referer", $"chrome-extension://{targetStoreId}/");

                        // Forward request body if present
                        if (bodyBytes != null && bodyBytes.Length > 0)
                        {
                            var byteContent = new ByteArrayContent(bodyBytes);
                            if (!string.IsNullOrEmpty(contentType))
                            {
                                byteContent.Headers.TryAddWithoutValidation("Content-Type", contentType);
                            }
                            reqMsg.Content = byteContent;
                        }

                        // Execute the request via .NET HttpClient (unconstrained by browser origin sandboxing)
                        var resMsg = await _httpClient.SendAsync(reqMsg).ConfigureAwait(false);
                        var resBytes = await resMsg.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                        // Build clean response headers with permissive CORS
                        var sb = new StringBuilder();
                        foreach (var h in resMsg.Headers)
                        {
                            if (h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)) continue;

                            sb.Append($"{h.Key}: {string.Join(", ", h.Value)}\r\n");
                        }
                        foreach (var h in resMsg.Content.Headers)
                        {
                            if (h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                            if (h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;

                            sb.Append($"{h.Key}: {string.Join(", ", h.Value)}\r\n");
                        }

                        sb.Append($"Access-Control-Allow-Origin: chrome-extension://{targetStoreId}\r\n");
                        sb.Append("Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS, HEAD, PATCH\r\n");
                        sb.Append("Access-Control-Allow-Headers: *\r\n");
                        sb.Append("Access-Control-Allow-Credentials: true\r\n");

                        int statusCode = (int)resMsg.StatusCode;
                        string reason = resMsg.ReasonPhrase ?? "OK";
                        string headersStr = sb.ToString();

                        LogService.Write("EXT_PROXY", $"Proxied {reqMethod} {reqUri} ({targetStoreId}) -> {statusCode}");

                        // Assign response on the UI thread and complete the deferral
                        var app = System.Windows.Application.Current;
                        Action assignResponse = () =>
                        {
                            try
                            {
                                var resStream = new MemoryStream(resBytes);
                                e.Response = env.CreateWebResourceResponse(resStream, statusCode, reason, headersStr);
                            }
                            catch (Exception ex)
                            {
                                LogService.Write("EXT_PROXY", $"CreateWebResourceResponse failed: {ex.Message}");
                            }
                            finally
                            {
                                deferral.Complete();
                            }
                        };

                        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                        {
                            app.Dispatcher.Invoke(assignResponse);
                        }
                        else
                        {
                            assignResponse();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Write("EXT_PROXY", $"Proxy request execution error: {ex.Message}");
                        try { deferral.Complete(); } catch { }
                    }
                });
            }
            catch
            {
            }
        };
    }
}
