using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Horizon.Stealth.Services;
using System.Threading.Tasks;

namespace Horizon.Stealth.Core;

public static class HorizonEnvironment
{
    public static CoreWebView2Environment? Instance { get; private set; }
    private static Task?       _initTask = null;
    private static bool        _initFailed = false;
    private static readonly object _initLock = new();

    public static readonly string GlobalUserAgent = BuildGlobalUserAgent();

    private static string BuildGlobalUserAgent()
    {
        try
        {
            string? ver = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrWhiteSpace(ver))
            {
                int dot = ver.IndexOf('.');
                string major = dot > 0 ? ver[..dot] : ver;
                if (int.TryParse(major, out int m) && m >= 120)
                    return $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{m}.0.0.0 Safari/537.36";
            }
        }
        catch { }
        return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";
    }

    private const string SCRIPT_YOUTUBE_INTERVENTION = @"
        (function() {
            if (window.__horizonScriptDisabled) return;
            if (!window.location.hostname.includes('youtube.com') || window.location.hostname.includes('music.youtube.com')) return;
            
            setInterval(() => {
                if (window.__horizonScriptDisabled || document.hidden) return;
                const adShowing = document.querySelector('.ad-showing') || document.querySelector('.ad-interrupting');
                if (!adShowing) return;
                const skipBtn = document.querySelector('.ytp-ad-skip-button, .ytp-skip-ad-button, .ytp-ad-skip-button-modern, .videoAdUiSkipButton');
                if (skipBtn) {
                    try { skipBtn.click(); } catch(e) {}
                }
            }, 600);
        })();
    ";

    private const string SCRIPT_YOUTUBE_SCROLL_FIX = @"
        (function() {
            if (window.__horizonScriptDisabled) return;
        })();
    ";

    private const string SCRIPT_TIKTOK_INTERVENTION = @"
        (function() {
            if (window.__horizonScriptDisabled) return;
            if (!window.location.hostname.includes('tiktok.com')) return;

            const origPlay = HTMLMediaElement.prototype.play;
            HTMLMediaElement.prototype.play = function() {
                if (!window.__hzUserInteracted && !window.__horizonScriptDisabled) {
                    try { this.pause(); } catch(e) {}
                    return Promise.resolve();
                }
                return origPlay.apply(this, arguments);
            };

            const blockAutoplay = (e) => {
                if (window.__horizonScriptDisabled || window.__hzUserInteracted) return;
                if (e.target && e.target.tagName === 'VIDEO') {
                    try { e.target.pause(); } catch(err) {}
                }
            };
            document.addEventListener('play', blockAutoplay, true);
            document.addEventListener('playing', blockAutoplay, true);

            const markTrustedInteraction = (e) => {
                if (e && e.isTrusted) {
                    window.__hzUserInteracted = true;
                }
            };
            document.addEventListener('pointerdown', markTrustedInteraction, true);
            document.addEventListener('keydown', markTrustedInteraction, true);
            document.addEventListener('wheel', markTrustedInteraction, { capture: true, passive: true });

            const CSS = `
                #login-modal,
                [id*=""login-modal""],
                [id*=""loginContainer""],
                [class*=""DivLoginContainer""],
                [class*=""DivModalContainer""],
                [class*=""DivLoginModal""],
                [class*=""DivLoginWall""],
                [class*=""DivLoginGate""],
                [class*=""DivGuestMode""],
                [class*=""login-modal-mask""],
                [class*=""tiktok-modal__mask""],
                [class*=""DivMask-StyledMask""],
                [class*=""DivModalMask""],
                [class*=""DivModalWrap""],
                [class*=""DivPopupContainer""],
                [class*=""DivBannerContainer""],
                [data-floating-ui-portal],
                #tiktok-portal-container,
                [data-e2e=""feed-follow-popup""],
                [data-e2e=""login-modal""] {
                    display: none !important;
                    visibility: hidden !important;
                    opacity: 0 !important;
                    pointer-events: none !important;
                }
                html, body, #app, #main-content-homepage_hot {
                    overflow: auto !important;
                    pointer-events: auto !important;
                }
            `;

            const injectStyle = () => {
                if (window.__horizonScriptDisabled) return;
                if (document.getElementById('hz-tiktok-bypass')) return;
                const s = document.createElement('style');
                s.id = 'hz-tiktok-bypass';
                s.textContent = CSS;
                (document.head || document.documentElement).appendChild(s);
            };

            const unlockScrollAndDismiss = () => {
                if (window.__horizonScriptDisabled) return;
                const closeBtn = document.querySelector('[data-e2e=""modal-close-inner-button""]')
                              || document.querySelector('[data-e2e=""modal-close-icon""]')
                              || document.querySelector('[class*=""DivCloseWrapper""]')
                              || document.querySelector('div[role=""dialog""] button[aria-label=""Close""]')
                              || document.querySelector('button[aria-label=""Close""]');
                if (closeBtn && !closeBtn.dataset.hzDismissed) {
                    closeBtn.dataset.hzDismissed = 'true';
                    try { closeBtn.click(); } catch(e) {}
                }

                const appRoot = document.getElementById('app');
                if (appRoot) {
                    if (appRoot.hasAttribute('inert')) appRoot.removeAttribute('inert');
                    if (appRoot.getAttribute('aria-hidden') === 'true') appRoot.removeAttribute('aria-hidden');
                }

                if (!window.__hzUserInteracted) {
                    const videos = document.querySelectorAll('video');
                    for (let i = 0; i < videos.length; i++) {
                        const v = videos[i];
                        if (v.hasAttribute('autoplay')) v.removeAttribute('autoplay');
                        if (!v.paused) {
                            try { v.pause(); } catch(e) {}
                        }
                    }
                }
            };

            document.readyState === 'loading'
                ? document.addEventListener('DOMContentLoaded', () => { injectStyle(); unlockScrollAndDismiss(); })
                : (injectStyle(), unlockScrollAndDismiss());

            let _timer = null;
            const debouncedCheck = () => {
                if (_timer || window.__horizonScriptDisabled) return;
                _timer = setTimeout(() => {
                    _timer = null;
                    unlockScrollAndDismiss();
                }, 400);
            };

            if (window.MutationObserver) {
                const observer = new MutationObserver(debouncedCheck);
                const root = document.body || document.documentElement;
                if (root) observer.observe(root, { childList: true, subtree: true });
            }
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

    private static void StartVpnAutoConnect()
    {
        var s = SettingsService.Current;
        if (!s.VpnAutoConnect) return;

        var profile = VpnProfileStore.GetById(s.VpnActiveProfileId);
        if (profile == null)
        {
            LogService.Write("ENGINE", "VPN auto-connect skipped: no active profile stored.");
            return;
        }

        var connectTask = VpnRelayService.ConnectAsync(profile);
        _ = connectTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                LogService.RecordCrash(t.Exception!, "VPN auto-connect");
            else
                LogService.Write("ENGINE", t.Result.Ok
                    ? $"VPN auto-connect OK: {t.Result.Message}"
                    : $"VPN auto-connect failed: {t.Result.Message}");
        });
    }

    private static async Task InitializeCoreAsync()
    {
        try
        {
            LogService.Write("ENGINE", "Initializing Stealth Environment...");

            var args = new StringBuilder();
            
            args.Append("--disable-blink-features=AutomationControlled ");
            if (SettingsService.Current.EnableRemoteDebugging)
            {
                int debugPort = SettingsService.Current.RemoteDebuggingPort > 0
                    ? SettingsService.Current.RemoteDebuggingPort
                    : 9222;
                args.Append($"--remote-debugging-port={debugPort} --remote-allow-origins=* ");
            }
            if (SettingsService.Current.RelaxSameSiteCookies)
                args.Append("--disable-features=SameSiteByDefaultCookies,CookiesWithoutSameSiteMustBeSecure ");
            args.Append("--disable-infobars ");
            if (!SettingsService.Current.EnableBackgroundTimerThrottling)
                args.Append("--disable-background-timer-throttling ");
            args.Append("--autoplay-policy=no-user-gesture-required ");
            // Auto-grant media streams and screen capture for extensions (audio/video recording, song identification)
            args.Append("--use-fake-ui-for-media-stream --enable-usermedia-screen-capturing --allow-http-screen-capture ");
            // GPU compositor flags (VizDisplayCompositor, UseSkiaRenderer, enable-gpu-rasterization,
            // enable-zero-copy) all omitted — they cause BrowserProcessExited on certain GPU/driver configs.
            
            // NOTE: --load-extension conflicts with Profile.AddBrowserExtensionAsync.
            // SponsorBlock should be placed in the Extensions folder instead.

            if (SettingsService.Current.VpnEnabled)
            {
                if (VpnRelayService.Start())
                {
                    args.Append($"--proxy-server=http://127.0.0.1:{VpnRelayService.Port} ");
                    if (SettingsService.Current.VpnBlockWebRtcLeak)
                        args.Append("--force-webrtc-ip-handling-policy=disable_non_proxied_udp ");
                    LogService.Write("ENGINE", $"VPN relay attached on 127.0.0.1:{VpnRelayService.Port}");
                    if (!VpnRelayService.IsPeer) StartVpnAutoConnect();
                }
                else if (SettingsService.Current.VpnKillSwitchEnabled)
                {
                    args.Append("--proxy-server=http://127.0.0.1:1 ");
                    LogService.Write("ENGINE", "VPN relay failed to start and kill switch is ON: browser traffic blocked via dead proxy.");
                }
                else
                {
                    LogService.Write("ENGINE", "VPN relay failed to start; browsing continues without VPN routing.");
                }
            }

            if (SettingsService.Current.FingerprintSpoofEnabled &&
                !(SettingsService.Current.VpnEnabled && SettingsService.Current.VpnBlockWebRtcLeak))
                args.Append("--force-webrtc-ip-handling-policy=default_public_interface_only ");

            if (!string.IsNullOrWhiteSpace(SettingsService.Current.NextDnsId))
            {
                var dnsUrl = $"https://dns.nextdns.io/{SettingsService.Current.NextDnsId}";
                args.Append($"--dns-over-https-mapping=https://{dnsUrl} ");
                args.Append($"--dns-over-https-templates={dnsUrl} ");
            }

            // GPU intentionally left enabled — disabling it breaks rendering for
            // GPU-composited content (canvas/WebGL), e.g. YouTube Music's player background.

            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--test-renderer-limit-2", StringComparison.OrdinalIgnoreCase)))
            {
                args.Append("--renderer-process-limit=2 ");
                LogService.Write("DIAGNOSTIC", "Diagnostic launch override applied: --renderer-process-limit=2");
            }

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
            LogService.RecordCrash(ex, "HorizonEnvironment.Initialize");
            MessageBox.Show("Fatal Engine Error: " + ex.Message);
        }
    }

    public static async Task SyncDevToolsActivePortAsync()
    {
        if (!SettingsService.Current.EnableRemoteDebugging) return;
        try
        {
            int port = SettingsService.Current.RemoteDebuggingPort > 0
                ? SettingsService.Current.RemoteDebuggingPort
                : 9222;

            string horizonActivePortFile = Path.Combine(ConfigService.UserDataRoot, "DevToolsActivePort");
            string? content = null;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (File.Exists(horizonActivePortFile))
                {
                    try
                    {
                        string existing = File.ReadAllText(horizonActivePortFile).Trim();
                        if (!string.IsNullOrEmpty(existing))
                        {
                            content = existing + "\n";
                            break;
                        }
                    }
                    catch { }
                }

                try
                {
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                    string json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version");
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsProp))
                    {
                        string? wsUrl = wsProp.GetString();
                        if (!string.IsNullOrWhiteSpace(wsUrl) && Uri.TryCreate(wsUrl, UriKind.Absolute, out var wsUri))
                        {
                            content = $"{port}\n{wsUri.PathAndQuery}\n";
                            try { File.WriteAllText(horizonActivePortFile, content); } catch { }
                            break;
                        }
                    }
                }
                catch { }

                await Task.Delay(300);
            }

            if (!string.IsNullOrEmpty(content))
            {
                string chromeUserDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data");
                Directory.CreateDirectory(chromeUserDataDir);
                string chromeActivePortFile = Path.Combine(chromeUserDataDir, "DevToolsActivePort");
                File.WriteAllText(chromeActivePortFile, content);
                LogService.Write("ENGINE", $"Synced DevToolsActivePort on port {port}.");
            }
        }
        catch (Exception ex)
        {
            LogService.Write("ENGINE", $"SyncDevToolsActivePort warning: {ex.Message}");
        }
    }

    private const string SCRIPT_HIDE_SPONSORED_RESULTS = @"
        (function() {
            if (window.__horizonScriptDisabled) return;
            if (window.location.hostname.includes('music.youtube.com')) return;
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
            if (selectors.length === 0) return;

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
            if (SettingsService.Current.FingerprintSpoofEnabled)
                await FingerprintProfile.RegisterAsync(webView);

            // NOTE: CDP Target.setAutoAttach with waitForDebuggerOnStart:true on worker/shared_worker
            // targets is intentionally omitted — in Edge WebView2 (msedge.dll) it triggers a fatal
            // 0x80000003 STATUS_BREAKPOINT crash on DedicatedWorker thread (e.g. claude.ai,
            // bg.z-library.sk, tiktok.com, web.whatsapp.com, twitch.tv) and sets devtools_present=true.
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_YOUTUBE_INTERVENTION);
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_YOUTUBE_SCROLL_FIX);
            await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_TIKTOK_INTERVENTION);

            if (SettingsService.Current.HideSponsoredResults)
                await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_HIDE_SPONSORED_RESULTS);

            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--enable-frame-probe", StringComparison.OrdinalIgnoreCase)))
            {
                await webView.AddScriptToExecuteOnDocumentCreatedAsync(SCRIPT_DIAGNOSTIC_FRAME_PROBE);
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ApplyStealthStrategies");
        }
    }

    private const string SCRIPT_DIAGNOSTIC_FRAME_PROBE = @"
        (function() {
            if (window.__hzFrameProbeInjected) return;
            window.__hzFrameProbeInjected = true;
            let lastT = performance.now();
            let frameDeltas = [];
            let longTaskCount = 0;

            if (window.PerformanceObserver) {
                try {
                    const po = new PerformanceObserver((list) => {
                        longTaskCount += list.getEntries().length;
                    });
                    po.observe({ entryTypes: ['longtask'] });
                } catch(e) {}
            }

            function onFrame(now) {
                const delta = now - lastT;
                lastT = now;
                frameDeltas.push(delta);
                if (frameDeltas.length > 6000) frameDeltas.shift();
                requestAnimationFrame(onFrame);
            }
            requestAnimationFrame(onFrame);

            window.__hzGetFrameMetrics = function() {
                if (frameDeltas.length === 0) return { p50: 0, p99: 0, longTasks: longTaskCount, sampleCount: 0 };
                const sorted = [...frameDeltas].sort((a,b) => a - b);
                const p50 = sorted[Math.floor(sorted.length * 0.5)];
                const p99 = sorted[Math.floor(sorted.length * 0.99)];
                return { p50: p50, p99: p99, longTasks: longTaskCount, sampleCount: frameDeltas.length };
            };
        })();
    ";
}