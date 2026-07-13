using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.IO;

namespace Horizon.Stealth.ViewModels;

public class TabViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Guid TabId { get; } = Guid.NewGuid();

    private bool _isActiveTab;
    public bool IsActiveTab
    {
        get => _isActiveTab;
        set { _isActiveTab = value; OnPropertyChanged(); }
    }

    private string _title = "New Tab";
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
    }

    // When true, tab was renamed by the user; automatic title updates are suppressed.
    private bool _hasCustomTitle;
    public bool HasCustomTitle
    {
        get => _hasCustomTitle;
        set { _hasCustomTitle = value; OnPropertyChanged(); }
    }

    private string _mediaTitle = "";
    public string MediaTitle
    {
        get => _mediaTitle;
        set
        {
            _mediaTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CleanMediaTitle));
            OnPropertyChanged(nameof(CombinedMediaTitle));
        }
    }

    private string _url = "about:blank";
    public string Url
    {
        get => _url;
        set
        {
            _url = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DomainTitle));
            OnPropertyChanged(nameof(CombinedMediaTitle));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    private bool _isMarqueeNeeded;
        public bool IsMarqueeNeeded
        {
            get => _isMarqueeNeeded;
            set { _isMarqueeNeeded = value; OnPropertyChanged(); }
        }

        private bool _isHoverWideningEnabled = true;
        public bool IsHoverWideningEnabled
        {
            get => _isHoverWideningEnabled;
            set { _isHoverWideningEnabled = value; OnPropertyChanged(); }
        }

        private bool _isMarqueeEnabled = true;
        public bool IsMarqueeEnabled
        {
            get => _isMarqueeEnabled;
            set { _isMarqueeEnabled = value; OnPropertyChanged(); }
        }
    
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private bool _hasVideo;
    public bool HasVideo
    {
        get => _hasVideo;
        set { _hasVideo = value; OnPropertyChanged(); }
    }

    private bool _isPlayingAudio;
    public bool IsPlayingAudio
    {
        get => _isPlayingAudio;
        set
        {
            _isPlayingAudio = value;
            OnPropertyChanged();
            if (value) HasEverPlayedAudio = true;
        }
    }

    private bool _hasEverPlayedAudio;
    public bool HasEverPlayedAudio
    {
        get => _hasEverPlayedAudio;
        set
        {
            _hasEverPlayedAudio = value;
            OnPropertyChanged();
        }
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set { _isMuted = value; OnPropertyChanged(); }
    }

    private bool _isMediaPaused;
    public bool IsMediaPaused
    {
        get => _isMediaPaused;
        set { _isMediaPaused = value; OnPropertyChanged(); }
    }

    private bool _isAudioOnlyMode;
    public bool IsAudioOnlyMode
    {
        get => _isAudioOnlyMode;
        set { _isAudioOnlyMode = value; OnPropertyChanged(); }
    }

    private bool _isActiveDownload;
    public bool HasEverDownloaded
    {
        get => _hasEverDownloaded;
        set { _hasEverDownloaded = value; OnPropertyChanged(); }
    }
    private bool _hasEverDownloaded = false;

    public bool IsActiveDownload
    {
        get => _isActiveDownload;
        set
        {
            _isActiveDownload = value;
            OnPropertyChanged();
        }
    }

    private string _downloadFileName = "";
    public string DownloadFileName
    {
        get => _downloadFileName;
        set { _downloadFileName = value; OnPropertyChanged(); }
    }

    private double _downloadProgressValue;
    public double DownloadProgressValue
    {
        get => _downloadProgressValue;
        set { _downloadProgressValue = value; OnPropertyChanged(); }
    }

    private double _downloadSpeedMBs;
    public double DownloadSpeedMBs
    {
        get => _downloadSpeedMBs;
        set { _downloadSpeedMBs = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadSpeedText)); }
    }

    private int _downloadEtaSecs;
    public int DownloadEtaSecs
    {
        get => _downloadEtaSecs;
        set { _downloadEtaSecs = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadEtaText)); }
    }

    public string DownloadSpeedText => _downloadSpeedMBs >= 1.0
        ? $"{_downloadSpeedMBs:F1} MB/s"
        : $"{_downloadSpeedMBs * 1024:F0} KB/s";

    public string DownloadEtaText
    {
        get
        {
            if (_downloadEtaSecs <= 0) return "--";
            if (_downloadEtaSecs < 60) return $"{_downloadEtaSecs}s";
            if (_downloadEtaSecs < 3600) return $"{_downloadEtaSecs / 60}m {_downloadEtaSecs % 60}s";
            return $"{_downloadEtaSecs / 3600}h {(_downloadEtaSecs % 3600) / 60}m";
        }
    }

    private double _loadingProgress;
    public double LoadingProgress
    {
        get => _loadingProgress;
        set { _loadingProgress = value; OnPropertyChanged(); }
    }

    private double _loadingOpacity;
    public double LoadingOpacity
    {
        get => _loadingOpacity;
        set { _loadingOpacity = value; OnPropertyChanged(); }
    }

    private SolidColorBrush _singleColorBrush = new SolidColorBrush(Colors.Transparent);
    public SolidColorBrush SingleColorBrush
    {
        get => _singleColorBrush;
        set { _singleColorBrush = value; OnPropertyChanged(); }
    }

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set { _volume = value; OnPropertyChanged(); }
    }

    

    // Dynamic tab width - set by ReflowTabs() so tabs shrink before overflowing
    private double _tabWidth = 145.0;
    public double TabWidth
    {
        get => _tabWidth;
        set { _tabWidth = value; OnPropertyChanged(); }
    }

    public string DomainTitle
    {
        get
        {
            try
            {
                if (!string.IsNullOrEmpty(_url) && Uri.TryCreate(_url, UriKind.Absolute, out var uri))
                    return uri.Host;
            }
            catch { }
            return _title;
        }
    }

    public string CleanMediaTitle
    {
        get
        {
            if (string.IsNullOrEmpty(_mediaTitle)) return "";
            string title = _mediaTitle;

            int pipeIdx = title.LastIndexOf(" | ");
            if (pipeIdx > 0)
            {
                title = title.Substring(0, pipeIdx).Trim();
            }
            else
            {
                int dashIdx = title.LastIndexOf(" - ");
                if (dashIdx > 0)
                {
                    string suffix = title.Substring(dashIdx + 3).Trim();
                    if (suffix.IndexOf("YouTube", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        suffix.IndexOf("Twitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        suffix.IndexOf("Spotify", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        suffix.IndexOf("SoundCloud", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        suffix.Equals(DomainTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        title = title.Substring(0, dashIdx).Trim();
                    }
                }
            }

            return title;
        }
    }

    public string CombinedMediaTitle
    {
        get
        {
            string domain = DomainTitle;
            string media  = (_mediaTitle ?? "").Trim();
            return !string.IsNullOrEmpty(media)
                ? $" {domain}  |  {media} "
                : $" {domain} ";
        }
    }

    // DisplayTitle: shown in the tab label. In DomainOnly mode = domain; in Full mode = full title.
    // Media tabs always show CleanMediaTitle.
    public string DisplayTitle
    {
        get
        {
            string baseTitle;
            if (HasEverPlayedAudio)
                baseTitle = CleanMediaTitle.Length > 0 ? CleanMediaTitle : DomainTitle;
            else
            {
                bool domainOnly = Services.SettingsService.Current.TabTitleMode == "DomainOnly";
                baseTitle = (domainOnly && !HasCustomTitle) ? DomainTitle : _title;
            }
            return IsSleeping ? baseTitle + " 💤" : baseTitle;
        }
    }

    // Set by ReflowTabs — non-zero when another tab has the exact same DisplayTitle.
    // Used to tint duplicate tabs with adjacent hue.
    private int _duplicateTitleIndex = 0;
    public int DuplicateTitleIndex
    {
        get => _duplicateTitleIndex;
        set
        {
            _duplicateTitleIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TitleForeground));
        }
    }

    // Multi-select (Ctrl/Shift-click) highlight state
    private bool _isMultiSelected;
    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        set { _isMultiSelected = value; OnPropertyChanged(); }
    }

    // Sleeping tab state (background tab suspended to save memory)
    private bool _isSleeping;
    public bool IsSleeping
    {
        get => _isSleeping;
        set { _isSleeping = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
    }

    // Per-tab sleep overrides — set via the tab right-click context menu.

    // NeverSleep: this tab is never suspended regardless of global settings.
    private bool _neverSleep;
    public bool NeverSleep
    {
        get => _neverSleep;
        set { _neverSleep = value; OnPropertyChanged(); }
    }

    // Custom idle minutes before sleep; null = use the global SleepingTabsMinutes setting.
    private int? _sleepIdleMinutesOverride;
    public int? SleepIdleMinutesOverride
    {
        get => _sleepIdleMinutesOverride;
        set { _sleepIdleMinutesOverride = value; OnPropertyChanged(); }
    }

    // Custom RAM threshold (MB) before sleep; null = use the global SLEEP_RAM_MODERATE_MB constant.
    private long? _sleepRamThresholdMbOverride;
    public long? SleepRamThresholdMbOverride
    {
        get => _sleepRamThresholdMbOverride;
        set { _sleepRamThresholdMbOverride = value; OnPropertyChanged(); }
    }

    private string _language = "en";
    public string Language
    {
        get => _language;
        set { _language = value; OnPropertyChanged(); }
    }

    public List<Color> PaletteColors { get; set; } = new();

    public LinearGradientBrush AnimatedBrush { get; } = new LinearGradientBrush
    {
        StartPoint = new Point(0, 0.5),
        EndPoint   = new Point(1, 0.5),
        GradientStops = new GradientStopCollection
        {
            new GradientStop(Colors.Transparent, 0.0),
            new GradientStop(Colors.Transparent, 0.5),
            new GradientStop(Colors.Transparent, 1.0)
        }
    };

    /// <summary>
    /// White for normal tabs; shifts to a distinct hue for each duplicate-titled tab group
    /// so users can visually distinguish two tabs showing the same title.
    /// Index 0 = no duplicate → white.  Index 1..N → evenly-spaced hues starting at 120° (green).
    /// Saturation/value are kept low so the tint is readable without being garish.
    /// </summary>
    private static readonly SolidColorBrush _whiteBrush = new SolidColorBrush(Colors.White);
    private SolidColorBrush? _cachedTitleForeground;
    private int _cachedTitleForegroundIndex = -1;

    public SolidColorBrush TitleForeground
    {
        get
        {
            if (_duplicateTitleIndex <= 0) return _whiteBrush;
            if (_cachedTitleForeground != null && _cachedTitleForegroundIndex == _duplicateTitleIndex)
                return _cachedTitleForeground;
            double hue = (120.0 * (_duplicateTitleIndex - 1)) % 360.0;
            _cachedTitleForeground = new SolidColorBrush(HsvToColor(hue, 0.55, 1.0));
            _cachedTitleForegroundIndex = _duplicateTitleIndex;
            return _cachedTitleForeground;
        }
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        double r, g, b;
        if      (h < 60)  { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}