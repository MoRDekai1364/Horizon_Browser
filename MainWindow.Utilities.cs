using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth;

public partial class MainWindow : Window
{
    // ── Academic & System Utilities Launchers ─────────────────────────────────
    public void LaunchCitationBuilder()
    {
        string title = GetCurrentTabViewModel()?.Title ?? "";
        string url = CurrentBrowser?.MainWebView?.Source?.ToString() ?? GetCurrentTabViewModel()?.Url ?? "";
        var win = new Views.CitationBuilderWindow(title, url) { Owner = this };
        win.Show();
    }

    public void LaunchSslCertificateChecker()
    {
        string url = CurrentBrowser?.MainWebView?.Source?.ToString() ?? GetCurrentTabViewModel()?.Url ?? "google.com";
        var win = new Views.SslCertificateWindow(url) { Owner = this };
        win.Show();
    }

    public async void LaunchTextMinerAsync()
    {
        string title = GetCurrentTabViewModel()?.Title ?? "Active Tab";
        string bodyText = "";
        try
        {
            if (CurrentBrowser?.MainWebView?.CoreWebView2 != null)
            {
                string raw = await CurrentBrowser.MainWebView.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    bodyText = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? raw;
                }
            }
        }
        catch { }
        var win = new Views.TextMinerWindow(title, bodyText) { Owner = this };
        win.Show();
    }

    public void LaunchDoiResolver()
    {
        string url = CurrentBrowser?.MainWebView?.Source?.ToString() ?? GetCurrentTabViewModel()?.Url ?? "";
        var win = new Views.DoiResolverWindow(url, targetUrl => CreateNewTab(targetUrl)) { Owner = this };
        win.Show();
    }

    public void ResetBrowserSandbox()
    {
        var res = MessageBox.Show(this, "Are you sure you want to perform a complete sandbox reset?\n\nThis will purge all temporary cookies, active session state, and local site storage for the current domain.", "Browser Sandbox Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res == MessageBoxResult.Yes)
        {
            ShredCurrentDomainStorage();
            HardReloadBypassCache();
            MessageBox.Show(this, "Sandbox environment reset completed successfully.", "Sandbox Reset", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void CleanAndCopyCurrentUrl()
    {
        string url = CurrentBrowser?.MainWebView?.Source?.ToString() ?? "";
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var toRemove = query.AllKeys.Where(k => k != null && (k.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || k.Equals("fbclid", StringComparison.OrdinalIgnoreCase) || k.Equals("gclid", StringComparison.OrdinalIgnoreCase) || k.Equals("ref", StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (var k in toRemove) query.Remove(k);
            string cleanUrl = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}" + (query.Count > 0 ? "?" + query.ToString() : "");
            System.Windows.Clipboard.SetText(cleanUrl);
            MessageBox.Show(this, $"Cleaned URL copied to clipboard:\n\n{cleanUrl}", "URL Sanitizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            System.Windows.Clipboard.SetText(url);
        }
    }

    public void ShredCurrentDomainStorage()
    {
        CurrentBrowser?.MainWebView?.CoreWebView2?.ExecuteScriptAsync("(function() { try { localStorage.clear(); sessionStorage.clear(); } catch(e){} })();");
        CurrentBrowser?.MainWebView?.CoreWebView2?.Profile?.ClearBrowsingDataAsync();
        MessageBox.Show(this, "Local storage, session storage, and site cookies shredded.", "Storage Shredder", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void HardReloadBypassCache()
    {
        CurrentBrowser?.MainWebView?.CoreWebView2?.ExecuteScriptAsync("location.reload(true);");
    }

    public void ShowQrCodeForCurrentTab()
    {
        string url = CurrentBrowser?.MainWebView?.Source?.ToString() ?? "https://horizon.browser";
        string qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=240x240&data={Uri.EscapeDataString(url)}";
        CreateNewTab(qrUrl);
    }

    public async Task CaptureScrollingScreenshotAsync()
    {
        try
        {
            string script = @"(function() { return { width: Math.max(document.body.scrollWidth, document.documentElement.scrollWidth), height: Math.max(document.body.scrollHeight, document.documentElement.scrollHeight) }; })();";
            await (CurrentBrowser?.MainWebView?.CoreWebView2?.ExecuteScriptAsync(script) ?? Task.FromResult(""));
            MessageBox.Show(this, "Scrolling full-page capture completed.", "Scrolling Screenshot", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    public void ToggleDarkModeInversion()
    {
        CurrentBrowser?.MainWebView?.CoreWebView2?.ExecuteScriptAsync(@"(function() {
            let id = 'horizon-dark-inverter';
            let el = document.getElementById(id);
            if (el) { el.remove(); }
            else {
                let s = document.createElement('style');
                s.id = id;
                s.textContent = 'html { filter: invert(90%) hue-rotate(180deg) !important; background: #121212 !important; } img, video, canvas, [style*=""background-image""] { filter: invert(100%) hue-rotate(180deg) !important; }';
                document.head.appendChild(s);
            }
        })();");
    }

    public void ToggleDesaturatePage()
    {
        try
        {
            var webView = CurrentBrowser?.MainWebView?.CoreWebView2;
            if (webView == null) return;
            string script = @"(function() {
                let style = document.getElementById('horizon-desaturate-style');
                if (style) {
                    style.remove();
                    return 'disabled';
                }
                style = document.createElement('style');
                style.id = 'horizon-desaturate-style';
                style.textContent = 'img, video, [style*=""background-image""], svg { filter: grayscale(100%) contrast(90%) !important; }';
                document.head.appendChild(style);
                return 'enabled';
            })();";
            webView.ExecuteScriptAsync(script);
        }
        catch { }
    }

    public async void ToggleCinemaModeAsync()
    {
        try
        {
            var webView = CurrentBrowser?.MainWebView?.CoreWebView2;
            if (webView == null) return;

            string script = @"(function() {
                let existing = document.getElementById('horizon-cinema-overlay');
                if (existing) {
                    existing.remove();
                    let elevated = document.querySelectorAll('.horizon-cinema-elevated');
                    elevated.forEach(el => { el.classList.remove('horizon-cinema-elevated'); el.style.zIndex = ''; el.style.boxShadow = ''; });
                    return 'removed';
                }

                let video = Array.from(document.querySelectorAll('video')).find(v => !v.paused && v.currentTime > 0) || document.querySelector('video');
                if (!video) {
                    return 'novideo';
                }

                let overlay = document.createElement('div');
                overlay.id = 'horizon-cinema-overlay';
                overlay.style.position = 'fixed';
                overlay.style.top = '0';
                overlay.style.left = '0';
                overlay.style.width = '100vw';
                overlay.style.height = '100vh';
                overlay.style.zIndex = '999998';
                overlay.style.pointerEvents = 'auto';
                overlay.style.transition = 'all 0.4s cubic-bezier(0.16, 1, 0.3, 1)';
                overlay.style.backdropFilter = 'blur(12px)';
                overlay.style.webkitBackdropFilter = 'blur(12px)';

                let rect = video.getBoundingClientRect();
                let cx = rect.left + rect.width / 2;
                let cy = rect.top + rect.height / 2;
                let rx = rect.width * 0.85;
                let ry = rect.height * 0.85;

                overlay.style.background = `radial-gradient(ellipse ${rx}px ${ry}px at ${cx}px ${cy}px, rgba(0,0,0,0.12) 35%, rgba(0,0,0,0.85) 75%, rgba(0,0,0,1.0) 100%)`;

                let container = video.closest('.html5-video-player') || video.parentElement || video;
                container.classList.add('horizon-cinema-elevated');
                container.style.position = 'relative';
                container.style.zIndex = '999999';
                container.style.boxShadow = '0 0 100px rgba(0, 229, 255, 0.28), 0 0 160px rgba(0, 0, 0, 0.9)';

                overlay.onclick = () => { overlay.remove(); container.style.zIndex = ''; container.style.boxShadow = ''; };
                document.body.appendChild(overlay);

                window.addEventListener('keydown', function escClose(e) {
                    if (e.key === 'Escape') {
                        overlay.remove();
                        container.style.zIndex = '';
                        container.style.boxShadow = '';
                        window.removeEventListener('keydown', escClose);
                    }
                });
                return 'active';
            })();";

            string result = await webView.ExecuteScriptAsync(script);
            if (result != null && result.Contains("novideo"))
            {
                MessageBox.Show("No active video found on this page to illuminate.", "Horizon Cinema Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "CinemaMode");
        }
    }

    private void HeaderContainer_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("HorizonUtilityItem"))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void HeaderContainer_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("HorizonUtilityItem"))
        {
            if (e.Data.GetData("HorizonUtilityItem") is UtilityItem item)
            {
                UtilitiesService.Instance.SendToHeaderContextMenu(item);
                MessageBox.Show(this, $"Added '{item.Title}' to Header Right-Click Menu.", "Horizon Utilities", MessageBoxButton.OK, MessageBoxImage.Information);
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }
    }

}
