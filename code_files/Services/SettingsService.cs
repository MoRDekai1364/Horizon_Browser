using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace Horizon.Stealth.Services;

public class PinItem
{
    public string Name { get; set; } = "New Pin";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "";
    public string IconPath  { get; set; } = "";   // path to a user-uploaded image
    public string IconEmoji { get; set; } = "";   // e.g. "🎮", "💼" — overrides image if set
    
    public override string ToString() => Name; 
}

public class FluxItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long TotalBytes { get; set; } = 0;
    public long ReceivedBytes { get; set; } = 0;
    public string State { get; set; } = "STAGING"; 
    public bool IsExpanded { get; set; } = false;
}

public class GoogleBrowserAccount
{
    public string Email     { get; set; } = "";
    public string Name      { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
}

public static class SettingsService
{
    private static readonly string _folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HorizonData");
    private static readonly string _path = Path.Combine(_folder, "config.json");

    public static SettingsData Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);

            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data != null) Current = data;
            }
            else
            {
                Save(); 
            }
        }
        catch (Exception ex)
        {
            Current = new SettingsData();
            LogService.RecordCrash(ex, "Settings Load");
        }

        // v2 migration: old default (1270) was nearly identical to the default window width
        // (1280). Any saved value ≥ 1000 was the broken value — reset to the correct default.
        if (Current.NarrowWindowThresholdPx >= 1000)
            Current.NarrowWindowThresholdPx = 600;
    }

    public static void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(Current, options);
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Settings Save");
        }
    }
}

public class SearchEngineEntry
{
    public string Name    { get; set; } = "";
    public string Url     { get; set; } = ""; //contain {query}
    public bool   BuiltIn { get; set; } = false;
}

public class SettingsData
{
    public string HomePage         { get; set; } = "https://alohafind.com";
    public string SearchEngine     { get; set; } = "AlohFind";
    public string SearchEngineUrl  { get; set; } = "https://alohafind.com/search/?q={query}";
    public List<SearchEngineEntry> CustomSearchEngines { get; set; } = new();

    public List<PinItem> PinnedUrls { get; set; } = new();
    public List<string> LastSessionUrls { get; set; } = new();
    
    public string Theme { get; set; } = "Horizon";
    public string BackgroundImage { get; set; } = ""; 
    public double BackgroundOpacity { get; set; } = 1.0; 

    public bool AutoHideHeader { get; set; } = false;
    public bool AutoHideSidebar { get; set; } = false;
    public int HeaderSensitivityMs { get; set; } = 300;
    public int SidebarSensitivityMs { get; set; } = 300;
    
    public bool ShowSessionRestore    { get; set; } = true;
    public bool AutoRestoreSession    { get; set; } = false;

    public bool SilentUpdateCheckEnabled    { get; set; } = true;
    public bool AutoDownloadUpdatesEnabled  { get; set; } = true;
    public string PendingUpdateInstallerPath { get; set; } = "";
    
    public bool IsStealthMode { get; set; } = true;
    public bool EnableSponsorBlock { get; set; } = true;
    public bool SB_Sponsors { get; set; } = true;
    public bool SB_Intro { get; set; } = true;
    public bool SB_Outro { get; set; } = true;
    public bool SB_Interaction { get; set; } = false;
    public bool SB_SelfPromo { get; set; } = true;
    public bool SB_MusicOfftopic { get; set; } = false;
    public string NextDnsId { get; set; } = "";
    public string DownloadsPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public int JanitorSmallFileMb { get; set; } = 0;
    public int JanitorLargeFileGb { get; set; } = 0;
    public int JanitorRetentionMin { get; set; } = 0;

    public bool   PerTabLanguageEnabled { get; set; } = true;
    public string DefaultLanguage       { get; set; } = "en";
    public int    TabsPerRow            { get; set; } = 10;

    // ── Extension toggles ────────────────────────────────────────────────────
    public bool ConsentOMaticEnabled    { get; set; } = true;
    public bool AdGuardEnabled          { get; set; } = true;
    
    // ── AdGuard filter toggles ───────────────────────────────────────────────────
    public bool AdGuard_BlockAds        { get; set; } = true;
    public bool AdGuard_BlockTrackers   { get; set; } = true;
    public bool AdGuard_BlockAnnoyances { get; set; } = true;
    public bool AdGuard_SocialWidgets   { get; set; } = false;

    // ── Consent-O-Matic default answers ─────────────────────────────────────────
    public bool CoM_RejectMarketing   { get; set; } = true;
    public bool CoM_RejectAnalytics   { get; set; } = true;
    public bool CoM_RejectPreferences { get; set; } = true;
    public bool CoM_RejectOthers      { get; set; } = true;

