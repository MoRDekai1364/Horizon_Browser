using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Horizon.Stealth.Services;
using System.Threading.Tasks;

namespace Horizon.Stealth.Core;

public static class StealthEnvironment
{
    public static CoreWebView2Environment? Instance { get; private set; }
    private static Task?       _initTask = null;
    private static bool        _initFailed = false;
    private static readonly object _initLock = new();

    public const string GlobalUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private const string SCRIPT_YOUTUBE_INTERVENTION = @"
        (function() {
            if (!window.location.hostname.includes('youtube.com') || window.location.hostname.includes('music.youtube.com')) return;
            
            setInterval(() => {
                const adShowing = document.querySelector('.ad-showing') || document.querySelector('.ad-interrupting');
                const video = document.querySelector('video');
                
                if (adShowing && video) {
                    video.playbackRate = 16.0;
                    video.muted = true;
                    
                    const skipBtn = document.querySelector('.ytp-ad-skip-button') || document.querySelector('.videoAdUiSkipButton');
                    if (skipBtn) skipBtn.click();
                }
            }, 250);

            setInterval(() => {
                const enforcement = document.querySelector('ytd-enforcement-message-view-model') 
                                 || document.querySelector('[test-id=""enforcement-message-view-model""]');
                
                if (enforcement && document.querySelector('video')?.paused) {
                    window.location.reload(); 
                }
            }, 1000); 
        })();
    ";

    private const string SCRIPT_YOUTUBE_SCROLL_FIX = @"
        (function() {
            if (!window.location.hostname.includes('youtube.com') || window.location.hostname.includes('music.youtube.com')) return;

            const CSS = `
                ytd-rich-item-renderer,
                ytd-video-renderer,
                ytd-compact-video-renderer,
                ytd-shelf-renderer,
                ytd-reel-shelf-renderer {
                    contain: layout style;
                }
                #primary, ytd-browse, #page-manager {
                    will-change: scroll-position;
                }
            `;

            const inject = () => {
                if (document.getElementById('hz-scroll-fix')) return;
                const s = document.createElement('style');
                s.id = 'hz-scroll-fix';
                s.textContent = CSS;
                (document.head || document.documentElement).appendChild(s);
            };

            document.readyState === 'loading'
                ? document.addEventListener('DOMContentLoaded', inject)
                : inject();

            const orig = history.pushState;
            history.pushState = function() {
                orig.apply(this, arguments);
                setTimeout(inject, 600);
            };
        })();
    ";

    private const string SCRIPT_STEALTH_CLOAK = @"
        (function() {
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            
            const shift = { r: Math.floor(Math.random() * 10) - 5, g: Math.floor(Math.random() * 10) - 5, b: Math.floor(Math.random() * 10) - 5 };
            
            const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
            HTMLCanvasElement.prototype.toDataURL = function() {
                const ctx = this.getContext('2d');
                if (ctx) {
                    const imageData = ctx.getImageData(0, 0, 1, 1); 
                }
                return originalToDataURL.apply(this, arguments);
            };
        })();
    ";

    /// <summary>
    /// Idempotent — all callers await the same underlying Task.
    /// Safe to call concurrently from multiple BrowserView instances.
    /// </summary>
    public static Task InitializeAsync()
    {
        if (Instance != null) return Task.CompletedTask;
        lock (_initLock)
        {
            if (_initFailed || _initTask == null)
            {
                _initFailed = false;
                _initTask = InitializeCoreAsync();
            }
        }
        return _initTask;
    }

    private static async Task InitializeCoreAsync()
    {
        try
        {
            LogService.Write("ENGINE", "Initializing Stealth Environment...");

            var args = new StringBuilder();
            
            args.Append("--disable-blink-features=AutomationControlled ");
            args.Append("--disable-infobars ");
            args.Append("--disable-background-timer-throttling ");
            args.Append("--autoplay-policy=no-user-gesture-required ");
            // GPU compositor flags (VizDisplayCompositor, UseSkiaRenderer, enable-gpu-rasterization,
            // enable-zero-copy) all omitted — they cause BrowserProcessExited on certain GPU/driver configs.
            
            // NOTE: --load-extension conflicts with Profile.AddBrowserExtensionAsync.
            // SponsorBlock should be placed in the Extensions folder instead.

            if (!string.IsNullOrWhiteSpace(SettingsService.Current.NextDnsId))
            {
                var dnsUrl = $"https://dns.nextdns.io/{SettingsService.Current.NextDnsId}";
                args.Append($"--dns-over-https-mapping=https://{dnsUrl} ");
                args.Append($"--dns-over-https-templates={dnsUrl} ");
            }

            args.Append("--disable-gpu ");
            args.Append("--disable-software-rasterizer ");

            var options = new CoreWebView2EnvironmentOptions(args.ToString(), null, null)
            {
                // Required for Profile.AddBrowserExtensionAsync and Chrome Web Store
                AreBrowserExtensionsEnabled = true
            };

            Instance = await CoreWebView2Environment.CreateAsync(
                userDataFolder: ConfigService.UserDataRoot, 
                options: options
            );

            LogService.Write("ENGINE", "WebView2 Environment Created.");
        }
        catch (Exception ex)
        {
            lock (_initLock) { _initFailed = true; }
            LogService.RecordCrash(ex, "StealthEnvironment.Initialize");
            MessageBox.Show("Fatal Engine Error: " + ex.Message);
        }
    }

