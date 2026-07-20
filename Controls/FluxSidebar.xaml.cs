using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows.Threading;
using Horizon.Stealth.Services;
using Horizon.Stealth.Core;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using Microsoft.Win32;
using System.Text.Json;

namespace Horizon.Stealth.Controls;

public static class GradientUtility
{
    public static Color ProcessColor(Color c)
    {
        ColorToHsv(c, out double h, out double s, out double v);
        s = Math.Max(0, s - 0.10);
        v = Math.Min(v, 0.35); 
        return HsvToRgb(h, s, v);
    }

    public static Color CalculateAgeColor(DateTime time, Color exclude)
    {
        double ageDays = (DateTime.Now - time).TotalDays;
        double hue = Math.Clamp((ageDays / 30.0) * 360.0, 0, 360);
        
        ColorToHsv(exclude, out double exH, out double exS, out double exV);
        if (Math.Abs(hue - exH) < 40 || Math.Abs(hue - exH) > 320)
            hue = (hue + 60) % 360;

        return HsvToRgb(hue, 0.72, 0.35);
    }

    public static void ColorToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else if (max == b) h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0) h += 360;
        s = max == 0 ? 0 : delta / max;
        v = max;
    }

    public static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}

public class FluxGradientConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PinItem pin)
        {
            int hash = pin.Url?.GetHashCode() ?? 0;
            double hue = Math.Abs(hash % 360);
            Color leftColor = GradientUtility.ProcessColor(GradientUtility.HsvToRgb(hue, 0.8, 0.5));
            double rightHue = (hue + 120) % 360;
            Color rightColor = GradientUtility.HsvToRgb(rightHue, 0.72, 0.35);

            var gs = new GradientStopCollection
            {
                new GradientStop(leftColor, 0.0),
                new GradientStop(rightColor, 1.0)
            };
            return new LinearGradientBrush(gs, new Point(0, 0.5), new Point(1, 0.5));
        }
        return new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}


public class CategoryColorConverter : IValueConverter
{
    // Maps a category name → a vivid, consistent, high-contrast color.
    // Same string → same color across all runs (pure hash, no RNG).
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string name = value as string ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

        // Stable hash: use Jenkins one-at-a-time so it doesn't change between .NET versions
        uint h = 0;
        foreach (char c in name) { h += c; h += h << 10; h ^= h >> 6; }
        h += h << 3; h ^= h >> 11; h += h << 15;

        double hue = (h % 360u);
        // Push saturation and brightness high so it reads on the dark #0b0b0b background
        Color col = GradientUtility.HsvToRgb(hue, 0.75, 0.90);
        return new SolidColorBrush(col);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class HistoryItemViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public string Url { get; }
    public string Title { get; }
    public DateTime VisitTime { get; }
    
    private Brush _itemGradient;
    public Brush ItemGradient 
    { 
        get => _itemGradient; 
        private set 
        { 
            _itemGradient = value; 
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ItemGradient))); 
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public HistoryItemViewModel(HistoryItem src)
    {
        Url = src.Url;
        Title = src.Title;
        VisitTime = src.VisitTime;
        _itemGradient = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
        GenerateGradientAsync();
    }

    private async void GenerateGradientAsync()
    {
        Color leftColor = Color.FromRgb(0x33, 0x33, 0x33);
        try
        {
            var uri = new Uri(Url);
            string favUrl = $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=32";
            using var hc = new System.Net.Http.HttpClient();
            var bytes = await hc.GetByteArrayAsync(favUrl);
            leftColor = GetDominantColor(bytes);
        }
        catch { }

        leftColor = GradientUtility.ProcessColor(leftColor);
        Color rightColor = GradientUtility.CalculateAgeColor(VisitTime, leftColor);
        
        var gs = new GradientStopCollection
        {
            new GradientStop(leftColor, 0.0),
            new GradientStop(rightColor, 1.0)
        };
        ItemGradient = new LinearGradientBrush(gs, new Point(0, 0.5), new Point(1, 0.5));
    }

    private static Color GetDominantColor(byte[] imageBytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(imageBytes);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var cb = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int stride = cb.PixelWidth * 4;
            byte[] pixels = new byte[stride * cb.PixelHeight];
            cb.CopyPixels(pixels, stride, 0);

            long r = 0, g = 0, b = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                b += pixels[i];
                g += pixels[i + 1];
                r += pixels[i + 2];
            }
            int count = pixels.Length / 4;
            if (count == 0) return Color.FromRgb(0x33, 0x33, 0x33);
            
            return Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }
        catch
        {
            return Color.FromRgb(0x33, 0x33, 0x33);
        }
    }
}


// ── Value converters ─────────────────────────────────────────────────────────

public class FaviconConverter : IValueConverter
{
    // ── Static disk cache ────────────────────────────────────────────────────
    private static readonly string _cacheDir =
        Path.Combine(Horizon.Stealth.Services.ConfigService.UserDataRoot, "FaviconCache");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object?> _memCache = new();

    static FaviconConverter() => Directory.CreateDirectory(_cacheDir);

    // ── IValueConverter ──────────────────────────────────────────────────────
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrEmpty(url)) return null;
        try
        {
            var uri  = new Uri(url);
            string key  = uri.Host.ToLowerInvariant();
            string file = Path.Combine(_cacheDir, SanitizeKey(key) + ".png");

            // 1. Hot memory cache — fastest path
            if (_memCache.TryGetValue(key, out var cached)) return cached;

            // 2. Disk cache — second fastest path
            if (File.Exists(file))
            {
                var bmp = LoadBitmapFromFile(file);
                _memCache[key] = bmp;
                return bmp;
            }

            // 3. Network fetch — async, return placeholder URL immediately
            _ = FetchAndCacheAsync(key, uri.Host, file);
            string placeholder = $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=64";
            _memCache[key] = placeholder; // store placeholder so we don't re-queue
            return placeholder;
        }
        catch { return null; }
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async System.Threading.Tasks.Task FetchAndCacheAsync(string key, string host, string file)
    {
        try
        {
            string[] sources =
            {
                $"https://www.google.com/s2/favicons?domain={host}&sz=64",
                $"https://icons.duckduckgo.com/ip3/{host}.ico",
                $"https://{host}/favicon.ico",
            };

            byte[]? bytes = null;
            using var hc = new System.Net.Http.HttpClient
            {
                Timeout = System.TimeSpan.FromSeconds(6)
            };
            hc.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            foreach (var src in sources)
            {
                try
                {
                    bytes = await hc.GetByteArrayAsync(src).ConfigureAwait(false);
                    if (bytes.Length > 64) break; // skip empty 1x1 responses
                }
                catch { bytes = null; }
            }

            if (bytes == null || bytes.Length <= 64) return;

            await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);

            // Evict the placeholder so next binding cycle loads the real BitmapImage
            _memCache.TryRemove(key, out _);
        }
        catch { /* silently discard — favicon is cosmetic */ }
    }

    private static System.Windows.Media.Imaging.BitmapImage? LoadBitmapFromFile(string path)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource    = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = 64;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static string SanitizeKey(string key) =>
        System.Text.RegularExpressions.Regex.Replace(key, @"[^a-zA-Z0-9._-]", "_");
}

// ── FluxItemViewModel ─────────────────────────────────────────────────────────

public class FluxItemViewModel
{
    private string _fileNameOverride = "";
    public string OverrideFileName
    {
        get => _fileNameOverride;
        set => _fileNameOverride = value;
    }

    public string   FileName      { get; }
    public string   FilePath      { get; }
    public long     TotalBytes    { get; }
    public long     ReceivedBytes { get; }
    public string   State         { get; }
    public DateTime CreationTime  { get; }
    public bool     IsFolder      { get; }

    public string DisplayName     => string.IsNullOrEmpty(_fileNameOverride) ? FileName : _fileNameOverride;
    public string FileSizeDisplay => IsFolder ? "folder" : FormatFileSize(TotalBytes);
    public string TypeIcon        => IsFolder ? "📁" : GetTypeIcon(Path.GetExtension(FileName));
    public Brush  ItemGradient    => IsFolder ? BuildFolderGradient() : BuildGradient(Path.GetExtension(FileName), TotalBytes);

    // File constructor
    public FluxItemViewModel(FluxItem src, DateTime creationTime)
    {
        FileName      = src.FileName;
        FilePath      = src.FilePath;
        TotalBytes    = src.TotalBytes;
        ReceivedBytes = src.ReceivedBytes;
        State         = src.State;
        CreationTime  = creationTime;
        IsFolder      = false;
    }

    // Folder constructor
    public FluxItemViewModel(DirectoryInfo dir)
    {
        FileName      = dir.Name;
        FilePath      = dir.FullName;
        TotalBytes    = 0;
        ReceivedBytes = 0;
        State         = "FOLDER";
        CreationTime  = dir.CreationTime;
        IsFolder      = true;
    }

    private static Brush BuildFolderGradient()
    {
        var gs = new GradientStopCollection
        {
            new GradientStop(Color.FromRgb(0x1A, 0x14, 0x05), 0.0),
            new GradientStop(Color.FromRgb(0x0A, 0x18, 0x2A), 1.0),
        };
        return new LinearGradientBrush(gs, new Point(0, 0.5), new Point(1, 0.5));
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)                return $"{bytes} B";
        if (bytes < 1024 * 1024)         return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string GetTypeIcon(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf"                                           => "📕",
        ".doc" or ".docx"                               => "📘",
        ".xls" or ".xlsx"                               => "📗",
        ".ppt" or ".pptx"                               => "📙",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz"    => "📦",
        ".exe" or ".msi" or ".bat" or ".cmd"            => "⚙",
        ".png" or ".jpg" or ".jpeg" or ".gif"
            or ".bmp" or ".webp" or ".ico" or ".svg"    => "🖼",
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "🎵",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "🎬",
        ".html" or ".htm" or ".css" or ".js" or ".ts"   => "🌐",
        ".cs" or ".py" or ".java" or ".cpp"
            or ".c" or ".h" or ".rb" or ".go"           => "💻",
        ".txt" or ".log" or ".md"                        => "📝",
        ".json" or ".xml" or ".yaml" or ".yml"
            or ".toml" or ".ini" or ".cfg"              => "⚙",
        ".xaml"                                          => "🖥",
        _                                                => "📄",
    };

    // Colors boosted ~25% for vibrancy
    private static Brush BuildGradient(string ext, long bytes)
    {
        Color typeColor = ext.ToLowerInvariant() switch
        {
            ".pdf"                                                       => Color.FromRgb(0x34, 0x0A, 0x0A),
            ".doc" or ".docx"                                           => Color.FromRgb(0x08, 0x20, 0x3C),
            ".xls" or ".xlsx"                                           => Color.FromRgb(0x07, 0x2A, 0x14),
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz"               => Color.FromRgb(0x2A, 0x1E, 0x07),
            ".exe" or ".msi" or ".bat" or ".cmd"                       => Color.FromRgb(0x2D, 0x0A, 0x2D),
            ".png" or ".jpg" or ".jpeg" or ".gif"
                or ".bmp" or ".webp" or ".ico" or ".svg"               => Color.FromRgb(0x07, 0x28, 0x2F),
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a"            => Color.FromRgb(0x25, 0x0A, 0x32),
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm"            => Color.FromRgb(0x32, 0x14, 0x07),
            ".html" or ".htm" or ".css" or ".js" or ".ts"              => Color.FromRgb(0x0C, 0x1E, 0x07),
            ".cs" or ".py" or ".java" or ".cpp"
                or ".c" or ".h" or ".rb" or ".go"                      => Color.FromRgb(0x00, 0x19, 0x32),
            ".txt" or ".log" or ".md"                                   => Color.FromRgb(0x20, 0x20, 0x20),
            ".json" or ".xml" or ".yaml" or ".yml"
                or ".toml" or ".ini" or ".cfg" or ".xaml"              => Color.FromRgb(0x14, 0x14, 0x32),
            _                                                            => Color.FromRgb(0x16, 0x16, 0x16),
        };

        Color sizeColor = bytes switch
        {
            < 50 * 1024                  => Color.FromRgb(0x07, 0x23, 0x07),
            < 1024 * 1024                => Color.FromRgb(0x14, 0x28, 0x07),
            < 10L * 1024 * 1024          => Color.FromRgb(0x2A, 0x20, 0x05),
            < 100L * 1024 * 1024         => Color.FromRgb(0x32, 0x16, 0x02),
            _                            => Color.FromRgb(0x32, 0x07, 0x02),
        };

        typeColor = GradientUtility.ProcessColor(typeColor);
        sizeColor = GradientUtility.ProcessColor(sizeColor);

        var gs = new GradientStopCollection
        {
            new GradientStop(typeColor, 0.0),
            new GradientStop(sizeColor, 1.0),
        };
        return new LinearGradientBrush(gs, new Point(0, 0.5), new Point(1, 0.5));
    }
}

// ── LocalImageConverter ───────────────────────────────────────────────────────
// Loads a local file path into a BitmapImage; returns null if path is empty/missing.
public class LocalImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource    = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = 64;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ── NullOrEmptyConverter ──────────────────────────────────────────────────────
// Returns bool false when a string is null/empty (used in DataTrigger bindings).
public class NullOrEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string);   // true = empty/null, false = has value

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ── Category edit mode enum ───────────────────────────────────────────────────

internal enum CategoryEditMode { Add, Rename, Delete }

// ── Main sidebar control ──────────────────────────────────────────────────────

public partial class FluxSidebar : UserControl
{
    public event EventHandler?                    RequestAddPin;
    public event EventHandler<string>?            RequestNavigate;
    public event EventHandler<ExtensionRecord>?   RequestExtensionPopup;

    public ObservableCollection<FluxItemViewModel> FluxItems { get; set; } = new();

    private PinItem? _itemBeingRenamed;

    public bool IsAnyPopupOpen { get; private set; }

    private void TrackPopup(System.Windows.Controls.Primitives.Popup popup)
    {
        popup.Opened += (_, _) => IsAnyPopupOpen = true;
        popup.Closed  += (_, _) =>
        {
            IsAnyPopupOpen = PinsContextPopup.IsOpen
                          || BookmarksContextPopup.IsOpen
                          || HistoryContextPopup.IsOpen
                          || DlContextPopup.IsOpen
                          || FluxContextPopup.IsOpen
                          || ExtContextPopup.IsOpen;
        };
    }

    // ── Downloads browse navigation ───────────────────────────────────────────
    private string         _browsePath     = string.Empty; // current directory shown
    private Stack<string>  _browseStack    = new();        // back history
    private string?        _clipCutPath    = null;         // null = copy, set = cut source

