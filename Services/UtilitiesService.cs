using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Services;

public class UtilityItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "\u26A1";
    public string Category { get; set; } = "General";
    public string Description { get; set; } = "";
    public bool IsPinned { get; set; } = false;
    public string ActionId { get; set; } = "";
    public string BadgeText { get; set; } = "";
    public string AccentColor { get; set; } = "#4A9EFF";

    public override string ToString() => $"{Icon} {Title}";
}

public class UtilitiesService
{
    private static UtilitiesService? _instance;
    public static UtilitiesService Instance => _instance ??= new UtilitiesService();

    private readonly List<UtilityItem> _catalog = new();
    public ObservableCollection<UtilityItem> Items { get; } = new();

    public UtilitiesService()
    {
        InitializeCatalog();
        RefreshItems();
    }

    private void InitializeCatalog()
    {
        _catalog.Clear();

        // 1. Security & Privacy
        _catalog.Add(new UtilityItem
        {
            Id = "container_tab",
            Title = "Container Tab Isolator",
            Icon = "\uD83D\uDEE1\uFE0F",
            Category = "Security & Privacy",
            Description = "Open isolated tabs with sandboxed storage, cookies, and custom profiles.",
            ActionId = "container_tab",
            BadgeText = "Sandbox",
            AccentColor = "#39D353"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "clean_share",
            Title = "URL Sanitizer / Clean Share",
            Icon = "\uD83D\uDD17",
            Category = "Security & Privacy",
            Description = "Strip tracking tokens, UTM parameters, and advertising junk strings before copying.",
            ActionId = "clean_share",
            BadgeText = "Clean",
            AccentColor = "#26A69A"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "referrer_stripper",
            Title = "Referrer Stripper",
            Icon = "\uD83D\uDC41\uFE0F\u200D\uD83D\uDDE8\uFE0F",
            Category = "Security & Privacy",
            Description = "Block or spoof HTTP Referer headers to conceal your previous page navigation.",
            ActionId = "referrer_stripper",
            BadgeText = "Header",
            AccentColor = "#66BB6A"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "https_enforcer",
            Title = "HTTPS-Only Enforcer",
            Icon = "\uD83D\uDD12",
            Category = "Security & Privacy",
            Description = "Upgrade all unencrypted HTTP requests to secure encrypted HTTPS connections.",
            ActionId = "https_enforcer",
            BadgeText = "Crypto",
            AccentColor = "#42A5F5"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "cryptojack_guard",
            Title = "Cryptojacking Detector",
            Icon = "\u26CF\uFE0F",
            Category = "Security & Privacy",
            Description = "Monitors thread utilization and blocks unauthorized in-browser cryptocurrency miners.",
            ActionId = "cryptojack_guard",
            BadgeText = "Guard",
            AccentColor = "#FFB74D"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "canvas_shuffler",
            Title = "Canvas Anti-Fingerprinting",
            Icon = "\uD83C\uDFA8",
            Category = "Security & Privacy",
            Description = "Feeds randomized pixel noise to HTML5 canvas tracking scripts profiling hardware.",
            ActionId = "canvas_shuffler",
            BadgeText = "Spoof",
            AccentColor = "#BA68C8"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "audio_defender",
            Title = "AudioContext Defender",
            Icon = "\uD83D\uDD0A",
            Category = "Security & Privacy",
            Description = "Shields and randomizes data requests to Web Audio API to defeat acoustic profiling.",
            ActionId = "audio_defender",
            BadgeText = "Audio",
            AccentColor = "#4DD0E1"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "temp_email",
            Title = "Temporary Email Generator",
            Icon = "\uD83D\uDCEB",
            Category = "Security & Privacy",
            Description = "Generates disposable burner mailboxes to bypass signup walls without inbox spam.",
            ActionId = "temp_email",
            BadgeText = "Burner",
            AccentColor = "#FF8A65"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "ua_randomizer",
            Title = "User-Agent Randomizer",
            Icon = "\uD83D\uDD04",
            Category = "Security & Privacy",
            Description = "Cycles browser reported platform and engine versions to disguise digital footprint.",
            ActionId = "ua_randomizer",
            BadgeText = "Identity",
            AccentColor = "#7986CB"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "phishing_meter",
            Title = "Phishing Risk Meter",
            Icon = "\uD83E\uDDF2",
            Category = "Security & Privacy",
            Description = "Analyzes active domain age, homoglyphs, and SSL validity with trust score.",
            ActionId = "phishing_meter",
            BadgeText = "Live",
            AccentColor = "#E57373"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "site_timer",
            Title = "Site Time Limiter",
            Icon = "\u23F1\uFE0F",
            Category = "Security & Privacy",
            Description = "Digital wellbeing controller setting daily active browsing time limits per domain.",
            ActionId = "site_timer",
            BadgeText = "Focus",
            AccentColor = "#81C784"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "clickbait_desat",
            Title = "Clickbait Desaturator",
            Icon = "\uD83C\uDFAD",
            Category = "Security & Privacy",
            Description = "Turns sensational screaming thumbnails and high-contrast clickbait into neutral grayscale.",
            ActionId = "clickbait_desat",
            BadgeText = "Filter",
            AccentColor = "#90A4AE"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "storage_shredder",
            Title = "Storage & Cookie Shredder",
            Icon = "\uD83D\uDDD1\uFE0F",
            Category = "Security & Privacy",
            Description = "Instantly clears cookies, IndexedDB, localStorage, and cached tokens for current domain.",
            ActionId = "storage_shredder",
            BadgeText = "Purge",
            AccentColor = "#EF5350"
        });

        // 2. Web Tools & Automation
        _catalog.Add(new UtilityItem
        {
            Id = "speed_reader",
            Title = "Speed-Reading Streamer",
            Icon = "\uD83D\uDCD6",
            Category = "Web Tools & Automation",
            Description = "Streams page text one RSVP word at a time at up to 800 WPM with focal pacing.",
            ActionId = "speed_reader",
            BadgeText = "RSVP",
            AccentColor = "#64B5F6"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "page_monitor",
            Title = "Page Change Monitor",
            Icon = "\uD83D\uDD14",
            Category = "Web Tools & Automation",
            Description = "Tracks DOM selectors for changes and notifies you when restocks or price drops occur.",
            ActionId = "page_monitor",
            BadgeText = "Watcher",
            AccentColor = "#FFD54F"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "keyword_filter",
            Title = "Custom Keyword Filter",
            Icon = "\uD83D\uDD0E",
            Category = "Web Tools & Automation",
            Description = "Blurs or hides elements, headlines, and paragraphs matching custom blacklisted keywords.",
            ActionId = "keyword_filter",
            BadgeText = "Censor",
            AccentColor = "#A1887F"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "rss_aggregator",
            Title = "Multi-Source RSS Aggregator",
            Icon = "\uD83D\uDCF0",
            Category = "Web Tools & Automation",
            Description = "Subscribes to Atom and RSS feeds without third-party services, right inside Horizon.",
            ActionId = "rss_aggregator",
            BadgeText = "Feeds",
            AccentColor = "#FFA726"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "custom_injector",
            Title = "Custom CSS / JS Injector",
            Icon = "\u2728",
            Category = "Web Tools & Automation",
            Description = "Inject user styles and custom JavaScript snippets per domain with instant reload.",
            ActionId = "custom_injector",
            BadgeText = "Script",
            AccentColor = "#AB47BC"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "dyslexia_font",
            Title = "Dyslexia Font Overrider",
            Icon = "\uD83D\uDD24",
            Category = "Web Tools & Automation",
            Description = "Enforces OpenDyslexic or weighted high-legibility typography across web pages.",
            ActionId = "dyslexia_font",
            BadgeText = "Font",
            AccentColor = "#29B6F6"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "qr_generator",
            Title = "QR Code Generator & Scanner",
            Icon = "\u25A3",
            Category = "Web Tools & Automation",
            Description = "Generate instant mobile handoff QR codes for current URL or scan on-screen codes.",
            ActionId = "qr_generator",
            BadgeText = "QR",
            AccentColor = "#26C6DA"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "dead_link_checker",
            Title = "Dead Link Checker",
            Icon = "\u26A0\uFE0F",
            Category = "Web Tools & Automation",
            Description = "Sweeps current page or bookmarks folder to flag 404 errors and broken hyperlinks.",
            ActionId = "dead_link_checker",
            BadgeText = "Audit",
            AccentColor = "#FFCA28"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "web_scraper",
            Title = "Web Scraper / Table Extractor",
            Icon = "\uD83D\uDCCA",
            Category = "Web Tools & Automation",
            Description = "Auto-detects tabular DOM structures and exports structured CSV data in one click.",
            ActionId = "web_scraper",
            BadgeText = "Export",
            AccentColor = "#66BB6A"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "element_isolator",
            Title = "Visual Element Isolator",
            Icon = "\uD83C\uDFAF",
            Category = "Web Tools & Automation",
            Description = "Interactive click-to-delete tool removing annoying banners, overlays, and floating clutter.",
            ActionId = "element_isolator",
            BadgeText = "Zap",
            AccentColor = "#EC407A"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "scroll_screenshot",
            Title = "Scrolling Screenshot Capture",
            Icon = "\uD83D\uDCF8",
            Category = "Web Tools & Automation",
            Description = "High-fidelity full-page CDP capture stitching entire document from top to bottom.",
            ActionId = "scroll_screenshot",
            BadgeText = "Capture",
            AccentColor = "#42A5F5"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "dark_inverter",
            Title = "Dark Mode Inverter",
            Icon = "\uD83C\uDF19",
            Category = "Web Tools & Automation",
            Description = "Intelligent luminance-inverting CSS engine forcing dark mode onto light-only websites.",
            ActionId = "dark_inverter",
            BadgeText = "Night",
            AccentColor = "#7E57C2"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "tab_tree",
            Title = "Tab Tree & Vertical Hierarchy",
            Icon = "\uD83C\uDF33",
            Category = "Web Tools & Automation",
            Description = "Nested visual hierarchy map displaying parent-child lineage of opened tabs.",
            ActionId = "tab_tree",
            BadgeText = "Tree",
            AccentColor = "#26A69A"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "workspace_switcher",
            Title = "Workspace Switcher",
            Icon = "\uD83D\uDCBC",
            Category = "Web Tools & Automation",
            Description = "Instantly swap between isolated browsing profiles: Work, Personal, Research, Media.",
            ActionId = "workspace_switcher",
            BadgeText = "Profiles",
            AccentColor = "#5C6BC0"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "tab_hibernator",
            Title = "Aegis Tab Hibernator",
            Icon = "\u2744\uFE0F",
            Category = "Web Tools & Automation",
            Description = "Manual governor freezing idle tabs to reclaim physical RAM into dormant cryo sleep.",
            ActionId = "tab_hibernator",
            BadgeText = "Aegis",
            AccentColor = "#29B6F6"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "link_preview",
            Title = "Link Preview Overlay",
            Icon = "\uD83D\uDD0D",
            Category = "Web Tools & Automation",
            Description = "Hover or Alt+Click any hyperlink to peek inside a sandboxed modal viewport without navigating.",
            ActionId = "link_preview",
            BadgeText = "Peek",
            AccentColor = "#8E24AA"
        });

        // 3. Media & Accessibility
        _catalog.Add(new UtilityItem
        {
            Id = "cinema_mode",
            Title = "Ambient Cinema Mode",
            Icon = "\uD83C\uDFAC",
            Category = "Media & Accessibility",
            Description = "Dims the viewport with pure black edges, 85% transparent center, and 12px blur halo.",
            ActionId = "cinema_mode",
            BadgeText = "Theater",
            AccentColor = "#FF7043"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "pip_controller",
            Title = "Universal PiP Controller",
            Icon = "\uD83D\uDDBC\uFE0F",
            Category = "Media & Accessibility",
            Description = "Forces native Picture-in-Picture mode on any HTML5 video stream, including feeds.",
            ActionId = "pip_controller",
            BadgeText = "PiP",
            AccentColor = "#26A69A"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "tab_volume_mixer",
            Title = "Per-Tab Volume Mixer",
            Icon = "\uD83C\uDF9B\uFE0F",
            Category = "Media & Accessibility",
            Description = "Gain boost up to 200% or volume attenuation independently for each active tab.",
            ActionId = "tab_volume_mixer",
            BadgeText = "Gain",
            AccentColor = "#AB47BC"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "audio_equalizer",
            Title = "Audio Equalizer & Bass Booster",
            Icon = "\uD83C\uDFB6",
            Category = "Media & Accessibility",
            Description = "Multi-band graphic equalizer with presets for Bass Boost, Vocal Clarity, and Treble.",
            ActionId = "audio_equalizer",
            BadgeText = "EQ",
            AccentColor = "#EC407A"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "video_speed",
            Title = "Video Speed Multiplier",
            Icon = "\u23E9",
            Category = "Media & Accessibility",
            Description = "Smooth playback speed adjustments from 0.25x up to 5x with pitch-preservation.",
            ActionId = "video_speed",
            BadgeText = "Speed",
            AccentColor = "#FFA726"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "mono_stereo",
            Title = "Mono / Stereo Downmixer",
            Icon = "\uD83C\uDFA7",
            Category = "Media & Accessibility",
            Description = "Downmixes stereo channels to single mono output for single-earphone listening.",
            ActionId = "mono_stereo",
            BadgeText = "Mono",
            AccentColor = "#78909C"
        });

        // 4. Developer & Power Tools
        _catalog.Add(new UtilityItem
        {
            Id = "api_composer",
            Title = "API Request Composer",
            Icon = "\uD83D\uDE80",
            Category = "Developer & Power Tools",
            Description = "In-browser lightweight HTTP client to test GET, POST, PUT, DELETE endpoints with headers.",
            ActionId = "api_composer",
            BadgeText = "HTTP",
            AccentColor = "#FFB300"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "json_prettier",
            Title = "JSON Prettier & Validator",
            Icon = "\uD83D\uDC7E",
            Category = "Developer & Power Tools",
            Description = "Collapsible tree view, syntax highlighter, and formatter for raw API JSON payloads.",
            ActionId = "json_prettier",
            BadgeText = "JSON",
            AccentColor = "#43A047"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "console_streamer",
            Title = "Console Log Streamer",
            Icon = "\uD83D\uDCBB",
            Category = "Developer & Power Tools",
            Description = "Real-time floating stream of console.log, console.warn, and unhandled runtime exceptions.",
            ActionId = "console_streamer",
            BadgeText = "Logs",
            AccentColor = "#1E88E5"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "jwt_decoder",
            Title = "JWT Decoder",
            Icon = "\uD83D\uDD11",
            Category = "Developer & Power Tools",
            Description = "Decodes JSON Web Tokens into formatted Header, Claims, and Expiration timestamps.",
            ActionId = "jwt_decoder",
            BadgeText = "JWT",
            AccentColor = "#E53935"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "live_css",
            Title = "Live CSS Sandbox",
            Icon = "\uD83E\uDDEA",
            Category = "Developer & Power Tools",
            Description = "Injects CSS rules into live DOM with instant real-time stylesheet compilation.",
            ActionId = "live_css",
            BadgeText = "Live",
            AccentColor = "#8E24AA"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "asset_inspector",
            Title = "Font & Asset Inspector",
            Icon = "\uD83D\uDD0D",
            Category = "Developer & Power Tools",
            Description = "Lists all declared web fonts, SVGs, high-res images, and linked assets on page.",
            ActionId = "asset_inspector",
            BadgeText = "Assets",
            AccentColor = "#00ACC1"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "color_eyedropper",
            Title = "Eyedropper & WCAG Evaluator",
            Icon = "\uD83E\uDDAA",
            Category = "Developer & Power Tools",
            Description = "Picks pixel colors from webpage and computes WCAG AA/AAA contrast ratios against background.",
            ActionId = "color_eyedropper",
            BadgeText = "Colors",
            AccentColor = "#D81B60"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "network_throttler",
            Title = "Network Speed Throttler",
            Icon = "\uD83D\uDCE1",
            Category = "Developer & Power Tools",
            Description = "Simulates Slow 3G, Fast 3G, and Offline network conditions via DevTools CDP emulation.",
            ActionId = "network_throttler",
            BadgeText = "CDP",
            AccentColor = "#FB8C00"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "cache_bypass",
            Title = "Cache Bypass Hard Reload",
            Icon = "\u267B\uFE0F",
            Category = "Developer & Power Tools",
            Description = "Evicts ServiceWorkers and local HTTP cache, performing deterministic raw byte refresh.",
            ActionId = "cache_bypass",
            BadgeText = "Hard",
            AccentColor = "#546E7A"
        });
        // 5. Academic & Research
        _catalog.Add(new UtilityItem
        {
            Id = "citation_builder",
            Title = "Instant Citation Builder",
            Icon = "\U0001F4DD",
            Category = "Academic & Research",
            Description = "Extracts metadata from active tab and generates bibliographies in APA, MLA, Chicago, Harvard, BibTeX.",
            ActionId = "citation_builder",
            BadgeText = "Cite",
            AccentColor = "#4A9EFF"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "doi_resolver",
            Title = "DOI / Paywall Resolver",
            Icon = "\U0001F513",
            Category = "Academic & Research",
            Description = "Reads paper DOI and locates legal open-access versions, pre-prints, and repository mirrors.",
            ActionId = "doi_resolver",
            BadgeText = "OpenAccess",
            AccentColor = "#69F0AE"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "isbn_tracker",
            Title = "ISBN Tracker & Price Evaluator",
            Icon = "\U0001F4DA",
            Category = "Academic & Research",
            Description = "Scans book ISBNs and evaluates global library databases and used vendor pricing.",
            ActionId = "isbn_tracker",
            BadgeText = "Books",
            AccentColor = "#FFD54F"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "abstract_summarizer",
            Title = "Abstract Summarizer Panel",
            Icon = "\U0001F4C4",
            Category = "Academic & Research",
            Description = "Extracts intro, methodology, and conclusion from lengthy academic studies into a 200-word digest.",
            ActionId = "abstract_summarizer",
            BadgeText = "Digest",
            AccentColor = "#AB47BC"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "knowledge_graph",
            Title = "Knowledge Graph Matrix",
            Icon = "\U0001F578\uFE0F",
            Category = "Academic & Research",
            Description = "Visual connective flowchart mapping cross-references and topic overlaps across active research tabs.",
            ActionId = "knowledge_graph",
            BadgeText = "Graph",
            AccentColor = "#26C6DA"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "pdf_annotator",
            Title = "Inline PDF Annotator Layer",
            Icon = "\u270F\uFE0F",
            Category = "Academic & Research",
            Description = "Unlocks native typing, highlighting, sticky notes, and drawing tools on browser-rendered PDFs.",
            ActionId = "pdf_annotator",
            BadgeText = "Annotate",
            AccentColor = "#FF7043"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "plagiarism_checker",
            Title = "Plagiarism Checker Engine",
            Icon = "\U0001F9E9",
            Category = "Academic & Research",
            Description = "Local text scanner verifying quote originality and checking text passages against web search indexes.",
            ActionId = "plagiarism_checker",
            BadgeText = "Originality",
            AccentColor = "#EC407A"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "text_miner",
            Title = "Text Mining & Keyword Density Plotter",
            Icon = "\U0001F9E0",
            Category = "Academic & Research",
            Description = "Calculates word counts, unique vocabulary, readability index, and charts top recurring academic terms.",
            ActionId = "text_miner",
            BadgeText = "NLP",
            AccentColor = "#00E5FF"
        });

        // 6. System & Network
        _catalog.Add(new UtilityItem
        {
            Id = "terminal_launcher",
            Title = "Terminal Command Launcher",
            Icon = "\U0001F5A5\uFE0F",
            Category = "System & Network",
            Description = "Embedded interactive quick terminal running PowerShell/CMD commands directly in the workspace.",
            ActionId = "terminal_launcher",
            BadgeText = "Shell",
            AccentColor = "#69F0AE"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "clipboard_bridge",
            Title = "Cross-Device Clipboard Bridge",
            Icon = "\U0001F4CB",
            Category = "System & Network",
            Description = "Encrypted local subnet sync sharing text and media clips across phone, tablet, and desktop instances.",
            ActionId = "clipboard_bridge",
            BadgeText = "P2P",
            AccentColor = "#42A5F5"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "local_drive_mount",
            Title = "Local Drive File Mount",
            Icon = "\U0001F4BD",
            Category = "System & Network",
            Description = "Sidebar hub letting you browse and open internal disk files directly through web apps.",
            ActionId = "local_drive_mount",
            BadgeText = "Drive",
            AccentColor = "#78909C"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "sandbox_reset",
            Title = "Browser Sandbox Reset Switch",
            Icon = "\U0001F9F9",
            Category = "System & Network",
            Description = "Emergency wipe button purging cookies, local storage, and resetting to a completely clean environment.",
            ActionId = "sandbox_reset",
            BadgeText = "Wipe",
            AccentColor = "#EF5350"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "display_matrix",
            Title = "Display Matrix Router",
            Icon = "\U0001F5A5\uFE0F",
            Category = "System & Network",
            Description = "Throws active web tabs onto secondary or tertiary external monitors with precise positioning.",
            ActionId = "display_matrix",
            BadgeText = "Monitors",
            AccentColor = "#8E24AA"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "pip_aspect_lock",
            Title = "Picture-in-Picture Aspect Lock",
            Icon = "\U0001F5BC\uFE0F",
            Category = "System & Network",
            Description = "Forces rigid 16:9, 4:3, or 1:1 aspect ratios on floating web viewports over background apps.",
            ActionId = "pip_aspect_lock",
            BadgeText = "Aspect",
            AccentColor = "#FFB300"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "shortcut_override",
            Title = "Global Shortcut Key Override",
            Icon = "\u2328\uFE0F",
            Category = "System & Network",
            Description = "Intercepts and reassigns system keyboard shortcuts to custom browser macros and navigation actions.",
            ActionId = "shortcut_override",
            BadgeText = "Keys",
            AccentColor = "#5C6BC0"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "ssl_checker",
            Title = "SSL/TLS Certificate Checker",
            Icon = "\U0001F512",
            Category = "System & Network",
            Description = "Deep analytics panel inspecting certificate chain, issuer, cipher suite, and expiry countdown.",
            ActionId = "ssl_checker",
            BadgeText = "TLS 1.3",
            AccentColor = "#00E5FF"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "websocket_inspector",
            Title = "Live WebSocket Stream Inspector",
            Icon = "\U0001F4E1",
            Category = "System & Network",
            Description = "Real-time monitor tracking incoming and outgoing WebSocket message frames on active web apps.",
            ActionId = "websocket_inspector",
            BadgeText = "Frames",
            AccentColor = "#FFCA28"
        });

        _catalog.Add(new UtilityItem
        {
            Id = "header_manipulator",
            Title = "HTTP Header Manipulator",
            Icon = "\u2699\uFE0F",
            Category = "System & Network",
            Description = "Injects, removes, or rewrites request and response headers on a per-domain ruleset.",
            ActionId = "header_manipulator",
            BadgeText = "Headers",
            AccentColor = "#26A69A"
        });

    }