    private const string SCRIPT_HIDE_SPONSORED_RESULTS = @"
        (function() {
            var host = window.location.hostname;

            var engineSelectors = {
                'google.': ['div[data-text-ad]', '.uEierd', '.commercial-unit-desktop-top', '.commercial-unit-desktop-bottom', ""div[aria-label='Ads']"", ""div[aria-label='Sponsored']""],
                'bing.': ['li.b_ad', '.b_adTop', '.b_adBottom', '.b_ad'],
                'duckduckgo.': [""[data-testid='ad']"", '.badge--ad', '.result--ad'],
                'yahoo.': ['.searchCenterTopAds', '.searchRightAds', '.compTopAds', '.compBottomAds'],
                'search.brave.': ['.ad-result', ""[data-type='ad']""],
                'ecosia.': ['.result-ad', '.ad-item']
            };

            var selectors = [];
            for (var key in engineSelectors) {
                if (host.indexOf(key) !== -1) { selectors = engineSelectors[key]; break; }
            }

            var injectCss = function() {
                if (selectors.length === 0) return;
                if (document.getElementById('hz-sponsor-hider')) return;
                var css = selectors.map(function(s) { return s + ' { display: none !important; }'; }).join('\n');
                var s = document.createElement('style');
                s.id = 'hz-sponsor-hider';
                s.textContent = css;
                (document.head || document.documentElement).appendChild(s);
            };

            var AD_LABELS = {
                ad: 1, ads: 1, sponsored: 1, advertisement: 1, promoted: 1,
                'спонсорирано': 1, 'реклама': 1, 'платена реклама': 1,
                'anuncio': 1, 'publicidad': 1, 'patrocinado': 1,
                'anzeige': 1, 'gesponsert': 1, 'werbung': 1,
                'annonce': 1, 'publicité': 1, 'sponsorisé': 1,
                'sponsorizzato': 1, 'pubblicità': 1,
                'reclama': 1, 'sponsorizat': 1
            };

            var findContainer = function(labelEl) {
                var node = labelEl;
                var candidate = labelEl;
                for (var i = 0; i < 8; i++) {
                    if (!node.parentElement) break;
                    node = node.parentElement;
                    var rect = node.getBoundingClientRect();
                    var linkCount = node.querySelectorAll('a').length;
                    if (rect.height > 700 || linkCount > 10) break;
                    candidate = node;
                }
                return candidate;
            };

            var hideByLabel = function() {
                var nodes = document.querySelectorAll('span, div, a, label, h1, h2, h3, b, strong');
                for (var i = 0; i < nodes.length; i++) {
                    var el = nodes[i];
                    if (el.dataset && el.dataset.hzHidden) continue;
                    var text = (el.textContent || '').trim().toLowerCase();
                    if (text.length > 0 && text.length <= 24 && AD_LABELS[text]) {
                        var container = findContainer(el);
                        if (container) {
                            container.style.setProperty('display', 'none', 'important');
                            if (container.dataset) container.dataset.hzHidden = '1';
                        }
                    }
                }
            };

            var runPass = function() {
                injectCss();
                hideByLabel();
            };

            var debounceTimer = null;
            var scheduleRun = function() {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(runPass, 200);
            };

            document.readyState === 'loading'
                ? document.addEventListener('DOMContentLoaded', runPass)
                : runPass();

            var observer = new MutationObserver(scheduleRun);
            var startObserving = function() {
                if (document.body) observer.observe(document.body, { childList: true, subtree: true });
                else setTimeout(startObserving, 100);
            };
            startObserving();

            var origPush = history.pushState;
            history.pushState = function() {
                origPush.apply(this, arguments);
                setTimeout(runPass, 300);
            };
        })();
    ";

    public static async Task ApplyStealthStrategies(CoreWebView2 webView)
    {
        try
        {
            webView.Settings.UserAgent = GlobalUserAgent;
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_YOUTUBE_INTERVENTION);
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_YOUTUBE_SCROLL_FIX);
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_STEALTH_CLOAK);

            if (SettingsService.Current.HideSponsoredResults)
                await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_HIDE_SPONSORED_RESULTS);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ApplyStealthStrategies");
        }
    }
}