    // ── Downloads sort state ──────────────────────────────────────────────────
    private enum SortField { Date, Name, Size }
    private SortField _sortField     = SortField.Date;
    private bool      _sortAscending = false;

    // ── Bookmarks sort state ──────────────────────────────────────────────────
    private enum BmSortField { Date, Name, Domain }
    private BmSortField _bmSortField     = BmSortField.Date;
    private bool        _bmSortAscending = false;

    // ── History sort state ────────────────────────────────────────────────────
    private enum HistSortField { Date, Title, Domain }
    private HistSortField _histSortField     = HistSortField.Date;
    private bool          _histSortAscending = false;

    // ── Pins sort / filter state ──────────────────────────────────────────────
    private enum PinSortField { Manual, Name, Category }
    private PinSortField _pinSortField     = PinSortField.Name;
    private bool         _pinSortAscending = true;

    // ── Extensions sort state ─────────────────────────────────────────────────
    private enum ExtSortField { Name, State, Source }
    private ExtSortField _extSortField     = ExtSortField.Name;
    private bool         _extSortAscending = true;

    // ── Category edit overlay state ───────────────────────────────────────────
    private CategoryEditMode _categoryEditMode;
    private string           _categoryEditTarget = ""; // for rename/delete

    // ── Active download tracking ──────────────────────────────────────────────
    private string _activeDownloadPath = "";
    private string _activeDownloadLink = "";
    private bool _activeDownloadComplete = false;

    // ── Preview state ─────────────────────────────────────────────────────────
    private double _imageZoom = 1.0;
    private double _textZoom = 1.0;
    private const double ZoomStep = 0.2, ZoomMin = 0.2, ZoomMax = 4.0;
    

    // ── In-file search state ──────────────────────────────────────────────────
    private List<TextRange> _searchMatches  = new();
    private int             _searchIndex    = -1;
    private string          _lastSearchTerm = "";

    

    public FluxSidebar()
    {
        InitializeComponent();
        ListFlux.ItemsSource = FluxItems;
        Loaded += FluxSidebar_Loaded;
    }

    

    private void FluxSidebar_Loaded(object sender, RoutedEventArgs e)
    {
        AddDefaultPin();
        LoadSortDefaults();
        RefreshPins();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, (Action)(() =>
        {
            LoadFluxStream();
        }));
        LoadHistory();
        RefreshBookmarks();
        ExtensionService.EnsureInstalled();
        BuildExtensionCards();

        TrackPopup(PinsContextPopup);
        TrackPopup(BookmarksContextPopup);
        TrackPopup(HistoryContextPopup);
        TrackPopup(DlContextPopup);
        TrackPopup(FluxContextPopup);
        TrackPopup(ExtContextPopup);