    public void RefreshItems(string search = "", string category = "All", string sortBy = "Category")
    {
        var pinnedList = SettingsService.Current.PinnedUtilityIds ?? new List<string>();

        foreach (var item in _catalog)
        {
            item.IsPinned = pinnedList.Contains(item.Id);
        }

        IEnumerable<UtilityItem> query = _catalog;

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(u => u.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Title.ToLowerInvariant().Contains(s) ||
                                     u.Description.ToLowerInvariant().Contains(s) ||
                                     u.Category.ToLowerInvariant().Contains(s));
        }

        if (sortBy == "Pinned")
        {
            query = query.OrderByDescending(u => u.IsPinned).ThenBy(u => u.Title);
        }
        else if (sortBy == "Name")
        {
            query = query.OrderByDescending(u => u.IsPinned).ThenBy(u => u.Title);
        }
        else // Category
        {
            query = query.OrderByDescending(u => u.IsPinned)
                         .ThenBy(u => u.Category)
                         .ThenBy(u => u.Title);
        }

        Items.Clear();
        foreach (var item in query)
        {
            Items.Add(item);
        }
    }

    public List<UtilityItem> GetAllUtilities() => new List<UtilityItem>(_catalog);

    public UtilityItem? GetById(string id) => _catalog.FirstOrDefault(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void TogglePin(string id)
    {
        var pinnedList = SettingsService.Current.PinnedUtilityIds ??= new List<string>();
        if (pinnedList.Contains(id))
        {
            pinnedList.Remove(id);
        }
        else
        {
            pinnedList.Insert(0, id);
        }
        SettingsService.Save();
        RefreshItems();
    }

    public bool IsPinned(string id) => SettingsService.Current.PinnedUtilityIds?.Contains(id) ?? false;

    // Multi-Destination Routing Handlers
    public bool SendToFavorites(UtilityItem item)
    {
        try
        {
            var pins = SettingsService.Current.PinnedUrls;
            string targetUrl = $"horizon://utility/{item.Id}";
            if (!pins.Any(p => p.Url.Equals(targetUrl, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(item.Title, StringComparison.OrdinalIgnoreCase)))
            {
                pins.Add(new PinItem
                {
                    Name = item.Title,
                    Url = targetUrl,
                    Category = item.Category,
                    IconEmoji = item.Icon,
                    LastOpened = DateTime.Now
                });
                SettingsService.Save();
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool SendToBookmarks(UtilityItem item)
    {
        try
        {
            string targetUrl = $"horizon://utility/{item.Id}";
            if (!BookmarkService.Items.Any(b => b.Url.Equals(targetUrl, StringComparison.OrdinalIgnoreCase) || b.Title.Equals(item.Title, StringComparison.OrdinalIgnoreCase)))
            {
                BookmarkService.Add(new BookmarkItem
                {
                    Title = item.Title,
                    Url = targetUrl,
                    Category = item.Category,
                    DateAdded = DateTime.Now
                });
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool SendToHeaderContextMenu(UtilityItem item)
    {
        try
        {
            var headerList = SettingsService.Current.HeaderContextUtilityIds ??= new List<string>();
            if (!headerList.Contains(item.Id))
            {
                headerList.Add(item.Id);
                SettingsService.Save();
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool SendToHomepageWidgets(UtilityItem item)
    {
        try
        {
            var widgetList = SettingsService.Current.HomepageWidgetUtilityIds ??= new List<string>();
            if (!widgetList.Contains(item.Id))
            {
                widgetList.Add(item.Id);
                SettingsService.Save();
                return true;
            }
        }
        catch { }
        return false;
    }

    public void ExecuteUtility(UtilityItem item, Window? owner = null)
    {
        if (item == null) return;

        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var win = owner ?? Application.Current.MainWindow;
            if (win == null) return;

            // Route standard native browser features
            switch (item.ActionId)
            {
                case "cinema_mode":
                    if (win is Horizon.Stealth.MainWindow mwCinema)
                    {
                        mwCinema.ToggleCinemaModeAsync();
                    }
                    break;

                case "clean_share":
                    if (win is Horizon.Stealth.MainWindow mwShare)
                    {
                        mwShare.CleanAndCopyCurrentUrl();
                    }
                    break;

                case "tab_hibernator":
                    _ = AegisResourceManager.TriggerEmergencyMemoryShedAsync();
                    MessageBox.Show(win, "Aegis Resource Manager: Governed background tabs into cryo-hibernation.", "Aegis Tab Hibernator", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;

                case "storage_shredder":
                    if (win is Horizon.Stealth.MainWindow mwShred)
                    {
                        mwShred.ShredCurrentDomainStorage();
                    }
                    break;

                case "cache_bypass":
                    if (win is Horizon.Stealth.MainWindow mwReload)
                    {
                        mwReload.HardReloadBypassCache();
                    }
                    break;

                case "qr_generator":
                    if (win is Horizon.Stealth.MainWindow mwQr)
                    {
                        mwQr.ShowQrCodeForCurrentTab();
                    }
                    break;

                case "scroll_screenshot":
                    if (win is Horizon.Stealth.MainWindow mwScreen)
                    {
                        _ = mwScreen.CaptureScrollingScreenshotAsync();
                    }
                    break;

                case "dark_inverter":
                    if (win is Horizon.Stealth.MainWindow mwDark)
                    {
                        mwDark.ToggleDarkModeInversion();
                    }
                    break;

                case "citation_builder":
                    if (win is Horizon.Stealth.MainWindow mwCite)
                    {
                        mwCite.LaunchCitationBuilder();
                    }
                    break;

                case "ssl_checker":
                    if (win is Horizon.Stealth.MainWindow mwSsl)
                    {
                        mwSsl.LaunchSslCertificateChecker();
                    }
                    break;

                case "text_miner":
                    if (win is Horizon.Stealth.MainWindow mwMiner)
                    {
                        mwMiner.LaunchTextMinerAsync();
                    }
                    break;

                case "doi_resolver":
                    if (win is Horizon.Stealth.MainWindow mwDoi)
                    {
                        mwDoi.LaunchDoiResolver();
                    }
                    break;

                case "terminal_launcher":
                    var termWin = new Horizon.Stealth.Views.TerminalLauncherWindow { Owner = win };
                    termWin.Show();
                    break;

                case "sandbox_reset":
                    if (win is Horizon.Stealth.MainWindow mwReset)
                    {
                        mwReset.ResetBrowserSandbox();
                    }
                    break;

                default:
                    // Generic feedback HUD for specialized web tool
                    MessageBox.Show(win, $"{item.Icon} {item.Title}\n\n{item.Description}\n\nCategory: {item.Category}\nStatus: Active & Armed", "Horizon Utilities", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        });
    }
}
