using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Horizon.Stealth.Services;

/// <summary>
/// Image context-menu helpers: full-resolution background fetch + clipboard copy,
/// with a small non-blocking "please wait" popup when the work takes noticeable time.
/// </summary>
public static class ImageActionService
{
    private static readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private const int SlowThresholdMs = 1200;

    // ── Public entry points ─────────────────────────────────────────────────

    /// <summary>
    /// Invisibly loads the image at its full resolution (never touches the visible page),
    /// then copies it to the clipboard. Shows a wait popup only if it takes a while.
    /// </summary>
    public static async Task CopyFullResolutionAsync(CoreWebView2 core, string imageUri, string pageUrl)
    {
        await RunWithWaitPopupAsync("Copying full-resolution image\u2026", async () =>
        {
            byte[]? bytes = await FetchImageBytesAsync(core, imageUri, pageUrl);
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("The image could not be loaded.");
            await CopyBytesToClipboardAsync(bytes);
        }, "Image copied to clipboard.");
    }

    /// <summary>Copies an already-downloaded image file to the clipboard.</summary>
    public static async Task CopyFileToClipboardAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        await CopyBytesToClipboardAsync(bytes);
    }

    public static async Task RunWithWaitPopupAsync(string waitMessage, Func<Task> work, string? doneMessage = null)
    {
        StatusPopup? popup = null;
        using var cts = new CancellationTokenSource();
        var delay = Task.Delay(SlowThresholdMs, cts.Token).ContinueWith(_ =>
        {
            if (cts.IsCancellationRequested) return;
            Application.Current?.Dispatcher.Invoke(() => popup = StatusPopup.Show(waitMessage + "\nPlease wait\u2026"));
        }, TaskScheduler.Default);

        try
        {
            await work();
            cts.Cancel();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                popup?.Close();
                if (popup != null && doneMessage != null) StatusPopup.Flash(doneMessage);
            });
        }
        catch (Exception ex)
        {
            cts.Cancel();
            LogService.Write("MEDIA", $"Image action failed: {ex.GetType().Name}: {ex.Message}");
            Application.Current?.Dispatcher.Invoke(() =>
            {
                popup?.Close();
                StatusPopup.Flash("Image action failed: " + ex.Message, 3500);
            });
        }
    }

    // ── Fetching ────────────────────────────────────────────────────────────

    private static async Task<byte[]?> FetchImageBytesAsync(CoreWebView2 core, string uri, string pageUrl)
    {
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = uri.IndexOf(',');
            if (comma < 0) return null;
            string meta = uri.Substring(0, comma);
            string payload = uri.Substring(comma + 1);
            return meta.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(Uri.UnescapeDataString(payload))
                : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }

        if (uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            string js = "(async()=>{const r=await fetch(" + JsonSerializer.Serialize(uri) + ");const b=await r.blob();" +
                        "return await new Promise(res=>{const f=new FileReader();f.onload=()=>res(f.result);f.readAsDataURL(b);});})()";
            string raw = await core.ExecuteScriptAsync(js);
            string? dataUrl = JsonSerializer.Deserialize<string>(raw);
            return string.IsNullOrEmpty(dataUrl) ? null : await FetchImageBytesAsync(core, dataUrl, pageUrl);
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("User-Agent", core.Settings.UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) &&
            (pageUri.Scheme == "http" || pageUri.Scheme == "https"))
            req.Headers.Referrer = pageUri;

        try
        {
            var cookies = await core.CookieManager.GetCookiesAsync(uri);
            if (cookies.Count > 0)
                req.Headers.TryAddWithoutValidation("Cookie",
                    string.Join("; ", cookies.ConvertAll(c => $"{c.Name}={c.Value}")));
        }
        catch { /* cookies are best-effort */ }

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }

    // ── Clipboard ───────────────────────────────────────────────────────────

    private static Task CopyBytesToClipboardAsync(byte[] bytes)
    {
        var dispatcher = Application.Current.Dispatcher;
        return dispatcher.InvokeAsync(() =>
        {
            BitmapSource bmp;
            try
            {
                var ms = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                bmp = decoder.Frames[0];
                bmp.Freeze();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("This image format can't be copied as a picture (" + ex.Message + ").");
            }

            var data = new DataObject();
            data.SetImage(bmp);
            using (var png = new MemoryStream())
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(png);
                data.SetData("PNG", new MemoryStream(png.ToArray()));
            }
            Clipboard.SetDataObject(data, true);
        }).Task;
    }

    // ── Small status popup ──────────────────────────────────────────────────

    private sealed class StatusPopup : Window
    {
        private StatusPopup(string text)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x12, 0x14, 0x19)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 12, 18, 12),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 340,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                Left = area.Left + (area.Width - ActualWidth) / 2;
                Top = area.Bottom - ActualHeight - 48;
            };
        }

        public static StatusPopup Show(string text)
        {
            var p = new StatusPopup(text);
            ((Window)p).Show();
            return p;
        }

        public static void Flash(string text, int ms = 1600)
        {
            var p = Show(text);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, _) => { t.Stop(); try { p.Close(); } catch { } };
            t.Start();
        }
    }

    public static void FlashStatus(string text, int ms = 1600)
    {
        try
        {
            Application.Current?.Dispatcher?.Invoke(() => StatusPopup.Flash(text, ms));
        }
        catch { }
    }
}