        HistoryService.HistoryUpdated    += (s, _) => Dispatcher.Invoke(LoadHistory);
        BookmarkService.OnUpdated        += ()      => Dispatcher.Invoke(RefreshBookmarks);
        FluxJanitorService.OnDownloadCompleted += () => Dispatcher.Invoke(() =>
        {
            // Only jump back to root on new download if we're already at root
            // (don't interrupt browsing mid-folder)
            if (_browseStack.Count == 0)
                LoadFluxStream(SettingsService.Current.DownloadsPath);
            else
                LoadFluxStream();
        });
        ExtensionService.CatalogChanged  += ()      => Dispatcher.Invoke(BuildExtensionCards);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EXTENSIONS
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildExtensionCards()
    {
        var all = ExtensionService.All;

        IEnumerable<ExtensionRecord> sorted = _extSortField switch
        {
            ExtSortField.State  => _extSortAscending
                ? all.OrderBy(e => e.Enabled ? 0 : 1)
                : all.OrderBy(e => e.Enabled ? 1 : 0),
            ExtSortField.Source => _extSortAscending
                ? all.OrderBy(e => e.Source.ToString())
                : all.OrderByDescending(e => e.Source.ToString()),
            _ => _extSortAscending
                ? all.OrderBy(e => e.Name)
                : all.OrderByDescending(e => e.Name),
        };

        PanelExtensions.Children.Clear();

        if (!sorted.Any())
        {
            PanelExtensions.Children.Add(new TextBlock
            {
                Text = "No extensions installed.\n\nDrop an unpacked extension folder\ninto the extensions directory,\nthen click ⟳ Refresh.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0),
            });
            return;
        }

        foreach (var ext in sorted)
        {
            var card = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(6, 5, 6, 5),
                Margin          = new Thickness(0, 0, 0, 4),
                Tag             = ext,  // stash for context menu
            };
            SetCardBorder(card, ext.Enabled);
            card.MouseRightButtonUp += ExtCard_RightClick;

            // Left-click opens the extension's popup (like clicking its toolbar icon in Chrome/Edge)
            var captExtPopup = ext;
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, me) =>
            {
                // Don't fire if the click was on toggle/trash buttons
                if (me.OriginalSource is FrameworkElement src &&
                    (src.Name == "toggle" || FindParent<Button>(src) != null)) return;
                if (captExtPopup.Enabled)
                    RequestExtensionPopup?.Invoke(this, captExtPopup);
                else
                    ShowStatus("Enable this extension first, then restart Horizon.");
                me.Handled = true;
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var iconTb = new TextBlock { Text = ext.Icon, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(iconTb, 0); Grid.SetRowSpan(iconTb, 2);

            var nameTb = new TextBlock { Text = ext.Name, Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)), FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(nameTb, 1); Grid.SetRow(nameTb, 0);

            string srcLabel = ext.Source switch
            {
                ExtensionSource.Bundled      => "bundled",
                ExtensionSource.ChromeStore  => "Chrome",
                ExtensionSource.EdgeStore    => "Edge",
                ExtensionSource.FirefoxStore => "Firefox",
                _                            => "manual",
            };
            string badge = string.IsNullOrEmpty(ext.Version) ? srcLabel : $"v{ext.Version} · {srcLabel}";
            var badgeTb = new TextBlock { Text = badge, Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x55, 0x33)), FontSize = 9, Margin = new Thickness(0, 2, 0, 0) };
            Grid.SetColumn(badgeTb, 1); Grid.SetRow(badgeTb, 1);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(btns, 2); Grid.SetRowSpan(btns, 2);

            var toggle = new Button { Content = ext.Enabled ? "ON" : "OFF", Width = 38, Height = 24, FontSize = 8, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
            ApplyToggleStyle(toggle, ext.Enabled);
            toggle.Template = RoundedTemplate(3);
            var captExt = ext; var captCard = card;
            toggle.Click += (_, _) =>
            {
                bool ns = !captExt.Enabled;
                ExtensionService.SetEnabled(captExt.Id, ns);
                toggle.Content = ns ? "ON" : "OFF";
                ApplyToggleStyle(toggle, ns);
                SetCardBorder(captCard, ns);
                ShowStatus("⚡ Restart Horizon to apply changes.");
            };

            bool isBundled = ext.Source == ExtensionSource.Bundled;
            var trash = new Button
            {
                Content = isBundled ? "✕" : "🗑", Width = 24, Height = 24, FontSize = 9,
                Background      = new SolidColorBrush(Color.FromRgb(0x22, 0x08, 0x08)),
                Foreground      = new SolidColorBrush(Color.FromRgb(0x88, 0x22, 0x22)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                ToolTip         = isBundled ? "Disable (bundled — cannot be removed)" : "Uninstall",
            };
            trash.Template = RoundedTemplate(3);
            trash.Click += (_, _) => { ExtensionService.Uninstall(captExt.Id); ShowStatus("⚡ Restart Horizon to apply changes."); };

            btns.Children.Add(toggle); btns.Children.Add(trash);
            row.Children.Add(iconTb); row.Children.Add(nameTb); row.Children.Add(badgeTb); row.Children.Add(btns);
            card.Child = row;
            PanelExtensions.Children.Add(card);
        }

        UpdateExtSortButtonStyles();
    }

    // Extensions sort buttons
    private void BtnExtSortName_Click(object sender, RoutedEventArgs e)   { _extSortField = ExtSortField.Name;   BuildExtensionCards(); }
    private void BtnExtSortState_Click(object sender, RoutedEventArgs e)  { _extSortField = ExtSortField.State;  BuildExtensionCards(); }
    private void BtnExtSortSource_Click(object sender, RoutedEventArgs e) { _extSortField = ExtSortField.Source; BuildExtensionCards(); }

    private void BtnExtInstall_Click(object sender, RoutedEventArgs e)
    {
        RequestNavigate?.Invoke(this, "https://chromewebstore.google.com/");
    }

    private async void BtnExtInstallUrl_Click(object sender, RoutedEventArgs e)
    {
        var tb = new TextBox
        {
            Height                   = 28,
            Background               = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a)),
            Foreground               = Brushes.White,
            BorderBrush              = new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0xcc)),
            BorderThickness          = new Thickness(1),
            Padding                  = new Thickness(6, 4, 6, 4),
            FontSize                 = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var okBtn = new Button
        {
            Content         = "Install",
            Width           = 80,
            Background      = new SolidColorBrush(Color.FromRgb(0x22, 0x44, 0xaa)),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            Height          = 28,
            Cursor          = Cursors.Hand,
            Margin          = new Thickness(8, 0, 0, 0),
        };

        var win = new Window
        {
            Title                 = "Install Extension — Horizon",
            Width                 = 440,
            Height                = 130,
            ResizeMode            = ResizeMode.NoResize,
            WindowStyle           = WindowStyle.ToolWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner                 = Window.GetWindow(this),
            Background            = new SolidColorBrush(Color.FromRgb(0x0f, 0x0f, 0x0f)),
        };
        okBtn.Click += (_, _) => win.DialogResult = true;
        tb.KeyDown  += (_, k) => { if (k.Key == Key.Enter) win.DialogResult = true; };

        var row = new DockPanel();
        DockPanel.SetDock(okBtn, Dock.Right);
        row.Children.Add(okBtn);
        row.Children.Add(tb);

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 0) };
        panel.Children.Add(new TextBlock
        {
            Text       = "Paste a Chrome / Edge / Firefox extension store URL:",
            Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xbb, 0xff)),
            FontSize   = 11,
            Margin     = new Thickness(0, 0, 0, 10),
        });
        panel.Children.Add(row);
        win.Content = panel;

        if (win.ShowDialog() != true) return;

        string url  = tb.Text.Trim();
        var info    = ExtensionInstaller.Detect(url);
        if (!info.IsStorePage)
        {
            ShowStatus("✗  Not a recognised extension store URL.");
            return;
        }

        ShowStatus($"⏳  Installing \"{info.Name}\"…");
        BtnExtInstallUrl.IsEnabled = false;

        var progress = new Progress<string>(msg =>
            Dispatcher.Invoke(() => ShowStatus($"⏳  {msg}")));
        var record = await ExtensionInstaller.InstallAsync(info, progress);

        BtnExtInstallUrl.IsEnabled = true;
        if (record != null)
        {
            BuildExtensionCards();
            ShowStatus("✓  Installed! Restart Horizon to apply.");
        }
        else
        {
            ShowStatus("✗  Installation failed. Check logs.");
        }
    }

    private void UpdateExtSortButtonStyles()
    {
        SetSortBtnActive(BtnExtSortName,  _extSortField == ExtSortField.Name);
        SetSortBtnActive(BtnExtSortState, _extSortField == ExtSortField.State);
    }

    // Extensions right-click
    private ExtensionRecord? _ctxExt;

    private void ExtCard_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border card && card.Tag is ExtensionRecord ext)
        {
            _ctxExt = ext;
            TxtExtSearch.Text = string.Empty;
            ExtContextPopup.IsOpen = true;
            e.Handled = true;
        }
    }

    private void CtxExtToggle_Click(object sender, RoutedEventArgs e)
    {
        ExtContextPopup.IsOpen = false;
        if (_ctxExt == null) return;
        bool ns = !_ctxExt.Enabled;
        ExtensionService.SetEnabled(_ctxExt.Id, ns);
        ShowStatus("⚡ Restart Horizon to apply changes.");
        BuildExtensionCards();
    }

    private void CtxExtOpen_Click(object sender, RoutedEventArgs e)
    {
        ExtContextPopup.IsOpen = false;
        if (_ctxExt == null) return;
        if (_ctxExt.Enabled)
            RequestExtensionPopup?.Invoke(this, _ctxExt);
        else
            ShowStatus("Enable this extension first, then restart Horizon.");
    }

    private void CtxExtSortName_Click(object sender, RoutedEventArgs e)   { ExtContextPopup.IsOpen = false; BtnExtSortName_Click(sender, e); }
    private void CtxExtSortState_Click(object sender, RoutedEventArgs e)  { ExtContextPopup.IsOpen = false; BtnExtSortState_Click(sender, e); }
    private void CtxExtSortSource_Click(object sender, RoutedEventArgs e) { ExtContextPopup.IsOpen = false; BtnExtSortSource_Click(sender, e); }

    private void CtxExtUninstall_Click(object sender, RoutedEventArgs e)
    {
        ExtContextPopup.IsOpen = false;
        if (_ctxExt == null) return;
        ExtensionService.Uninstall(_ctxExt.Id);
        ShowStatus("⚡ Restart Horizon to apply changes.");
        BuildExtensionCards();
    }

    // Extensions context search — filters cards in real time
    private void TxtExtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtExtSearchHint.Visibility = string.IsNullOrEmpty(TxtExtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        string q = TxtExtSearch.Text.Trim().ToLowerInvariant();
        foreach (UIElement child in PanelExtensions.Children)
        {
            if (child is Border card && card.Tag is ExtensionRecord ext)
                card.Visibility = string.IsNullOrEmpty(q) || ext.Name.ToLowerInvariant().Contains(q)
                    ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BtnExtOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(ExtensionService.InstallRoot); Process.Start("explorer.exe", ExtensionService.InstallRoot); }
        catch (Exception ex) { LogService.RecordCrash(ex, "ExtOpenFolder"); }
    }

    private void BtnExtRefresh_Click(object sender, RoutedEventArgs e)
    {
        ExtensionService.EnsureInstalled();
        BuildExtensionCards();
        ShowStatus("Catalog refreshed.");
    }

    private void ShowStatus(string msg)
    {
        TxtStatus.Text = msg;
        BannerStatus.Visibility = Visibility.Visible;
    }

    private static void ApplyToggleStyle(Button b, bool on)
    {
        b.Background  = new SolidColorBrush(on ? Color.FromRgb(0x00, 0x33, 0x00) : Color.FromRgb(0x22, 0x22, 0x22));
        b.Foreground  = new SolidColorBrush(on ? Color.FromRgb(0x00, 0xFF, 0x00) : Color.FromRgb(0x55, 0x55, 0x55));
        b.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x00, 0x88, 0x00) : Color.FromRgb(0x33, 0x33, 0x33));
    }

    private static void SetCardBorder(Border card, bool on)
        => card.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x00, 0x55, 0x00) : Color.FromRgb(0x22, 0x22, 0x22));

    private static ControlTemplate RoundedTemplate(int r = 4)
    {
        var t  = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetBinding(Border.BackgroundProperty,      new Binding("Background")      { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.BorderBrushProperty,     new Binding("BorderBrush")     { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(r));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PINS / FAVOURITES
    // ══════════════════════════════════════════════════════════════════════════

    private void AddDefaultPin()
    {
        string targetUrl  = "https://ghappstore-j93n3sq2.manus.space/";
        string targetName = "AppStore";
        if (!SettingsService.Current.PinnedUrls.Any(p => p.Url == targetUrl))
        {
            SettingsService.Current.PinnedUrls.Insert(0, new PinItem { Name = targetName, Url = targetUrl });
            SettingsService.Save();
        }
        targetUrl  = "https://feedbackcol-nszemvwz.manus.space/";
        targetName = "Feedback";
        if (!SettingsService.Current.PinnedUrls.Any(p => p.Url == targetUrl))
        {
            SettingsService.Current.PinnedUrls.Insert(1, new PinItem { Name = targetName, Url = targetUrl });
            SettingsService.Save();
        }
        targetUrl  = "https://manussend-8ladxhzk.manus.space/";
        targetName = "File Sharing";
        if (!SettingsService.Current.PinnedUrls.Any(p => p.Url == targetUrl))
        {
            SettingsService.Current.PinnedUrls.Insert(1, new PinItem { Name = targetName, Url = targetUrl });
            SettingsService.Save();
        }
    }

    



    private void RefreshPins()
    {
        var pins = SettingsService.Current.PinnedUrls.AsEnumerable();

        // Apply sort (skip when in manual order mode)
        if (_pinSortField != PinSortField.Manual)
        {
            pins = _pinSortField switch
            {
                PinSortField.Category => _pinSortAscending
                    ? pins.OrderBy(p => p.Category).ThenBy(p => p.Name)
                    : pins.OrderByDescending(p => p.Category).ThenBy(p => p.Name),
                _ => _pinSortAscending
                    ? pins.OrderBy(p => p.Name)
                    : pins.OrderByDescending(p => p.Name),
            };
        }

        ListPins.ItemsSource = pins.ToList();
        UpdatePinSortButtonStyles();
    }

    private void UpdatePinSortButtonStyles()
    {
        SetSortBtnActive(BtnPinSortName, _pinSortField == PinSortField.Name);
        SetSortBtnActive(BtnPinSortCat,  _pinSortField == PinSortField.Category);
        BtnPinSortDir.Content    = _pinSortAscending ? "↑" : "↓";
        BtnPinSortDir.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    }

    private void BtnPinSortName_Click(object sender, RoutedEventArgs e) { _pinSortField = PinSortField.Name;     RefreshPins(); }
    private void BtnPinSortCat_Click(object sender, RoutedEventArgs e)  { _pinSortField = PinSortField.Category; RefreshPins(); }
    private void BtnPinSortDir_Click(object sender, RoutedEventArgs e)  { _pinSortAscending = !_pinSortAscending; RefreshPins(); }

    private void SortBtn_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Button btn) return;
        e.Handled = true;

        var menu = new ContextMenu();
        var item = new MenuItem { Header = "☑  Set as default" };
        item.Click += (_, _) =>
        {
            switch (btn.Name)
            {
                case "BtnPinSortName": _pinSortField = PinSortField.Name;         break;
                case "BtnPinSortCat":  _pinSortField = PinSortField.Category;     break;
                case "BtnPinSortDir":                                              break;
                case "BtnPinSearch":                                               break;
                case "BtnBmSortDate":   _bmSortField = BmSortField.Date;          break;
                case "BtnBmSortName":   _bmSortField = BmSortField.Name;          break;
                case "BtnBmSortDomain": _bmSortField = BmSortField.Domain;        break;
                case "BtnBmSortDir":                                               break;
                case "BtnHistSortDate":   _histSortField = HistSortField.Date;    break;
                case "BtnHistSortTitle":  _histSortField = HistSortField.Title;   break;
                case "BtnHistSortDomain": _histSortField = HistSortField.Domain;  break;
                case "BtnHistSortDir":                                             break;
                case "BtnExtSortName":   _extSortField = ExtSortField.Name;       break;
                case "BtnExtSortState":  _extSortField = ExtSortField.State;      break;
                case "BtnExtSortSource": _extSortField = ExtSortField.Source;     break;
                case "BtnSortDate":      _sortField = SortField.Date;             break;
                case "BtnSortName":      _sortField = SortField.Name;             break;
                case "BtnSortSize":      _sortField = SortField.Size;             break;
                case "BtnSortDir":                                                 break;
            }
            SaveSortDefaults();
        };
        menu.Items.Add(item);
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private static string SortDefaultsPath =>
        Path.Combine(Horizon.Stealth.Services.ConfigService.UserDataRoot, "flux_sort_defaults.json");

    private void LoadSortDefaults()
    {
        try
        {
            if (!File.Exists(SortDefaultsPath)) return;
            var json = File.ReadAllText(SortDefaultsPath);
            var doc  = JsonDocument.Parse(json).RootElement;
            if (doc.TryGetProperty("PinSort",  out var ps))  _pinSortField    = Enum.Parse<PinSortField>(ps.GetString()!,  true);
            if (doc.TryGetProperty("PinAsc",   out var pa))  _pinSortAscending  = pa.GetBoolean();
            if (doc.TryGetProperty("BmSort",   out var bs))  _bmSortField     = Enum.Parse<BmSortField>(bs.GetString()!,   true);
            if (doc.TryGetProperty("BmAsc",    out var ba))  _bmSortAscending   = ba.GetBoolean();
            if (doc.TryGetProperty("HistSort", out var hs))  _histSortField   = Enum.Parse<HistSortField>(hs.GetString()!, true);
            if (doc.TryGetProperty("HistAsc",  out var ha))  _histSortAscending = ha.GetBoolean();
            if (doc.TryGetProperty("ExtSort",  out var es))  _extSortField    = Enum.Parse<ExtSortField>(es.GetString()!,  true);
            if (doc.TryGetProperty("ExtAsc",   out var ea))  _extSortAscending  = ea.GetBoolean();
            if (doc.TryGetProperty("FluxSort", out var fs))  _sortField       = Enum.Parse<SortField>(fs.GetString()!,     true);
            if (doc.TryGetProperty("FluxAsc",  out var fa))  _sortAscending     = fa.GetBoolean();
        }
        catch { }
    }

    private void SaveSortDefaults()
    {
        try
        {
            var obj = new
            {
                PinSort  = _pinSortField.ToString(),
                PinAsc   = _pinSortAscending,
                BmSort   = _bmSortField.ToString(),
                BmAsc    = _bmSortAscending,
                HistSort = _histSortField.ToString(),
                HistAsc  = _histSortAscending,
                ExtSort  = _extSortField.ToString(),
                ExtAsc   = _extSortAscending,
                FluxSort = _sortField.ToString(),
                FluxAsc  = _sortAscending,
            };
            File.WriteAllText(SortDefaultsPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void BtnPinManageCats_Click(object sender, RoutedEventArgs e)
    {
        _categoryEditMode   = CategoryEditMode.Add;
        TxtCategoryEditTitle.Text = "ADD CATEGORY";
        TxtCategoryEditInput.Text = "";
        OverlayCategoryEdit.Visibility = Visibility.Visible;
        TxtCategoryEditInput.Focus();
    }

    private void BtnAddPin_Click(object sender, RoutedEventArgs e) => RequestAddPin?.Invoke(this, EventArgs.Empty);

    public void AddPin(string url, string title = "")
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (string.IsNullOrWhiteSpace(title)) title = url;
        if (!SettingsService.Current.PinnedUrls.Any(p => p.Url == url))
        {
            SettingsService.Current.PinnedUrls.Add(new PinItem { Name = title, Url = url });
            SettingsService.Save();
            RefreshPins();
        }
    }

    private void ListPins_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListPins.SelectedItem is PinItem item) RequestNavigate?.Invoke(this, item.Url);
    }

    private void BtnRemovePin_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        if (ListPins.SelectedItem is PinItem item)
        {
            SettingsService.Current.PinnedUrls.Remove(item);
            SettingsService.Save();
            RefreshPins();
        }
    }

    private void BtnPinUp_Click(object sender, RoutedEventArgs e)
    {
        if (ListPins.SelectedItem is not PinItem item) return;
        if (_pinSortField != PinSortField.Manual) MaterialisePinSortOrder();
        var list  = SettingsService.Current.PinnedUrls;
        int index = list.IndexOf(item);
        if (index > 0) { list.RemoveAt(index); list.Insert(index - 1, item); SettingsService.Save(); _pinSortField = PinSortField.Manual; RefreshPins(); ListPins.SelectedItem = item; }
    }

    private void BtnPinDown_Click(object sender, RoutedEventArgs e)
    {
        if (ListPins.SelectedItem is not PinItem item) return;
        if (_pinSortField != PinSortField.Manual) MaterialisePinSortOrder();
        var list  = SettingsService.Current.PinnedUrls;
        int index = list.IndexOf(item);
        if (index < list.Count - 1) { list.RemoveAt(index); list.Insert(index + 1, item); SettingsService.Save(); _pinSortField = PinSortField.Manual; RefreshPins(); ListPins.SelectedItem = item; }
    }

    private void MaterialisePinSortOrder()
    {
        var backing = SettingsService.Current.PinnedUrls;
        var sorted = (_pinSortField switch
        {
            PinSortField.Category => _pinSortAscending
                ? backing.OrderBy(p => p.Category).ThenBy(p => p.Name)
                : backing.OrderByDescending(p => p.Category).ThenBy(p => p.Name),
            PinSortField.Name => _pinSortAscending
                ? backing.OrderBy(p => p.Name)
                : backing.OrderByDescending(p => p.Name),
            _ => backing.AsEnumerable(),
        }).ToList();
        backing.Clear();
        foreach (var p in sorted) backing.Add(p);
    }

    private void BtnPinSearch_Click(object sender, RoutedEventArgs e)
    {
        bool show = PinSearchRow.Visibility != Visibility.Visible;
        PinSearchRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
            TxtPinSearchBar.Focus();
        else
        {
            TxtPinSearchBar.Text = string.Empty;
            RefreshPins();
        }
        BtnPinSearch.Foreground = show
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0x00))
            : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        BtnPinSearch.Background = new SolidColorBrush(Color.FromRgb(
            show ? (byte)0x1a : (byte)0x11,
            show ? (byte)0x2a : (byte)0x11,
            show ? (byte)0x1a : (byte)0x11));
    }

    private void TxtPinSearchBar_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtPinSearchBarHint.Visibility = string.IsNullOrEmpty(TxtPinSearchBar.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        string q = TxtPinSearchBar.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q)) { RefreshPins(); return; }

        var pins = SettingsService.Current.PinnedUrls.AsEnumerable();
        if (_pinSortField != PinSortField.Manual)
        {
            pins = _pinSortField switch
            {
                PinSortField.Category => _pinSortAscending
                    ? pins.OrderBy(p => p.Category).ThenBy(p => p.Name)
                    : pins.OrderByDescending(p => p.Category).ThenBy(p => p.Name),
                _ => _pinSortAscending
                    ? pins.OrderBy(p => p.Name)
                    : pins.OrderByDescending(p => p.Name),
            };
        }
        ListPins.ItemsSource = pins
            .Where(p => p.Name.ToLowerInvariant().Contains(q)
                     || (p.Url?.ToLowerInvariant().Contains(q) ?? false)
                     || (p.Category?.ToLowerInvariant().Contains(q) ?? false))
            .ToList();
    }

    private void BtnRenamePin_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        if (ListPins.SelectedItem is PinItem item)
        {
            _itemBeingRenamed        = item;
            TxtRenameInput.Text      = item.Name;
            OverlayRename.Visibility = Visibility.Visible;
            TxtRenameInput.Focus();
        }
    }

    private void BtnCancelRename_Click(object sender, RoutedEventArgs e)
    {
        OverlayRename.Visibility = Visibility.Collapsed;
        _itemBeingRenamed = null;
    }

    private void BtnSaveRename_Click(object sender, RoutedEventArgs e)
    {
        if (_itemBeingRenamed != null)
        {
            _itemBeingRenamed.Name = TxtRenameInput.Text;
            SettingsService.Save();
            RefreshPins();
        }
        OverlayRename.Visibility = Visibility.Collapsed;
        _itemBeingRenamed = null;
    }

    // ── Pins right-click context menu ─────────────────────────────────────────

    private void ListPins_RightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(ListPins, e.GetPosition(ListPins));
        if (hit?.VisualHit is DependencyObject dep)
        {
            var container = FindParent<ListBoxItem>(dep);
            if (container != null) container.IsSelected = true;
        }
        TxtPinsSearch.Text = string.Empty;
        PinsContextPopup.IsOpen = true;
        e.Handled = true;
    }

    private void CtxPinChangeWebsite_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        if (ListPins.SelectedItem is not PinItem pin) return;

        var dlg = new System.Windows.Window
        {
            Title           = $"Change Website — {pin.Name}",
            Width           = 380,
            Height          = 150,
            WindowStyle     = System.Windows.WindowStyle.ToolWindow,
            ResizeMode      = ResizeMode.NoResize,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner           = System.Windows.Window.GetWindow(this),
            Background      = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
        };

        var sp = new StackPanel { Margin = new Thickness(14) };

        var lbl   = new TextBlock { Text = "URL", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
        var txUrl = new TextBox  { Text = pin.Url, Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), Height = 28, Padding = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };

        var btnRow    = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var btnCancel = new Button { Content = "CANCEL", Width = 68, Height = 26, Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), BorderThickness = new Thickness(0) };
        var btnSave   = new Button { Content = "SAVE",   Width = 68, Height = 26, Background = new SolidColorBrush(Color.FromRgb(0x00, 0x44, 0x00)), Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x00)), BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold };

        btnCancel.Click += (_, _) => dlg.Close();
        btnSave.Click += (_, _) =>
        {
            string url = txUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            pin.Url = url;
            SettingsService.Save();
            RefreshPins();
            dlg.Close();
        };

        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnSave);
        sp.Children.Add(lbl);
        sp.Children.Add(txUrl);
        sp.Children.Add(btnRow);
        dlg.Content = sp;
        txUrl.SelectAll();
        txUrl.Focus();
        dlg.ShowDialog();
    }

    

    private void CtxPinMoveUp_Click(object sender, RoutedEventArgs e)   { PinsContextPopup.IsOpen = false; BtnPinUp_Click(sender, e); }
    private void CtxPinMoveDown_Click(object sender, RoutedEventArgs e) { PinsContextPopup.IsOpen = false; BtnPinDown_Click(sender, e); }
    private void CtxPinRename_Click(object sender, RoutedEventArgs e)   { BtnRenamePin_Click(sender, e); }

    private void CtxPinSetIcon_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        if (ListPins.SelectedItem is not PinItem pin) return;

        // ── Build the icon picker dialog ──────────────────────────────────────
        var dlg = new System.Windows.Window
        {
            Title           = $"Set Icon — {pin.Name}",
            Width           = 360,
            Height          = 300,
            WindowStyle     = System.Windows.WindowStyle.ToolWindow,
            ResizeMode      = ResizeMode.NoResize,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner           = System.Windows.Window.GetWindow(this),
            Background      = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
        };

        var root = new StackPanel { Margin = new Thickness(14) };

        root.Children.Add(new TextBlock
        {
            Text = "CHOOSE EMOJI  or  UPLOAD IMAGE",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 10, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // ── Emoji grid ───────────────────────────────────────────────────────
        string[] emojis =
        {
            "⭐","🌟","💫","✨","🔴","🟠","🟡","🟢","🔵","🟣","⚪","⚫",
            "🌐","🏠","🔗","📡","🛜","🔍","🌍","🌏","🗺","🧭",
            "🎮","🕹","🎯","🏆","🥇","🎉","🎊","🎁","🎨","🎭","🎬","📺","🎞","📽",
            "🎵","🎧","🎙","📻","🎸","🎹","🎺","🥁","🎼","🎤",
            "💼","📁","📂","📄","📋","📊","📈","📌","📎","✏","🖊","📝","🗒","📅",
            "🖥","💻","🖨","⌨","🖱","📱","📞","📟","☎","📠",
            "📧","💬","🗣","👥","🔔","📣","📢","🗨","💌",
            "🛒","💳","💰","💵","💎","🏦","🏪","🏬","🧾","🪙",
            "📚","📖","🎓","🧠","🔬","🔭","📐","📏","🧪","🧬","⚗","🏫",
            "🔧","🔨","⚙","🛠","🔩","🔑","🔒","🔓","🛡","⚔","🪛","🪚",
            "🚀","✈","🚗","🚂","⚡","🛸","🛩","🚁","⛵","🏎","🚀",
            "🌿","🌱","🌸","🍀","🌊","💧","❄","☀","🌙","🔥","🌈","⛅","🌪","🌻","🍃",
            "❤","🧡","💛","💚","💙","💜","🖤","🤍","❤‍🔥","💔",
            "💡","🕯","🔦","🏮","🪔","🌠","🌌","🪐","☄","🌅",
            "🐱","🐶","🦊","🐸","🐧","🦁","🐉","🦋","🦄","🐺","🦅","🐬",
            "🍎","🍕","☕","🧃","🍺","🍓","🧁","🍜","🥗","🍔","🍩",
            "🏋","🧘","⚽","🏀","🎾","🏊","🚴","🏇","🥊","🏹",
            "🏔","🏖","🏕","🗼","🏛","🗽","🏰","⛩","🕌","🗿",
        };

        var wrapPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var em in emojis)
        {
            string captured = em;
            var btn = new Button
            {
                Content         = em,
                FontSize        = 20,
                Width           = 40, Height = 40,
                Margin          = new Thickness(2),
                Background      = System.Windows.Media.Brushes.White,
                Foreground      = System.Windows.Media.Brushes.Black,
                BorderThickness = new Thickness(1),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                Cursor          = Cursors.Hand,
            };
            btn.Click += (_, _) =>
            {
                pin.IconEmoji = captured;
                pin.IconPath  = "";
                SettingsService.Save();
                RefreshPins();
                dlg.Close();
            };
            wrapPanel.Children.Add(btn);
        }
        root.Children.Add(wrapPanel);

        // ── Button row ───────────────────────────────────────────────────────
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };

        var btnUpload = new Button
        {
            Content         = "📁  Upload Image",
            Height          = 30, Padding = new Thickness(10, 0, 10, 0),
            Margin          = new Thickness(0, 0, 6, 0),
            Background      = new SolidColorBrush(Color.FromRgb(0x1a, 0x2a, 0x1a)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0x00)),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
        };
        btnUpload.Click += (_, _) =>
        {
            var fd = new OpenFileDialog
            {
                Title  = "Choose Icon Image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.ico;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.ico;*.webp;*.bmp|All files (*.*)|*.*"
            };
            if (fd.ShowDialog() != true) return;
            string stored = BookmarkService.CopyFavouriteIconToData(fd.FileName);
            if (string.IsNullOrEmpty(stored)) return;
            pin.IconPath  = stored;
            pin.IconEmoji = "";
            SettingsService.Save();
            RefreshPins();
            dlg.Close();
        };

        var btnClear = new Button
        {
            Content         = "↺  Use Default",
            Height          = 30, Padding = new Thickness(10, 0, 10, 0),
            Background      = new SolidColorBrush(Color.FromRgb(0x2a, 0x10, 0x10)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
        };
        btnClear.Click += (_, _) =>
        {
            pin.IconEmoji = "";
            pin.IconPath  = "";
            SettingsService.Save();
            RefreshPins();
            dlg.Close();
        };

        btnRow.Children.Add(btnUpload);
        btnRow.Children.Add(btnClear);
        root.Children.Add(btnRow);
        dlg.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dlg.ShowDialog();
    }

    private void CtxPinChangeCategory_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        if (ListPins.SelectedItem is not PinItem item) return;

        TxtChangeCatPinName.Text = item.Name;
        var cats = SettingsService.Current.PinnedUrls
            .Select(p => p.Category ?? "").Where(c => c != "").Distinct().OrderBy(c => c).ToList();
        cats.Insert(0, "(none)");
        ListChangeCatOptions.ItemsSource = cats;
        ListChangeCatOptions.SelectedItem = string.IsNullOrEmpty(item.Category) ? "(none)" : item.Category;
        OverlayChangeCategory.Visibility = Visibility.Visible;
    }

    private void ListChangeCatOptions_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void BtnCancelChangeCategory_Click(object sender, RoutedEventArgs e)
        => OverlayChangeCategory.Visibility = Visibility.Collapsed;

    private void BtnApplyChangeCategory_Click(object sender, RoutedEventArgs e)
    {
        OverlayChangeCategory.Visibility = Visibility.Collapsed;
        if (ListPins.SelectedItem is not PinItem item) return;
        string sel = ListChangeCatOptions.SelectedItem as string ?? "";
        item.Category = sel == "(none)" ? "" : sel;
        SettingsService.Save();
        RefreshPins();
    }

    private void CtxPinAddCategory_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        _categoryEditMode          = CategoryEditMode.Add;
        TxtCategoryEditTitle.Text  = "ADD CATEGORY";
        TxtCategoryEditInput.Text  = "";
        OverlayCategoryEdit.Visibility = Visibility.Visible;
        TxtCategoryEditInput.Focus();
    }

    private void CtxPinRenameCategory_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        // Rename the category of the selected pin's current category
        if (ListPins.SelectedItem is not PinItem item || string.IsNullOrEmpty(item.Category)) return;
        _categoryEditMode          = CategoryEditMode.Rename;
        _categoryEditTarget        = item.Category;
        TxtCategoryEditTitle.Text  = $"RENAME CATEGORY: {item.Category}";
        TxtCategoryEditInput.Text  = item.Category;
        OverlayCategoryEdit.Visibility = Visibility.Visible;
        TxtCategoryEditInput.Focus();
    }

    private void CtxPinDeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        if (ListPins.SelectedItem is not PinItem item || string.IsNullOrEmpty(item.Category)) return;
        _categoryEditMode          = CategoryEditMode.Delete;
        _categoryEditTarget        = item.Category;
        TxtCategoryEditTitle.Text  = $"DELETE CATEGORY: {item.Category}";
        TxtCategoryEditInput.Text  = item.Category;
        OverlayCategoryEdit.Visibility = Visibility.Visible;
    }

    private void BtnCancelCategoryEdit_Click(object sender, RoutedEventArgs e)
        => OverlayCategoryEdit.Visibility = Visibility.Collapsed;

    private void BtnSaveCategoryEdit_Click(object sender, RoutedEventArgs e)
    {
        OverlayCategoryEdit.Visibility = Visibility.Collapsed;
        string newName = TxtCategoryEditInput.Text.Trim();

        switch (_categoryEditMode)
        {
            case CategoryEditMode.Add:
                // The category is created by assigning it to pins; nothing more needed here.
                // If the user wants to assign this category to the selected pin, do it now.
                if (ListPins.SelectedItem is PinItem selAdd && !string.IsNullOrEmpty(newName))
                {
                    selAdd.Category = newName;
                    SettingsService.Save();
                }
                break;

            case CategoryEditMode.Rename:
                foreach (var p in SettingsService.Current.PinnedUrls.Where(p => p.Category == _categoryEditTarget))
                    p.Category = newName;
                SettingsService.Save();
                break;

            case CategoryEditMode.Delete:
                foreach (var p in SettingsService.Current.PinnedUrls.Where(p => p.Category == _categoryEditTarget))
                    p.Category = "";
                SettingsService.Save();
                break;
        }

        RefreshPins();
    }

    // Pins context search — filters visible items inline
    private void TxtPinsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtPinsSearchHint.Visibility = string.IsNullOrEmpty(TxtPinsSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        string q = TxtPinsSearch.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q)) { RefreshPins(); return; }

        var all = SettingsService.Current.PinnedUrls
            .Where(p => p.Name.ToLowerInvariant().Contains(q)
                     || (p.Url?.ToLowerInvariant().Contains(q) ?? false)
                     || (p.Category?.ToLowerInvariant().Contains(q) ?? false))
            .ToList();
        ListPins.ItemsSource = all;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // BOOKMARKS
    // ══════════════════════════════════════════════════════════════════════════

    public void RefreshBookmarks()
    {
        var bookmarks = BookmarkService.Items;
        if (bookmarks == null || bookmarks.Count == 0)
        {
            ListBookmarks.ItemsSource        = null;
            ListBookmarks.Visibility         = Visibility.Collapsed;
            PanelBookmarkFallback.Visibility = Visibility.Visible;
            return;
        }

        IEnumerable<BookmarkItem> sorted = _bmSortField switch
        {
            BmSortField.Name   => _bmSortAscending
                ? bookmarks.OrderBy(b => b.Title)
                : bookmarks.OrderByDescending(b => b.Title),
            BmSortField.Domain => _bmSortAscending
                ? bookmarks.OrderBy(b => GetDomain(b.Url))
                : bookmarks.OrderByDescending(b => GetDomain(b.Url)),
            _ => _bmSortAscending
                ? bookmarks.OrderBy(b => b.DateAdded)
                : bookmarks.OrderByDescending(b => b.DateAdded),
        };

        ListBookmarks.ItemsSource        = sorted.ToList();
        ListBookmarks.Visibility         = Visibility.Visible;
        PanelBookmarkFallback.Visibility = Visibility.Collapsed;
        UpdateBmSortButtonStyles();
    }

    private void UpdateBmSortButtonStyles()
    {
        SetSortBtnActive(BtnBmSortDate,   _bmSortField == BmSortField.Date);
        SetSortBtnActive(BtnBmSortName,   _bmSortField == BmSortField.Name);
        SetSortBtnActive(BtnBmSortDomain, _bmSortField == BmSortField.Domain);
        BtnBmSortDir.Content    = _bmSortAscending ? "↑" : "↓";
        BtnBmSortDir.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    }

    private void BtnBmSortDate_Click(object sender, RoutedEventArgs e)   { _bmSortField = BmSortField.Date;   RefreshBookmarks(); }
    private void BtnBmSortName_Click(object sender, RoutedEventArgs e)   { _bmSortField = BmSortField.Name;   RefreshBookmarks(); }
    private void BtnBmSortDomain_Click(object sender, RoutedEventArgs e) { _bmSortField = BmSortField.Domain; RefreshBookmarks(); }
    private void BtnBmSortDir_Click(object sender, RoutedEventArgs e)    { _bmSortAscending = !_bmSortAscending; RefreshBookmarks(); }

    private void ListBookmarks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListBookmarks.SelectedItem is BookmarkItem item) RequestNavigate?.Invoke(this, item.Url);
    }

    private void ListBookmarks_RightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(ListBookmarks, e.GetPosition(ListBookmarks));
        if (hit?.VisualHit is DependencyObject dep)
        {
            var container = FindParent<ListBoxItem>(dep);
            if (container != null) container.IsSelected = true;
        }
        TxtBookmarksSearch.Text = string.Empty;
        BookmarksContextPopup.IsOpen = true;
        e.Handled = true;
    }

    private void TxtBookmarksSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtBookmarksSearchHint.Visibility = string.IsNullOrEmpty(TxtBookmarksSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        string q = TxtBookmarksSearch.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q)) { RefreshBookmarks(); return; }

        var filtered = (BookmarkService.Items ?? new List<BookmarkItem>())
            .Where(b => (b.Title?.ToLowerInvariant().Contains(q) ?? false)
                     || (b.Url?.ToLowerInvariant().Contains(q)   ?? false))
            .ToList();
        ListBookmarks.ItemsSource        = filtered;
        ListBookmarks.Visibility         = Visibility.Visible;
        PanelBookmarkFallback.Visibility = Visibility.Collapsed;
    }

    private void CtxBmSortDate_Click(object sender, RoutedEventArgs e)   { BookmarksContextPopup.IsOpen = false; BtnBmSortDate_Click(sender, e); }
    private void CtxBmSortName_Click(object sender, RoutedEventArgs e)   { BookmarksContextPopup.IsOpen = false; BtnBmSortName_Click(sender, e); }
    private void CtxBmSortDomain_Click(object sender, RoutedEventArgs e) { BookmarksContextPopup.IsOpen = false; BtnBmSortDomain_Click(sender, e); }

    private void BtnManualBookmarkImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "HTML Files (*.html)|*.html|All files (*.*)|*.*", Title = "Select Bookmark HTML File" };
        if (dlg.ShowDialog() == true)
        {
            try { BookmarkService.ImportHtml(dlg.FileName); RefreshBookmarks(); }
            catch (Exception ex) { LogService.RecordCrash(ex, "Manual Bookmark Import"); MessageBox.Show("Extraction Failed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    private void BtnBookmarkCopy_Click(object sender, RoutedEventArgs e)
    {
        BookmarksContextPopup.IsOpen = false;
        if (ListBookmarks.SelectedItem is BookmarkItem item) try { Clipboard.SetText(item.Url); } catch { }
    }

    private void BtnBookmarkOpen_Click(object sender, RoutedEventArgs e)
    {
        BookmarksContextPopup.IsOpen = false;
        if (ListBookmarks.SelectedItem is BookmarkItem item) RequestNavigate?.Invoke(this, item.Url);
    }

    private void BtnBookmarkDelete_Click(object sender, RoutedEventArgs e)
    {
        BookmarksContextPopup.IsOpen = false;
        if (ListBookmarks.SelectedItem is BookmarkItem item) { BookmarkService.Remove(item); RefreshBookmarks(); }
    }

    private void CtxPinSendToBookmarks_Click(object sender, RoutedEventArgs e)
    {
        PinsContextPopup.IsOpen = false;
        if (ListPins.SelectedItem is not PinItem pin) return;
        try
        {
            BookmarkService.Add(new BookmarkItem { Title = pin.Name, Url = pin.Url, DateAdded = DateTime.Now });
            RefreshBookmarks();
        }
        catch (Exception ex) { LogService.RecordCrash(ex, "Pin→Bookmark"); }
    }

    private void CtxBmSendToFavourites_Click(object sender, RoutedEventArgs e)
    {
        BookmarksContextPopup.IsOpen = false;
        if (ListBookmarks.SelectedItem is not BookmarkItem bm) return;
        var existing = SettingsService.Current.PinnedUrls;
        if (existing.Any(p => p.Url == bm.Url)) return; // avoid duplicates
        existing.Insert(0, new PinItem { Name = bm.Title, Url = bm.Url });
        SettingsService.Save();
        RefreshPins();
    }

    private void CtxBmAddNew_Click(object sender, RoutedEventArgs e)
    {
        BookmarksContextPopup.IsOpen = false;

        // Simple modal: reuse the OverlayRename grid with a two-field prompt
        // built inline as a lightweight child-window to avoid adding a full XAML overlay.
        var dlg = new System.Windows.Window
        {
            Title           = "Add Bookmark",
            Width           = 340,
            Height          = 190,
            WindowStyle     = System.Windows.WindowStyle.ToolWindow,
            ResizeMode      = ResizeMode.NoResize,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner           = System.Windows.Window.GetWindow(this),
            Background      = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
        };

        var sp = new StackPanel { Margin = new Thickness(14) };

        var lblName = new TextBlock { Text = "Title", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) };
        var txName  = new TextBox  { Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), Height = 28, Padding = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };

        var lblUrl  = new TextBlock { Text = "URL",   Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 11, Margin = new Thickness(0, 8, 0, 3) };
        var txUrl   = new TextBox  { Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), Height = 28, Padding = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };

        var btnRow  = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var btnCancel = new Button { Content = "CANCEL", Width = 68, Height = 26, Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), BorderThickness = new Thickness(0) };
        var btnSave   = new Button { Content = "SAVE",   Width = 68, Height = 26, Background = new SolidColorBrush(Color.FromRgb(0x00, 0x44, 0x00)), Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x00)), BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold };

        btnCancel.Click += (_, _) => dlg.Close();
        btnSave.Click += (_, _) =>
        {
            string url  = txUrl.Text.Trim();
            string name = txName.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;
            if (string.IsNullOrEmpty(name)) name = url;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            BookmarkService.Add(new BookmarkItem { Title = name, Url = url, DateAdded = DateTime.Now });
            RefreshBookmarks();
            dlg.Close();
        };

        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnSave);
        sp.Children.Add(lblName);
        sp.Children.Add(txName);
        sp.Children.Add(lblUrl);
        sp.Children.Add(txUrl);
        sp.Children.Add(btnRow);
        dlg.Content = sp;
        txName.Focus();
        dlg.ShowDialog();
    }

    private void CtxBmExport_Click(object sender, RoutedEventArgs e)
    {
        BookmarksContextPopup.IsOpen = false;
        var dlg = new SaveFileDialog
        {
            Title            = "Export Bookmarks",
            Filter           = "HTML Bookmark File (*.html)|*.html",
            FileName         = "horizon_bookmarks.html",
            DefaultExt       = ".html",
            OverwritePrompt  = true,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            sb.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
            sb.AppendLine("<TITLE>Bookmarks</TITLE>");
            sb.AppendLine("<H1>Bookmarks</H1>");
            sb.AppendLine("<DL><p>");
            foreach (var bm in BookmarkService.Items)
            {
                string ts = ((DateTimeOffset)bm.DateAdded).ToUnixTimeSeconds().ToString();
                string title = System.Security.SecurityElement.Escape(bm.Title ?? bm.Url);
                string url   = System.Security.SecurityElement.Escape(bm.Url);
                sb.AppendLine($"    <DT><A HREF=\"{url}\" ADD_DATE=\"{ts}\">{title}</A>");
            }
            sb.AppendLine("</DL><p>");
            File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            ShowStatus($"✔ Exported {BookmarkService.Items.Count} bookmarks.");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BookmarkExport");
            MessageBox.Show("Export failed: " + ex.Message, "Horizon", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HISTORY
    // ══════════════════════════════════════════════════════════════════════════

    private void LoadHistory()
    {
        var items = HistoryService.GetRecent().Take(50).Select(h => new HistoryItemViewModel(h)).ToList();
        ApplyHistorySort(items);
    }

    private void ApplyHistorySort(List<HistoryItemViewModel>? items = null)
    {
        items ??= (ListHistory.ItemsSource as List<HistoryItemViewModel>) ?? new List<HistoryItemViewModel>();

        IEnumerable<HistoryItemViewModel> sorted = _histSortField switch
        {
            HistSortField.Title  => _histSortAscending
                ? items.OrderBy(h => h.Title)
                : items.OrderByDescending(h => h.Title),
            HistSortField.Domain => _histSortAscending
                ? items.OrderBy(h => GetDomain(h.Url))
                : items.OrderByDescending(h => GetDomain(h.Url)),
            _ => _histSortAscending
                ? items.OrderBy(h => h.VisitTime)
                : items.OrderByDescending(h => h.VisitTime),
        };

        ListHistory.ItemsSource = sorted.ToList();
        UpdateHistSortButtonStyles();
    }

    private void UpdateHistSortButtonStyles()
    {
        SetSortBtnActive(BtnHistSortDate,   _histSortField == HistSortField.Date);
        SetSortBtnActive(BtnHistSortTitle,  _histSortField == HistSortField.Title);
        SetSortBtnActive(BtnHistSortDomain, _histSortField == HistSortField.Domain);
        BtnHistSortDir.Content    = _histSortAscending ? "↑" : "↓";
        BtnHistSortDir.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    }

    private void BtnHistSortDate_Click(object sender, RoutedEventArgs e)   { _histSortField = HistSortField.Date;   ApplyHistorySort(); }
    private void BtnHistSortTitle_Click(object sender, RoutedEventArgs e)  { _histSortField = HistSortField.Title;  ApplyHistorySort(); }
    private void BtnHistSortDomain_Click(object sender, RoutedEventArgs e) { _histSortField = HistSortField.Domain; ApplyHistorySort(); }
    private void BtnHistSortDir_Click(object sender, RoutedEventArgs e)    { _histSortAscending = !_histSortAscending; ApplyHistorySort(); }

    private void ListHistory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListHistory.SelectedItem is HistoryItemViewModel item) RequestNavigate?.Invoke(this, item.Url);
    }

    private void ListHistory_RightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(ListHistory, e.GetPosition(ListHistory));
        if (hit?.VisualHit is DependencyObject dep)
        {
            var container = FindParent<ListBoxItem>(dep);
            if (container != null) container.IsSelected = true;
        }
        TxtHistorySearch.Text = string.Empty;
        HistoryContextPopup.IsOpen = true;
        e.Handled = true;
    }

    private void TxtHistorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtHistorySearchHint.Visibility = string.IsNullOrEmpty(TxtHistorySearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        string q = TxtHistorySearch.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q)) { LoadHistory(); return; }

        var filtered = HistoryService.GetRecent()
            .Where(h => (h.Title?.ToLowerInvariant().Contains(q) ?? false)
                     || (h.Url?.ToLowerInvariant().Contains(q)   ?? false))
            .Take(50).ToList();
        ListHistory.ItemsSource = filtered;
    }

    private void CtxHistSortDate_Click(object sender, RoutedEventArgs e)   { HistoryContextPopup.IsOpen = false; BtnHistSortDate_Click(sender, e); }
    private void CtxHistSortTitle_Click(object sender, RoutedEventArgs e)  { HistoryContextPopup.IsOpen = false; BtnHistSortTitle_Click(sender, e); }
    private void CtxHistSortDomain_Click(object sender, RoutedEventArgs e) { HistoryContextPopup.IsOpen = false; BtnHistSortDomain_Click(sender, e); }

    private void BtnHistoryCopy_Click(object sender, RoutedEventArgs e)
    {
        HistoryContextPopup.IsOpen = false;
        if (ListHistory.SelectedItem is HistoryItemViewModel item) try { Clipboard.SetText(item.Url); } catch { }
    }

    private void BtnHistoryOpen_Click(object sender, RoutedEventArgs e)
    {
        HistoryContextPopup.IsOpen = false;
        if (ListHistory.SelectedItem is HistoryItemViewModel item) RequestNavigate?.Invoke(this, item.Url);
    }

    private void BtnHistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        HistoryContextPopup.IsOpen = false;
        if (ListHistory.SelectedItem is HistoryItemViewModel item) { HistoryService.Remove(item.Url); LoadHistory(); }
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e) { HistoryService.Clear(); LoadHistory(); }

    // ══════════════════════════════════════════════════════════════════════════
    // DOWNLOADS
    // ══════════════════════════════════════════════════════════════════════════

    private const string _fsRoot = "__DRIVES__"; // sentinel for the drives list view

    private void LoadFluxStream(string? path = null)
    {
        // If no path given, use stored browse path or fall back to downloads root
        if (path != null)
            _browsePath = path;
        else if (string.IsNullOrEmpty(_browsePath))
            _browsePath = SettingsService.Current.DownloadsPath;

        string dlRoot = SettingsService.Current.DownloadsPath;
        bool atDownloads = string.Equals(_browsePath, dlRoot, StringComparison.OrdinalIgnoreCase);
        bool atDrives    = _browsePath == _fsRoot;

        // Path bar visibility & label
        FluxPathBar.Visibility = (atDownloads && _browseStack.Count == 0)
            ? Visibility.Collapsed : Visibility.Visible;
        if (!atDownloads || _browseStack.Count > 0)
        {
            TxtFluxPath.Text = atDrives ? "📁 This PC — All Drives"
                : _browsePath.Length > dlRoot.Length && _browsePath.StartsWith(dlRoot, StringComparison.OrdinalIgnoreCase)
                    ? _browsePath[dlRoot.Length..].TrimStart('\\', '/')
                    : _browsePath;
        }

        FluxItems.Clear();

        // ── Drives root ──────────────────────────────────────────────────────
        if (atDrives)
        {
            try
            {
                // System quick-access locations (always shown)
                var quickAccess = new (string Label, string Path)[]
                {
                    ("🖥  Desktop",   Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
                    ("⬇  Downloads", SettingsService.Current.DownloadsPath),
                    ("📄  Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                    ("🎵  Music",     Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
                    ("🖼  Images",    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                    ("🎬  Videos",    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
                };
                foreach (var (lbl, p) in quickAccess)
                {
                    if (!Directory.Exists(p)) continue;
                    FluxItems.Add(new FluxItemViewModel(new DirectoryInfo(p)) { OverrideFileName = lbl });
                }
                // Recycle Bin (open via Explorer)
                FluxItems.Add(new FluxItemViewModel(new DirectoryInfo(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))
                    { OverrideFileName = "🗑  Recycle Bin" });

                // User-pinned locations
                foreach (var pinned in SettingsService.Current.PinnedFsLocations)
                {
                    if (!Directory.Exists(pinned)) continue;
                    FluxItems.Add(new FluxItemViewModel(new DirectoryInfo(pinned))
                        { OverrideFileName = $"📌  {System.IO.Path.GetFileName(pinned)}" });
                }

                // Physical drives
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    FluxItems.Add(new FluxItemViewModel(new DirectoryInfo(drive.RootDirectory.FullName))
                        { OverrideFileName = $"[{drive.DriveType}]  {drive.Name}  —  {drive.VolumeLabel}" });
                }
            }
            catch (Exception ex) { LogService.RecordCrash(ex, "FluxStream drives"); }
            return; // don't ApplySort — drives list is already ordered
        }

        // ── Normal directory ─────────────────────────────────────────────────
        if (!Directory.Exists(_browsePath)) return;
        try
        {
            var dirInfo = new DirectoryInfo(_browsePath);

            foreach (var dir in dirInfo.GetDirectories().Take(500))
                FluxItems.Add(new FluxItemViewModel(dir));

            foreach (var file in dirInfo.GetFiles().Take(500))
                FluxItems.Add(new FluxItemViewModel(
                    new FluxItem
                    {
                        FileName      = file.Name,
                        FilePath      = file.FullName,
                        TotalBytes    = file.Length,
                        ReceivedBytes = file.Length,
                        State         = "COMPLETE",
                    },
                    file.CreationTime
                ));
        }
        catch (UnauthorizedAccessException) { /* skip protected folders silently */ }
        catch (Exception ex) { LogService.RecordCrash(ex, "FluxStream Load"); }

        ApplySort();
    }

    private void BrowseInto(string folderPath)
    {
        _browseStack.Push(_browsePath);
        LoadFluxStream(folderPath);
    }

    private void BtnFluxBack_Click(object sender, RoutedEventArgs e)
    {
        if (_browseStack.TryPop(out string? prev))
            LoadFluxStream(prev);
        else
            LoadFluxStream(SettingsService.Current.DownloadsPath);
    }

    private void ApplySort()
    {
        IEnumerable<FluxItemViewModel> sorted = _sortField switch
        {
            SortField.Name => _sortAscending
                ? FluxItems.OrderBy(f => f.IsFolder ? 0 : 1).ThenBy(f => f.FileName)
                : FluxItems.OrderBy(f => f.IsFolder ? 0 : 1).ThenByDescending(f => f.FileName),
            SortField.Size => _sortAscending
                ? FluxItems.OrderBy(f => f.IsFolder ? 0 : 1).ThenBy(f => f.TotalBytes)
                : FluxItems.OrderBy(f => f.IsFolder ? 0 : 1).ThenByDescending(f => f.TotalBytes),
            _ => _sortAscending
                ? FluxItems.OrderBy(f => f.IsFolder ? 0 : 1).ThenBy(f => f.CreationTime)
                : FluxItems.OrderBy(f => f.IsFolder ? 0 : 1).ThenByDescending(f => f.CreationTime),
        };

        var list = sorted.ToList();
        FluxItems.Clear();
        foreach (var item in list) FluxItems.Add(item);
        UpdateSortButtonStyles();
    }

    private void UpdateSortButtonStyles()
    {
        SetSortBtnActive(BtnSortDate, _sortField == SortField.Date);
        SetSortBtnActive(BtnSortName, _sortField == SortField.Name);
        SetSortBtnActive(BtnSortSize, _sortField == SortField.Size);
        BtnSortDir.Content    = _sortAscending ? "↑" : "↓";
        BtnSortDir.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    }

    private void BtnSortDate_Click(object sender, RoutedEventArgs e) { _sortField = SortField.Date; ApplySort(); }
    private void BtnSortName_Click(object sender, RoutedEventArgs e) { _sortField = SortField.Name; ApplySort(); }
    private void BtnSortSize_Click(object sender, RoutedEventArgs e) { _sortField = SortField.Size; ApplySort(); }
    private void BtnSortDir_Click(object sender, RoutedEventArgs e)  { _sortAscending = !_sortAscending; ApplySort(); }

    private void CtxSortDate_Click(object sender, RoutedEventArgs e) { FluxContextPopup.IsOpen = false; BtnSortDate_Click(sender, e); }
    private void CtxSortName_Click(object sender, RoutedEventArgs e) { FluxContextPopup.IsOpen = false; BtnSortName_Click(sender, e); }
    private void CtxSortSize_Click(object sender, RoutedEventArgs e) { FluxContextPopup.IsOpen = false; BtnSortSize_Click(sender, e); }

    private static readonly HashSet<string> _archiveExts = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" };

    private static bool IsArchive(string fileName)
    {
        // Handle compound extensions like .tar.gz
        if (fileName.EndsWith(".tar.gz",  StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.EndsWith(".tar.xz",  StringComparison.OrdinalIgnoreCase)) return true;
        return _archiveExts.Contains(Path.GetExtension(fileName));
    }

    // ── Active download panel ─────────────────────────────────────────────────
    public event Action? PauseDownloadRequested;
    public event Action? CancelDownloadRequested;
    private bool _dlPaused = false;

    private void BtnDlPause_Click(object sender, RoutedEventArgs e)
    {
        PauseDownloadRequested?.Invoke();
        _dlPaused = !_dlPaused;
        BtnDlPause.Content = _dlPaused ? "▶" : "⏸";
    }

    private void BtnDlCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDownloadComplete)
        {
            try
            {
                if (!string.IsNullOrEmpty(_activeDownloadPath) && File.Exists(_activeDownloadPath))
                    File.Delete(_activeDownloadPath);
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "Dismiss+Delete finished download");
                MessageBox.Show($"Could not delete the file:\n{ex.Message}",
                    "Horizon", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            ActiveDownloadPanel.Visibility = Visibility.Collapsed;
            _activeDownloadPath = "";
            _activeDownloadLink = "";
            _activeDownloadComplete = false;
            return;
        }

        TxtDlSpeed.Text = "Cancelled";
        TxtDlEta.Text = "";
        _dlPaused = false;
        BtnDlPause.Content = "⏸";
        CancelDownloadRequested?.Invoke();
    }

    public void NotifyDownloadProgress(Controls.BrowserView.DownloadInfo info)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => NotifyDownloadProgress(info)); return; }

        _activeDownloadPath = info.FilePath;
        _activeDownloadLink = info.Uri;
        _activeDownloadComplete = info.IsComplete;
        ActiveDownloadPanel.Visibility = Visibility.Visible;
        TxtDlFileName.Text = System.IO.Path.GetFileName(info.FilePath);
        PbDlProgress.Value = info.Progress;

        if (_dlPaused)
        {
            TxtDlSpeed.Text = "Paused";
            TxtDlEta.Text = "";
        }
        else if (info.IsComplete)
        {
            if (info.Progress < 1.0)
            {
                TxtDlSpeed.Text = "Cancelled / Failed";
            }
            else
            {
                TxtDlSpeed.Text = "Complete";
            }
            TxtDlEta.Text = "";
            _dlPaused = false;
            BtnDlPause.Content = "⏸";
            BtnDlPause.Visibility = Visibility.Collapsed;
            BtnDlCancel.Content = "✕ Dismiss";
            BtnDlCancel.ToolTip = "Remove from list and delete the downloaded file";
        }
        else
        {
            BtnDlPause.Visibility = Visibility.Visible;
            BtnDlCancel.Content = "✕ Cancel";
            BtnDlCancel.ToolTip = "Cancel Download";
            TxtDlSpeed.Text = info.SpeedMBs >= 1.0
                ? $"{info.SpeedMBs:F1} MB/s"
                : $"{info.SpeedMBs * 1024:F0} KB/s";
            TxtDlEta.Text = FormatEta(info.EtaSecs);
        }
    }

    private static string FormatEta(int secs)
    {
        if (secs <= 0) return "--";
        if (secs < 60) return $"{secs}s";
        if (secs < 3600) return $"{secs / 60}m {secs % 60}s";
        return $"{secs / 3600}h {(secs % 3600) / 60}m";
    }

    private void ActiveDownloadPanel_RightClick(object sender, MouseButtonEventArgs e)
    {
        DlContextPopup.IsOpen = true;
        e.Handled = true;
    }

    private void DlCtxShowFolder_Click(object sender, RoutedEventArgs e)
    {
        DlContextPopup.IsOpen = false;
        if (!string.IsNullOrEmpty(_activeDownloadPath))
        {
            string folder = System.IO.Path.GetDirectoryName(_activeDownloadPath) ?? _activeDownloadPath;
            if (Directory.Exists(folder))
                System.Diagnostics.Process.Start("explorer.exe", folder);
        }
    }

    private void DlCtxCopyLink_Click(object sender, RoutedEventArgs e)
    {
        DlContextPopup.IsOpen = false;
        if (!string.IsNullOrEmpty(_activeDownloadLink))
            System.Windows.Clipboard.SetText(_activeDownloadLink);
    }

    private void DlCtxDismiss_Click(object sender, RoutedEventArgs e)
    {
        DlContextPopup.IsOpen = false;
        ActiveDownloadPanel.Visibility = Visibility.Collapsed;
        _activeDownloadPath = null;
        _activeDownloadLink = null;
        _activeDownloadComplete = false;
    }

    private void ListFlux_RightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(ListFlux, e.GetPosition(ListFlux));
        if (hit?.VisualHit is DependencyObject dep)
        {
            var container = FindParent<ListBoxItem>(dep);
            if (container != null)
            {
                // If the right-clicked item is already in the multi-selection, keep it.
                // Otherwise collapse to just that item (File Explorer behaviour).
                if (!container.IsSelected)
                {
                    ListFlux.SelectedItems.Clear();
                    container.IsSelected = true;
                }
            }
        }
        TxtFluxSearch.Text = string.Empty;

        // Show/hide extract option depending on whether selected item is an archive
        bool isArchive = ListFlux.SelectedItem is FluxItemViewModel sel
                         && !sel.IsFolder
                         && IsArchive(sel.FileName)
                         && File.Exists(sel.FilePath);
        BtnCtxExtractHere.Visibility = isArchive ? Visibility.Visible : Visibility.Collapsed;
        SepExtractHere.Visibility    = isArchive ? Visibility.Visible : Visibility.Collapsed;

        // Show Paste only when the clipboard carries files
        BtnCtxPaste.Visibility = System.Windows.Clipboard.ContainsFileDropList()
            ? Visibility.Visible : Visibility.Collapsed;

        // FIX: BtnCtxPinLocation now exists in XAML — show/hide based on current browse location
        bool canPin = !string.IsNullOrEmpty(_browsePath)
                   && _browsePath != _fsRoot
                   && Directory.Exists(_browsePath)
                   && !string.Equals(_browsePath, SettingsService.Current.DownloadsPath,
                                     StringComparison.OrdinalIgnoreCase);
        if (canPin)
        {
            bool alreadyPinned = SettingsService.Current.PinnedFsLocations.Contains(_browsePath);
            BtnCtxPinLocation.Content    = alreadyPinned ? "📌  Unpin This Location" : "📌  Pin This Location";
            BtnCtxPinLocation.Visibility = Visibility.Visible;
            SepPinLocation.Visibility    = Visibility.Visible;
        }
        else
        {
            BtnCtxPinLocation.Visibility = Visibility.Collapsed;
            SepPinLocation.Visibility    = Visibility.Collapsed;
        }

        FluxContextPopup.IsOpen = true;
        e.Handled = true;
    }

    // Downloads context smart search
    private void TxtFluxSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtFluxSearchHint.Visibility = string.IsNullOrEmpty(TxtFluxSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        string q = TxtFluxSearch.Text.Trim().ToLowerInvariant();
        ListFlux.ItemsSource = string.IsNullOrEmpty(q)
            ? (IEnumerable<FluxItemViewModel>)FluxItems
            : FluxItems.Where(f => f.FileName.ToLowerInvariant().Contains(q));
    }

    private void CtxFileInfo_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        if (ListFlux.SelectedItem is not FluxItemViewModel item) return;

        if (item.IsFolder && Directory.Exists(item.FilePath))
        {
            var di = new DirectoryInfo(item.FilePath);
            TxtFileInfoName.Text = di.Name;
            TxtFileInfoSize.Text = "Size:      (folder)";
            TxtFileInfoDate.Text = $"Created:   {di.CreationTime:yyyy-MM-dd  HH:mm:ss}";
            TxtFileInfoType.Text = "Extension: FOLDER";
            TxtFileInfoPath.Text = di.FullName;
            OverlayFileInfo.Visibility = Visibility.Visible;
            return;
        }

        if (!File.Exists(item.FilePath)) return;
        var fi = new FileInfo(item.FilePath);
        TxtFileInfoName.Text = fi.Name;
        TxtFileInfoSize.Text = $"Size:      {FormatFileSize(fi.Length)}  ({fi.Length:N0} bytes)";
        TxtFileInfoDate.Text = $"Created:   {fi.CreationTime:yyyy-MM-dd  HH:mm:ss}";
        TxtFileInfoType.Text = $"Extension: {fi.Extension.ToUpperInvariant()}";
        TxtFileInfoPath.Text = fi.FullName;
        OverlayFileInfo.Visibility = Visibility.Visible;
    }

    private void BtnCloseFileInfo_Click(object sender, RoutedEventArgs e)
        => OverlayFileInfo.Visibility = Visibility.Collapsed;

    private void ListFlux_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListFlux.SelectedItem is not FluxItemViewModel item) return;
        if (item.IsFolder)
        {
            // From drives root, FilePath is the drive root (e.g. "C:\")
            if (Directory.Exists(item.FilePath))
                BrowseInto(item.FilePath);
        }
        else
            OpenFile(item.FilePath);
    }

    private void BtnFluxOpen_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        if (ListFlux.SelectedItem is not FluxItemViewModel item) return;
        if (item.IsFolder && Directory.Exists(item.FilePath))
            BrowseInto(item.FilePath);
        else
            OpenFile(item.FilePath);
    }

    private void CtxPreview_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        if (ListFlux.SelectedItem is FluxItemViewModel item && !item.IsFolder)
            OpenPreview(item.FilePath);
    }

    private void BtnFluxFolder_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        if (ListFlux.SelectedItem is not FluxItemViewModel item) return;
        try
        {
            if (item.IsFolder && Directory.Exists(item.FilePath))
                // Open the folder itself selected in Explorer
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            else if (File.Exists(item.FilePath))
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        }
        catch { }
    }

    private void CtxBrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        _browseStack.Clear();
        _browseStack.Push(SettingsService.Current.DownloadsPath);
        LoadFluxStream(_fsRoot);
    }

    private void CtxPinLocation_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        if (string.IsNullOrEmpty(_browsePath) || _browsePath == _fsRoot) return;
        bool alreadyPinned = SettingsService.Current.PinnedFsLocations.Contains(_browsePath);
        if (alreadyPinned) RemovePinnedFsLocation(_browsePath);
        else AddPinnedFsLocation(_browsePath);
    }

    private void AddPinnedFsLocation(string path)
    {
        var list = SettingsService.Current.PinnedFsLocations;
        if (!list.Contains(path)) { list.Add(path); SettingsService.Save(); }
        if (_browsePath == _fsRoot) LoadFluxStream(_fsRoot); // refresh drives view
    }

    private void RemovePinnedFsLocation(string path)
    {
        SettingsService.Current.PinnedFsLocations.Remove(path);
        SettingsService.Save();
        if (_browsePath == _fsRoot) LoadFluxStream(_fsRoot);
    }

    private void BtnFluxDelete_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        var targets = ListFlux.SelectedItems.OfType<FluxItemViewModel>().ToList();
        if (targets.Count == 0) return;

        string prompt = targets.Count == 1
            ? (targets[0].IsFolder
                ? $"Delete folder '{targets[0].FileName}' and all its contents?"
                : $"Delete '{targets[0].FileName}'?")
            : $"Delete {targets.Count} selected items?";

        if (MessageBox.Show(prompt, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;

        foreach (var item in targets)
        {
            try
            {
                if (item.IsFolder && Directory.Exists(item.FilePath))
                    Directory.Delete(item.FilePath, recursive: true);
                else if (File.Exists(item.FilePath))
                    File.Delete(item.FilePath);
                FluxItems.Remove(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete failed for '{item.FileName}':\n{ex.Message}", "Error");
            }
        }
    }

    private async void CtxExtractHere_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        if (ListFlux.SelectedItem is not FluxItemViewModel item ||
            item.IsFolder || !File.Exists(item.FilePath)) return;

        // Build destination folder name (strip compound extensions like .tar.gz)
        string baseName = item.FileName;
        foreach (var compound in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
            if (baseName.EndsWith(compound, StringComparison.OrdinalIgnoreCase))
            { baseName = baseName[..^compound.Length]; break; }
        if (baseName == item.FileName)
            baseName = Path.GetFileNameWithoutExtension(item.FileName);

        string destDir = Path.Combine(Path.GetDirectoryName(item.FilePath)!, baseName);

        // Don't overwrite silently — ask if folder already exists
        if (Directory.Exists(destDir))
        {
            var res = MessageBox.Show(
                $"Folder '{baseName}' already exists.\nOverwrite its contents?",
                "Extract Here", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        ShowStatus("⏳ Extracting…");
        string? errorMsg = null;

        try
        {
            string archivePath = item.FilePath;
            string ext = Path.GetExtension(item.FileName).ToLowerInvariant();

            await Task.Run(() =>
            {
                Directory.CreateDirectory(destDir);

                if (ext == ".zip")
                {
                    ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
                }
                else
                {
                    // Try 7-Zip, then WinRAR
                    if (!TryExtractWith7Zip(archivePath, destDir) &&
                        !TryExtractWithWinRar(archivePath, destDir))
                    {
                        // Last resort for .gz (single file, not tarball)
                        if (ext == ".gz" && !item.FileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                        {
                            string outFile = Path.Combine(destDir, Path.GetFileNameWithoutExtension(item.FileName));
                            using var fs  = File.OpenRead(archivePath);
                            using var gz  = new GZipStream(fs, CompressionMode.Decompress);
                            using var dst = File.Create(outFile);
                            gz.CopyTo(dst);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                "No archive extractor found.\n\nInstall 7-Zip or WinRAR to extract .rar, .7z, .tar, and .gz files.\n\nhttps://www.7-zip.org/");
                        }
                    }
                }
            });

            ShowStatus($"✔ Extracted to '{baseName}'");
        }
        catch (Exception ex)
        {
            errorMsg = ex.Message;
            // Clean up empty destination if extraction failed
            try { if (Directory.Exists(destDir) && !Directory.EnumerateFileSystemEntries(destDir).Any()) Directory.Delete(destDir); } catch { }
        }

        LoadFluxStream(); // Refresh to show the new folder

        if (errorMsg != null)
            MessageBox.Show(errorMsg, "Extract Failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static bool TryExtractWith7Zip(string archivePath, string destDir)
    {
        string[] candidates =
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),    "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
        };

        foreach (var exe in candidates.Where(File.Exists))
        {
            var psi = new ProcessStartInfo(exe, $"x \"{archivePath}\" -o\"{destDir}\" -y")
            {
                CreateNoWindow        = true,
                UseShellExecute       = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var proc = Process.Start(psi)!;
            proc.WaitForExit(60_000);
            if (proc.ExitCode == 0) return true;
        }
        return false;
    }

    private static bool TryExtractWithWinRar(string archivePath, string destDir)
    {
        string[] candidates =
        {
            @"C:\Program Files\WinRAR\WinRAR.exe",
            @"C:\Program Files (x86)\WinRAR\WinRAR.exe",
            @"C:\Program Files\WinRAR\UnRAR.exe",
            @"C:\Program Files (x86)\WinRAR\UnRAR.exe",
        };

        foreach (var exe in candidates.Where(File.Exists))
        {
            // WinRAR: x = extract with full paths, -y = assume yes to all
            string args = exe.EndsWith("UnRAR.exe", StringComparison.OrdinalIgnoreCase)
                ? $"x -y \"{archivePath}\" \"{destDir}\\\""
                : $"x -y \"{archivePath}\" \"{destDir}\\\"";

            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow        = true,
                UseShellExecute       = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var proc = Process.Start(psi)!;
            proc.WaitForExit(60_000);
            if (proc.ExitCode == 0) return true;
        }
        return false;
    }

    private void BtnPreviewFile_Click(object sender, RoutedEventArgs e)
    {
        if (ListFlux.SelectedItem is not FluxItemViewModel item || !File.Exists(item.FilePath))
        {
            MessageBox.Show("Select a completed download first.", "Horizon Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenPreview(item.FilePath);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PREVIEW
    // ══════════════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> _imageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif" };

    private static readonly HashSet<string> _textExts = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".log", ".md", ".csv", ".json", ".xml", ".yaml", ".yml", ".ini",
          ".cfg", ".toml", ".html", ".htm", ".css", ".js", ".ts", ".py", ".cs",
          ".cpp", ".c", ".h", ".java", ".rb", ".sh", ".bat", ".ps1", ".sql",
          ".xaml", ".axaml", ".svg", ".csproj", ".sln", ".props", ".targets",
          ".gitignore", ".editorconfig", ".env", ".dockerfile", ".tf", ".kt",
          ".swift", ".rs", ".go", ".lua", ".php", ".r", ".m", ".vb", ".fs" };

    private const long MaxTextPreviewBytes = 512 * 1024;

    private void OpenPreview(string path)
    {
        string ext  = Path.GetExtension(path);
        var    info = new FileInfo(path);

        TxtPreviewFileName.Text = info.Name;
        PreviewImageScroll.Visibility    = Visibility.Collapsed;
        PreviewTextScroll.Visibility     = Visibility.Collapsed;
        PreviewUnsupportedText.Visibility = Visibility.Collapsed;
        PreviewImage.Source              = null;
        PreviewSearchBar.Visibility      = Visibility.Collapsed;
        PreviewZoomPanel.Visibility      = Visibility.Collapsed;
        TxtPreviewSearch.Text            = string.Empty;
        TxtSearchCount.Text              = string.Empty;
        TxtSearchHint.Visibility         = Visibility.Visible;
        _searchMatches.Clear();
        _searchIndex    = -1;
        _lastSearchTerm = "";
        _imageZoom = 1.0;
        _textZoom = 1.0;
        TxtZoomLevel.Text = "100%";

        if (_imageExts.Contains(ext))      LoadImagePreview(path, info);
        else if (_textExts.Contains(ext))  LoadTextPreview(path, info, ext);
        else if (ext == ".docx" || ext == ".doc") LoadDocxPreview(path, info);
        else if (ext == ".xlsx" || ext == ".xls") LoadXlsxPreview(path, info);
        else
        {
            PreviewUnsupportedText.Text       = $"No preview for *{ext} files.\n\nDouble-click to open with the default app.";
            PreviewUnsupportedText.Visibility = Visibility.Visible;
            TxtPreviewInfo.Text               = FormatFileSize(info.Length);
        }
        OverlayPreview.Visibility = Visibility.Visible;
    }

    private void LoadDocxPreview(string path, FileInfo info)
    {
        // Extract plain text from docx by reading the word/document.xml inside the zip
        try
        {
            var sb = new System.Text.StringBuilder();
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = zip.GetEntry("word/document.xml");
            if (entry == null) throw new Exception("Not a valid .docx file.");
            using var stream = entry.Open();
            using var reader = new System.Xml.XmlTextReader(stream);
            while (reader.Read())
            {
                if (reader.NodeType == System.Xml.XmlNodeType.Text || reader.NodeType == System.Xml.XmlNodeType.SignificantWhitespace)
                    sb.Append(reader.Value);
                else if (reader.NodeType == System.Xml.XmlNodeType.EndElement
                      && (reader.LocalName == "p" || reader.LocalName == "br"))
                    sb.AppendLine();
            }
            string content = sb.ToString().Trim();
            PreviewRichText.Document     = BuildSyntaxDocument(content, ".txt");
            PreviewTextScroll.Visibility = Visibility.Visible;
            PreviewSearchBar.Visibility  = Visibility.Visible;
            PreviewZoomPanel.Visibility  = Visibility.Visible;
            TxtPreviewInfo.Text          = $"DOCX  ·  {content.Split('\n').Length} lines  ·  {FormatFileSize(info.Length)}";
        }
        catch (Exception ex)
        {
            PreviewUnsupportedText.Text       = $"Cannot preview this .docx:\n{ex.Message}\n\nDouble-click to open with Word.";
            PreviewUnsupportedText.Visibility = Visibility.Visible;
            TxtPreviewInfo.Text               = FormatFileSize(info.Length);
        }
    }

    private void LoadXlsxPreview(string path, FileInfo info)
    {
        // Extract sheet data from xlsx by reading xl/worksheets/sheet1.xml
        try
        {
            var rows = new List<string>();
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            // Find first sheet
            var sheetEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.Name.EndsWith(".xml"));
            if (sheetEntry == null) throw new Exception("No worksheet found.");

            // Load shared strings for text cells
            var sharedStrings = new List<string>();
            var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
            if (ssEntry != null)
            {
                using var ssStream = ssEntry.Open();
                var ssDoc = new System.Xml.XmlDocument();
                ssDoc.Load(ssStream);
                foreach (System.Xml.XmlNode t in ssDoc.GetElementsByTagName("t"))
                    sharedStrings.Add(t.InnerText);
            }

            using var stream = sheetEntry.Open();
            var doc = new System.Xml.XmlDocument();
            doc.Load(stream);
            var rowNodes = doc.GetElementsByTagName("row");
            foreach (System.Xml.XmlNode row in rowNodes)
            {
                var cells = new List<string>();
                foreach (System.Xml.XmlNode cell in row.ChildNodes)
                {
                    string t2 = cell.Attributes?["t"]?.Value ?? "";
                    var vNode = ((System.Xml.XmlElement)cell).GetElementsByTagName("v");
                    string val = vNode.Count > 0 ? vNode[0]!.InnerText : "";
                    if (t2 == "s" && int.TryParse(val, out int si) && si < sharedStrings.Count)
                        val = sharedStrings[si];
                    cells.Add(val);
                }
                rows.Add(string.Join("\t", cells));
            }
            string content = string.Join("\n", rows);
            PreviewRichText.Document     = BuildSyntaxDocument(content, ".txt");
            PreviewTextScroll.Visibility = Visibility.Visible;
            PreviewSearchBar.Visibility  = Visibility.Visible;
            PreviewZoomPanel.Visibility  = Visibility.Visible;
            TxtPreviewInfo.Text          = $"XLSX  ·  {rows.Count} rows  ·  {FormatFileSize(info.Length)}";
        }
        catch (Exception ex)
        {
            PreviewUnsupportedText.Text       = $"Cannot preview this .xlsx:\n{ex.Message}\n\nDouble-click to open with Excel.";
            PreviewUnsupportedText.Visibility = Visibility.Visible;
            TxtPreviewInfo.Text               = FormatFileSize(info.Length);
        }
    }

    private void LoadImagePreview(string path, FileInfo info)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource     = new Uri(path, UriKind.Absolute);
            bmp.CacheOption   = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze();

            PreviewImage.Source           = bmp;
            PreviewImageScale.ScaleX      = 1;
            PreviewImageScale.ScaleY      = 1;
            PreviewImageScroll.Visibility = Visibility.Visible;
            PreviewZoomPanel.Visibility   = Visibility.Visible;
            TxtPreviewInfo.Text           = $"{bmp.PixelWidth} × {bmp.PixelHeight}  ·  {FormatFileSize(info.Length)}";
        }
        catch (Exception ex)
        {
            PreviewUnsupportedText.Text       = $"Could not load image:\n{ex.Message}";
            PreviewUnsupportedText.Visibility = Visibility.Visible;
            TxtPreviewInfo.Text               = FormatFileSize(info.Length);
        }
    }

    private void LoadTextPreview(string path, FileInfo info, string ext)
    {
        try
        {
            string content;
            bool   truncated = false;
            if (info.Length > MaxTextPreviewBytes)
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[MaxTextPreviewBytes];
                int read = fs.Read(buf, 0, buf.Length);
                content  = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                truncated = true;
            }
            else { content = File.ReadAllText(path); }

            if (truncated) content += $"\n\n[... truncated — file is {FormatFileSize(info.Length)} total ...]";

            PreviewRichText.Document      = BuildSyntaxDocument(content, ext);
            PreviewTextScroll.Visibility  = Visibility.Visible;
            PreviewSearchBar.Visibility   = Visibility.Visible;
            PreviewZoomPanel.Visibility   = Visibility.Visible;
            TxtPreviewInfo.Text           = $"{info.Extension.ToUpperInvariant()}  ·  {content.Split('\n').Length} lines  ·  {FormatFileSize(info.Length)}";
        }
        catch (Exception ex)
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run($"Could not read file:\n{ex.Message}"))
                { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)) });
            PreviewRichText.Document     = doc;
            PreviewTextScroll.Visibility = Visibility.Visible;
            TxtPreviewInfo.Text          = FormatFileSize(info.Length);
        }
    }

    // ── Syntax colouring ─────────────────────────────────────────────────────

    private static FlowDocument BuildSyntaxDocument(string content, string ext)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 10,
            Background = Brushes.Transparent,
            PageWidth  = 2900,
        };
        bool isCode = IsCodeExtension(ext);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var para = new Paragraph { Margin = new Thickness(0), LineHeight = 14 };
            if (isCode) ColourLine(para, line, ext);
            else para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)) });
            doc.Blocks.Add(para);
        }
        return doc;
    }

    private static bool IsCodeExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".cs" or ".java" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h"
            or ".rb" or ".go" or ".rs" or ".kt" or ".swift" or ".fs" or ".vb"
            or ".php" or ".lua" or ".sql" or ".sh" or ".bat" or ".ps1"
            or ".html" or ".htm" or ".css" or ".xml" or ".xaml" or ".axaml"
            or ".json" or ".yaml" or ".yml" or ".toml" or ".md"
            or ".csproj" or ".sln" or ".props" or ".targets" => true,
        _ => false,
    };

    private static void ColourLine(Paragraph para, string line, string ext)
    {
        string trimmed = line.TrimStart();
        bool isLineComment = trimmed.StartsWith("//") || trimmed.StartsWith("#")
            || trimmed.StartsWith("--") || trimmed.StartsWith("<!--") || trimmed.StartsWith("/*");
        if (isLineComment) { para.Inlines.Add(new Run(line) { Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x60, 0x4A)) }); return; }

        string extLow = ext.ToLowerInvariant();
        if (extLow is ".xml" or ".xaml" or ".axaml" or ".html" or ".htm" or ".csproj" or ".props" or ".targets") { ColourXml(para, line); return; }
        if (extLow is ".json") { ColourJson(para, line); return; }
        ColourGenericCode(para, line, extLow);
    }

    private static readonly SolidColorBrush BrushXmlTag  = new(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush BrushXmlAttr = new(Color.FromRgb(0x9C, 0xDB, 0xA8));
    private static readonly SolidColorBrush BrushXmlVal  = new(Color.FromRgb(0xCE, 0x91, 0x78));
    private static readonly SolidColorBrush BrushXmlText = new(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly SolidColorBrush BrushString  = new(Color.FromRgb(0xCE, 0x91, 0x78));
    private static readonly SolidColorBrush BrushKeyword = new(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush BrushNumber  = new(Color.FromRgb(0xB5, 0xCE, 0xA8));
    private static readonly SolidColorBrush BrushDefault = new(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly SolidColorBrush BrushJsonKey = new(Color.FromRgb(0x9C, 0xDB, 0xA8));
    private static readonly SolidColorBrush BrushJsonBool= new(Color.FromRgb(0x56, 0x9C, 0xD6));

    private static void ColourXml(Paragraph para, string line)
    {
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '<')
            {
                int end = line.IndexOf('>', i); if (end < 0) end = line.Length - 1;
                ColourXmlTag(para, line.Substring(i, end - i + 1));
                i = end + 1;
            }
            else
            {
                int next = line.IndexOf('<', i);
                string text = next < 0 ? line.Substring(i) : line.Substring(i, next - i);
                if (text.Length > 0) para.Inlines.Add(new Run(text) { Foreground = BrushXmlText });
                i = next < 0 ? line.Length : next;
            }
        }
        if (!para.Inlines.Any()) para.Inlines.Add(new Run(line) { Foreground = BrushDefault });
    }

    private static void ColourXmlTag(Paragraph para, string tag)
    {
        int attrStart = 0;
        for (int i = 0; i < tag.Length; i++)
            if (tag[i] == ' ' || tag[i] == '>' || tag[i] == '/') { attrStart = i; break; }
        if (attrStart == 0) attrStart = tag.Length;
        para.Inlines.Add(new Run(tag.Substring(0, attrStart)) { Foreground = BrushXmlTag });
        string rest = tag.Substring(attrStart);
        int j = 0;
        while (j < rest.Length)
        {
            int eq = rest.IndexOf('=', j);
            if (eq < 0) { para.Inlines.Add(new Run(rest.Substring(j)) { Foreground = BrushXmlAttr }); break; }
            para.Inlines.Add(new Run(rest.Substring(j, eq - j)) { Foreground = BrushXmlAttr });
            int q1 = rest.IndexOf('"', eq); if (q1 < 0) { para.Inlines.Add(new Run(rest.Substring(eq)) { Foreground = BrushXmlVal }); break; }
            int q2 = rest.IndexOf('"', q1 + 1); if (q2 < 0) { para.Inlines.Add(new Run(rest.Substring(eq)) { Foreground = BrushXmlVal }); break; }
            para.Inlines.Add(new Run("=") { Foreground = BrushDefault });
            para.Inlines.Add(new Run(rest.Substring(q1, q2 - q1 + 1)) { Foreground = BrushXmlVal });
            j = q2 + 1;
        }
    }

    private static void ColourJson(Paragraph para, string line)
    {
        int colon = line.IndexOf(':');
        if (colon > 0)
        {
            para.Inlines.Add(new Run(line.Substring(0, colon + 1)) { Foreground = BrushJsonKey });
            string valPart = line.Substring(colon + 1);
            string valTrim = valPart.Trim();
            Brush valBrush = valTrim switch
            {
                "true" or "false" or "null"                          => BrushJsonBool,
                _ when valTrim.StartsWith("\"")                      => BrushString,
                _ when double.TryParse(valTrim.TrimEnd(','), out _)  => BrushNumber,
                _                                                     => BrushDefault,
            };
            para.Inlines.Add(new Run(valPart) { Foreground = valBrush });
        }
        else para.Inlines.Add(new Run(line) { Foreground = BrushDefault });
    }

    private static readonly HashSet<string> _csKeywords = new(StringComparer.Ordinal)
        { "using","namespace","class","struct","interface","enum","public","private","protected",
          "internal","static","readonly","const","void","var","string","int","bool","double",
          "float","long","object","new","return","if","else","for","foreach","while","do",
          "switch","case","break","continue","null","true","false","this","base","override",
          "virtual","abstract","sealed","partial","async","await","try","catch","finally",
          "throw","typeof","sizeof","is","as","in","out","ref","event","delegate","get","set",
          "value","where","yield","from","select","orderby","group","join" };

    private static void ColourGenericCode(Paragraph para, string line, string ext)
    {
        foreach (var (tok, isWord) in TokeniseLine(line))
        {
            Brush b = !isWord ? BrushDefault
                : _csKeywords.Contains(tok) ? BrushKeyword
                : (tok.StartsWith("\"") || tok.StartsWith("'") || tok.StartsWith("`")) ? BrushString
                : double.TryParse(tok, out _) ? BrushNumber
                : BrushDefault;
            para.Inlines.Add(new Run(tok) { Foreground = b });
        }
    }

    private static List<(string tok, bool isWord)> TokeniseLine(string line)
    {
        var result = new List<(string, bool)>();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '"' || c == '\'')
            {
                int end = line.IndexOf(c, i + 1); if (end < 0) end = line.Length - 1;
                result.Add((line.Substring(i, end - i + 1), true)); i = end + 1; continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                int j = i;
                while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '_' || line[j] == '.')) j++;
                result.Add((line.Substring(i, j - i), true)); i = j; continue;
            }
            int k = i;
            while (k < line.Length && !char.IsLetterOrDigit(line[k]) && line[k] != '_' && line[k] != '"' && line[k] != '\'') k++;
            result.Add((line.Substring(i, k - i), false)); i = k;
        }
        return result;
    }

    // ── Preview close / zoom / search ─────────────────────────────────────────

    private void BtnClosePreview_Click(object sender, RoutedEventArgs e)
    {
        OverlayPreview.Visibility         = Visibility.Collapsed;
        PreviewImage.Source               = null;
        PreviewImageScroll.Visibility     = Visibility.Collapsed;
        PreviewTextScroll.Visibility      = Visibility.Collapsed;
        PreviewUnsupportedText.Visibility = Visibility.Collapsed;
        PreviewSearchBar.Visibility       = Visibility.Collapsed;
        PreviewZoomPanel.Visibility       = Visibility.Collapsed;
        _searchMatches.Clear(); _searchIndex = -1;
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewImageScroll.Visibility == Visibility.Visible) _imageZoom = Math.Min(_imageZoom + ZoomStep, ZoomMax);
        if (PreviewTextScroll.Visibility == Visibility.Visible) _textZoom = Math.Min(_textZoom + ZoomStep, ZoomMax);
        ApplyZoom();
    }
    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewImageScroll.Visibility == Visibility.Visible) _imageZoom = Math.Max(_imageZoom - ZoomStep, ZoomMin);
        if (PreviewTextScroll.Visibility == Visibility.Visible) _textZoom = Math.Max(_textZoom - ZoomStep, ZoomMin);
        ApplyZoom();
    }
    private void ApplyZoom()
    {
        if (PreviewImageScroll.Visibility == Visibility.Visible)
        {
            TxtZoomLevel.Text = $"{_imageZoom * 100:F0}%";
            PreviewImageScale.ScaleX = _imageZoom;
            PreviewImageScale.ScaleY = _imageZoom;
        }
        else if (PreviewTextScroll.Visibility == Visibility.Visible)
        {
            TxtZoomLevel.Text = $"{_textZoom * 100:F0}%";
            PreviewRichText.FontSize = Math.Max(6, 12 * _textZoom);
        }
    }

    private void TxtPreviewSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string term = TxtPreviewSearch.Text;
        TxtSearchHint.Visibility = string.IsNullOrEmpty(term) ? Visibility.Visible : Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(term)) { ClearSearchHighlights(); TxtSearchCount.Text = string.Empty; return; }
        if (term == _lastSearchTerm) return;
        _lastSearchTerm = term;
        RunSearch(term);
    }

    private void RunSearch(string term)
    {
        ClearSearchHighlights(); _searchMatches.Clear(); _searchIndex = -1;
        if (string.IsNullOrWhiteSpace(term) || PreviewRichText.Document == null) return;

        TextPointer? pos = PreviewRichText.Document.ContentStart;
        while (pos != null)
        {
            TextPointer? found = FindText(pos, PreviewRichText.Document.ContentEnd, term);
            if (found == null) break;
            TextPointer end = found.GetPositionAtOffset(term.Length, LogicalDirection.Forward);
            if (end == null) break;
            var range = new TextRange(found, end);
            range.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x22, 0x44, 0x22)));
            range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xCC, 0xFF, 0xCC)));
            _searchMatches.Add(range);
            pos = end;
        }

        TxtSearchCount.Text = _searchMatches.Count == 0 ? "no match" : $"1 / {_searchMatches.Count}";
        if (_searchMatches.Count > 0) { _searchIndex = 0; ScrollToMatch(0); }
    }

    private void BtnSearchNext_Click(object sender, RoutedEventArgs e)
    {
        if (_searchMatches.Count == 0) return;
        _searchIndex = (_searchIndex + 1) % _searchMatches.Count;
        ScrollToMatch(_searchIndex);
        TxtSearchCount.Text = $"{_searchIndex + 1} / {_searchMatches.Count}";
    }

    private void BtnSearchPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_searchMatches.Count == 0) return;
        _searchIndex = (_searchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        ScrollToMatch(_searchIndex);
        TxtSearchCount.Text = $"{_searchIndex + 1} / {_searchMatches.Count}";
    }

    private void ScrollToMatch(int index)
    {
        if (index < 0 || index >= _searchMatches.Count) return;
        for (int i = 0; i < _searchMatches.Count; i++)
        {
            bool active = i == index;
            _searchMatches[i].ApplyPropertyValue(TextElement.BackgroundProperty,
                new SolidColorBrush(active ? Color.FromRgb(0x00, 0x66, 0x00) : Color.FromRgb(0x22, 0x44, 0x22)));
            _searchMatches[i].ApplyPropertyValue(TextElement.ForegroundProperty,
                new SolidColorBrush(active ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0xCC, 0xFF, 0xCC)));
        }
        // Scroll the outer ScrollViewer to the active match
        var rect = _searchMatches[index].Start.GetCharacterRect(LogicalDirection.Forward);
        if (!rect.IsEmpty)
        {
            double offset = PreviewRichText.TranslatePoint(rect.TopLeft, PreviewTextScroll).Y;
            double target = offset + PreviewTextScroll.VerticalOffset - PreviewTextScroll.ViewportHeight / 2;
            PreviewTextScroll.ScrollToVerticalOffset(Math.Max(0, target));
        }
        else
        {
            _searchMatches[index].Start.Paragraph?.BringIntoView();
        }
    }

    private void ClearSearchHighlights()
    {
        if (PreviewRichText.Document == null) return;
        foreach (var range in _searchMatches)
        {
            try
            {
                range.ApplyPropertyValue(TextElement.BackgroundProperty, DependencyProperty.UnsetValue);
                range.ApplyPropertyValue(TextElement.ForegroundProperty, DependencyProperty.UnsetValue);
            }
            catch { }
        }
        _searchMatches.Clear(); _searchIndex = -1;
    }

    private static TextPointer? FindText(TextPointer start, TextPointer end, string term)
    {
        TextPointer pos = start;
        while (pos != null && pos.CompareTo(end) < 0)
        {
            if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string text = pos.GetTextInRun(LogicalDirection.Forward);
                int    idx  = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) return pos.GetPositionAtOffset(idx, LogicalDirection.Forward);
            }
            pos = pos.GetNextContextPosition(LogicalDirection.Forward);
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SHARED HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Applies the active/inactive visual to a sort bar button.</summary>
    private static void SetSortBtnActive(Button btn, bool active)
    {
        btn.Background       = new SolidColorBrush(active ? Color.FromRgb(0x14, 0x14, 0x14) : Color.FromRgb(0x0b, 0x0b, 0x0b));
        btn.Foreground       = new SolidColorBrush(active ? Color.FromRgb(0x00, 0xFF, 0x00) : Color.FromRgb(0x44, 0x44, 0x44));
        btn.BorderBrush      = new SolidColorBrush(active ? Color.FromRgb(0x00, 0xFF, 0x00) : Color.FromRgb(0x1a, 0x1a, 0x1a));
        btn.BorderThickness  = active ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 1, 1);
    }

    private static string GetDomain(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try { return new Uri(url).Host; }
        catch { return url; }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)                return $"{bytes} B";
        if (bytes < 1024 * 1024)         return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static void OpenFile(string path)
    {
        try { if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(child);
        while (parent != null) { if (parent is T t) return t; parent = VisualTreeHelper.GetParent(parent); }
        return null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DOWNLOADS — COPY / CUT / PASTE
    // ══════════════════════════════════════════════════════════════════════════

    private void CtxCopy_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        var targets = ListFlux.SelectedItems.OfType<FluxItemViewModel>()
                              .Where(i => File.Exists(i.FilePath) || Directory.Exists(i.FilePath))
                              .ToList();
        if (targets.Count == 0) return;

        _clipCutPath = null;
        var sc = new System.Collections.Specialized.StringCollection();
        foreach (var t in targets) sc.Add(t.FilePath);
        System.Windows.Clipboard.SetFileDropList(sc);
    }

    private void CtxCut_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        var targets = ListFlux.SelectedItems.OfType<FluxItemViewModel>()
                              .Where(i => File.Exists(i.FilePath) || Directory.Exists(i.FilePath))
                              .ToList();
        if (targets.Count == 0) return;

        // For multi-cut, store first path as sentinel; all paths go on clipboard
        _clipCutPath = targets[0].FilePath;

        var sc = new System.Collections.Specialized.StringCollection();
        foreach (var t in targets) sc.Add(t.FilePath);
        var data = new System.Windows.DataObject();
        data.SetFileDropList(sc);
        using var ms = new System.IO.MemoryStream(new byte[] { 2, 0, 0, 0 }); // DROPEFFECT_MOVE
        data.SetData("Preferred DropEffect", ms);
        System.Windows.Clipboard.SetDataObject(data, copy: true);
    }

    private void CtxPaste_Click(object sender, RoutedEventArgs e)
    {
        FluxContextPopup.IsOpen = false;
        string destDir = string.IsNullOrEmpty(_browsePath)
            ? SettingsService.Current.DownloadsPath
            : _browsePath;
        if (!Directory.Exists(destDir)) return;

        var files = System.Windows.Clipboard.GetFileDropList();
        if (files == null || files.Count == 0) return;

        bool isCut = _clipCutPath != null;
        foreach (string? src in files)
        {
            if (string.IsNullOrEmpty(src)) continue;
            try
            {
                if (File.Exists(src))
                {
                    string dest = UniqueFilePath(Path.Combine(destDir, Path.GetFileName(src)));
                    if (isCut) File.Move(src, dest);
                    else        File.Copy(src, dest, overwrite: false);
                }
                else if (Directory.Exists(src))
                {
                    string dest = UniqueDirPath(Path.Combine(destDir, Path.GetFileName(src)));
                    if (isCut) Directory.Move(src, dest);
                    else        DeepCopyDirectory(src, dest);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Paste failed:\n{ex.Message}", "Horizon",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        if (isCut)
        {
            _clipCutPath = null;
            System.Windows.Clipboard.Clear();
        }

        LoadFluxStream(destDir);
    }

    private static string UniqueFilePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir  = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext  = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string c = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(c)) return c;
        }
    }

    private static string UniqueDirPath(string path)
    {
        if (!Directory.Exists(path)) return path;
        for (int i = 2; ; i++)
        {
            string c = $"{path} ({i})";
            if (!Directory.Exists(c)) return c;
        }
    }

    private static void DeepCopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        foreach (string dir in Directory.GetDirectories(src))
            DeepCopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }
}