    // ── Header Widget ────────────────────────────────────────────────────────
    public List<string> WidgetModes       { get; set; } = new() { "Clock" };
    public int          WidgetCycleSecs   { get; set; } = 7;
    public bool         WidgetShowCycle   { get; set; } = true;
    public string       WidgetNotes       { get; set; } = "";
    public Dictionary<string, string> WidgetNoteTabs { get; set; } = new();
    public string       WidgetWeatherCity { get; set; } = "";
    public bool         WidgetDisableCycle         { get; set; } = false;
    public bool         WeatherWidgetMarquee       { get; set; } = false;
    public bool         WeatherWidgetHoverWiden    { get; set; } = false;
    public bool         MediaWidgetMarquee         { get; set; } = false;
    public bool         MediaWidgetHoverWiden      { get; set; } = false;
    public string       NavigationProvider       { get; set; } = "Google Maps";
    public List<string> NavigationCustomDomains  { get; set; } = new();

    // ── Tab title display ────────────────────────────────────────────────────
    public string TabTitleMode           { get; set; } = "Full";       // "Full" | "DomainOnly"
    public string TabTitleHoverMode      { get; set; } = "FadeIn";     // "FadeIn" | "None"
    public string VisualizerColorScheme  { get; set; } = "Thumbnail";  // "Favicon" | "Thumbnail"
    public double PaletteSampleRateSec   { get; set; } = 1.0;

    // ── Sleeping tabs ────────────────────────────────────────────────────────
    public bool   SleepingTabsEnabled    { get; set; } = true;
    public int    SleepingTabsMinutes    { get; set; } = 10;
    public double MediaTabHoverWidth     { get; set; } = 181.0;
    public double TabDefaultWidth       { get; set; } = 140.0;
    public double TabMediaPlaybackWidth { get; set; } = 160.0;
    public double TabDownloadModeWidth  { get; set; } = 160.0;
    public double DownloadTabHoverWidth { get; set; } = 200.0;

    // ── Pinned file browser locations ────────────────────────────────────────
    public List<string> PinnedFsLocations { get; set; } = new();

    // ── Clock widget ─────────────────────────────────────────────────────────
    // 0 = 24 h + seconds   1 = 24 h no seconds   2 = 12 h AM/PM
    // 3 = 24 h + :ss corner (smaller)   4 = Stopwatch   5 = Timer
    public int ClockMode { get; set; } = 0;

    // ── Scroll speed ─────────────────────────────────────────────────────────
    // 1.0 = native Chrome speed. Default 1.07 = Chrome + 7%.
    public double ScrollSpeedMultiplier { get; set; } = 1.07;

    // ── Calculator AI mode API keys ──────────────────────────────────────────
    public string ClaudeApiKey   { get; set; } = "";
    public string ChatGptApiKey  { get; set; } = "";
    public string GeminiApiKey   { get; set; } = "";

    // ── Account sync — Google ────────────────────────────────────────────────
    // Obtain from console.cloud.google.com → OAuth 2.0 credentials (Desktop App)
    public string GoogleClientId     { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";

    // ── Account sync — Microsoft ─────────────────────────────────────────────
    // Obtain from portal.azure.com → App registrations (public client — no secret needed)
    public string MicrosoftClientId  { get; set; } = "";

    // ── Logged-in sync accounts (multi-account, both providers) ─────────────
    // Each entry holds its own access + refresh tokens.
    
    // ── OAuth tokens (cached after login) ───────────────────────────────────────
    public string GoogleOAuthToken      { get; set; } = "";
    public string MicrosoftOAuthToken   { get; set; } = "";
    
    public List<SyncAccount> SyncAccounts { get; set; } = new();

    public int NarrowWindowMode        { get; set; } = 1;
    public int NarrowWindowThresholdPx { get; set; } = 600;

    // ── Settings window UX ───────────────────────────────────────────────────
    public int LastSettingsTabIndex { get; set; } = 0;

    // ── Color mode (Dark / Light / System) ───────────────────────────────────
    public bool   LinkSettingsAndSidebarColorMode { get; set; } = true;
    public string SettingsColorMode { get; set; } = "System";  // "Dark" | "Light" | "System"
    public string SidebarColorMode  { get; set; } = "System";  // "Dark" | "Light" | "System"

    // ── Google Account Switcher button ──────────────────────────────────────
    public string       DefaultGoogleAccountEmail { get; set; } = "";
    public List<string> GoogleAccountOrder        { get; set; } = new();
    public List<GoogleBrowserAccount> GoogleBrowserAccounts { get; set; } = new();
    public double?      AccountSwitcherButtonX    { get; set; } = null;
    public double?      AccountSwitcherButtonY    { get; set; } = null;

    // ── Background keep-alive ─────────────────────────────────────────────────
    // When true, closing the window hides to tray instead of terminating.
    // The WebView2 environment stays warm → near-instant next open.
    public bool BackgroundKeepAliveEnabled { get; set; } = false;
}