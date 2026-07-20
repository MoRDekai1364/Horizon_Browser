using System.IO;
using System.IO.Pipes;
using Horizon.Stealth.Services;
using Horizon.Stealth.Core;
using Horizon.Stealth.Views;
using System.Collections.ObjectModel;
using System.Windows.Controls.Primitives;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Net.Http;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Horizon.Stealth.ViewModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Horizon.Stealth;

public partial class MainWindow : Window
{
    private Window? _mediaWidgetWindow;
    private ITaskbarList3? _mediaWidgetTaskbar;
    private const int FULLSCREEN_PRIMARY_TAB_COUNT = 6;
    private DispatcherTimer? _reflowDebounce;
    private const int REFLOW_DEBOUNCE_MS = 80;
    private double _lastGoodTabBarWidth = 0;
    private readonly HashSet<TabViewModel> _closingTabs = new();
    private readonly List<(string Url, string Title)> _closedTabHistory = new();
    private const int MaxClosedTabHistory = 25;

    private void ScheduleReflow()
    {
        if (_reflowDebounce == null)
        {
            _reflowDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(REFLOW_DEBOUNCE_MS) };
            _reflowDebounce.Tick += (_, _) => { _reflowDebounce.Stop(); ReflowTabs(); };
        }
        else
        {
            _reflowDebounce.Stop();
        }
        _reflowDebounce.Start();
    }
    private void ShowImageOverlay(string url)
    {
        _currentImageUrl = url;
        OverlaySingleImage.Visibility = Visibility.Visible;
    }

    private void HideImageOverlay()
    {
        OverlaySingleImage.Visibility = Visibility.Collapsed;
        _currentImageUrl = "";
    }

    private async void BtnDownloadImage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImageUrl)) return;
        string targetUrl = _currentImageUrl;
        HideImageOverlay();
        
        if (GetCurrentTabViewModel() is TabViewModel current)
            _tabImageUrls.Remove(current);
        
        try
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(targetUrl);
            if (string.IsNullOrEmpty(fileName)) fileName = "image_download";
            if (fileName.Contains('?')) fileName = fileName.Substring(0, fileName.IndexOf('?'));
            
            string ext = ".jpg";
            if (targetUrl.Contains(".png", StringComparison.OrdinalIgnoreCase)) ext = ".png";
            else if (targetUrl.Contains(".gif", StringComparison.OrdinalIgnoreCase)) ext = ".gif";
            else if (targetUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase)) ext = ".webp";
            
            string path = System.IO.Path.Combine(SettingsService.Current.DownloadsPath, fileName + ext);
            int count = 1;
            while (System.IO.File.Exists(path))
            {
                path = System.IO.Path.Combine(SettingsService.Current.DownloadsPath, $"{fileName} ({count}){ext}");
                count++;
            }
            
            var bytes = await _imageHttp.GetByteArrayAsync(targetUrl);
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            LogService.Write("DOWNLOAD", $"Saved standalone image to {path}");
        }
        catch (Exception ex)
        {
            LogService.Write("DOWNLOAD", $"Image download failed: {ex.Message}");
        }
    }

    private void BtnDismissImageOverlay_Click(object sender, RoutedEventArgs e)
    {
        HideImageOverlay();
        if (GetCurrentTabViewModel() is TabViewModel current)
        {
            _tabImageUrls.Remove(current);
        }
    }
    
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow([In] IntPtr appWindow);
    }

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid,
        out IntPtr factory);

    private static Guid _dtmIid = new Guid("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    public ObservableCollection<TabViewModel> Tabs { get; set; } = new();

    public ObservableCollection<TabViewModel> OverflowTabs { get; set; } = new();

    private List<TabViewModel> _allTabs = new();

    private Dictionary<TabViewModel, Controls.BrowserView> _tabViews = new();
    private Controls.BrowserView? _activeTabView = null;

    private Dictionary<TabViewModel, int>            _mediaTabOriginalIndices    = new();
    private Dictionary<TabViewModel, string>         _mediaOriginHost            = new();
    private Dictionary<TabViewModel, DispatcherTimer> _mediaDeactivationTimers   = new();
    private Dictionary<TabViewModel, DispatcherTimer> _paletteRefreshTimers      = new();

    private Dictionary<TabViewModel, DispatcherTimer> _colorAnimTimers = new();
    private Dictionary<TabViewModel, double> _colorAnimT = new();
    private Dictionary<TabViewModel, int> _colorAnimPhase = new();
    private Dictionary<TabViewModel, double> _colorAnimFade = new();
    private Dictionary<TabViewModel, List<System.Windows.Media.Color>> _prevPaletteColors = new();
    private Dictionary<TabViewModel, double> _paletteBlendT = new();
    private Dictionary<TabViewModel, DispatcherTimer> _loadingTimers = new();
    private Dictionary<TabViewModel, DispatcherTimer> _volumeDebounceTimers = new();

    private Dictionary<TabViewModel, string> _tabImageUrls = new();
    private Dictionary<TabViewModel, string> _tabSleepUrls = new();
    private Dictionary<TabViewModel, int>    _tabCrashCounts = new();
    private Dictionary<TabViewModel, System.Windows.Media.ImageSource> _tabThumbnails = new();
    private string _currentImageUrl = "";
    private static readonly System.Net.Http.HttpClient _imageHttp = new();

    private Dictionary<TabViewModel, double> _vizTime = new();
    private Dictionary<TabViewModel, double[]> _vizSmoothedAmps = new();
    private static readonly Random _vizRng = new Random();

    private DispatcherTimer _headerTimer = new DispatcherTimer();
    private DispatcherTimer _webAppBarTimer = new DispatcherTimer();
    private DispatcherTimer _sidebarDwellTimer = new DispatcherTimer();
    private DispatcherTimer _sensorPollTimer   = new DispatcherTimer();
    private bool _isHeaderLockedOpen = false;
    private bool _wpfShellLastFocused = true; // true = WPF shell had last click, false = WebView
    private bool _isSidebarLocked    = false;
    private DispatcherTimer _headerHideTimer = new DispatcherTimer();
    private System.Windows.Media.Animation.Storyboard? _notifySb;
    private bool _isReflowing = false;
    private bool _isFullscreen = false;
    private bool _isWebAppMode = false;
    private WindowState _previousWindowState = WindowState.Normal;

    // ── Tab Drag & Drop ──────────────────────────────────────────────
    private TabViewModel?   _draggedTab      = null;
    private Point           _dragStartPoint;
    private bool            _isDragging      = false;
    private Window?         _dragGhost       = null;
    private int             _dragInsertIndex = -1;

    // ── Multi-select (Ctrl/Shift-click) ──────────────────────────────
    private HashSet<TabViewModel> _multiSelectedTabs = new();
    private TabViewModel?         _lastClickedTab    = null;

    // ── Sleeping tabs ─────────────────────────────────────────────────
    private readonly DispatcherTimer _sleepTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private Dictionary<TabViewModel, DateTime> _tabLastActive = new();
    private Dictionary<TabViewModel, int>      _tabClickCount = new();
    private const int  SLEEP_PROTECT_N       = 5;
    private const int  SLEEP_MIN_TABS        = 6;
    private const long SLEEP_RAM_HIGH_MB     = 2500;
    private const long SLEEP_RAM_MODERATE_MB = 2000;
    private Controls.BrowserView? _activeDownloadBrowser;

    // ── Header Widget ─────────────────────────────────────────────────
    private int              _widgetModeIndex  = 0;

    // ── Tab Switcher (Ctrl+Tab window) ───────────────────────────────
    private Views.TabSwitcherWindow? _tabSwitcherWindow = null;
    private int                      _tabSwitcherIndex  = 0;
    private List<TabViewModel>       _mruTabs           = new();
    private int                      _naturalWorkBottom = 0;
    private bool                     _isNarrowMode      = false;
    private Point            _widgetSwipeStart;
    private bool             _widgetSwiping    = false;
    private const double     _widgetSwipePx    = 18.0; // min drag distance to count as a swipe
    private DispatcherTimer? _widgetCycleTimer = null;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime         _cpuLastCheck     = DateTime.UtcNow;
    private TimeSpan         _cpuLastTotal     = TimeSpan.Zero;
    private double           _cpuLastPct       = 0.0;
    private string           _weatherCache     = "⛅ N/A";
    private long             _cachedRamMb      = 0;
    private DateTime         _ramCacheTime     = DateTime.MinValue;
    private string           _weatherDetailCache   = "";
    private int              _weatherWmoCode       = -1;
    // Structured current-conditions cache (populated by FetchWeatherDetailAsync)
    private double _cachedWxTemp, _cachedWxFeelsLike, _cachedWxTempMax, _cachedWxTempMin;
    private int    _cachedWxHumidity, _cachedWxWindDir;
    private double _cachedWxWindSpd, _cachedWxWindGust;
    private double _cachedWxPrecip, _cachedWxDailyPrecip, _cachedWxRainChance;
    private double _cachedWxPressure, _cachedWxUv, _cachedWxVisM;
    private string _cachedWxSunrise = "", _cachedWxSunset = "", _cachedWxLoc = "";
    private double           _cachedWeatherLat     = 0;
    private double           _cachedWeatherLon     = 0;
    private string           _cachedWeatherTz      = "auto";
    private string           _cachedWeatherCity    = "";
    private const double          _widgetDefaultWidth   = 146.0;
    private LinearGradientBrush?  _weatherWidgetBrush;
    private DispatcherTimer? _weatherWidgetAnimTimer;
    private double           _weatherWidgetT       = 0.0;
    private int              _weatherWidgetPhase   = 0;
    private double           _weatherWidgetVizTime = 0.0;
    private double[]         _weatherWidgetAmps    = { 0.0, 0.0, 0.0 };
    private double           _weatherWidgetFade    = 0.0;
    private bool             _widgetFading     = false;
    private DispatcherTimer? _marqueeTimer;
    private double           _marqueeOffset    = 0;
    private double           _marqueeTextWidth = 0;
    private bool             _isMarqueeRunning = false;
    private DispatcherTimer? _videoWidgetTimer = null;
    private readonly DispatcherTimer _sessionAutoSaveTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool             _widgetDragging   = false;
    private Point            _widgetDragOrigin;
    private static readonly (string Key, string Label)[] _allWidgetDefs =
    {
        ("Clock",      "🕐  Clock"),      ("CPU",        "⚡  CPU Usage"),
        ("RAM",        "💾  RAM Usage"),  ("Media",      "🎵  Media"),
        ("Weather",    "⛅  Weather"),     ("Calculator", "🧮  Calculator"),
        ("Notes",      "📝  Notes"),      ("Converter",  "⇄   Converter"),
        ("Calendar",   "📅  Calendar"),   ("Navigation", "🧭  Navigation"),
        ("Notifications", "🔔  Notifications"),
    };

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (BackgroundKeepAliveService.InterceptClose()) { e.Cancel = true; return; }

        _clockTimer.Stop();
        _widgetCycleTimer?.Stop();
        _weatherRefreshTimer?.Stop();
        _sensorPollTimer.Stop();
        _sleepTimer.Stop();
        _videoWidgetTimer?.Stop();
        _headerTimer.Stop();
        _webAppBarTimer.Stop();
        _sidebarDwellTimer.Stop();
        _headerHideTimer.Stop();

        _sessionAutoSaveTimer.Stop();
        SaveCurrentSession();   // save while _tabViews + WebViews are still alive

        foreach (var view in _tabViews.Values.ToList())
        {
            try { view.MainWebView?.Dispose(); } catch { }
        }
        _tabViews.Clear();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            BorderThickness = new Thickness(0);
        }
        else
        {
            MaxWidth  = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            BorderThickness = new Thickness(1);
            if (SettingsService.Current.NarrowWindowMode == 0)
                this.MinWidth = Math.Max(400, SettingsService.Current.NarrowWindowThresholdPx);
            else
                this.MinWidth = 400;
        }

        if (WindowState == WindowState.Minimized)
        {
            // Drop all non-suspended WebViews to low memory mode
            foreach (var bv in _tabViews.Values)
            {
                try
                {
                    if (bv.MainWebView?.CoreWebView2 != null && !bv.MainWebView.CoreWebView2.IsSuspended)
                        bv.MainWebView.CoreWebView2.MemoryUsageTargetLevel =
                            CoreWebView2MemoryUsageTargetLevel.Low;
                }
                catch { }
            }
            // Ask Windows to page out the WPF process working set
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        else
        {
            // Restore to normal on un-minimize (skip suspended — Resume() handles those)
            foreach (var bv in _tabViews.Values)
            {
                try
                {
                    if (bv.MainWebView?.CoreWebView2 != null && !bv.MainWebView.CoreWebView2.IsSuspended)
                        bv.MainWebView.CoreWebView2.MemoryUsageTargetLevel =
                            CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                catch { }
            }
        }
    }

    // ── Taskbar-aware window management ─────────────────────────────────────
    // Handles: visible taskbar, auto-hide taskbar (all 4 edges), multi-monitor,
    // maximise constraints (WM_GETMINMAXINFO) AND manual resize/move clamping
    // (WM_WINDOWPOSCHANGING) so the window border never slides under the taskbar.

    private const int  WM_GETMINMAXINFO        = 0x0024;
    private const int  WM_WINDOWPOSCHANGING     = 0x0046;
    private const int  WM_LBUTTONDOWN          = 0x0201;
    private const int  WM_RBUTTONDOWN          = 0x0204;
    private const int  WM_NCLBUTTONDOWN        = 0x00A1;
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out System.Drawing.Point pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    private const int VK_CONTROL = 0x11;
    private static bool IsCtrlPhysicallyDown()
        => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    [DllImport("kernel32.dll")] private static extern IntPtr  CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
    [DllImport("kernel32.dll")] private static extern bool    Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] private static extern bool    Process32NextW (IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] private static extern bool    CloseHandle    (IntPtr hObject);
    [DllImport("kernel32.dll")] private static extern bool    SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMin, IntPtr dwMax);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint    dwSize, cntUsage, th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint    th32ModuleID, cntThreads, th32ParentProcessID;
        public int     pcPriClassBase;
        public uint    dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string  szExeFile;
    }

    private static long GetProcessTreeWorkingSetBytes()
    {
        const uint TH32CS_SNAPPROCESS = 0x00000002;
        var childMap = new Dictionary<int, List<int>>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == new IntPtr(-1)) goto fallback;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (Process32FirstW(snap, ref entry))
                do
                {
                    int pid = (int)entry.th32ProcessID, ppid = (int)entry.th32ParentProcessID;
                    if (!childMap.ContainsKey(ppid)) childMap[ppid] = new List<int>();
                    childMap[ppid].Add(pid);
                }
                while (Process32NextW(snap, ref entry));
        }
        finally { CloseHandle(snap); }

        long total = 0;
        var queue = new Queue<int>();
        queue.Enqueue(Process.GetCurrentProcess().Id);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            try { var p = Process.GetProcessById(cur); p.Refresh(); total += p.WorkingSet64; } catch { }
            if (childMap.TryGetValue(cur, out var kids)) foreach (var k in kids) queue.Enqueue(k);
        }
        return total;

        fallback:
        var mp = Process.GetCurrentProcess(); mp.Refresh();
        return mp.WorkingSet64;
    }
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2; // DWM rounded corners (Windows 11+)
    private const uint MONITOR_DEFAULTTONEAREST = 2u;

    // AppBar messages + states
    private const uint ABM_GETSTATE      = 0x00000004;
    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const int  ABS_AUTOHIDE      = 0x00000001;
    private const uint ABE_LEFT   = 0;
    private const uint ABE_TOP    = 1;
    private const uint ABE_RIGHT  = 2;
    private const uint ABE_BOTTOM = 3;

    // 2 px gap reserved when taskbar is auto-hidden — enough for Windows to
    // trigger the slide-out animation without a visible gap to the user.
    private const int AUTOHIDE_GAP = 2;

    [StructLayout(LayoutKind.Sequential)] private struct WPT  { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct WRC  { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WMMI
    { public WPT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct WMI
    { public int cbSize; public WRC rcMonitor; public WRC rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int    x, y, cx, cy, flags;
    }
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int    cbSize;
        public IntPtr hWnd;
        public uint   uCallbackMessage;
        public uint   uEdge;
        public WRC    rc;
        public IntPtr lParam;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool   GetMonitorInfo(IntPtr hMonitor, ref WMI lpmi);
    [DllImport("user32.dll")] private static extern bool   GetWindowRect(IntPtr hWnd, out WRC lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);
    [DllImport("user32.dll")] private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(TaskbarWndProc);

        // Windows 11 rounded corners — no-op on Windows 10 and below
        try
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { }
    }

    private IntPtr TaskbarWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if      (msg == WM_GETMINMAXINFO)     HandleMaximizeConstraints(hwnd, lParam, ref handled);
        else if (msg == WM_WINDOWPOSCHANGING) HandleWindowPositionChanging(hwnd, lParam);
        
        return IntPtr.Zero;
    }

    // ── WM_GETMINMAXINFO — maximised position & size ─────────────────────────

    private void HandleMaximizeConstraints(IntPtr hwnd, IntPtr lParam, ref bool handled)
    {
        var mmi     = (WMMI)Marshal.PtrToStructure(lParam, typeof(WMMI))!;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var mi = new WMI { cbSize = Marshal.SizeOf(typeof(WMI)) };
        GetMonitorInfo(monitor, ref mi);

        WRC work = _isFullscreen ? mi.rcMonitor : mi.rcWork;
        if (!_isFullscreen) AdjustForAutoHideTaskbar(monitor, mi, ref work);

        mmi.ptMaxPosition.x  = work.left   - mi.rcMonitor.left;
        mmi.ptMaxPosition.y  = work.top    - mi.rcMonitor.top;
        mmi.ptMaxSize.x      = work.right  - work.left;
        mmi.ptMaxSize.y      = work.bottom - work.top;
        int _nwMode      = SettingsService.Current.NarrowWindowMode;
        int _nwThreshold = SettingsService.Current.NarrowWindowThresholdPx;
        mmi.ptMinTrackSize.x = _nwMode == 0 ? Math.Max(400, _nwThreshold) : 400;
        mmi.ptMinTrackSize.y = 300;

        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
    }

    // ── WM_WINDOWPOSCHANGING — clamps every manual move & resize ─────────────
    // Tray hwnd is constant for the lifetime of the session; cache it once.
    private IntPtr _cachedTrayHwnd = IntPtr.Zero;

    private void HandleWindowPositionChanging(IntPtr hwnd, IntPtr lParam)
    {
        var wp = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS))!;
        if ((wp.flags & SWP_NOMOVE) != 0 && (wp.flags & SWP_NOSIZE) != 0) return;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var mi = new WMI { cbSize = Marshal.SizeOf(typeof(WMI)) };
        GetMonitorInfo(monitor, ref mi);

        WRC work = mi.rcWork;

        // Only invoke auto-hide detection when rcWork == rcMonitor (bar may be hidden).
        // During a normal drag the work area is always smaller, so this is nearly always skipped,
        // which avoids the expensive FindWindow + SHAppBarMessage calls on every mouse-move message.
        bool rcWorkMatchesMonitor =
            work.left == mi.rcMonitor.left && work.top    == mi.rcMonitor.top &&
            work.right == mi.rcMonitor.right && work.bottom == mi.rcMonitor.bottom;
        if (rcWorkMatchesMonitor)
            AdjustForAutoHideTaskbar(monitor, mi, ref work);

        GetWindowRect(hwnd, out var cur);
        int left   = (wp.flags & SWP_NOMOVE) != 0 ? cur.left              : wp.x;
        int top    = (wp.flags & SWP_NOMOVE) != 0 ? cur.top               : wp.y;
        int width  = (wp.flags & SWP_NOSIZE) != 0 ? cur.right  - cur.left : wp.cx;
        int height = (wp.flags & SWP_NOSIZE) != 0 ? cur.bottom - cur.top  : wp.cy;

        bool changed = false;

        // Bottom edge — most common case (taskbar at the bottom).
        // Self-calibrate the natural work bottom: if the work area extends to within 80px
        // of the monitor bottom it is a normal taskbar state (not a soft keyboard).
        if (work.bottom >= mi.rcMonitor.bottom - 80)
            _naturalWorkBottom = work.bottom;

        // When the soft keyboard is visible it shrinks work.bottom by 150-500px.
        // In that case let the OS move the window freely — clamping creates a black gap.
        bool softKeyboardActive = _naturalWorkBottom > 0 && work.bottom < _naturalWorkBottom - 120;

        if (!softKeyboardActive && top + height > work.bottom)
        {
            if ((wp.flags & SWP_NOSIZE) == 0) height = Math.Max(300, work.bottom - top);
            else                               top    = work.bottom - height;
            changed = true;
        }
        // Top edge
        if (top < work.top) { top = work.top; changed = true; }
        // Right edge
        if (left + width > work.right)
        {
            if ((wp.flags & SWP_NOSIZE) == 0) width = Math.Max(400, work.right - left);
            else                               left  = work.right - width;
            changed = true;
        }
        // Left edge
        if (left < work.left) { left = work.left; changed = true; }

        if (!changed) return;
        wp.x = left; wp.y = top; wp.cx = width; wp.cy = height;
        Marshal.StructureToPtr(wp, lParam, true);
    }

    // ── Auto-hide taskbar detection ──────────────────────────────────────────
    // When auto-hide is ON, rcWork == rcMonitor (the OS doesn't subtract the
    // hidden bar). We detect it and shrink the effective work rect by AUTOHIDE_GAP
    // on the correct edge so Windows can still trigger the slide-out animation.

    private void AdjustForAutoHideTaskbar(IntPtr monitor, WMI mi, ref WRC work)
    {
        // If rcWork is already smaller than rcMonitor the bar is visible — nothing to do.
        if (work.left   > mi.rcMonitor.left   || work.top    > mi.rcMonitor.top  ||
            work.right  < mi.rcMonitor.right  || work.bottom < mi.rcMonitor.bottom) return;

        // Cache the tray hwnd — it never changes while the shell is running.
        if (_cachedTrayHwnd == IntPtr.Zero)
            _cachedTrayHwnd = FindWindow("Shell_TrayWnd", null);
        IntPtr tray = _cachedTrayHwnd;
        if (tray == IntPtr.Zero) return;

        var abd = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)), hWnd = tray };
        if (((int)SHAppBarMessage(ABM_GETSTATE, ref abd) & ABS_AUTOHIDE) == 0) return;

        switch (GetAutoHideTaskbarEdge(monitor, tray))
        {
            case ABE_BOTTOM: work.bottom -= AUTOHIDE_GAP; break;
            case ABE_TOP:    work.top    += AUTOHIDE_GAP; break;
            case ABE_LEFT:   work.left   += AUTOHIDE_GAP; break;
            case ABE_RIGHT:  work.right  -= AUTOHIDE_GAP; break;
        }
    }

    private uint GetAutoHideTaskbarEdge(IntPtr targetMonitor, IntPtr tray)
    {
        var mi = new WMI { cbSize = Marshal.SizeOf(typeof(WMI)) };
        GetMonitorInfo(targetMonitor, ref mi);
        WRC mon = mi.rcMonitor;

        foreach (uint edge in new[] { ABE_BOTTOM, ABE_TOP, ABE_LEFT, ABE_RIGHT })
        {
            var abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(APPBARDATA)),
                hWnd   = tray,
                uEdge  = edge
            };
            SHAppBarMessage(ABM_GETTASKBARPOS, ref abd);

            if (abd.rc.right > mon.left && abd.rc.left < mon.right &&
                abd.rc.bottom > mon.top && abd.rc.top  < mon.bottom)
                return edge;
        }
        return ABE_BOTTOM; // safe default
    }

    private static readonly string _cookiesBackupPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cookies.json");

    public async Task<int> SyncCookiesFromBrowserAsync(bool useChrome, bool useEdge, string? domainFilter)
    {
        var cookies = new List<ImportedCookie>();

        if (useChrome) cookies.AddRange(await Task.Run(() => ChromiumHarvester.HarvestCookies(BrowserType.Chrome, domainFilter)));
        if (useEdge)   cookies.AddRange(await Task.Run(() => ChromiumHarvester.HarvestCookies(BrowserType.Edge,   domainFilter)));

        if (cookies.Count == 0) return 0;

        await InjectCookiesIntoAllTabsAsync(cookies);
        await SaveCookieBackupAsync(cookies, domainFilter);
        return cookies.Count;
    }

    private async Task InjectCookiesIntoAllTabsAsync(List<ImportedCookie> cookies)
    {
        var mgr = _tabViews.Values
            .Select(bv => bv.MainWebView?.CoreWebView2?.CookieManager)
            .FirstOrDefault(m => m != null);
        if (mgr == null) return;

        await Task.Run(() =>
        {
            foreach (var c in cookies)
            {
                try
                {
                    var wv2Cookie = mgr.CreateCookie(c.Name, c.Value, c.Domain, c.Path ?? "/");
                    wv2Cookie.IsHttpOnly = c.IsHttpOnly;
                    wv2Cookie.IsSecure   = c.IsSecure;
                    if (c.ExpiresUtc.HasValue)
                        wv2Cookie.Expires = c.ExpiresUtc.Value;
                    mgr.AddOrUpdateCookie(wv2Cookie);
                }
                catch { }
            }
        });
    }

    private async Task SaveCookieBackupAsync(List<ImportedCookie> incoming, string? domainFilter)
    {
        try
        {
            List<ImportedCookie> existing = new();
            if (File.Exists(_cookiesBackupPath))
            {
                string raw = await File.ReadAllTextAsync(_cookiesBackupPath);
                existing = JsonSerializer.Deserialize<List<ImportedCookie>>(raw)
                           ?? new();
            }

            var merged = existing
                .Where(e => !incoming.Any(i => i.Name == e.Name && i.Domain == e.Domain))
                .Concat(incoming)
                .ToList();

            await File.WriteAllTextAsync(_cookiesBackupPath,
                JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));

            LogService.Write("COOKIES", $"Backup saved: {merged.Count} total cookies.");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "SaveCookieBackup");
        }
    }

    public async Task LoadPersistedCookiesAsync(CoreWebView2CookieManager mgr)
    {
        if (!File.Exists(_cookiesBackupPath)) return;
        try
        {
            string raw = await File.ReadAllTextAsync(_cookiesBackupPath);
            var cookies = JsonSerializer.Deserialize<List<ImportedCookie>>(raw);
            if (cookies == null || cookies.Count == 0) return;

            await Task.Run(() =>
            {
                foreach (var c in cookies)
                {
                    try
                    {
                        var wv2Cookie = mgr.CreateCookie(c.Name, c.Value, c.Domain, c.Path ?? "/");
                        wv2Cookie.IsHttpOnly = c.IsHttpOnly;
                        wv2Cookie.IsSecure   = c.IsSecure;
                        if (c.ExpiresUtc.HasValue)
                            wv2Cookie.Expires = c.ExpiresUtc.Value;
                        mgr.AddOrUpdateCookie(wv2Cookie);
                    }
                    catch { }
                }
            });

            LogService.Write("COOKIES", $"Loaded {cookies.Count} persisted cookies on startup.");
        }
        catch (Exception ex)
        {
            LogService.Write("COOKIES", $"WARNING: cookies.json is malformed, resetting. ({ex.Message})");
            try { File.Delete(_cookiesBackupPath); } catch { }
        }
    }

    private bool _cookiesLoadedOnce = false;

    private void SaveCurrentSession()
    {
        try
        {
            var urls = _allTabs.Select(t => t.Url).Where(u => !string.IsNullOrEmpty(u)).ToList();
            SettingsService.Current.LastSessionUrls = urls;
            SettingsService.Save();
            LogService.Write("SESSION", $"Saved {urls.Count} tabs for session restore.");
        }
        catch (Exception ex)
        {
            LogService.Write("SESSION", $"Shutdown save failed: {ex.Message}");
        }
    }

    public Controls.BrowserView? CurrentBrowser
    {
        get
        {
            if (ListTabs.SelectedItem is TabViewModel pTab && _tabViews.ContainsKey(pTab))
                return _tabViews[pTab];
            if (ListOverflowTabs.SelectedItem is TabViewModel oTab && _tabViews.ContainsKey(oTab))
                return _tabViews[oTab];
            return null;
        }
    }

    private void CheckSessionRestore()
    {
        if (!SettingsService.Current.ShowSessionRestore) return;
        var savedUrls = SettingsService.Current.LastSessionUrls;
        if (savedUrls == null || savedUrls.Count == 0) return;

        if (SettingsService.Current.AutoRestoreSession)
        {
            OpenSessionRestorePicker();
            return;
        }

        RestoreBanner.Visibility = Visibility.Visible;
    }

    private void BtnRestoreSession_Click(object sender, RoutedEventArgs e)
    {
        RestoreBanner.Visibility = Visibility.Collapsed;
        OpenSessionRestorePicker();
    }

    private void BtnDismissRestore_Click(object sender, RoutedEventArgs e)
    {
        RestoreBanner.Visibility = Visibility.Collapsed;
        try
        {
            SettingsService.Current.LastSessionUrls = new List<string>();
            SettingsService.Save();
            LogService.Write("SESSION", "User dismissed session restore.");
        }
        catch (Exception ex)
        {
            LogService.Write("SESSION", $"Failed to clear session: {ex.Message}");
        }
    }

    private void OpenSessionRestorePicker()
    {
        var savedUrls = SettingsService.Current.LastSessionUrls
            ?.Where(u => !string.IsNullOrEmpty(u)).ToList();
        if (savedUrls == null || savedUrls.Count == 0) return;

        var win = new Window
        {
            Title = "Restore Previous Session", Width = Math.Min(savedUrls.Count * 160 + 40, 900),
            Height = 240, MinWidth = 360,
            Background  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x13,0x13,0x13)),
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.CanResizeWithGrip,
            Owner = this, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var outer = new DockPanel();

        // ── Top label ─────────────────────────────────────────────────────────
        var hdr = new TextBlock
        {
            Text = "Choose tabs to reopen from your last session:",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88,0x88,0x88)),
            FontSize = 11, Margin = new Thickness(12, 10, 12, 6)
        };
        DockPanel.SetDock(hdr, Dock.Top);
        outer.Children.Add(hdr);

        // ── Bottom button row ─────────────────────────────────────────────────
        var btnRow = new System.Windows.Controls.StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 6, 10, 10)
        };
        DockPanel.SetDock(btnRow, Dock.Bottom);

        Button MkBtn(string label, System.Windows.Media.Color bg, System.Windows.Media.Color border) => new Button
        {
            Content = label, Height = 30, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(6, 0, 0, 0),
            Background  = new System.Windows.Media.SolidColorBrush(bg),
            Foreground  = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(border),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontSize = 11
        };

        var btnRestoreAll = MkBtn("↺  Restore All",
            System.Windows.Media.Color.FromRgb(0x1a,0x3a,0x1a),
            System.Windows.Media.Color.FromRgb(0x2a,0x6a,0x2a));
        var btnRestoreSel = MkBtn("✔  Restore Selected",
            System.Windows.Media.Color.FromRgb(0x1a,0x34,0x54),
            System.Windows.Media.Color.FromRgb(0x2e,0x6a,0xa0));
        var btnCancel = MkBtn("✕  Cancel",
            System.Windows.Media.Color.FromRgb(0x22,0x22,0x22),
            System.Windows.Media.Color.FromRgb(0x44,0x44,0x44));

        btnRow.Children.Add(btnRestoreAll);
        btnRow.Children.Add(btnRestoreSel);
        btnRow.Children.Add(btnCancel);
        outer.Children.Add(btnRow);

        // ── Horizontal tab cards ──────────────────────────────────────────────
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Margin = new Thickness(10, 0, 10, 4)
        };
        var cardRow = new System.Windows.Controls.StackPanel { Orientation = Orientation.Horizontal };
        scroll.Content = cardRow;
        outer.Children.Add(scroll);

        var checkboxes = new List<(CheckBox chk, string url)>();

        foreach (var url in savedUrls)
        {
            string display = url;
            try
            {
                var uri = new Uri(url);
                display = uri.Host.TrimStart('w', '.');
                if (display.Length > 22) display = display[..22] + "…";
            }
            catch { if (display.Length > 25) display = display[..25] + "…"; }

            var card = new Border
            {
                Width = 150, Margin = new Thickness(4),
                Background      = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1a,0x1a,0x1a)),
                BorderBrush     = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2e,0x2e,0x2e)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 6, 8, 6), Cursor = Cursors.Hand
            };
            var chk = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 6, 0) };
            var lbl = new TextBlock
            {
                Text = display, FontSize = 11, Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                ToolTip = url
            };
            var inner = new System.Windows.Controls.StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(chk); inner.Children.Add(lbl);
            card.Child = inner;
            card.MouseLeftButtonUp += (s, e) => { chk.IsChecked = !(chk.IsChecked == true); };
            checkboxes.Add((chk, url));
            cardRow.Children.Add(card);
        }

        async Task DoRestore(IEnumerable<string> urls)
        {
            win.Close();
            SettingsService.Current.LastSessionUrls = new List<string>();
            SettingsService.Save();
            try
            {
                foreach (var url in urls)
                {
                    CreateNewTab(url);
                    await Task.Delay(120);
                }
                LogService.Write("SESSION", $"Restored tabs from previous session.");
            }
            catch (Exception ex)
            {
                LogService.Write("SESSION", $"Session restore failed: {ex.Message}");
            }
        }

        btnRestoreAll.Click += async (s, e) => await DoRestore(savedUrls);
        btnRestoreSel.Click += async (s, e) =>
            await DoRestore(checkboxes.Where(x => x.chk.IsChecked == true).Select(x => x.url));
        btnCancel.Click += (s, e) =>
        {
            win.Close();
            SettingsService.Current.LastSessionUrls = new List<string>();
            SettingsService.Save();
        };

        win.Content = outer;
        win.Show();
    }

    // startUrl: optional file path or URL passed as a command-line argument
    private async Task RunSilentUpdateCheckAsync()
    {
        try
        {
            string? updateTxtPath = ResolveStartupFilePath("update.txt");
            string? versionTxtPath = ResolveStartupFilePath("version.txt");
            if (updateTxtPath == null || versionTxtPath == null) return;

            var result = await GithubUpdateService.CheckForUpdateAsync(updateTxtPath, versionTxtPath);
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                LogService.Write("UPDATE", $"Silent check failed: {result.ErrorMessage}");
                return;
            }
            if (!result.UpdateAvailable) return;

            if (SettingsService.Current.AutoDownloadUpdatesEnabled
                && !string.IsNullOrEmpty(result.AssetUrl)
                && !string.IsNullOrEmpty(result.AssetName))
            {
                string path = await GithubUpdateService.DownloadAssetAsync(result.AssetUrl, result.AssetName);
                SettingsService.Current.PendingUpdateInstallerPath = path;
                SettingsService.Save();
                LogService.Write("UPDATE", $"Auto-downloaded update {result.LatestVersion} to {path}");
                Dispatcher.Invoke(() => ShowUpdateInstallBanner(path));
            }
            else
            {
                Dispatcher.Invoke(() => ShowUpdateNotification(
                    $"Update available: {result.LatestVersion} ({result.CurrentChannel}).\nOpen Settings → General to download."));
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Silent Update Check");
        }
    }

    private void ShowUpdateNotification(string message)
    {
        MessageBox.Show(message, "Horizon Update", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string? _pendingUpdateInstallerPath;

    private void ShowUpdateInstallBanner(string installerPath)
    {
        _pendingUpdateInstallerPath = installerPath;
        PanelUpdateBanner.Visibility = Visibility.Visible;
    }

    private void BtnDismissUpdateBanner_Click(object sender, RoutedEventArgs e)
    {
        PanelUpdateBanner.Visibility = Visibility.Collapsed;
        SettingsService.Current.PendingUpdateInstallerPath = "";
        SettingsService.Save();
    }

    private void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_pendingUpdateInstallerPath) || !File.Exists(_pendingUpdateInstallerPath))
        {
            MessageBox.Show("The downloaded update file is missing. Please check for updates again.",
                "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            PanelUpdateBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var confirm = MessageBox.Show(
            "Horizon will close, back up your data, install the update, then restore and relaunch automatically.\n\nContinue?",
            "Install Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        string? orchestratorPath = ResolveStartupFilePath("update_orchestrator.bat");
        if (orchestratorPath == null)
        {
            MessageBox.Show("update_orchestrator.bat not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        WriteCurrentPathFile();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = orchestratorPath,
                Arguments = $"\"{_pendingUpdateInstallerPath}\"",
                WorkingDirectory = Path.GetDirectoryName(orchestratorPath),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "Launching update_orchestrator.bat");
            MessageBox.Show($"Could not start the update: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SettingsService.Current.PendingUpdateInstallerPath = "";
        SettingsService.Save();

        Application.Current.Shutdown();
    }

    private static void WriteCurrentPathFile()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string? pathTxt = ResolveStartupFilePath("path.txt");
            string targetFile = pathTxt ?? Path.Combine(baseDir, "path.txt");
            File.WriteAllText(targetFile, baseDir);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WriteCurrentPathFile");
        }
    }

    private static string? ResolveStartupFilePath(string filename)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string candidate = Path.Combine(baseDir, filename);
        if (File.Exists(candidate)) return candidate;

        string? dir = baseDir;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "start_mapper.bat")))
            {
                string nested = Path.Combine(dir, filename);
                if (File.Exists(nested)) return nested;
                break;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    public MainWindow(string? startUrl = null, bool isWebApp = false)
    {
        try { SettingsService.Load(); }
        catch (Exception ex) { LogService.RecordCrash(ex, "Startup"); }

        

        _ = Task.Run(() =>
        {
            try
            {
                VaultService.Initialize();
                HistoryService.Load();
                if (!string.IsNullOrEmpty(SettingsService.Current.DownloadsPath))
                    FluxJanitorService.Initialize();
            }
            catch (Exception ex) { LogService.RecordCrash(ex, "Background Startup"); }
        });

        if (SettingsService.Current.SilentUpdateCheckEnabled)
            _ = RunSilentUpdateCheckAsync();

        WriteCurrentPathFile();

        GithubUpdateService.UpdateReadyToInstall += (path) => Dispatcher.Invoke(() => ShowUpdateInstallBanner(path));

        if (!string.IsNullOrEmpty(SettingsService.Current.PendingUpdateInstallerPath)
            && File.Exists(SettingsService.Current.PendingUpdateInstallerPath))
        {
            ShowUpdateInstallBanner(SettingsService.Current.PendingUpdateInstallerPath);
        }

        _isWebAppMode = isWebApp;

        // FIX: When launched via the default-browser protocol (e.g. opening an HTML file
        // or a URL handler), startUrl is a one-time launch target — NOT the new homepage.
        // Previously this line permanently overwrote SettingsService.Current.HomePage,
        // so every protocol-open would silently change the user's home page.
        // Now we keep HomePage untouched and route the launch URL directly to CreateNewTab.
        string _initialTabUrl = !string.IsNullOrEmpty(startUrl)
            ? startUrl
            : SettingsService.Current.HomePage;

        InitializeComponent();

        

        this.Closing += MainWindow_Closing;
        BackgroundKeepAliveService.Initialize(this);
        BackgroundKeepAliveService.OnMediaCommandReceived = ExecuteMediaWidgetCommand;
        this.StateChanged += MainWindow_StateChanged;

        Application.Current.SessionEnding += (s, e) => SaveCurrentSession();
        Application.Current.Exit += (s, e) => { if (!Controls.BrowserView.IsRestartPending) SaveCurrentSession(); };
        Controls.BrowserView.SaveSessionBeforeRestart = SaveCurrentSession;
        _sessionAutoSaveTimer.Tick += (s, e) => SaveCurrentSession();
        _sessionAutoSaveTimer.Start();

        DataContext = this;

        OmniboxControl.NavigateRequested += (s, url) =>
        {
            if (GetCurrentTabViewModel() is TabViewModel tab && !tab.IsLoading)
            {
                tab.IsLoading = true;
                StartLoadingAnimation(tab);
            }
            _wpfShellLastFocused = false;
            CurrentBrowser?.Navigate(url);
        };

        MobileOmniboxControl.NavigateRequested += (s, url) =>
        {
            if (GetCurrentTabViewModel() is TabViewModel tab && !tab.IsLoading)
            {
                tab.IsLoading = true;
                StartLoadingAnimation(tab);
            }
            _wpfShellLastFocused = false;
            CurrentBrowser?.Navigate(url);
        };

        RightSidebar.RequestAddPin += (s, e) =>
        {
            if (CurrentBrowser?.MainWebView != null && CurrentBrowser.MainWebView.Source != null)
            {
                string title = CurrentBrowser.MainWebView.CoreWebView2?.DocumentTitle ?? "";
                RightSidebar.AddPin(CurrentBrowser.MainWebView.Source.ToString(), title);
            }
        };

        RightSidebar.RequestNavigate += (s, url) => CurrentBrowser?.Navigate(url);

        RightSidebar.RequestExtensionPopup += RightSidebar_RequestExtensionPopup;

        RightSidebar.PauseDownloadRequested  += () => _activeDownloadBrowser?.ToggleDownloadPause();
        RightSidebar.CancelDownloadRequested += () => _activeDownloadBrowser?.CancelCurrentDownload();

        SetupGhostTrigger();

        HeaderContainer.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        MobileHeaderContainer.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        this.ContentRendered += (s, e) => ApplyNarrowWindowMode();
        HeaderContainer.MouseRightButtonUp  += HeaderContainer_RightClick;

        this.SizeChanged += (s, e) =>
        {
            if (ActualWidth < 100) return;
            ScheduleReflow();
            ApplyNarrowWindowMode();
        };
        ListTabs.SizeChanged += (s, e) =>
        {
            if (e.WidthChanged && ListTabs.ActualWidth >= 50)
            {
                _lastGoodTabBarWidth = ListTabs.ActualWidth;
                ScheduleReflow();
            }
        };
        this.ContentRendered += (s, e) => ScheduleReflow();
        this.PreviewKeyDown  += Window_PreviewKeyDown;
        this.PreviewKeyUp    += Window_PreviewKeyUp;

        // Track which surface received the last mouse click so arrow-key routing is correct.
        // WebView2 sinks focus silently — this is the only reliable detection point.
        this.PreviewMouseDown += (s, e) =>
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is Microsoft.Web.WebView2.Wpf.WebView2)
                { _wpfShellLastFocused = false; return; }
                hit = VisualTreeHelper.GetParent(hit);
            }
            _wpfShellLastFocused = true;
        };

        ApplyLayoutState();

        CreateNewTab(_initialTabUrl);

        LogService.Write("UI", "MainWindow Ready (Multi-Instance + Shortcuts).");

        this.Loaded += (s, e) => CheckSessionRestore();
        this.Loaded += (s, e) => InitWidget();
        this.Loaded += (s, e) => InitNotificationsWidget();
        this.Loaded += (s, e) => InitSleepingTabs();
        
    }

    private void UpdateCurrentTabInfo(string title, string url)
    {
        if (ListTabs.SelectedItem is TabViewModel pTab) { pTab.Title = title; pTab.Url = url; }
        else if (ListOverflowTabs.SelectedItem is TabViewModel oTab) { oTab.Title = title; oTab.Url = url; }
    }

    private void SetupGhostTrigger()
    {
        _sidebarDwellTimer.Interval = TimeSpan.FromMilliseconds(100);
        _sidebarDwellTimer.Tick += GhostTrigger_Tick;

        // ── Sensor poll: bypasses WebView2 HWND airspace by polling cursor position ──
        // Fires every 100ms. When mouse is in the rightmost 20px and sidebar is hidden,
        // starts the dwell timer (same as the old SensorRight.MouseEnter did).
        bool _sensorWasHot = false;
        bool _headerPollWasHot = false;
        _sensorPollTimer.Interval = TimeSpan.FromMilliseconds(100);
        _sensorPollTimer.Tick += (s, e) =>
        {
            if (!this.IsVisible || PresentationSource.FromVisual(this) == null) return;

            GetCursorPos(out var screenPt);
            var clientPt = this.PointFromScreen(new Point(screenPt.X, screenPt.Y));

            // ── Header top-edge poll ───────────────────────────────────────
            bool headerAutoHide = SettingsService.Current.AutoHideHeader || _isFullscreen;
            bool headerHidden   = HeaderContainer.Height == 0;
            if (headerAutoHide && headerHidden && !_isWebAppMode)
            {
                bool headerHot = clientPt.Y <= 2
                              && clientPt.X >= 0
                              && clientPt.X < this.ActualWidth * (2.0 / 3.0);
                if (headerHot  && !_headerPollWasHot) { _headerPollWasHot = true;  _headerTimer.Start(); }
                if (!headerHot && _headerPollWasHot)  { _headerPollWasHot = false; _headerTimer.Stop();  }
            }
            else if (_headerPollWasHot)
            {
                _headerPollWasHot = false;
                _headerTimer.Stop();
            }

            // ── Sidebar right-edge poll ────────────────────────────────────
            bool autoHide = SettingsService.Current.AutoHideSidebar || _isFullscreen;
            bool sidebarHidden = SidebarContainer.Width < 10;
            if (!autoHide || !sidebarHidden)
            {
                if (_sensorWasHot) { _sensorWasHot = false; _sidebarDwellTimer.Stop(); }
                return;
            }
            double sensorBottom = Math.Max(HeaderContainer.ActualHeight, 60);
            bool hot = clientPt.X >= this.ActualWidth - 20
                    && clientPt.X <= this.ActualWidth
                    && clientPt.Y >= 0
                    && clientPt.Y <= sensorBottom;
            if (hot && !_sensorWasHot)  { _sensorWasHot = true;  _sidebarDwellTimer.Start(); }
            if (!hot && _sensorWasHot)  { _sensorWasHot = false; _sidebarDwellTimer.Stop();  }
        };
        _sensorPollTimer.Start();

        // ── Context-menu guard: ContextMenu popup fires spurious MouseLeave ──
        bool _ctxOpen = false;
        SidebarContainer.ContextMenuOpening += (s, e) => _ctxOpen = true;
        SidebarContainer.ContextMenuClosing += (s, e) =>
        {
            _ctxOpen = false;
            if (!SidebarContainer.IsMouseOver && !RightSidebar.IsAnyPopupOpen)
            {
                bool shouldHide = (SettingsService.Current.AutoHideSidebar && !_isSidebarLocked) || _isFullscreen;
                if (shouldHide) ToggleSidebar(false);
            }
        };

        SidebarContainer.MouseLeave += (s, e) =>
        {
            if (_ctxOpen) return;
            if (RightSidebar.IsAnyPopupOpen) return;
            bool shouldHide = (SettingsService.Current.AutoHideSidebar && !_isSidebarLocked) || _isFullscreen;
            if (shouldHide) ToggleSidebar(false);
        };

        // ── WebApp bar auto-hide timer ─────────────────────────────────────
        _webAppBarTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _webAppBarTimer.Tick += (s, e) => { _webAppBarTimer.Stop(); FadeWebAppBar(false); };

        _headerTimer.Interval = TimeSpan.FromMilliseconds(50);
        _headerTimer.Tick += (s, e) =>
        {
            _headerTimer.Stop();
            ToggleHeader(true);
        };

        _headerHideTimer.Interval = TimeSpan.FromMilliseconds(200);
        _headerHideTimer.Tick += (s, e) =>
        {
            _headerHideTimer.Stop();
            if (!_isHeaderLockedOpen) ToggleHeader(false);
        };

        // Click anywhere outside the header clears the lock
        this.PreviewMouseDown += (s, e) =>
        {
            if (!_isHeaderLockedOpen) return;
            var hit = e.OriginalSource as DependencyObject;
            bool insideHeader = hit != null && IsVisualChild(HeaderContainer, hit);
            if (!insideHeader) _isHeaderLockedOpen = false;
        };

        HeaderContainer.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _isHeaderLockedOpen = true;
            _headerHideTimer.Stop();
        };

        SensorTop.MouseEnter += (s, e) =>
        {
            if (_isWebAppMode) { ShowWebAppBar(); return; }
            _headerHideTimer.Stop();
            if ((SettingsService.Current.AutoHideHeader || _isFullscreen) && HeaderContainer.Height == 0)
                _headerTimer.Start();
        };

        HeaderContainer.MouseEnter += (s, e) => _headerHideTimer.Stop();
        SensorTop.MouseLeave += (s, e) =>
        {
            if (_isWebAppMode) return;
            _headerTimer.Stop();
        };

        SensorPillLeft.MouseEnter += (s, e) =>
        {
            if (_isWebAppMode) { ShowWebAppBar(); return; }
            if ((SettingsService.Current.AutoHideHeader || _isFullscreen) && HeaderContainer.Height == 0)
                _headerTimer.Start();
        };
        SensorPillLeft.MouseLeave += (s, e) =>
        {
            if (_isWebAppMode) return;
            _headerTimer.Stop();
        };

        WebAppBar.MouseEnter += (s, e) => _webAppBarTimer.Stop();
        WebAppBar.MouseLeave += (s, e) => { _webAppBarTimer.Stop(); _webAppBarTimer.Start(); };
        WebAppDragGrip.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    private void GhostTrigger_Tick(object? sender, EventArgs e)
    {
        _sidebarDwellTimer.Stop();
        ToggleSidebar(true);
    }

    private static bool IsVisualChild(DependencyObject parent, DependencyObject child)
    {
        var current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                   ?? System.Windows.LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void StartLoadingAnimation(TabViewModel tab)
    {
        if (_loadingTimers.ContainsKey(tab)) return;
        tab.LoadingOpacity = 0.0;
        tab.LoadingProgress = 0.05;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (s, e) =>
        {
            if (tab.LoadingOpacity < 1.0)
                tab.LoadingOpacity = Math.Min(1.0, tab.LoadingOpacity + (40.0 / 860.0));
            if (tab.LoadingProgress < 0.85)
                tab.LoadingProgress += 0.015;
        };
        _loadingTimers[tab] = timer;
        timer.Start();
    }

    private void StopLoadingAnimation(TabViewModel tab)
    {
        if (_loadingTimers.TryGetValue(tab, out var timer))
        {
            timer.Stop();
            _loadingTimers.Remove(tab);
        }
        tab.LoadingProgress = 1.0;

        int steps = 8;
        int stepIndex = 0;
        double initialOpacity = tab.LoadingOpacity;
        var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(107.5) };
        fadeTimer.Tick += (s, e) =>
        {
            if (!_tabViews.ContainsKey(tab)) { fadeTimer.Stop(); return; }
            stepIndex++;
            double alpha = 1.0 - (stepIndex / (double)steps);
            tab.LoadingOpacity = initialOpacity * alpha;
            if (stepIndex >= steps)
            {
                tab.LoadingOpacity = 0;
                fadeTimer.Stop();
            }
        };
        _loadingTimers[tab] = fadeTimer;
        fadeTimer.Start();
    }
    private static double TAB_DEFAULT_WIDTH => Math.Max(90.0, SettingsService.Current.TabDefaultWidth);
    private static double TAB_MEDIA_WIDTH   => TAB_DEFAULT_WIDTH + 2.7;
    private const  double TAB_MIN_WIDTH     =  90.0;
    private const  double TAB_MARGIN        =   3.8;

    private double GetTabBarAvailableWidth()
    {
        double available = ListTabs.ActualWidth;
        if (available >= 50)
        {
            _lastGoodTabBarWidth = available;
            return available;
        }
        // Use last known good measurement — prevents all tabs from vanishing
        // during layout invalidation (resize, add/remove tab, etc.)
        if (_lastGoodTabBarWidth >= 50)
            return _lastGoodTabBarWidth;
        // Last resort: estimate from window width
        double reserved = 136 + 290 + 44 + 250;
        available = ActualWidth - reserved;
        return available < 50 ? 50 : available;
    }

    /// <summary>
    /// Calculates how many regular (non-protected) tabs fit in the primary bar
    /// and at what width, after reserving space for protected tabs at TAB_DEFAULT_WIDTH.
    /// Protected tabs = media-mode (HasEverPlayedAudio) or download-mode (HasEverDownloaded).
    /// </summary>
    private (int RegularFit, double RegularWidth) GetRegularTabLayout(int protectedCount, int regularCount)
    {
        double available      = GetTabBarAvailableWidth();
        double protectedSpace = protectedCount * (TAB_DEFAULT_WIDTH + TAB_MARGIN * 2);
        double remaining      = Math.Max(0, available - protectedSpace);

        if (regularCount == 0) return (0, TAB_DEFAULT_WIDTH);

        double exact = remaining / regularCount - TAB_MARGIN * 2;
        if (exact >= TAB_DEFAULT_WIDTH) return (regularCount, TAB_DEFAULT_WIDTH);
        if (exact >= TAB_MIN_WIDTH)     return (regularCount, exact);

        int fit = Math.Max(0, (int)(remaining / (TAB_MIN_WIDTH + TAB_MARGIN * 2)));
        if (fit == 0) return (0, TAB_MIN_WIDTH);
        double fitWidth = Math.Min(TAB_DEFAULT_WIDTH, remaining / fit - TAB_MARGIN * 2);
        return (fit, Math.Max(TAB_MIN_WIDTH, fitWidth));
    }

    private void ReflowTabs()
    {
        if (_isNarrowMode) { RefreshCompactBadge(); RefreshMobileTabBadge(); }
        if (_allTabs.Count == 0) return;

        var selectedItem = ListTabs.SelectedItem ?? ListOverflowTabs.SelectedItem;

        var previousOverflow = new HashSet<TabViewModel>(OverflowTabs);

        _isReflowing = true;
        Tabs.Clear();
        OverflowTabs.Clear();

        // Protected tabs (media-mode or download-mode) always stay at TAB_DEFAULT_WIDTH.
        // Regular tabs get the remaining space and can shrink or overflow.
        int protectedCount = _allTabs.Count(t => t.HasEverPlayedAudio || t.HasEverDownloaded);
        int regularCount   = _allTabs.Count - protectedCount;
        var (regularFit, regularWidth) = GetRegularTabLayout(protectedCount, regularCount);

        var titleGroups = _allTabs
            .GroupBy(t => t.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        int regularPrimary = 0;
        foreach (var tab in _allTabs)
        {
            bool isProtected  = tab.HasEverPlayedAudio || tab.HasEverDownloaded;
            bool goesToPrimary;

            if (isProtected)
            {
                // Protected tabs: always primary, always default width — no shrinking
                tab.TabWidth   = tab.HasEverPlayedAudio ? TAB_MEDIA_WIDTH : TAB_DEFAULT_WIDTH;
                goesToPrimary  = true;
            }
            else
            {
                // Regular tabs: fill remaining space; overflow when space runs out
                goesToPrimary = regularPrimary < regularFit;
                tab.TabWidth  = goesToPrimary ? regularWidth : TAB_DEFAULT_WIDTH;
                if (goesToPrimary) regularPrimary++;
            }

            if (titleGroups.TryGetValue(tab.DisplayTitle, out var group))
                tab.DuplicateTitleIndex = group.IndexOf(tab) + 1;
            else
                tab.DuplicateTitleIndex = 0;

            if (goesToPrimary) Tabs.Add(tab);
            else
            {
                OverflowTabs.Add(tab);
                if (!previousOverflow.Contains(tab)) AnimateOverflowTabIn(tab);
            }
        }

        if (selectedItem != null)
        {
            if (Tabs.Contains(selectedItem))
            { ListTabs.SelectedItem = selectedItem; ListOverflowTabs.SelectedItem = null; }
            else if (OverflowTabs.Contains(selectedItem))
            { ListOverflowTabs.SelectedItem = selectedItem; ListTabs.SelectedItem = null; }
        }

        bool hasOverflow = OverflowTabs.Count > 0;
        if (!hasOverflow) _overflowBarHidden = false;
        ListOverflowTabs.Visibility = hasOverflow && !_overflowBarHidden && !_isFullscreen ? Visibility.Visible : Visibility.Collapsed;
        BtnExpandOverflow.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
        UpdateExpandButtonLayout();
        

        if (!hasOverflow && _overflowExpanded)
        {
            _overflowExpanded = false;
            ListOverflowTabs.Height = 50;
            BtnExpandOverflow.Content = "▼";
        }

        _isReflowing = false;

        var reflowSel = ListTabs.SelectedItem as TabViewModel
                     ?? ListOverflowTabs.SelectedItem as TabViewModel;
        if (reflowSel != null && _tabViews.TryGetValue(reflowSel, out var reflowView))
            reflowView.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ResetPrimaryScrollToStart);
    }

    private void ResetPrimaryScrollToStart()
    {
        if (FindVisualChild<ScrollViewer>(ListTabs) is ScrollViewer sv)
            sv.ScrollToHorizontalOffset(0);
    }

    private int GetMaxPrimaryTabs()
    {
        int prot = _allTabs.Count(t => t.HasEverPlayedAudio || t.HasEverDownloaded);
        int reg  = _allTabs.Count - prot;
        return prot + GetRegularTabLayout(prot, reg).RegularFit;
    }

    /// <summary>Slides a newly created primary-bar tab in from width=0.</summary>
    private void AnimateTabIn(TabViewModel tab)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (ListTabs.ItemContainerGenerator.ContainerFromItem(tab) is not ListBoxItem lbi) return;
            var bb  = lbi.Template?.FindName("BaseBorder", lbi) as Border;
            var tb2 = lbi.Template?.FindName("TabBorder",  lbi) as Border;
            if (bb == null || tb2 == null) return;
            var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.28))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior   = FillBehavior.Stop
            };
            bb.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            tb2.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        });
    }

    private void AnimateOverflowTabIn(TabViewModel tab)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (ListOverflowTabs.ItemContainerGenerator.ContainerFromItem(tab) is not ListBoxItem lbi) return;
            var tb = lbi.Template?.FindName("TabBorder", lbi) as Border;
            if (tb == null) return;
            tb.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.28))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            });
        });
    }

    private double GetOverflowTabBarAvailableWidth()
    {
        double w = ListOverflowTabs.ActualWidth;
        if (w < 100) w = ActualWidth - 16; // pre-layout fallback
        return Math.Max(TAB_MIN_WIDTH * 2, w);
    }

    private bool _overflowExpanded = false;
    private bool _overflowBarHidden = false;

    private void BtnExpandOverflow_Click(object sender, RoutedEventArgs e)
    {
        if (_overflowBarHidden)
        {
            _overflowBarHidden = false;
            ListOverflowTabs.Visibility = Visibility.Visible;
            UpdateExpandButtonLayout();
            return;
        }
        _overflowExpanded = !_overflowExpanded;
        ListOverflowTabs.Height = _overflowExpanded ? double.NaN : 50;
        BtnExpandOverflow.Content = _overflowExpanded ? "▲" : "▼";
    }

    private void BtnExpandOverflow_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_overflowBarHidden)
        {
            _overflowBarHidden = false;
            ListOverflowTabs.Visibility = Visibility.Visible;
        }
        else
        {
            _overflowBarHidden = true;
            _overflowExpanded = false;
            ListOverflowTabs.Height = 50;
            ListOverflowTabs.Visibility = Visibility.Collapsed;
            BtnExpandOverflow.Content = "▼";
        }
        UpdateExpandButtonLayout();
    }

    private void UpdateExpandButtonLayout()
    {
        if (_overflowBarHidden)
        {
            Grid.SetRow(BtnExpandOverflow, 0);
            BtnExpandOverflow.VerticalAlignment = VerticalAlignment.Bottom;
            BtnExpandOverflow.Height = 20;
        }
        else
        {
            Grid.SetRow(BtnExpandOverflow, 1);
            BtnExpandOverflow.VerticalAlignment = VerticalAlignment.Top;
            BtnExpandOverflow.Height = 50;
        }
    }

    private void CreateNewTab(string initialUrl = "")
{
    string targetUrl = !string.IsNullOrEmpty(initialUrl) ? initialUrl : SettingsService.Current.HomePage;
    if (string.IsNullOrEmpty(targetUrl)) targetUrl = "https://alohafind.com";

    var newTab = new TabViewModel { Title = "New Tab", MediaTitle = "", Url = targetUrl };
    var newBrowser = new Controls.BrowserView();

    newBrowser.NewTabRequested += (s, url) => CreateNewTab(url);

    // ── Download progress indicator ─────────────────────────────────────────
    newBrowser.DownloadProgressChanged += (s, info) =>
    {
        Dispatcher.Invoke(() =>
        {
            if (info.IsComplete)
            {
                if (!newTab.IsLoading) { StopLoadingAnimation(newTab); newTab.LoadingProgress = 0; }
                newTab.IsActiveDownload      = false;
                newTab.HasEverDownloaded     = false;
                newTab.DownloadProgressValue = 0;
                newTab.DownloadSpeedMBs      = 0;
                newTab.DownloadEtaSecs       = 0;
                if (_activeDownloadBrowser == newBrowser) _activeDownloadBrowser = null;
                ScheduleReflow();
            }
            else
            {
                if (!newTab.IsLoading)
                {
                    newTab.LoadingProgress = info.Progress;
                    if (newTab.LoadingOpacity < 0.4) newTab.LoadingOpacity = 0.55;
                }
                newTab.IsActiveDownload      = true;
                newTab.HasEverDownloaded     = true;
                newTab.DownloadProgressValue = info.Progress;
                newTab.DownloadSpeedMBs      = info.SpeedMBs;
                newTab.DownloadEtaSecs       = info.EtaSecs;
                _activeDownloadBrowser       = newBrowser;
                if (newBrowser.IsDownloadPaused) newBrowser.ToggleDownloadPause(); // safety: never inherit stale pause state
                if (!string.IsNullOrEmpty(info.FilePath))
                    newTab.DownloadFileName = System.IO.Path.GetFileName(info.FilePath);
            }
            RightSidebar.NotifyDownloadProgress(info);
        });
    };

    bool _audioHooked = false;

    newBrowser.MainWebView.NavigationStarting += (s, e) =>
    {
        newTab.IsLoading = true;
        newTab.IsPlayingAudio        = false;
        newTab.MediaTitle            = "";
        newTab.IsActiveDownload      = false;
        newTab.HasEverDownloaded     = false;
        newTab.DownloadFileName      = "";
        newTab.DownloadProgressValue = 0;
        newTab.DownloadSpeedMBs      = 0;
        newTab.DownloadEtaSecs       = 0;
        _mediaTabOriginalIndices.Remove(newTab);
        Dispatcher.Invoke(() => {
                _tabImageUrls.Remove(newTab);
                _tabThumbnails.Remove(newTab);
                if (GetCurrentTabViewModel() == newTab) HideImageOverlay();
                StartLoadingAnimation(newTab);
                ScheduleReflow();
            });
    };

    newBrowser.MainWebView.NavigationCompleted += async (s, e) =>
{
    try
    {
        if (!_tabViews.ContainsKey(newTab)) return;
        if (newBrowser.MainWebView == null || newBrowser.MainWebView.CoreWebView2 == null) return;

        string title = newBrowser.MainWebView.CoreWebView2?.DocumentTitle ?? "New Tab";
        string url = newBrowser.MainWebView.Source?.ToString() ?? string.Empty;

        if (newTab.IsSleeping) { newTab.IsLoading = false; return; }

        if (!newTab.HasEverPlayedAudio && !newTab.HasCustomTitle)
        {
            newTab.Title = title;
        }
        
        newTab.Url = url;

        if (newTab.HasEverPlayedAudio && _mediaOriginHost.TryGetValue(newTab, out var mediaOrigin))
        {
            try
            {
                string newHost = new Uri(url).Host;
                if (!string.IsNullOrEmpty(newHost) && newHost != mediaOrigin)
                {
                    if (_mediaDeactivationTimers.TryGetValue(newTab, out var existing))
                    { existing.Stop(); _mediaDeactivationTimers.Remove(newTab); }

                    var deactivateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    deactivateTimer.Tick += (_, _) =>
                    {
                        deactivateTimer.Stop();
                        _mediaDeactivationTimers.Remove(newTab);
                        _mediaOriginHost.Remove(newTab);
                        newTab.HasEverPlayedAudio = false;
                        newTab.MediaTitle = "";
                        StopColorAnimation(newTab, fadeOut: true);
                        ScheduleReflow();
                    };
                    _mediaDeactivationTimers[newTab] = deactivateTimer;
                    deactivateTimer.Start();
                }
                else if (newHost == mediaOrigin)
                {
                    if (_mediaDeactivationTimers.TryGetValue(newTab, out var cancel))
                    { cancel.Stop(); _mediaDeactivationTimers.Remove(newTab); }
                }
            }
            catch { }
        }

        TabSleepRulesService.ApplyIfExists(newTab); // reapply saved domain rule after each nav
        newTab.IsLoading = false;
        Dispatcher.Invoke(() => StopLoadingAnimation(newTab));

        if (CurrentBrowser == newBrowser)
        {
            OmniboxControl.SetText(url);
            MobileOmniboxControl.SetText(url);
            UpdateInstallButtonVisibility(url);
        }
        if (e.IsSuccess) HistoryService.Add(title, url);

        // Reapply per-tab volume setting after each navigation
        if (newTab.Volume < 1.0 && newBrowser.MainWebView.CoreWebView2 != null)
        {
            try
            {
                string volStr = newTab.Volume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                await newBrowser.MainWebView.CoreWebView2.ExecuteScriptAsync(
                    $"(() => {{ const v = document.querySelector('video, audio'); if(v) v.volume = {volStr}; }})()");
            }
            catch { }
        }

        // Image-only page detection - runs on every navigation
        try
        {
            if (newBrowser.MainWebView.CoreWebView2 != null)
            {
                string js = @"(() => {
                    if (document.contentType && document.contentType.startsWith('image/')) return document.location.href;
                    if (document.body && document.body.children.length === 1 && document.body.children[0].tagName === 'IMG') return document.body.children[0].src;
                    let allText = document.body ? document.body.innerText.trim() : '';
                    let imgs = document.querySelectorAll('img');
                    if (imgs.length === 1 && allText.length === 0) return imgs[0].src;
                    return null;
                })()";
                string res = await newBrowser.MainWebView.CoreWebView2.ExecuteScriptAsync(js);
                if (!string.IsNullOrEmpty(res) && res != "null")
                {
                    string imageUrl = System.Text.Json.JsonSerializer.Deserialize<string>(res) ?? "";
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        Dispatcher.Invoke(() => {
                            _tabImageUrls[newTab] = imageUrl;
                            if (GetCurrentTabViewModel() == newTab)
                                ShowImageOverlay(imageUrl);
                        });
                    }
                }
            }
        }
        catch { }

        if (!_audioHooked && newBrowser.MainWebView.CoreWebView2 != null)
        {
            _audioHooked = true;
            if (!_cookiesLoadedOnce && newBrowser.MainWebView.CoreWebView2.CookieManager != null)
            {
                _cookiesLoadedOnce = true;
                _ = LoadPersistedCookiesAsync(newBrowser.MainWebView.CoreWebView2.CookieManager);
            }

            newBrowser.MainWebView.CoreWebView2.ProcessFailed += (sender2, args2) =>
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        LogService.Write("CRASH", $"WebView2 process failed [{newTab.Title}]: Kind={args2.ProcessFailedKind} | Reason={args2.Reason} | Exit={args2.ExitCode} | Process='{args2.ProcessDescription}'");
                        foreach (var fi in args2.FrameInfosForFailedProcess ?? Array.Empty<CoreWebView2FrameInfo>())
                            LogService.Write("CRASH", $"  FrameFailed: name='{fi.Name}' src='{fi.Source}'");

                        string lastUrl = newTab.Url ?? string.Empty;
                        if (string.IsNullOrEmpty(lastUrl) || lastUrl == "about:blank")
                            lastUrl = SettingsService.Current.HomePage;

                        newTab.Title    = "⚠ Crashed";
                        newTab.IsLoading = false;

                        if (args2.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                        {
                            _tabCrashCounts.TryGetValue(newTab, out int crashCount);
                            _tabCrashCounts[newTab] = ++crashCount;

                            if (crashCount > 2)
                            {
                                newTab.Title = "⚠ Crash Loop";
                                LogService.Write("CRASH", $"Circuit breaker: tab '{lastUrl}' crashed {crashCount}x, halting auto-recovery.");
                            }
                            else
                            {
                                _ = Dispatcher.InvokeAsync(async () =>
                                {
                                    try
                                    {
                                        await Task.Delay(400);
                                        await newBrowser.MainWebView.EnsureCoreWebView2Async(StealthEnvironment.Instance);
                                        newBrowser.MainWebView.Source = new Uri(lastUrl);
                                    }
                                    catch (Exception reinitEx)
                                    {
                                        LogService.Write("CRASH", $"WebView2 reinit failed: {reinitEx.Message}");
                                    }
                                });
                            }
                        }
                        else
                        {
                            try { newBrowser.MainWebView.Reload(); }
                            catch { newBrowser.MainWebView.Source = new Uri(lastUrl); }
                        }
                    }
                    catch (Exception recEx)
                    {
                        LogService.RecordCrash(recEx, "ProcessFailed recovery");
                    }
                });
            };

            newBrowser.MainWebView.CoreWebView2.IsDocumentPlayingAudioChanged += (sender2, args2) =>
                Dispatcher.Invoke(() => HandleMediaStateChanged(newTab, newBrowser));

            newBrowser.MainWebView.CoreWebView2.PermissionRequested += (sender2, args2) =>
            {
                if (args2.PermissionKind == CoreWebView2PermissionKind.Geolocation ||
                    args2.PermissionKind == CoreWebView2PermissionKind.Camera      ||
                    args2.PermissionKind == CoreWebView2PermissionKind.Microphone)
                {
                    args2.State = CoreWebView2PermissionState.Allow;
                }
                // Notifications: left to WebView2's native permission flow, same as the main tab handler.
            };

            newBrowser.MainWebView.CoreWebView2.DocumentTitleChanged += async (sender2, args2) =>
            {
                try 
                {
                    if (newTab.IsSleeping) return;
                    if (newTab.HasCustomTitle) return; 

                    if (newTab.IsPlayingAudio || newTab.HasEverPlayedAudio)
                    {
                        try
                        {
                            string script = @"(() => {
                                const m = document.querySelector('video, audio');
                                if (m && m.title) return m.title;
                                const yt = document.querySelector('h1.style-scope.ytd-watch-metadata');
                                if (yt) return yt.innerText;
                                const og = document.querySelector('meta[property=""og:title""]');
                                if (og) return og.content;
                                return null;
                            })()";
                            string result = await newBrowser.MainWebView.CoreWebView2.ExecuteScriptAsync(script);
                            if (!string.IsNullOrEmpty(result) && result != "null")
                            {
                                var parsed = System.Text.Json.JsonSerializer.Deserialize<string>(result);
                                if (!string.IsNullOrEmpty(parsed))
                                    Dispatcher.Invoke(() => { newTab.MediaTitle = parsed; newTab.Title = parsed; });
                            }
                        }
                        catch { }

                        _ = RefreshMediaTabPaletteAsync(newTab, newBrowser);
                    }
                    else
                    {
                        string pageTitle = newBrowser.MainWebView.CoreWebView2?.DocumentTitle ?? string.Empty;
                        if (!string.IsNullOrEmpty(pageTitle))
                            Dispatcher.Invoke(() => newTab.Title = pageTitle);
                    }
                }
                catch (ObjectDisposedException) { }
            };
        }

        try
        {
            if (newBrowser.MainWebView.CoreWebView2 == null) return;
            await newBrowser.MainWebView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
    if (window._ytAdBoost) return;
    window._ytAdBoost = true;
    setInterval(() => {
        if (window.location.hostname.includes('youtube.com')) {
            const ad = document.querySelector('.ad-showing video') || document.querySelector('.html5-video-player.ad-showing video');
            if (ad && ad.playbackRate !== 16.0) ad.playbackRate = 16.0;
        }
    }, 300);
})();
");

            string paletteJson = await newBrowser.MainWebView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
const colors = [];
const add = (v) => {
    if (!v) return;
    v = v.trim();
    if (v === 'transparent' || v === 'rgba(0, 0, 0, 0)') return;
    if (!v.startsWith('#') && !v.startsWith('rgb')) return;
    if (!colors.includes(v)) colors.push(v);
};

add(document.querySelector('meta[name=""theme-color""]')?.content);
add(document.querySelector('meta[name=""msapplication-TileColor""]')?.content);

const rs = getComputedStyle(document.documentElement);
for (const v of ['--primary-color','--brand-color','--color-primary','--accent-color',
                 '--main-color','--primary','--brand','--theme-color','--color-accent',
                 '--color-brand','--ui-primary','--color-link','--color-highlight']) {
    add(rs.getPropertyValue(v));
    if (colors.length >= 4) break;
}

if (colors.length < 3) {
    for (const sel of ['header','nav','[role=""banner""]','#header','#navbar',
                       '.navbar','.header','.app-header','.site-header','.top-bar']) {
        const el = document.querySelector(sel);
        if (!el) continue;
        const bg = getComputedStyle(el).backgroundColor;
        add(bg);
        if (colors.length >= 4) break;
    }
}

return colors.length > 0 ? colors : null;
})()
");
            UpdateTabPalette(newTab, paletteJson);
            
        }
        catch { }

        if (e.IsSuccess && !newTab.IsSleeping)
            _ = BackgroundCaptureAfterNavAsync(newTab);
    }
    catch (ObjectDisposedException)
    {
        // Safe exit: The tab was closed by the user while an async operation was yielding.
        LogService.Write("UI", $"NavigationCompleted silently aborted for '{newTab.Title}' - tab was disposed.");
    }
    catch (Exception ex)
    {
        LogService.RecordCrash(ex, "NavigationCompleted");
    }
};

    newBrowser.MainWebView.WebMessageReceived += (s, e) =>
    {
        try
        {
            string json = e.WebMessageAsJson ?? "";
            if (json.Contains("fullscreen"))
            {
                bool isFullscreen = json.Contains("true");
                if (isFullscreen != _isFullscreen) ToggleFullscreen();
            }
        }
        catch { }
    };

    _allTabs.Add(newTab);
    _tabViews[newTab] = newBrowser;
    if (_isNarrowMode) RefreshCompactBadge();

    newBrowser.Visibility = Visibility.Collapsed;
    ViewContainer.Children.Add(newBrowser);

    // FIX: Defer reflow by one layout cycle so WPF has committed the new item's
    // width before we compute how many tabs fit. Without this, the last tab gets
    // sized based on pre-layout ActualWidth and overflows by one tab width.
    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
    {
        ReflowTabs();

        if (Tabs.Contains(newTab)) ListTabs.SelectedItem = newTab;
        else if (OverflowTabs.Contains(newTab)) ListOverflowTabs.SelectedItem = newTab;

        AnimateTabIn(newTab);
    });

    newBrowser.Navigate(targetUrl);
}

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isReflowing) return;

        TabViewModel? selectedTab = null;

        if (sender == ListTabs) selectedTab = ListTabs.SelectedItem as TabViewModel;
        else if (sender == ListOverflowTabs) selectedTab = ListOverflowTabs.SelectedItem as TabViewModel;

        if (selectedTab != null && _tabViews.ContainsKey(selectedTab))
        {
            if (_activeTabView != null) _activeTabView.Visibility = Visibility.Collapsed;
            var activeView = _tabViews[selectedTab];
            activeView.Visibility = Visibility.Visible;
            _activeTabView = activeView;

            // Wake sleeping tab + reset its activity clock
            _mruTabs.Remove(selectedTab);
            _mruTabs.Insert(0, selectedTab);
            _tabLastActive[selectedTab] = DateTime.UtcNow;
            _tabClickCount[selectedTab] = _tabClickCount.TryGetValue(selectedTab, out int cc) ? cc + 1 : 1;
            WakeTab(selectedTab);

            if (activeView.MainWebView?.Source != null)
            {
                string tabUrl = activeView.MainWebView.Source.ToString();
                OmniboxControl.SetText(tabUrl);
                MobileOmniboxControl.SetText(tabUrl);
                UpdateInstallButtonVisibility(tabUrl);
            }
        }

        _isReflowing = true;
        if (sender == ListTabs && ListTabs.SelectedItem != null) ListOverflowTabs.SelectedItem = null;
        if (sender == ListOverflowTabs && ListOverflowTabs.SelectedItem != null) ListTabs.SelectedItem = null;
        _isReflowing = false;

        if (selectedTab != null)
        {
            if (_tabImageUrls.TryGetValue(selectedTab, out string? imgUrl))
            {
                ShowImageOverlay(imgUrl);
            }
            else
            {
                HideImageOverlay();
            }
        }
    }

    private void CloseTab(TabViewModel tabToClose)
    {
        if (_closingTabs.Contains(tabToClose)) return;

        if (ListTabs.ItemContainerGenerator.ContainerFromItem(tabToClose) is ListBoxItem lbi)
        {
            var bb  = lbi.Template?.FindName("BaseBorder", lbi) as Border;
            var tb2 = lbi.Template?.FindName("TabBorder",  lbi) as Border;
            if (bb != null)
            {
                _closingTabs.Add(tabToClose);
                var anim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15))
                    { FillBehavior = FillBehavior.HoldEnd };
                anim.Completed += (_, _) => { _closingTabs.Remove(tabToClose); DoCloseTab(tabToClose); };
                bb.BeginAnimation(UIElement.OpacityProperty, anim);
                tb2?.BeginAnimation(UIElement.OpacityProperty, anim);
                return;
            }
        }

        if (ListOverflowTabs.ItemContainerGenerator.ContainerFromItem(tabToClose) is ListBoxItem olbi)
        {
            var tb = olbi.Template?.FindName("TabBorder", olbi) as Border;
            if (tb != null)
            {
                _closingTabs.Add(tabToClose);
                var anim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15))
                    { FillBehavior = FillBehavior.HoldEnd };
                anim.Completed += (_, _) => { _closingTabs.Remove(tabToClose); DoCloseTab(tabToClose); };
                tb.BeginAnimation(UIElement.OpacityProperty, anim);
                return;
            }
        }

        DoCloseTab(tabToClose);
    }

    private void DoCloseTab(TabViewModel tabToClose)
    {
        // Track for Reopen Last Closed Tab (Ctrl+Shift+T / header context menu)
        string _ctUrl = tabToClose.Url ?? "";
        if (!string.IsNullOrEmpty(_ctUrl) && !_ctUrl.StartsWith("about:"))
        {
            _closedTabHistory.Add((_ctUrl, tabToClose.Title ?? "Closed Tab"));
            if (_closedTabHistory.Count > MaxClosedTabHistory)
                _closedTabHistory.RemoveAt(0);
        }

        _tabImageUrls.Remove(tabToClose);

        if (_multiSelectedTabs.Remove(tabToClose))
            tabToClose.IsMultiSelected = false;

        int  indexToRemove = _allTabs.IndexOf(tabToClose);
        bool wasSelected   = ListTabs.SelectedItem == tabToClose
                          || ListOverflowTabs.SelectedItem == tabToClose;

        _allTabs.Remove(tabToClose);
        _mruTabs.Remove(tabToClose);
        if (_isNarrowMode) RefreshCompactBadge();

        if (_tabViews.TryGetValue(tabToClose, out var view))
        {
            ViewContainer.Children.Remove(view);
            try { view.MainWebView?.Dispose(); } catch { }
            _tabViews.Remove(tabToClose);
        }

        if (_colorAnimTimers.TryGetValue(tabToClose, out var animTimer))
        { animTimer.Stop(); _colorAnimTimers.Remove(tabToClose); }
        _colorAnimT.Remove(tabToClose);
        _colorAnimPhase.Remove(tabToClose);
        _colorAnimFade.Remove(tabToClose);
        _vizTime.Remove(tabToClose);
        _vizSmoothedAmps.Remove(tabToClose);
       _tabLastActive.Remove(tabToClose);
        _tabClickCount.Remove(tabToClose);
        _tabSleepUrls.Remove(tabToClose);
        _tabThumbnails.Remove(tabToClose);
        _mediaTabOriginalIndices.Remove(tabToClose);
        if (_loadingTimers.TryGetValue(tabToClose, out var ldTimer))
        { ldTimer.Stop(); _loadingTimers.Remove(tabToClose); }
        if (_volumeDebounceTimers.TryGetValue(tabToClose, out var vdTimer))
        { vdTimer.Stop(); _volumeDebounceTimers.Remove(tabToClose); }
        if (_mediaDeactivationTimers.TryGetValue(tabToClose, out var medDt))
        { medDt.Stop(); _mediaDeactivationTimers.Remove(tabToClose); }
        if (_paletteRefreshTimers.TryGetValue(tabToClose, out var prtClose))
        { prtClose.Stop(); _paletteRefreshTimers.Remove(tabToClose); }
        _mediaOriginHost.Remove(tabToClose);
        _tabCrashCounts.Remove(tabToClose);

        ReflowTabs();

        if (_allTabs.Count == 0)
        {
            CreateNewTab();
        }
        else if (wasSelected)
        {
            int newIndex = Math.Clamp(indexToRemove - 1, 0, _allTabs.Count - 1);
            var newSelected = _allTabs[newIndex];

            if      (Tabs.Contains(newSelected))         ListTabs.SelectedItem         = newSelected;
            else if (OverflowTabs.Contains(newSelected)) ListOverflowTabs.SelectedItem = newSelected;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ResetPrimaryScrollToStart);
    }

    private void BtnTabDlPause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TabViewModel tab
            && _tabViews.TryGetValue(tab, out var browser))
        {
            browser.ToggleDownloadPause();
            if (fe.Parent is StackPanel sp)
                foreach (var child in sp.Children.OfType<Button>())
                    if (child.Tag?.ToString() == "DlPause")
                        child.Content = browser.IsDownloadPaused ? "▶" : "⏸";
        }
        e.Handled = true;
    }

    private void BtnTabDlCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TabViewModel tab
            && _tabViews.TryGetValue(tab, out var browser))
            browser.CancelCurrentDownload();
        e.Handled = true;
    }

    private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TabViewModel tabToClose)
        {
            CloseTab(tabToClose);
        }
    }

    private void BtnNewTab_Click(object sender, RoutedEventArgs e) => CreateNewTab();

    private void ApplyLayoutState()
    {
        if (_isWebAppMode)
        {
            HeaderContainer.Height = 0; RowHeader.Height = new GridLength(0);
            SidebarContainer.Width = 0; ColSidebar.Width = new GridLength(0);
            SensorTop.Visibility = Visibility.Collapsed;
            SensorPillLeft.Visibility = Visibility.Collapsed;
            SensorRight.Visibility = Visibility.Collapsed;
            // Re-enable sensor top as a thin invisible strip for bar reveal
            SensorTop.Visibility = Visibility.Visible;
            SensorPillLeft.Visibility = Visibility.Visible;
            WebAppBar.Visibility = Visibility.Visible;
            return;
        }

        WebAppBar.Visibility = Visibility.Collapsed;

        if (SettingsService.Current.AutoHideHeader) { HeaderContainer.Height = 0; RowHeader.Height = GridLength.Auto; }
        else { HeaderContainer.Height = 60; RowHeader.Height = GridLength.Auto; }

        if (SettingsService.Current.AutoHideSidebar)
        {
            // Auto-hide: sidebar starts fully collapsed. It MUST stay in Column 1 (not Column 0).
            // Reason: WebView2 is a Win32 HWND and always paints above every WPF visual
            // regardless of Panel.ZIndex — the column-0 overlay approach is invisible because
            // the HWND covers it. When the sidebar opens, ColSidebar expands to Auto and the
            // * column (ViewContainer) gives way — which is the only approach that works.
            SidebarContainer.Width = 0;
            ColSidebar.Width = new GridLength(0);
            Grid.SetColumn(SidebarContainer, 1);
            Grid.SetColumnSpan(SidebarContainer, 1);
            SidebarContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
            Panel.SetZIndex(SidebarContainer, 0);
        }
        else
        {
            SidebarContainer.Width = 320;
            ColSidebar.Width = GridLength.Auto;
            Grid.SetColumn(SidebarContainer, 1);
            Grid.SetColumnSpan(SidebarContainer, 1);
            SidebarContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
            Panel.SetZIndex(SidebarContainer, 0);
        }
    }

    // ── WebApp bar helpers ────────────────────────────────────────────────────
    private readonly Dictionary<string, List<(string Url, string Title)>> _navHistories = new();
    private readonly Dictionary<string, int> _navHistoryIndex = new();
    private void ShowWebAppBar()
    {
        _webAppBarTimer.Stop();
        WebAppBar.IsHitTestVisible = true;
        FadeWebAppBar(true);
        _webAppBarTimer.Start();
    }

    private void FadeWebAppBar(bool fadeIn)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            To       = fadeIn ? 1.0 : 0.0,
            Duration = TimeSpan.FromMilliseconds(fadeIn ? 150 : 400),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd,
        };
        if (!fadeIn)
            anim.Completed += (s, e) => WebAppBar.IsHitTestVisible = false;
        WebAppBar.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void BtnWebAppMin_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void BtnWebAppMax_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnWebAppClose_Click(object sender, RoutedEventArgs e)
        => Close();


    private void ToggleHeader(bool isOpen)
    {
        _headerTimer.Stop();
        var storyboardName = isOpen ? "Anim_HeaderOpen" : "Anim_HeaderClose";
        if (isOpen) RowHeader.Height = GridLength.Auto;
        var sb = this.FindResource(storyboardName) as System.Windows.Media.Animation.Storyboard;
        sb?.Begin();

        if (isOpen)
        {
            HeaderContainer.MouseLeave -= HeaderContainer_MouseLeave;
            HeaderContainer.MouseLeave += HeaderContainer_MouseLeave;
        }
        else
        {
            HeaderContainer.MouseLeave -= HeaderContainer_MouseLeave;
        }
    }

    private void HeaderContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        bool shouldHide = SettingsService.Current.AutoHideHeader || _isFullscreen;
        if (shouldHide && !_isHeaderLockedOpen) _headerHideTimer.Start();
    }

    private void ToggleSidebar(bool isOpen)
    {
        var storyboardName = isOpen ? "Anim_SidebarOpen" : "Anim_SidebarClose";
        if (isOpen)
        {
            // Expand the column first so the * column shrinks and makes room.
            ColSidebar.Width = GridLength.Auto;
        }
        var sb = this.FindResource(storyboardName) as System.Windows.Media.Animation.Storyboard;
        if (!isOpen)
        {
            // After the 300ms close animation, pin ColSidebar to explicit 0 so the
            // * column (ViewContainer) reclaims full width without depending on Auto→0.
            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                ColSidebar.Width = new GridLength(0);
            };
            closeTimer.Start();
        }
        sb?.Begin();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentBrowser?.MainWebView?.CanGoBack == true) CurrentBrowser.MainWebView.GoBack();
    }

    private void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentBrowser?.MainWebView?.CanGoForward == true) CurrentBrowser.MainWebView.GoForward();
    }

    private void BtnBack_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowNavHistoryMenu(sender as UIElement, isBack: true);
    }

    private void BtnForward_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowNavHistoryMenu(sender as UIElement, isBack: false);
    }

    

    private void ShowNavHistoryMenu(UIElement? anchor, bool isBack)
    {
        // CoreWebView2 does not expose a BackForwardList API.
        // Right-click nav history requires manual history tracking — not yet implemented.
    }

    private async void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        var browser = CurrentBrowser;
        if (browser == null) return;
        try
        {
            CoreWebView2? core = null;
        try { core = browser.MainWebView?.CoreWebView2; } catch { }

        if (core != null)
        {
            browser.MainWebView!.Reload();
        }
        else
        {
            string url = GetCurrentTabViewModel()?.Url ?? string.Empty;
            if (string.IsNullOrEmpty(url) || url == "about:blank")
                url = SettingsService.Current.HomePage;
            await Task.Delay(400);
            await browser.MainWebView!.EnsureCoreWebView2Async(StealthEnvironment.Instance);
            browser.Navigate(url);
        }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "BtnReload recovery");
        }
    }

    private void HeaderContainer_RightClick(object sender, MouseButtonEventArgs e)
    {
        // Only fire on the bare header background — tabs have their own XAML ContextMenu.
        // Walk the visual tree from the click source; if we're inside a ListBoxItem, it's a tab.
        var src = e.OriginalSource as DependencyObject;
        while (src != null)
        {
            if (src is System.Windows.Controls.ListBoxItem) return;
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        }

        e.Handled = true;
        var cm = new ContextMenu { StaysOpen = false };

        // ── Reopen Last Closed Tab ───────────────────────────────────────────
        bool hasHistory  = _closedTabHistory.Count > 0;
        string rawTitle  = hasHistory ? _closedTabHistory[^1].Title : "";
        string shortTitle = rawTitle.Length > 30 ? rawTitle[..30] + "…" : rawTitle;
        var miReopen = new MenuItem
        {
            Header           = hasHistory ? $"↩  Reopen \"{shortTitle}\"" : "↩  Reopen Last Closed Tab",
            IsEnabled        = hasHistory,
            InputGestureText = "Ctrl+Shift+T"
        };
        miReopen.Click += (_, _) =>
        {
            if (_closedTabHistory.Count > 0)
            {
                var (url, _) = _closedTabHistory[^1];
                _closedTabHistory.RemoveAt(_closedTabHistory.Count - 1);
                CreateNewTab(url);
            }
        };
        cm.Items.Add(miReopen);

        cm.Items.Add(new Separator());

        // ── Developer Tools ──────────────────────────────────────────────────
        var miDevTools = new MenuItem { Header = "🔧  Developer Tools", InputGestureText = "F12" };
        miDevTools.Click += (_, _) =>
            CurrentBrowser?.MainWebView?.CoreWebView2?.OpenDevToolsWindow();
        cm.Items.Add(miDevTools);

        // ── Browser Task Manager ─────────────────────────────────────────────
        var miTaskMgr = new MenuItem { Header = "📊  Browser Task Manager", InputGestureText = "Shift+Esc" };
        miTaskMgr.Click += (_, _) =>
            CurrentBrowser?.MainWebView?.CoreWebView2?.OpenTaskManagerWindow();
        cm.Items.Add(miTaskMgr);

        // ── Downloads Manager ────────────────────────────────────────────────
        var miDownloads = new MenuItem { Header = "⬇  Downloads Manager" };
        miDownloads.Click += (_, _) =>
            CurrentBrowser?.MainWebView?.CoreWebView2?.OpenDefaultDownloadDialog();
        cm.Items.Add(miDownloads);

        // ── View Page Source ─────────────────────────────────────────────────
        var miSource = new MenuItem { Header = "📄  View Page Source" };
        miSource.Click += (_, _) =>
        {
            string srcUrl = CurrentBrowser?.MainWebView?.Source?.ToString() ?? "";
            if (!string.IsNullOrEmpty(srcUrl) && !srcUrl.StartsWith("view-source:"))
                CreateNewTab("view-source:" + srcUrl);
        };
        cm.Items.Add(miSource);

        // ── Share Page ───────────────────────────────────────────────────────
        var miShare = new MenuItem { Header = "📤  Share Page" };
        miShare.Click += (_, _) => Dispatcher.InvokeAsync(
            () => BtnShare_Click(miShare, new RoutedEventArgs()),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        cm.Items.Add(miShare);

        cm.Items.Add(new Separator());

        // ── Zoom sub-menu ────────────────────────────────────────────────────
        double curZoom   = CurrentBrowser?.MainWebView?.ZoomFactor ?? 1.0;
        var miZoomParent = new MenuItem { Header = $"🔍  Zoom  ({(int)(curZoom * 100)}%)" };

        var miZoomIn = new MenuItem { Header = "Zoom In  (+10%)", InputGestureText = "Ctrl++" };
        miZoomIn.Click += (_, _) =>
        {
            if (CurrentBrowser?.MainWebView is { } wv)
                wv.ZoomFactor = Math.Min(5.0, wv.ZoomFactor + 0.10);
        };

        var miZoomOut = new MenuItem { Header = "Zoom Out  (−10%)", InputGestureText = "Ctrl+−" };
        miZoomOut.Click += (_, _) =>
        {
            if (CurrentBrowser?.MainWebView is { } wv)
                wv.ZoomFactor = Math.Max(0.25, wv.ZoomFactor - 0.10);
        };

        var miZoomReset = new MenuItem { Header = "Reset Zoom  (100%)", InputGestureText = "Ctrl+0" };
        miZoomReset.Click += (_, _) =>
        {
            if (CurrentBrowser?.MainWebView is { } wv)
                wv.ZoomFactor = 1.0;
        };

        miZoomParent.Items.Add(miZoomIn);
        miZoomParent.Items.Add(miZoomOut);
        miZoomParent.Items.Add(new Separator());
        miZoomParent.Items.Add(miZoomReset);
        cm.Items.Add(miZoomParent);

        cm.Placement       = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        cm.PlacementTarget = sender as UIElement;
        cm.IsOpen          = true;
    }

    private void BtnShare_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetForegroundWindow(hwnd);   // share sheet requires the window to be foreground
            string typeName = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            Guid iid = new Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8");
            IntPtr factoryPtr;
            int hr = RoGetActivationFactory(typeName, ref iid, out factoryPtr);

            if (hr < 0 || factoryPtr == IntPtr.Zero) return;

            var interop = Marshal.GetObjectForIUnknown(factoryPtr) as IDataTransferManagerInterop;
            Marshal.Release(factoryPtr);

            if (interop != null)
            {
                IntPtr dtmPtr = interop.GetForWindow(hwnd, ref _dtmIid);
                var dataTransferManager = Marshal.GetObjectForIUnknown(dtmPtr) as DataTransferManager;

                if (dataTransferManager != null)
                {
                    dataTransferManager.DataRequested -= OnShareDataRequested;
                    dataTransferManager.DataRequested += OnShareDataRequested;
                    interop.ShowShareUIForWindow(hwnd);
                }
            }
        }
        catch (Exception ex) { LogService.RecordCrash(ex, "Share Interface"); }
    }

    private void OnShareDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        if (CurrentBrowser?.MainWebView == null) return;

        var request = args.Request;
        var webView = CurrentBrowser.MainWebView;

        request.Data.Properties.Title = webView.CoreWebView2?.DocumentTitle ?? "Horizon Browser";
        request.Data.Properties.Description = "Shared via Horizon Stealth";

        string url = webView.Source?.ToString() ?? "";
        if (!string.IsNullOrEmpty(url))
        {
            request.Data.SetWebLink(new Uri(url));
            request.Data.SetText($"Check this out: {url}");
        }
    }

    private Views.SettingsWindow? _openSettingsWindow;

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_openSettingsWindow != null)
        {
            if (_openSettingsWindow.WindowState == WindowState.Minimized)
                _openSettingsWindow.WindowState = WindowState.Normal;

            _openSettingsWindow.Activate();
            return;
        }

        var settingsWin = new Views.SettingsWindow();
        settingsWin.SettingsApplied += () =>
        {
            ApplyLayoutState();
            ThemeService.ApplyTheme(SettingsService.Current.Theme);
        };
        settingsWin.Closed += (_, _) => _openSettingsWindow = null;
        _openSettingsWindow = settingsWin;
        settingsWin.Show();
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarContainer.Width > 0) { _isSidebarLocked = false; ToggleSidebar(false); }
        else { _isSidebarLocked = true; ToggleSidebar(true); }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseOpenSettingsWindowIfAny() => _openSettingsWindow?.Close();
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsService.Current.BackgroundKeepAliveEnabled)
            this.Close(); // triggers Closing → intercepted by BackgroundKeepAliveService
        else
            Application.Current.Shutdown();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool alt   = (Keyboard.Modifiers & ModifierKeys.Alt)     == ModifierKeys.Alt;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   == ModifierKeys.Shift;

        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.T)
        {
            CreateNewTab();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.W)
        {
            // If multiple tabs are selected, close them all at once
            if (_multiSelectedTabs.Count > 1)
            {
                var toClose = _multiSelectedTabs.ToList();
                foreach (var t in toClose) CloseTab(t);
            }
            else
            {
                var current = GetCurrentTabViewModel();
                if (current != null) CloseTab(current);
            }
            e.Handled = true;
            return;
        }

        if ((ctrl && e.Key == Key.R) || e.Key == Key.F5)
        {
            CurrentBrowser?.MainWebView?.Reload();
            e.Handled = true;
            return;
        }

        if (alt && e.Key == Key.Left)
        {
            if (CurrentBrowser?.MainWebView?.CanGoBack == true) CurrentBrowser.MainWebView.GoBack();
            e.Handled = true;
            return;
        }

        if (alt && e.Key == Key.Right)
        {
            if (CurrentBrowser?.MainWebView?.CanGoForward == true) CurrentBrowser.MainWebView.GoForward();
            e.Handled = true;
            return;
        }

        // ── Tab Switcher: navigation delegated to TabSwitcherWindow ──────────
        if (_tabSwitcherWindow != null)
        {
            if (e.Key == Key.Escape)
            { CancelTabSwitcher(); e.Handled = true; return; }
            if (e.Key == Key.Return || e.Key == Key.Space)
            { CommitTabSwitcher(); e.Handled = true; return; }
            if (e.Key == Key.Right || e.Key == Key.Down)
            { StepTabSwitcher(+1); e.Handled = true; return; }
            if (e.Key == Key.Left  || e.Key == Key.Up)
            { StepTabSwitcher(-1); e.Handled = true; return; }
        }

        // ── Ctrl+Tab: open/step the switcher ─────────────────────────────────
        if (ctrl && e.Key == Key.Tab)
        {
            if (_tabSwitcherWindow == null) OpenTabSwitcher();
            StepTabSwitcher(shift ? -1 : +1);
            e.Handled = true;
            return;
        }

        // ── Ctrl+1-9: jump directly to tab by position ───────────────────────
        if (ctrl && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            int idx = e.Key - Key.D1; // 0-based
            if (idx < _allTabs.Count)
            {
                var t = _allTabs[idx];
                if (Tabs.Contains(t))              ListTabs.SelectedItem         = t;
                else if (OverflowTabs.Contains(t)) ListOverflowTabs.SelectedItem = t;
            }
            e.Handled = true;
            return;
        }

        }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Fast path — fires when WPF owns focus. Poller in TabSwitcherWindow handles the fallback.
        if (_tabSwitcherWindow != null && !IsCtrlPhysicallyDown())
        {
            CommitTabSwitcher();
            e.Handled = true;
        }
    }

    private TabViewModel? GetCurrentTabViewModel()
    {
        if (ListTabs.SelectedItem is TabViewModel t1) return t1;
        if (ListOverflowTabs.SelectedItem is TabViewModel t2) return t2;
        return null;
    }

    private void CycleTabs(int direction)
    {
        if (_allTabs.Count <= 1) return;

        var current = GetCurrentTabViewModel();
        if (current == null) return;

        int index = _allTabs.IndexOf(current);
        if (index == -1) return;

        int newIndex = index + direction;
        if (newIndex >= _allTabs.Count) newIndex = 0;
        if (newIndex < 0) newIndex = _allTabs.Count - 1;

        var newTab = _allTabs[newIndex];

        if (Tabs.Contains(newTab)) ListTabs.SelectedItem = newTab;
        else if (OverflowTabs.Contains(newTab)) ListOverflowTabs.SelectedItem = newTab;
    }

    // ════════════════════════════════════════════════════════════════════════
    // CTRL+TAB  TAB SWITCHER
    // ════════════════════════════════════════════════════════════════════════

    private void OpenTabSwitcher()
    {
        if (_allTabs.Count <= 1 || _tabSwitcherWindow != null) return;

        // Build MRU-ordered list: most-recently-used tab first.
        // Any tabs not yet recorded in _mruTabs are appended at the end.
        var mruOrder = _mruTabs.Where(t => _allTabs.Contains(t)).ToList();
        foreach (var t in _allTabs)
            if (!mruOrder.Contains(t)) mruOrder.Add(t);

        // Start at index 1 (the previously used tab), mirroring Windows 11 Alt+Tab:
        // the first Ctrl+Tab press immediately highlights the previous tab,
        // and releasing Ctrl commits to it without a second keypress.
        int startIndex = mruOrder.Count > 1 ? 1 : 0;

        var win = new Views.TabSwitcherWindow(
            tabs:         mruOrder,
            initialIndex: startIndex,
            captureFunc:  CaptureTabSnapshotAsync
        );

        win.TabCommitted += idx =>
        {
            _tabSwitcherWindow = null;
            var target = idx >= 0 && idx < mruOrder.Count ? mruOrder[idx] : null;
            if (target == null) return;
            if (Tabs.Contains(target))              ListTabs.SelectedItem         = target;
            else if (OverflowTabs.Contains(target)) ListOverflowTabs.SelectedItem = target;
        };

        win.Cancelled += () => { _tabSwitcherWindow = null; };
        win.Closed    += (_, _) => { _tabSwitcherWindow = null; };

        SubscribeSwitcherEvents(win);

        _tabSwitcherWindow = win;
        win.Show();

        _ = PreWarmSwitcherThumbnailsAsync(mruOrder, startIndex);
    }

    private async Task PreWarmSwitcherThumbnailsAsync(System.Collections.Generic.List<TabViewModel> tabs, int startIndex)
    {
        var order = new System.Collections.Generic.List<int>();
        for (int d = 0; d < tabs.Count; d++)
        {
            int fwd = (startIndex + d) % tabs.Count;
            int bwd = ((startIndex - d) % tabs.Count + tabs.Count) % tabs.Count;
            if (!order.Contains(fwd)) order.Add(fwd);
            if (!order.Contains(bwd)) order.Add(bwd);
        }
        foreach (int i in order)
        {
            if (_tabSwitcherWindow == null) break;
            if (!_tabThumbnails.ContainsKey(tabs[i]))
                await CaptureTabSnapshotAsync(tabs[i]);
            await Task.Yield();
        }
    }

    private void StepTabSwitcher(int direction)
    {
        _tabSwitcherWindow?.Step(direction);
    }

    private void CommitTabSwitcher()
    {
        _tabSwitcherWindow?.CommitCurrent();
    }

    private void CancelTabSwitcher()
    {
        _tabSwitcherWindow?.CancelSelf();
        _tabSwitcherWindow = null;
    }

    private void Switcher_TabDuplicate(TabViewModel tab)
    {
        CreateNewTab(tab.Url);
        var newTab = _allTabs.LastOrDefault();
        if (newTab != null && newTab != tab)
        {
            int from = _allTabs.IndexOf(newTab);
            int to   = _allTabs.IndexOf(tab) + 1;
            if (from >= 0 && to <= _allTabs.Count && from != to)
            {
                _allTabs.RemoveAt(from);
                _allTabs.Insert(Math.Min(to, _allTabs.Count), newTab);
            }
            ReflowTabs();
        }
    }

    private void Switcher_TabRename(TabViewModel tab)
    {
        string? newTitle = ShowInputDialog("Rename Tab", "Enter new tab name:", tab.Title);
        if (!string.IsNullOrWhiteSpace(newTitle))
        {
            tab.Title = newTitle.Trim();
            tab.HasCustomTitle = true;
        }
    }

    private void Switcher_TabChangePosition(TabViewModel tab)
    {
        int total   = _allTabs.Count;
        int current = _allTabs.IndexOf(tab) + 1;
        string? input = ShowInputDialog("Change Tab Position",
            $"Enter position (1–{total}):\nCurrent position: {current}",
            current.ToString());
        if (input == null || !int.TryParse(input.Trim(), out int pos)) return;
        int targetIdx = Math.Clamp(pos - 1, 0, total - 1);
        int from = _allTabs.IndexOf(tab);
        if (from == targetIdx) return;
        _allTabs.RemoveAt(from);
        _allTabs.Insert(targetIdx, tab);
        ReflowTabs();
    }

    private void Switcher_TabBringToTop(TabViewModel tab)
    {
        int from = _allTabs.IndexOf(tab);
        if (from <= 0) return;
        _allTabs.RemoveAt(from);
        _allTabs.Insert(0, tab);
        ReflowTabs();
    }

    private void Switcher_TabMuteToggle(TabViewModel tab)
    {
        if (!_tabViews.TryGetValue(tab, out var bv)) return;
        var core = bv.MainWebView?.CoreWebView2;
        if (core == null) return;
        core.IsMuted = !core.IsMuted;
        tab.IsMuted  = core.IsMuted;
    }

    private void Switcher_TabSleepToggle(TabViewModel tab)
    {
        if (tab.IsSleeping) WakeTab(tab);
        else if (!tab.IsPlayingAudio && !tab.NeverSleep) SleepTab(tab);
    }

    private void Switcher_MultiDuplicate(IReadOnlyList<TabViewModel> tabs)
    {
        foreach (var tab in tabs)
        {
            CreateNewTab(tab.Url);
            var newTab = _allTabs.LastOrDefault();
            if (newTab != null && newTab != tab)
            {
                int from = _allTabs.IndexOf(newTab);
                int to   = _allTabs.IndexOf(tab) + 1;
                if (from >= 0 && to <= _allTabs.Count && from != to)
                {
                    _allTabs.RemoveAt(from);
                    _allTabs.Insert(Math.Min(to, _allTabs.Count), newTab);
                }
            }
        }
        ReflowTabs();
    }

    private void Switcher_MultiRename(IReadOnlyList<TabViewModel> tabs)
    {
        if (tabs.Count == 0) return;
        string? newTitle = ShowInputDialog("Rename All Selected Tabs",
            $"Enter a name for all {tabs.Count} selected tabs:", tabs[0].Title);
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        foreach (var tab in tabs)
        {
            tab.Title = newTitle.Trim();
            tab.HasCustomTitle = true;
        }
    }

    private void Switcher_MultiBringToTop(IReadOnlyList<TabViewModel> tabs)
    {
        var ordered = tabs.OrderBy(t => _allTabs.IndexOf(t)).ToList();
        foreach (var t in ordered) _allTabs.Remove(t);
        for (int i = ordered.Count - 1; i >= 0; i--)
            _allTabs.Insert(0, ordered[i]);
        ReflowTabs();
    }

    private void Switcher_MultiSendToEnd(IReadOnlyList<TabViewModel> tabs)
    {
        var ordered = tabs.OrderBy(t => _allTabs.IndexOf(t)).ToList();
        foreach (var t in ordered) _allTabs.Remove(t);
        foreach (var t in ordered) _allTabs.Add(t);
        ReflowTabs();
    }

    private void Switcher_MultiGroup(IReadOnlyList<TabViewModel> tabs)
    {
        if (tabs.Count < 2) return;
        string? existingGroup = null;
        if (tabs.All(t => t.Title.StartsWith("[") && t.Title.Contains("] ")))
        {
            var gname = tabs[0].Title[1..tabs[0].Title.IndexOf("] ")].Trim();
            if (tabs.All(t => t.Title.StartsWith($"[{gname}] ")))
                existingGroup = gname;
        }
        string? groupName = ShowInputDialog("Group Tabs",
            $"Enter a group name for {tabs.Count} tabs:", existingGroup ?? "Group");
        if (string.IsNullOrWhiteSpace(groupName)) return;
        var anchor = tabs[0];
        int insertIdx = _allTabs.IndexOf(anchor) + 1;
        for (int i = 1; i < tabs.Count; i++)
        {
            _allTabs.Remove(tabs[i]);
            _allTabs.Insert(Math.Min(insertIdx++, _allTabs.Count), tabs[i]);
        }
        foreach (var tab in tabs)
        {
            string baseTitle = tab.Title;
            if (baseTitle.StartsWith("[") && baseTitle.Contains("] "))
                baseTitle = baseTitle[(baseTitle.IndexOf("] ") + 2)..];
            tab.Title = $"[{groupName.Trim()}] {baseTitle}";
            tab.HasCustomTitle = true;
        }
        ReflowTabs();
    }

    private void Switcher_MultiSleepAll(IReadOnlyList<TabViewModel> tabs)
    {
        foreach (var tab in tabs)
            if (!tab.IsSleeping && !tab.NeverSleep) SleepTab(tab);
    }

    private void Switcher_MultiWakeAll(IReadOnlyList<TabViewModel> tabs)
    {
        foreach (var tab in tabs)
            if (tab.IsSleeping) WakeTab(tab);
    }

    private void SubscribeSwitcherEvents(Views.TabSwitcherWindow win)
    {
        win.TabDuplicateRequested      += Switcher_TabDuplicate;
        win.TabRenameRequested         += Switcher_TabRename;
        win.TabChangePositionRequested += Switcher_TabChangePosition;
        win.TabBringToTopRequested     += Switcher_TabBringToTop;
        win.TabMuteToggleRequested     += Switcher_TabMuteToggle;
        win.TabSleepToggleRequested    += Switcher_TabSleepToggle;
        win.MultiDuplicateRequested    += Switcher_MultiDuplicate;
        win.MultiRenameRequested       += Switcher_MultiRename;
        win.MultiBringToTopRequested   += Switcher_MultiBringToTop;
        win.MultiSendToEndRequested    += Switcher_MultiSendToEnd;
        win.MultiGroupRequested        += Switcher_MultiGroup;
        win.MultiSleepAllRequested     += Switcher_MultiSleepAll;
        win.MultiWakeAllRequested      += Switcher_MultiWakeAll;
    }

    private void ApplyNarrowWindowMode()
    {
        int mode      = SettingsService.Current.NarrowWindowMode;
        int threshold = SettingsService.Current.NarrowWindowThresholdPx;

        if (mode == 0)
        {
            this.MinWidth  = Math.Max(400, threshold);
            if (_isNarrowMode) { _isNarrowMode = false; HeaderContainer.Visibility = Visibility.Visible; MobileHeaderContainer.Visibility = Visibility.Collapsed; ListTabs.Visibility = Visibility.Visible; BtnCompactTabsBadge.Visibility = Visibility.Collapsed; }
            return;
        }

        bool shouldBeNarrow = mode == 2 || (mode == 1 && ActualWidth < threshold);
        if (shouldBeNarrow == _isNarrowMode) return;

        _isNarrowMode                  = shouldBeNarrow;
        HeaderContainer.Visibility     = shouldBeNarrow ? Visibility.Collapsed : Visibility.Visible;
        MobileHeaderContainer.Visibility = shouldBeNarrow ? Visibility.Visible : Visibility.Collapsed;
        // Tab list is inside the normal header; keep it for when we restore
        ListTabs.Visibility            = Visibility.Visible;
        BtnCompactTabsBadge.Visibility = Visibility.Collapsed;

        if (shouldBeNarrow) RefreshMobileTabBadge();
        else                MobileHeaderContainer.Visibility = Visibility.Collapsed;
    }

    private void RefreshCompactBadge()
    {
        int count = _allTabs?.Count ?? 0;
        BtnCompactTabsBadge.Content = $"🗂 {count}";
    }

    private void RefreshMobileTabBadge()
    {
        int count = _allTabs?.Count ?? 0;
        MBtnTabBadge.Content = $"🗂 {count}";
    }

    private void BtnCompactTabsBadge_Click(object sender, RoutedEventArgs e)
    {
        if (_tabSwitcherWindow != null) { _tabSwitcherWindow.Close(); return; }
        OpenTabSwitcherPersistent();
    }

    private void OpenTabSwitcherPersistent()
    {
        if (_tabSwitcherWindow != null) { _tabSwitcherWindow.Activate(); return; }
        if (_allTabs.Count == 0) return;

        var cur = GetCurrentTabViewModel();
        int startIndex = cur != null ? _allTabs.IndexOf(cur) : 0;

        var win = new Views.TabSwitcherWindow(
            tabs:         new System.Collections.Generic.List<TabViewModel>(_allTabs),
            initialIndex: startIndex,
            captureFunc:  CaptureTabSnapshotAsync,
            persistent:   true
        );

        win.TabCommitted += idx =>
        {
            _tabSwitcherWindow = null;
            var target = idx < _allTabs.Count ? _allTabs[idx] : null;
            if (target == null) return;
            if (Tabs.Contains(target))              ListTabs.SelectedItem         = target;
            else if (OverflowTabs.Contains(target)) ListOverflowTabs.SelectedItem = target;
        };

        win.TabCloseRequested += vm => CloseTab(vm);
        win.Cancelled += () => { _tabSwitcherWindow = null; };
        win.Closed    += (_, _) => { _tabSwitcherWindow = null; };

        SubscribeSwitcherEvents(win);

        _tabSwitcherWindow = win;
        win.Show();

        _ = PreWarmSwitcherThumbnailsAsync(new System.Collections.Generic.List<TabViewModel>(_allTabs), startIndex);
    }

    private async Task BackgroundCaptureAfterNavAsync(TabViewModel tab)
    {
        await Task.Delay(2000);
        if (!_tabViews.ContainsKey(tab) || tab.IsSleeping || tab.IsLoading) return;
        _tabThumbnails.Remove(tab);
        var diskPath = GetThumbCachePath(tab);
        if (diskPath != null) { try { File.Delete(diskPath); } catch { } }
        await CaptureTabSnapshotAsync(tab);
    }

    private string? GetThumbCachePath(TabViewModel tab)
    {
        if (string.IsNullOrEmpty(tab.Url) || tab.Url == "about:blank") return null;
        string urlHash = Math.Abs(tab.Url.GetHashCode()).ToString("X8");
        return Path.Combine(Path.GetTempPath(), "HorizonThumb", $"{tab.TabId:N}_{urlHash}.png");
    }

    private async Task<ImageSource?> CaptureTabSnapshotAsync(TabViewModel tab)
    {
        try
        {
            if (_tabThumbnails.TryGetValue(tab, out var cached)) return cached;

            var diskPath = GetThumbCachePath(tab);
            if (diskPath != null && File.Exists(diskPath))
            {
                var diskBmp = new System.Windows.Media.Imaging.BitmapImage();
                diskBmp.BeginInit();
                using var diskFs = File.OpenRead(diskPath);
                diskBmp.StreamSource = diskFs;
                diskBmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                diskBmp.EndInit();
                diskBmp.Freeze();
                _tabThumbnails[tab] = diskBmp;
                return diskBmp;
            }

            if (!_tabViews.TryGetValue(tab, out var browser)) return null;
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return null;

            var ms          = new System.IO.MemoryStream();
            var captureTask = core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            var timeoutTask = Task.Delay(2500);

            if (await Task.WhenAny(captureTask, timeoutTask) == timeoutTask)
            {
                _ = captureTask.ContinueWith(t => { _ = t.Exception; ms.Dispose(); }, TaskContinuationOptions.None);
                LogService.Write("SWITCHER", $"Snapshot timed out for: {tab.DisplayTitle}");
                return null;
            }

            await captureTask;
            ms.Position = 0;

            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ms.Dispose();

            _tabThumbnails[tab] = bmp;

            if (diskPath != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
                    using var fs  = File.Create(diskPath);
                    var encoder   = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    encoder.Save(fs);
                }
                catch { }
            }

            return bmp;
        }
        catch (Exception ex)
        {
            LogService.Write("SWITCHER", $"Snapshot failed: {ex.Message}");
            return null;
        }
    }

    private async void HandleMediaStateChanged(TabViewModel tab, Controls.BrowserView browser)
    {
        bool isAudioReported = browser.MainWebView.CoreWebView2?.IsDocumentPlayingAudio == true;
        bool isPlaying = isAudioReported;

        if (isAudioReported)
        {
            try
            {
                var core = browser.MainWebView.CoreWebView2;
                if (core != null)
                {
                    string check = await core.ExecuteScriptAsync(
                        "(() => { const m = [...document.querySelectorAll('video,audio')].filter(x => !x.paused && !x.ended && x.readyState > 2); if(m.length===0) return '{\"isPlaying\":false}'; const hasVid = m.some(x => x.tagName === 'VIDEO' && x.videoWidth > 0); return JSON.stringify({isPlaying:true, hasVideo:hasVid}); })()");
                    
                    if (check != "null" && !string.IsNullOrEmpty(check))
                    {
                        try 
                        {
                            var unescaped = System.Text.Json.JsonSerializer.Deserialize<string>(check);
                            var st = System.Text.Json.JsonDocument.Parse(unescaped!);
                            isPlaying = st.RootElement.GetProperty("isPlaying").GetBoolean();
                            if (isPlaying && st.RootElement.TryGetProperty("hasVideo", out var hv))
                                tab.HasVideo = hv.GetBoolean();
                        }
                        catch { isPlaying = false; }
                    }
                }
            }
            catch { }
        }

        tab.IsPlayingAudio = isPlaying;
        Dispatcher.Invoke(RefreshWidgetDisplay);

        if (isPlaying)
        {
            tab.IsMediaPaused = false;

            try
            {
                var mediaUrl = browser.MainWebView?.Source?.ToString();
                if (!string.IsNullOrEmpty(mediaUrl))
                    _mediaOriginHost[tab] = new Uri(mediaUrl).Host;
                if (_mediaDeactivationTimers.TryGetValue(tab, out var cancelDt))
                { cancelDt.Stop(); _mediaDeactivationTimers.Remove(tab); }
            }
            catch { }

            try
            {
                if (browser.MainWebView?.CoreWebView2 != null)
                {
                    await browser.MainWebView.CoreWebView2.ExecuteScriptAsync(
                        $"(() => {{ const v = document.querySelector('video, audio'); if(v) {{ v.muted = {(tab.IsMuted ? "true" : "false")}; v.volume = {tab.Volume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}; }} }})()");
                        
                    if (tab.IsAudioOnlyMode)
                    {
                        await browser.MainWebView.CoreWebView2.ExecuteScriptAsync("(() => { document.querySelectorAll('video').forEach(v => v.style.opacity = '0'); })()");
                    }

                    string script = @"(() => {
                    const m = document.querySelector('video, audio');
                    if (m && m.title) return m.title;
                    const yt = document.querySelector('h1.style-scope.ytd-watch-metadata');
                    if (yt) return yt.innerText;
                    const og = document.querySelector('meta[property=""og:title""]');
                    if (og) return og.content;
                    return null;
                })()";
                    string result = await browser.MainWebView.CoreWebView2.ExecuteScriptAsync(script);
                    if (!string.IsNullOrEmpty(result) && result != "null")
                    {
                        var _parsedMt = System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? tab.MediaTitle;
                        tab.MediaTitle = _parsedMt;
                        tab.Title = _parsedMt;
                    }
                }
            }
            catch { }

            if (!_mediaTabOriginalIndices.ContainsKey(tab))
            {
                _mediaTabOriginalIndices[tab] = _allTabs.IndexOf(tab);
                
                int maxPrimary = GetMaxPrimaryTabs();
                int currentIndex = _allTabs.IndexOf(tab);

                if (currentIndex >= maxPrimary)
                {
                    int targetIndex = -1;
                    for (int i = maxPrimary - 1; i >= 0; i--)
                    {
                        if (!_allTabs[i].IsPlayingAudio)
                        {
                            targetIndex = i;
                            break;
                        }
                    }

                    if (targetIndex != -1)
                    {
                        _allTabs.Remove(tab);
                        _allTabs.Insert(targetIndex, tab);
                    }
                }

                var previouslySelected = GetCurrentTabViewModel();
                _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    ReflowTabs();

                    if (previouslySelected != null && previouslySelected != tab)
                    {
                        if (Tabs.Contains(previouslySelected))              ListTabs.SelectedItem         = previouslySelected;
                        else if (OverflowTabs.Contains(previouslySelected)) ListOverflowTabs.SelectedItem = previouslySelected;
                        if (_tabViews.TryGetValue(previouslySelected, out var prevView))
                            prevView.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        if (Tabs.Contains(tab))              ListTabs.SelectedItem         = tab;
                        else if (OverflowTabs.Contains(tab)) ListOverflowTabs.SelectedItem = tab;
                    }
                });
            }

            StartColorAnimation(tab);

            // Palette may not be populated yet — fetch it now so the animation can start
            if (tab.PaletteColors.Count < 2)
                _ = RefreshMediaTabPaletteAsync(tab, browser);

            if (SettingsService.Current.VisualizerColorScheme == "Thumbnail" && tab.HasVideo && !tab.IsAudioOnlyMode)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    await Dispatcher.InvokeAsync(async () => await RefreshThumbnailPaletteAsync(tab, browser));
                });
            }
        }
        else
        {
            tab.IsMediaPaused = true;
            StopColorAnimation(tab, fadeOut: true);
        }
    }

    private async Task RefreshThumbnailPaletteAsync(TabViewModel tab, Controls.BrowserView browser)
    {
        if (browser.MainWebView?.CoreWebView2 == null || !tab.IsPlayingAudio) return;
        try
        {
            string js = @"(() => {
                const v = document.querySelector('video');
                if (!v || v.videoWidth === 0) return null;
                const c = document.createElement('canvas');
                c.width = 128; c.height = 72;
                c.getContext('2d').drawImage(v, 0, 0, c.width, c.height);
                return c.toDataURL('image/jpeg', 0.5);
            })()";
            string res = await browser.MainWebView.CoreWebView2.ExecuteScriptAsync(js);
            if (!string.IsNullOrEmpty(res) && res != "null")
            {
                string b64 = System.Text.Json.JsonSerializer.Deserialize<string>(res) ?? "";
                var extracted = await Core.TabColorPaletteExtractor.ExtractFromBase64Async(b64);
                if (extracted.Count > 0)
                {
                    if (tab.PaletteColors.Count >= 2 && _colorAnimTimers.ContainsKey(tab)) { _prevPaletteColors[tab] = tab.PaletteColors; _paletteBlendT[tab] = 0.0; }
                    tab.PaletteColors = extracted;
                    tab.SingleColorBrush = new SolidColorBrush(extracted[0]);
                }
            }
        }
        catch { }
    }

    private async Task RefreshMediaTabPaletteAsync(TabViewModel tab, Controls.BrowserView browser)
    {
        try
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null || tab.IsSleeping) return;

            string res = await core.ExecuteScriptAsync(@"(() => {
                const ytm = document.querySelector('#song-image yt-img-shadow img, ytmusic-player-bar yt-img-shadow img, ytmusic-player-bar img, #song-image img');
                if (ytm?.src?.startsWith('http')) return ytm.src;
                const sp = document.querySelector('[data-testid=""CoverSlotExpanded""] img, .cover-art img');
                if (sp?.src?.startsWith('http')) return sp.src;
                const sc = document.querySelector('.playbackSoundBadge__artworkLink img, .sc-artwork img');
                if (sc?.src?.startsWith('http')) return sc.src;
                const og = document.querySelector('meta[property=""og:image""]')?.content;
                if (og?.startsWith('http')) return og;
                return null;
            })()");

            if (string.IsNullOrEmpty(res) || res == "null") return;
            string imgUrl = System.Text.Json.JsonSerializer.Deserialize<string>(res) ?? "";
            if (string.IsNullOrEmpty(imgUrl)) return;

            var colors = await Core.TabColorPaletteExtractor.ExtractFromImageUrlAsync(imgUrl);
            if (colors.Count < 2) return;

            if (tab.PaletteColors.Count >= 2 && _colorAnimTimers.ContainsKey(tab)) { _prevPaletteColors[tab] = tab.PaletteColors; _paletteBlendT[tab] = 0.0; }
            tab.PaletteColors = colors;
            tab.SingleColorBrush = new SolidColorBrush(colors[0]);

            // If already animating, swap the palette — blend transition is handled in the tick.
            if (!_colorAnimTimers.ContainsKey(tab))
                StartColorAnimation(tab);
        }
        catch { }
    }

    private void UpdateTabPalette(TabViewModel tab, string paletteJson)
    {
        var colors = new List<System.Windows.Media.Color>();

        try
        {
            if (paletteJson != "null" && !string.IsNullOrEmpty(paletteJson))
            {
                var rawList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(paletteJson);
                if (rawList != null)
                {
                    foreach (var raw in rawList)
                    {
                        var c = ParseCssColor(raw);
                        if (c.HasValue) colors.Add(c.Value);
                    }
                }
            }
        }
        catch { }

        if (colors.Count == 0 && !string.IsNullOrEmpty(tab.Url))
        {
            try
            {
                var host = new Uri(tab.Url).Host;
                int seed = host.Aggregate(0, (acc, ch) => acc * 31 + ch);
                double baseHue = Math.Abs(seed % 360) / 360.0;
                colors.Add(HslToRgb(baseHue, 0.75, 0.50));
                colors.Add(HslToRgb((baseHue + 0.33) % 1.0, 0.75, 0.50));
                colors.Add(HslToRgb((baseHue + 0.66) % 1.0, 0.75, 0.50));
            }
            catch { }
        }

        if (colors.Count == 0) return;

        for (int i = 0; i < colors.Count; i++)
        {
            RgbToHsl(colors[i], out double h, out double s, out double l);
            s = Math.Max(s, 0.65);
            l = Math.Max(0.40, Math.Min(0.62, l));
            colors[i] = HslToRgb(h, s, l);
        }

        if (colors.Count == 1)
        {
            RgbToHsl(colors[0], out double h, out double s, out double l);
            colors.Add(HslToRgb((h + 0.30) % 1.0, s, l));
            colors.Add(HslToRgb((h + 0.60) % 1.0, s, l));
        }
        else if (colors.Count == 2)
        {
            RgbToHsl(colors[0], out double h, out _, out _);
            colors.Add(HslToRgb((h + 0.50) % 1.0, 0.70, 0.52));
        }

        tab.PaletteColors = colors;
        tab.SingleColorBrush = colors.Count > 0 
            ? new SolidColorBrush(colors[0]) 
            : new SolidColorBrush(Color.FromRgb(80, 80, 80));
    }

    private static System.Windows.Media.Color? ParseCssColor(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        raw = raw.Trim();
        try
        {
            if (raw.StartsWith("#"))
                return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(raw);

            if (raw.StartsWith("rgb"))
            {
                var inner = raw.Substring(raw.IndexOf('(') + 1).TrimEnd(')');
                var parts = inner.Split(',');
                if (parts.Length >= 3 &&
                    byte.TryParse(parts[0].Trim(), out byte r) &&
                    byte.TryParse(parts[1].Trim(), out byte g) &&
                    byte.TryParse(parts[2].Trim().Split('.')[0], out byte b))
                    return System.Windows.Media.Color.FromRgb(r, g, b);
            }
        }
        catch { }
        return null;
    }

    private const double PaletteTickMs        = 28.0;
    private static double PaletteSampleRateSec => SettingsService.Current.PaletteSampleRateSec > 0 ? SettingsService.Current.PaletteSampleRateSec : 0.9;

    private static readonly double[] _bandFreq = { 0.15, 0.40, 0.75 };

    private static readonly double[] _bandPhase = { 0.0, 1.1, 2.4 };

    private void StartColorAnimation(TabViewModel tab)
    {
        if (_colorAnimTimers.ContainsKey(tab)) return;
        if (tab.PaletteColors.Count < 2) return;

        _colorAnimT[tab]   = 0.0;
        _colorAnimPhase[tab] = 0;
        _vizTime[tab]      = 0.0;
        _vizSmoothedAmps[tab] = new double[3];
        _colorAnimFade[tab] = 0.0; 

        const double phaseStep     = 0.006;
        double ticksPerSample      = PaletteSampleRateSec * 1000.0 / PaletteTickMs;
        double paletteBlendStep    = 1.0 / (ticksPerSample * 0.90);
        double fadeStep            = 1.0 / (ticksPerSample * 0.45);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PaletteTickMs) }; 
        timer.Tick += (s, e) =>
        {
            var palette = tab.PaletteColors;
            if (palette.Count < 2) return;
            int n = palette.Count;

            _colorAnimT[tab] += phaseStep;
            if (_colorAnimT[tab] >= 1.0)
            {
                _colorAnimT[tab] = 0.0;
                _colorAnimPhase[tab] = (_colorAnimPhase[tab] + 1) % n;
            }

            _vizTime[tab] += 1.0; 

            if (_colorAnimFade[tab] < 1.0)
            {
                _colorAnimFade[tab] = Math.Min(1.0, _colorAnimFade[tab] + fadeStep);
            }
            double fadeAlpha = _colorAnimFade[tab];

            double t  = _colorAnimT[tab];
            double vt = _vizTime[tab];
            int    p  = _colorAnimPhase[tab];

            var baseC0 = LerpColor(palette[p % n],         palette[(p + 1) % n], t);
            var baseC1 = LerpColor(palette[(p + 1) % n],   palette[(p + 2) % n], t);
            var baseC2 = LerpColor(palette[(p + 2) % n],   palette[p % n],       t);

            if (_prevPaletteColors.TryGetValue(tab, out var prevPalette) && prevPalette.Count >= 2)
            {
                double bt = _paletteBlendT[tab];
                if (bt < 1.0)
                {
                    bt = Math.Min(1.0, bt + paletteBlendStep);
                    _paletteBlendT[tab] = bt;
                    int pn = prevPalette.Count;
                    var prevC0 = LerpColor(prevPalette[p % pn],         prevPalette[(p + 1) % pn], t);
                    var prevC1 = LerpColor(prevPalette[(p + 1) % pn],   prevPalette[(p + 2) % pn], t);
                    var prevC2 = LerpColor(prevPalette[(p + 2) % pn],   prevPalette[p % pn],       t);
                    baseC0 = LerpColor(prevC0, baseC0, bt);
                    baseC1 = LerpColor(prevC1, baseC1, bt);
                    baseC2 = LerpColor(prevC2, baseC2, bt);
                }
                else
                {
                    _prevPaletteColors.Remove(tab);
                    _paletteBlendT.Remove(tab);
                }
            }

            double amp0 = ComputeBandAmplitude(vt, _bandFreq[0], _bandPhase[0]);
            double amp1 = ComputeBandAmplitude(vt, _bandFreq[1], _bandPhase[1]);
            double amp2 = ComputeBandAmplitude(vt, _bandFreq[2], _bandPhase[2]);

            const double smoothAlpha = 0.88;
            var sa = _vizSmoothedAmps[tab];
            sa[0] += (amp0 - sa[0]) * smoothAlpha;
            sa[1] += (amp1 - sa[1]) * smoothAlpha;
            sa[2] += (amp2 - sa[2]) * smoothAlpha;
            amp0 = sa[0];
            amp1 = sa[1];
            amp2 = sa[2];

            var c0 = ModulateVisualizerColor(baseC0, amp0);
            var c1 = ModulateVisualizerColor(baseC1, amp1);
            var c2 = ModulateVisualizerColor(baseC2, amp2);

            c0.A = (byte)(255 * fadeAlpha);
            c1.A = (byte)((190 + (int)(amp1 * 65)) * fadeAlpha);  
            c2.A = (byte)(255 * fadeAlpha);

            tab.AnimatedBrush.GradientStops[0].Color = c0;
            tab.AnimatedBrush.GradientStops[1].Color = c1;
            tab.AnimatedBrush.GradientStops[2].Color = c2;

            double midOffset = 0.25 + 0.5 * ((Math.Sin(vt * 0.031) * 0.5) + 0.5);
            tab.AnimatedBrush.GradientStops[1].Offset = midOffset;

            double lum = GetLuminance(c0) * 0.5 + GetLuminance(c1) * 0.3 + GetLuminance(c2) * 0.2;

            System.Windows.Media.Color fg;
            if (lum > 0.45)
            {
                RgbToHsl(c0, out double h, out _, out _);
                fg = HslToRgb(h, 0.25, 0.10);
            }
            else
            {
                fg = System.Windows.Media.Color.FromRgb(240, 240, 235);
            }
            tab.TitleForeground.Color = fg;
        };

        timer.Start();
        _colorAnimTimers[tab] = timer;

        if (!_paletteRefreshTimers.ContainsKey(tab))
        {
            var prt = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PaletteSampleRateSec) };
            prt.Tick += async (_, _) =>
            {
                if (!tab.HasEverPlayedAudio || !_tabViews.TryGetValue(tab, out var bv)) return;
                if (SettingsService.Current.VisualizerColorScheme == "Thumbnail" && tab.HasVideo && !tab.IsAudioOnlyMode)
                    await RefreshThumbnailPaletteAsync(tab, bv);
                else
                    await RefreshMediaTabPaletteAsync(tab, bv);
            };
            prt.Start();
            _paletteRefreshTimers[tab] = prt;
        }
    }

    private static double ComputeBandAmplitude(double tick, double freq, double phase)
    {
        double primary   = (Math.Sin(tick * freq + phase) + 1.0) * 0.5;             
        double harmonic  = (Math.Sin(tick * freq * 1.4 + phase * 0.7) + 1.0) * 0.5; 
        double noise     = _vizRng.NextDouble();
        return (primary * 0.70 + harmonic * 0.20 + noise * 0.10) * 0.200;
    }

    private static System.Windows.Media.Color ModulateVisualizerColor(
        System.Windows.Media.Color baseColor, double amplitude)
    {
        RgbToHsl(baseColor, out double h, out double s, out double l);

        s = Math.Min(1.0, s * (0.55 + amplitude * 0.90));
        l = Math.Max(0.18, Math.Min(0.78, l * (0.45 + amplitude * 1.10)));

        return HslToRgb(h, s, l);
    }

    private void StopColorAnimation(TabViewModel tab, bool fadeOut = false)
    {
        if (!_colorAnimTimers.TryGetValue(tab, out var timer)) return;
        timer.Stop();
        _colorAnimTimers.Remove(tab);
        if (_paletteRefreshTimers.TryGetValue(tab, out var prt)) { prt.Stop(); _paletteRefreshTimers.Remove(tab); }
        _colorAnimT.Remove(tab);
        _colorAnimPhase.Remove(tab);
        _vizTime.Remove(tab);
        _vizSmoothedAmps.Remove(tab);
        _colorAnimFade.Remove(tab);

        if (fadeOut)
        {
            int steps = 8;
            int stepIndex = 0;
            var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            fade.Tick += (s, e) =>
            {
                stepIndex++;
                double alpha = 1.0 - (stepIndex / (double)steps);
                byte a = (byte)(alpha * 210);
                foreach (var stop in tab.AnimatedBrush.GradientStops)
                    stop.Color = System.Windows.Media.Color.FromArgb(a, stop.Color.R, stop.Color.G, stop.Color.B);

                if (stepIndex >= steps)
                {
                    fade.Stop();
                    foreach (var stop in tab.AnimatedBrush.GradientStops)
                        stop.Color = Colors.Transparent;
                    tab.AnimatedBrush.GradientStops[1].Offset = 0.5;
                    tab.TitleForeground.Color = Colors.White;
                }
            };
            fade.Start();
        }
        else
        {
            foreach (var stop in tab.AnimatedBrush.GradientStops)
                stop.Color = Colors.Transparent;
            tab.AnimatedBrush.GradientStops[1].Offset = 0.5;
            tab.TitleForeground.Color = Colors.White;
        }
    }

    private static System.Windows.Media.Color LerpColor(
        System.Windows.Media.Color a, System.Windows.Media.Color b, double t)
    {
        double s = t * t * (3 - 2 * t); 
        return System.Windows.Media.Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * s),
            (byte)(a.R + (b.R - a.R) * s),
            (byte)(a.G + (b.G - a.G) * s),
            (byte)(a.B + (b.B - a.B) * s));
    }

    private static double GetLuminance(System.Windows.Media.Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        r = r < 0.04045 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = g < 0.04045 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = b < 0.04045 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static void RgbToHsl(System.Windows.Media.Color c,
        out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        if (max == min) { h = s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if      (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
        else if (max == g) h = ((b - r) / d + 2) / 6.0;
        else               h = ((r - g) / d + 4) / 6.0;
    }

    private static System.Windows.Media.Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue2Rgb(p, q, h + 1.0 / 3);
            g = Hue2Rgb(p, q, h);
            b = Hue2Rgb(p, q, h - 1.0 / 3);
        }
        return System.Windows.Media.Color.FromRgb(
            (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double Hue2Rgb(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    // Prevents right-click on any media playback button from opening the tab's context menu.
    // PreviewMouseRightButtonUp tunnels before WPF's context-menu trigger fires.
    private void HoverMediaPanel_SuppressContextMenu(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    private async void BtnMediaPlayPause_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return;
            await core.ExecuteScriptAsync(
                "(() => { const m = document.querySelector('video, audio'); if(m) { if(m.paused) m.play().catch(()=>{}); else m.pause(); } })()");
        }
    }

    private void BtnMediaMute_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return;
            // Use CoreWebView2.IsMuted so the state stays in sync with the IsMutedChanged event
            core.IsMuted = !core.IsMuted;
            tab.IsMuted = core.IsMuted;
        }
    }

    private async void BtnMediaBack_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return;
            await core.ExecuteScriptAsync(
                "(() => { const v = document.querySelector('video'); if(v) v.currentTime = Math.max(0, v.currentTime - 10); })()");
        }
    }

    private async void BtnMediaForward_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return;
            await core.ExecuteScriptAsync(
                "(() => { const v = document.querySelector('video'); if(v) v.currentTime = Math.min(v.duration || v.currentTime, v.currentTime + 10); })()");
        }
    }

    private async void BtnMediaBack_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return;
            // Right-click ⏮ = Previous Track
            // Try platform-specific buttons first, then restart current track as fallback
            await core.ExecuteScriptAsync(@"(() => {
                const yt = document.querySelector('.ytp-prev-button');
                if (yt) { yt.click(); return; }
                const sp = document.querySelector('[data-testid=""previous-button""]');
                if (sp) { sp.click(); return; }
                const sc = document.querySelector('.skipControl__previous');
                if (sc) { sc.click(); return; }
                // Generic fallback: restart current track
                const m = document.querySelector('video, audio');
                if (m) m.currentTime = 0;
            })()");
        }
    }

    private async void BtnMediaForward_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return;
            // Right-click ⏭ = Next Track
            await core.ExecuteScriptAsync(@"(() => {
                const yt = document.querySelector('.ytp-next-button');
                if (yt) { yt.click(); return; }
                const sp = document.querySelector('[data-testid=""next-button""]');
                if (sp) { sp.click(); return; }
                const sc = document.querySelector('.skipControl__next');
                if (sc) { sc.click(); return; }
                // Generic fallback: skip to end to trigger playlist advance
                const m = document.querySelector('video, audio');
                if (m && isFinite(m.duration)) m.currentTime = m.duration - 0.1;
            })()");
        }
    }

    // ── Extension popup (clicking card in sidebar = open popup like Chrome/Edge toolbar click) ──

    private async void RightSidebar_RequestExtensionPopup(object? sender, ExtensionRecord ext)
    {
        var browser = CurrentBrowser;
        if (browser?.MainWebView?.CoreWebView2 == null) return;

        try
        {
            // Look up the browser-assigned extension ID that was stored at load time.
            // FolderName is the lowercase directory name (e.g. "adguard", "consent-o-matic").
            string? extId = null;
            if (Controls.BrowserView.LoadedExtensionIds.TryGetValue(ext.FolderName, out var storedId))
                extId = storedId;

            // Fallback: query the profile live (covers extensions loaded before this session's
            // BrowserView version, or manually installed extensions)
            if (string.IsNullOrEmpty(extId))
            {
                var wv2Extensions = await browser.MainWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
                var match = wv2Extensions.FirstOrDefault(e =>
                    e.Name.Equals(ext.Name, StringComparison.OrdinalIgnoreCase));
                extId = match?.Id;
            }

            if (string.IsNullOrEmpty(extId))
            {
                MessageBox.Show(
                    $"'{ext.Name}' is not active in this session.\n\nMake sure it's enabled and restart Horizon.",
                    "Extension Not Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string manifestPath = System.IO.Path.Combine(ExtensionService.InstallRoot, ext.FolderName, "manifest.json");
            string popupRelPath = ReadExtensionPopupPath(manifestPath);

            if (string.IsNullOrEmpty(popupRelPath))
            {
                MessageBox.Show(
                    $"'{ext.Name}' has no popup defined in its manifest.\n\nSome extensions only inject scripts and have no clickable popup.",
                    "No Popup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string popupUrl = $"chrome-extension://{extId}/{popupRelPath.TrimStart('/')}";
            LogService.Write("EXT", $"Opening popup: {popupUrl}");
            await OpenExtensionPopupWindowAsync(popupUrl, ext.Name);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ExtensionPopup");
            MessageBox.Show($"Could not open extension popup:\n{ex.Message}", "Error");
        }
    }

    /// <summary>
    /// Reads action.default_popup (MV3) or browser_action.default_popup (MV2) from a manifest.json.
    /// </summary>
    private static string ReadExtensionPopupPath(string manifestPath)
    {
        if (!System.IO.File.Exists(manifestPath)) return string.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(manifestPath));
            var root = doc.RootElement;

            // Manifest V3
            if (root.TryGetProperty("action", out var action) &&
                action.TryGetProperty("default_popup", out var p3))
                return p3.GetString() ?? string.Empty;

            // Manifest V2
            if (root.TryGetProperty("browser_action", out var ba) &&
                ba.TryGetProperty("default_popup", out var p2))
                return p2.GetString() ?? string.Empty;

            // Some extensions use page_action
            if (root.TryGetProperty("page_action", out var pa) &&
                pa.TryGetProperty("default_popup", out var pp))
                return pp.GetString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    /// <summary>
    /// Opens a small floating window containing a WebView2 that navigates to the extension's popup URL.
    /// Shares the same CoreWebView2Environment as the main browser so extensions are available.
    /// </summary>
    private async Task OpenExtensionPopupWindowAsync(string popupUrl, string extName)
    {
        var popupWin = new Window
        {
            Title            = extName,
            Width            = 380,
            Height           = 500,
            MinWidth         = 250,
            MinHeight        = 200,
            WindowStyle      = WindowStyle.ToolWindow,
            ResizeMode       = ResizeMode.CanResize,
            Background       = new System.Windows.Media.SolidColorBrush(
                                   System.Windows.Media.Color.FromRgb(0x1a, 0x1a, 0x1a)),
            Owner            = this,
            ShowInTaskbar    = false,
        };

        var wv = new Microsoft.Web.WebView2.Wpf.WebView2
        {
            
            DefaultBackgroundColor = System.Drawing.Color.White,
        };
        popupWin.Content = wv;
        popupWin.Show();

        // Share the same environment (profile) as the main browser — required for extension access
        await wv.EnsureCoreWebView2Async(CurrentBrowser?.MainWebView?.CoreWebView2?.Environment);

        wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        wv.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
        wv.CoreWebView2.Settings.IsWebMessageEnabled = true;
        wv.CoreWebView2.Settings.AreHostObjectsAllowed = true;

        // Handle NewWindowRequested: extension popups that open OAuth flows (Google, etc.)
        // need their target=_blank and window.open() calls to open in a real navigable window.
        wv.CoreWebView2.NewWindowRequested += (nwSender, nwArgs) =>
        {
            nwArgs.Handled = true;
            string targetUrl = nwArgs.Uri ?? "";
            LogService.Write("EXT", $"Extension popup NewWindow: {targetUrl}");

            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    bool isOAuth = targetUrl.Contains("accounts.google.com") ||
                                   targetUrl.Contains("oauth") ||
                                   targetUrl.Contains("login") ||
                                   targetUrl.Contains("signin") ||
                                   targetUrl.Contains("auth");

                    var authWin = new Window
                    {
                        Title         = isOAuth ? "Sign In" : "Extension",
                        Width         = 500,
                        Height        = 700,
                        WindowStyle   = WindowStyle.ToolWindow,
                        ResizeMode    = ResizeMode.CanResize,
                        Owner         = popupWin,
                        ShowInTaskbar = false,
                    };
                    var authWv = new Microsoft.Web.WebView2.Wpf.WebView2();
                    authWin.Content = authWv;
                    authWin.Show();

                    await authWv.EnsureCoreWebView2Async(wv.CoreWebView2.Environment);

                    // Allow Google sign-in and third-party cookies so OAuth flows complete
                    authWv.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;

                    // When auth redirects back into the extension scheme or closes, relay it
                    authWv.CoreWebView2.NavigationStarting += (_, navArgs) =>
                    {
                        string url = navArgs.Uri ?? "";
                        // chromiumapp.org is the standard Chrome extension OAuth redirect host
                        if (url.Contains(".chromiumapp.org") || url.StartsWith("chrome-extension://"))
                        {
                            navArgs.Cancel = true;
                            // Post the redirect URL back to the extension popup as a message
                            string escaped = System.Text.Json.JsonSerializer.Serialize(url);
                            wv.CoreWebView2.ExecuteScriptAsync(
                                $"window._horizonOAuthRedirect && window._horizonOAuthRedirect({escaped})");
                            Dispatcher.Invoke(() => authWin.Close());
                        }
                    };
                    authWv.CoreWebView2.Navigate(targetUrl);
                }
                catch (Exception ex)
                {
                    LogService.Write("EXT", $"Failed to open extension auth window: {ex.Message}");
                }
            });
        };

        await wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
(function() {
    const patch = () => {
        if (typeof chrome === 'undefined' || !chrome) return false;

        // ── chrome.downloads ────────────────────────────────────────────────
        chrome.downloads = chrome.downloads || {};
        chrome.downloads.download = function(options, callback) {
            const url = options?.url || '';
            if (!url) { if (typeof callback === 'function') callback(-1); return; }
            try {
                const a = document.createElement('a');
                a.href = url; a.download = options?.filename || '';
                a.style.display = 'none';
                document.body.appendChild(a);
                a.click();
                setTimeout(() => { try { document.body.removeChild(a); } catch(e){} }, 500);
                if (typeof callback === 'function') callback(Date.now());
            } catch(e) {
                try { window.open(url, '_blank'); } catch(e2) {}
                if (typeof callback === 'function') callback(-1);
            }
        };
        chrome.downloads.search      = chrome.downloads.search      || function(q,cb){ if(cb) cb([]); return Promise.resolve([]); };
        chrome.downloads.onCreated   = chrome.downloads.onCreated   || { addListener:()=>{}, removeListener:()=>{} };
        chrome.downloads.onChanged   = chrome.downloads.onChanged   || { addListener:()=>{}, removeListener:()=>{} };
        chrome.downloads.onErased    = chrome.downloads.onErased    || { addListener:()=>{}, removeListener:()=>{} };
        chrome.downloads.erase       = chrome.downloads.erase       || function(q,cb){ if(cb) cb([]); };
        chrome.downloads.removeFile  = chrome.downloads.removeFile  || function(id,cb){ if(cb) cb(); };

        // ── chrome.storage ──────────────────────────────────────────────────
        if (!chrome.storage || !chrome.storage._horizonPatched) {
            const _store = {};
            const _makeArea = (prefix) => ({
                get: (keys, cb) => {
                    const result = {};
                    const ks = typeof keys === 'string' ? [keys] : (Array.isArray(keys) ? keys : Object.keys(keys));
                    ks.forEach(k => { try { const v = localStorage.getItem(prefix+k); if(v!==null) result[k] = JSON.parse(v); else if(typeof keys==='object'&&!Array.isArray(keys)) result[k]=keys[k]; } catch(e){} });
                    if (typeof cb === 'function') cb(result);
                    return Promise.resolve(result);
                },
                set: (items, cb) => {
                    Object.entries(items).forEach(([k,v]) => { try { localStorage.setItem(prefix+k, JSON.stringify(v)); } catch(e){} });
                    if (typeof cb === 'function') cb();
                    return Promise.resolve();
                },
                remove: (keys, cb) => {
                    const ks = typeof keys === 'string' ? [keys] : keys;
                    ks.forEach(k => localStorage.removeItem(prefix+k));
                    if (typeof cb === 'function') cb();
                    return Promise.resolve();
                },
                clear: (cb) => {
                    Object.keys(localStorage).filter(k=>k.startsWith(prefix)).forEach(k=>localStorage.removeItem(k));
                    if (typeof cb === 'function') cb();
                    return Promise.resolve();
                },
                onChanged: { addListener:()=>{}, removeListener:()=>{} }
            });
            chrome.storage = {
                local:   _makeArea('__ext_local_'),
                sync:    _makeArea('__ext_sync_'),
                session: _makeArea('__ext_session_'),
                onChanged: { addListener:()=>{}, removeListener:()=>{} },
                _horizonPatched: true
            };
        }

        // ── chrome.runtime extras ───────────────────────────────────────────
        chrome.runtime = chrome.runtime || {};
        chrome.runtime.sendMessage = chrome.runtime.sendMessage || function(a,b,c){ const cb=typeof b==='function'?b:c; if(typeof cb==='function') cb({}); return Promise.resolve({}); };
        chrome.runtime.onMessage   = chrome.runtime.onMessage   || { addListener:()=>{}, removeListener:()=>{}, hasListeners:()=>false };
        chrome.runtime.onInstalled = chrome.runtime.onInstalled || { addListener:()=>{}, removeListener:()=>{} };
        chrome.runtime.onStartup   = chrome.runtime.onStartup   || { addListener:()=>{}, removeListener:()=>{} };
        chrome.runtime.openOptionsPage = chrome.runtime.openOptionsPage || function(cb){ if(typeof cb==='function') cb(); };
        chrome.runtime.getManifest = chrome.runtime.getManifest || function(){ return {}; };
        chrome.runtime.getURL      = chrome.runtime.getURL      || function(path){ return 'chrome-extension://unknown/'+path; };

        // ── chrome.identity (OAuth — powers Google login in extensions) ─────
        chrome.identity = chrome.identity || {};
        chrome.identity.getAuthToken = chrome.identity.getAuthToken || function(details, cb) {
            // Not supported natively — notify the extension so it can fall back
            if (typeof cb === 'function') cb(undefined);
            return Promise.reject(new Error('getAuthToken not supported outside Chrome'));
        };
        chrome.identity.launchWebAuthFlow = chrome.identity.launchWebAuthFlow || function(details, cb) {
            // Ask the host WPF layer to open an auth window via window.open,
            // then relay the redirect URL back via window._horizonOAuthRedirect.
            window._horizonOAuthCallback = cb;
            window._horizonOAuthRedirect = function(redirectUrl) {
                window._horizonOAuthCallback && window._horizonOAuthCallback(redirectUrl);
            };
            try { window.open(details.url, '_blank', 'width=500,height=700'); }
            catch(e) { if(typeof cb==='function') cb(undefined, String(e)); }
        };
        chrome.identity.getRedirectURL = chrome.identity.getRedirectURL || function(path) {
            return 'https://' + (chrome.runtime.id || 'unknown') + '.chromiumapp.org/' + (path||'');
        };
        chrome.identity.onSignInChanged = chrome.identity.onSignInChanged || { addListener:()=>{}, removeListener:()=>{} };

        // ── chrome.notifications ────────────────────────────────────────────
        chrome.notifications = chrome.notifications || {
            create:       function(id,opts,cb){ if(typeof cb==='function') cb(id||'n1'); },
            clear:        function(id,cb){ if(typeof cb==='function') cb(true); },
            getAll:       function(cb){ if(typeof cb==='function') cb({}); },
            onClicked:    { addListener:()=>{}, removeListener:()=>{} },
            onClosed:     { addListener:()=>{}, removeListener:()=>{} },
            onButtonClicked: { addListener:()=>{}, removeListener:()=>{} }
        };

        // ── chrome.alarms ───────────────────────────────────────────────────
        chrome.alarms = chrome.alarms || {
            create:    function(){},
            clear:     function(n,cb){ if(typeof cb==='function') cb(true); },
            clearAll:  function(cb){ if(typeof cb==='function') cb(true); },
            get:       function(n,cb){ if(typeof cb==='function') cb(undefined); },
            getAll:    function(cb){ if(typeof cb==='function') cb([]); },
            onAlarm:   { addListener:()=>{}, removeListener:()=>{} }
        };

        // ── chrome.contextMenus ─────────────────────────────────────────────
        chrome.contextMenus = chrome.contextMenus || {
            create:     function(){ return 'menu_'+Date.now(); },
            update:     function(id,props,cb){ if(typeof cb==='function') cb(); },
            remove:     function(id,cb){ if(typeof cb==='function') cb(); },
            removeAll:  function(cb){ if(typeof cb==='function') cb(); },
            onClicked:  { addListener:()=>{}, removeListener:()=>{} }
        };

        return true;
    };
    if (!patch()) { const t = setInterval(() => { if (patch()) clearInterval(t); }, 20); }
})();
");

        
        string tabUrl   = CurrentBrowser?.MainWebView?.Source?.ToString() ?? "";
        string tabTitle = CurrentBrowser?.MainWebView?.CoreWebView2?.DocumentTitle ?? "";

        // Escape for embedding in a JS string literal
        string safeUrl   = System.Text.Json.JsonSerializer.Serialize(tabUrl);
        string safeTitle = System.Text.Json.JsonSerializer.Serialize(tabTitle);

        await wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($@"
(function() {{
    'use strict';
    const _horizonTab = {{
        id: 1, index: 0, windowId: 1,
        active: true, highlighted: true, selected: true,
        pinned: false, incognito: false,
        status: 'complete',
        url:   {safeUrl},
        title: {safeTitle},
        favIconUrl: ''
    }};

    // Patch as soon as chrome.tabs is available (may be set up after this script runs)
    const patchTabs = () => {{
        if (typeof chrome === 'undefined' || !chrome || !chrome.tabs) return false;
        const _origQuery  = chrome.tabs.query?.bind(chrome.tabs);
        const _origGet    = chrome.tabs.get?.bind(chrome.tabs);
        const _origGetCur = chrome.tabs.getCurrent?.bind(chrome.tabs);

        chrome.tabs.query = function(queryInfo, callback) {{
            // Return our fake tab for any ""what is the active tab?"" query
            if (!queryInfo || queryInfo.active !== false) {{
                if (typeof callback === 'function') {{ callback([_horizonTab]); return; }}
                return Promise.resolve([_horizonTab]);
            }}
            if (_origQuery) return _origQuery(queryInfo, callback);
            if (typeof callback === 'function') {{ callback([]); return; }}
            return Promise.resolve([]);
        }};

        chrome.tabs.get = function(tabId, callback) {{
            if (typeof callback === 'function') {{ callback(_horizonTab); return; }}
            return Promise.resolve(_horizonTab);
        }};

        chrome.tabs.getCurrent = function(callback) {{
            if (typeof callback === 'function') {{ callback(_horizonTab); return; }}
            return Promise.resolve(_horizonTab);
        }};

        // Expose directly for extensions that read window._tab or similar globals
        window._horizonCurrentTab = _horizonTab;
        return true;
    }};

    if (!patchTabs()) {{
        // chrome.tabs not ready yet — retry after a short yield
        const t = setInterval(() => {{ if (patchTabs()) clearInterval(t); }}, 20);
    }}
}})();
");

        // Resize window to match the extension popup's natural dimensions after load
        wv.CoreWebView2.NavigationCompleted += async (s, args) =>
        {
            try
            {
                string json = await wv.CoreWebView2.ExecuteScriptAsync(@"
(function() {
    var b = document.body;
    if (!b) return JSON.stringify({w:0,h:0});
    return JSON.stringify({ w: b.scrollWidth, h: b.scrollHeight });
})()");
                // ExecuteScriptAsync returns a JSON-encoded string — unwrap it
                string inner = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "{}";
                var dims = System.Text.Json.JsonDocument.Parse(inner);
                double w = dims.RootElement.GetProperty("w").GetDouble();
                double h = dims.RootElement.GetProperty("h").GetDouble();
                if (w > 50 && h > 50)
                    Dispatcher.Invoke(() =>
                    {
                        popupWin.Width  = Math.Clamp(w + 20, 200, 800);
                        popupWin.Height = Math.Clamp(h + 40, 150, 700);
                    });
            }
            catch { }
        };

        wv.CoreWebView2.Navigate(popupUrl);
    }

    /// <summary>Injects a wheel-event override into a CoreWebView2 so scroll speed matches ScrollSpeedMultiplier.</summary>
    private static async Task InjectScrollSpeedAsync(Microsoft.Web.WebView2.Core.CoreWebView2 core)
    {
        double mult = SettingsService.Current.ScrollSpeedMultiplier;
        string multStr = mult.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        await core.AddScriptToExecuteOnDocumentCreatedAsync($@"
(function() {{
    var _m = {multStr};
    if (Math.abs(_m - 1.0) < 0.005) return; // effectively 1× — leave Chromium native
    window.addEventListener('wheel', function(e) {{
        if (e.ctrlKey || e.metaKey) return; // don't interfere with zoom
        e.preventDefault();
        window.scrollBy({{
            top:  e.deltaY * _m,
            left: e.deltaX * _m,
            behavior: 'auto'
        }});
    }}, {{ passive: false, capture: true }});
}})();
");
    }

    private async void BtnMediaMiniPlayer_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return;
            string hideOpacity = tab.IsAudioOnlyMode ? "'0'" : "'1'";
            await core.ExecuteScriptAsync(
                $"(() => {{ const v = document.querySelector('video'); if(!v) return; " +
                $"v.style.opacity = {hideOpacity}; " +
                "document.pictureInPictureElement ? document.exitPictureInPicture() : v.requestPictureInPicture().catch(()=>{}); })()");
        }
    }
    
    private async void BtnMediaAudioOnly_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            var core = browser.MainWebView?.CoreWebView2;
            if (core == null) return;
            tab.IsAudioOnlyMode = !tab.IsAudioOnlyMode;
            await core.ExecuteScriptAsync(
                "(() => { document.querySelectorAll('video').forEach(v => v.style.opacity = v.style.opacity === '0' ? '1' : '0'); })()");
        }
    }

    private void BtnMediaTools_Click(object sender, RoutedEventArgs e) => e.Handled = true;

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Guard: fires during XAML binding before CoreWebView2 is ready
        if (sender is Slider slider && slider.DataContext is TabViewModel tab && _tabViews.TryGetValue(tab, out var browser))
        {
            tab.Volume = slider.Value / 100.0;

            // FIX #4 – Debounce: coalesce rapid slider drags into one JS call
            if (_volumeDebounceTimers.TryGetValue(tab, out var existing))
            {
                existing.Stop();
                _volumeDebounceTimers.Remove(tab);
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += async (s, args) =>
            {
                timer.Stop();
                _volumeDebounceTimers.Remove(tab);
                if (browser.MainWebView?.CoreWebView2 != null)
                {
                    await browser.MainWebView.CoreWebView2.ExecuteScriptAsync(
                        $"(() => {{ const v = document.querySelector('video, audio'); if(v) v.volume = {tab.Volume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}; }})()");
                }
            };
            _volumeDebounceTimers[tab] = timer;
            timer.Start();
        }
    }

    private void ToggleFullscreen()
    {
        PlayFullscreenFlash();

        if (!_isFullscreen)
        {
            _previousWindowState = WindowState;
            _isFullscreen = true;

            RowHeader.Height = new GridLength(0);
            HeaderContainer.Height = 0;
            SidebarContainer.Width = 0;
            ListOverflowTabs.Visibility = Visibility.Collapsed;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            PlayFullscreenNotify();
        }
        else
        {
            _isFullscreen = false;

            _notifySb?.Stop();
            _notifySb = null;
            FullscreenNotifyBar.Opacity = 0;

            ApplyLayoutState();
            ReflowTabs();
            WindowState = _previousWindowState;
        }
    }

    private void PlayFullscreenFlash()
    {
        FullscreenFlash.Opacity = 0;
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
        {
            From         = 0.0,
            To           = 0.75,
            Duration     = TimeSpan.FromMilliseconds(100),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        };
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            From         = 0.75,
            To           = 0.0,
            BeginTime    = TimeSpan.FromMilliseconds(100),
            Duration     = TimeSpan.FromMilliseconds(350),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        };
        var sb = new System.Windows.Media.Animation.Storyboard
        {
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        sb.Children.Add(fadeIn);
        sb.Children.Add(fadeOut);
        System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn,  FullscreenFlash);
        System.Windows.Media.Animation.Storyboard.SetTarget(fadeOut, FullscreenFlash);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn,  new PropertyPath(UIElement.OpacityProperty));
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
        sb.Completed += (s, e) => FullscreenFlash.Opacity = 0;
        sb.Begin();
    }


    private void PlayFullscreenNotify()
    {
        _notifySb?.Stop();
        var bar = FullscreenNotifyBar;
        bar.RenderTransform = new System.Windows.Media.TranslateTransform(0, -36);
        bar.Opacity = 0;

        var sb = new System.Windows.Media.Animation.Storyboard
        {
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };

        void Add(System.Windows.Media.Animation.DoubleAnimation a, DependencyProperty prop)
        {
            a.FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop;
            System.Windows.Media.Animation.Storyboard.SetTarget(a, bar);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(a, new PropertyPath(prop));
            sb.Children.Add(a);
        }

        var slideIn = new System.Windows.Media.Animation.DoubleAnimation(-36, 0, TimeSpan.FromMilliseconds(320))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        slideIn.FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop;
        System.Windows.Media.Animation.Storyboard.SetTarget(slideIn, bar);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideIn, new PropertyPath("RenderTransform.(TranslateTransform.Y)"));
        sb.Children.Add(slideIn);

        Add(new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)),
            UIElement.OpacityProperty);
        Add(new System.Windows.Media.Animation.DoubleAnimation(1, 1, TimeSpan.FromMilliseconds(3500))
            { BeginTime = TimeSpan.FromMilliseconds(220) },
            UIElement.OpacityProperty);
        Add(new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
            { BeginTime = TimeSpan.FromMilliseconds(3720) },
            UIElement.OpacityProperty);

        sb.Completed += (s, e) => { bar.Opacity = 0; _notifySb = null; };
        _notifySb = sb;
        sb.Begin();
    }

    // ────────────────────────────────────────────────────────────────
    // TAB DRAG & DROP
    // ────────────────────────────────────────────────────────────────

    // ────────────────────────────────────────────────────────────────
    // TAB DRAG & DROP  — proper animated mouse drag
    // ────────────────────────────────────────────────────────────────

    private void TabHeader_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    public void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TabViewModel tab) return;
        _dragStartPoint = e.GetPosition(null);
        _draggedTab     = tab;
        _isDragging     = false;

        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   == ModifierKeys.Shift;

        if (ctrl)
        {
            // Toggle tab in/out of multi-selection (like Ctrl-click in File Explorer)
            if (_multiSelectedTabs.Contains(tab))
            {
                _multiSelectedTabs.Remove(tab);
                tab.IsMultiSelected = false;
            }
            else
            {
                _multiSelectedTabs.Add(tab);
                tab.IsMultiSelected = true;
            }
            _lastClickedTab = tab;
            e.Handled = true; // don't hand off to ListBox selection
            return;
        }

        if (shift && _lastClickedTab != null)
        {
            // Range-select from last clicked to this tab (like Shift-click in File Explorer)
            int from = _allTabs.IndexOf(_lastClickedTab);
            int to   = _allTabs.IndexOf(tab);
            if (from < 0 || to < 0) goto ClearAndContinue;
            if (from > to) (from, to) = (to, from);

            foreach (var t in _multiSelectedTabs) t.IsMultiSelected = false;
            _multiSelectedTabs.Clear();

            for (int i = from; i <= to; i++)
            {
                _multiSelectedTabs.Add(_allTabs[i]);
                _allTabs[i].IsMultiSelected = true;
            }
            e.Handled = true;
            return;
        }

        ClearAndContinue:
        // Plain click — clear any multi-selection and proceed normally
        foreach (var t in _multiSelectedTabs) t.IsMultiSelected = false;
        _multiSelectedTabs.Clear();
        _lastClickedTab = tab;

        // DO NOT call CaptureMouse here — it swallows all button clicks inside the tab.
        // Mouse capture is set only once we confirm a real drag has started in MouseMove.
        e.Handled = false;
    }

    public void TabHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTab == null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos   = e.GetPosition(null);
        var delta = pos - _dragStartPoint;

        if (!_isDragging)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _isDragging = true;
            // Only capture mouse once we are sure a drag is happening
            if (sender is FrameworkElement fe2) fe2.CaptureMouse();
            CreateDragGhost(_draggedTab, pos);
        }

        if (_dragGhost != null)
        {
            _dragGhost.Left = pos.X + System.Windows.SystemParameters.WorkArea.Left + 10;
            _dragGhost.Top  = pos.Y + System.Windows.SystemParameters.WorkArea.Top  - 24;
        }

        UpdateDropIndicator(e.GetPosition(ListTabs));
    }

    public void TabHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();

        if (_isDragging && _draggedTab != null && _dragInsertIndex >= 0)
        {
            // If multiple tabs are selected, move them all as a group
            var toMove = _multiSelectedTabs.Count >= 2
                ? _multiSelectedTabs.OrderBy(t => _allTabs.IndexOf(t)).ToList()
                : new List<TabViewModel> { _draggedTab };

            int insertAt = _dragInsertIndex;

            // Remove all from current positions (track lowest index for correction)
            var originalIndices = toMove.Select(t => _allTabs.IndexOf(t)).OrderBy(i => i).ToList();
            foreach (var t in toMove) _allTabs.Remove(t);

            // Adjust insert position for removed items that were before it
            int removedBefore = originalIndices.Count(i => i < insertAt);
            insertAt = Math.Clamp(insertAt - removedBefore, 0, _allTabs.Count);

            // Re-insert preserving relative order
            for (int i = 0; i < toMove.Count; i++)
                _allTabs.Insert(Math.Min(insertAt + i, _allTabs.Count), toMove[i]);

            ReflowTabs();
            var dropped = _draggedTab;
            Dispatcher.BeginInvoke(() => {
                if (Tabs.Contains(dropped))              ListTabs.SelectedItem = dropped;
                else if (OverflowTabs.Contains(dropped)) ListOverflowTabs.SelectedItem = dropped;
            });
        }

        DestroyDragGhost();
        _draggedTab      = null;
        _isDragging      = false;
        _dragInsertIndex = -1;
    }

    public void TabHeader_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("HorizonTab") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    public void TabHeader_Drop(object sender, DragEventArgs e)
    {
        // Handled entirely by MouseLeftButtonUp for mouse drag.
        // This handles the edge case of OS DragDrop being used.
        if (!e.Data.GetDataPresent("HorizonTab")) return;
        if (sender is FrameworkElement fe && fe.DataContext is TabViewModel target)
        {
            var source = (TabViewModel)e.Data.GetData("HorizonTab");
            if (source != null && source != target) DoTabReorder(source, target);
        }
        e.Handled = true;
    }

    private void CreateDragGhost(TabViewModel tab, Point screenPos)
    {
        DestroyDragGhost();

        _dragGhost = new Window
        {
            WindowStyle           = WindowStyle.None,
            AllowsTransparency    = true,
            Background            = System.Windows.Media.Brushes.Transparent,
            IsHitTestVisible      = false,
            ShowInTaskbar         = false,
            Topmost               = true,
            SizeToContent         = SizeToContent.WidthAndHeight,
            Left                  = screenPos.X + System.Windows.SystemParameters.WorkArea.Left + 10,
            Top                   = screenPos.Y + System.Windows.SystemParameters.WorkArea.Top  - 24,
            Opacity               = 0.82
        };

        var border = new Border
        {
            Background      = new System.Windows.Media.SolidColorBrush(
                                  System.Windows.Media.Color.FromArgb(230, 68, 68, 68)),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(14, 6, 14, 6),
            MinWidth        = 120,
            Effect          = new System.Windows.Media.Effects.DropShadowEffect
                              { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.6 }
        };

        var txt = new TextBlock
        {
            Text            = tab.Title,
            Foreground      = System.Windows.Media.Brushes.White,
            FontWeight      = FontWeights.SemiBold,
            FontSize        = 12,
            MaxWidth        = 160,
            TextTrimming    = TextTrimming.CharacterEllipsis
        };

        border.Child      = txt;
        _dragGhost.Content = border;
        _dragGhost.Show();
    }

    private void DestroyDragGhost()
    {
        _dragGhost?.Close();
        _dragGhost = null;
    }

    private void UpdateDropIndicator(Point posRelativeToTabList)
    {
        // Walk through _allTabs that are in the primary strip (Tabs collection)
        // and figure out which slot the cursor is currently over
        int bestIdx = _allTabs.Count; // default: append at end

        var items = ListTabs.Items;
        for (int i = 0; i < items.Count; i++)
        {
            var container = ListTabs.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container == null) continue;

            var bounds = container.TransformToAncestor(ListTabs)
                                   .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));

            if (posRelativeToTabList.X < bounds.X + bounds.Width / 2)
            {
                bestIdx = i;
                break;
            }
        }

        _dragInsertIndex = bestIdx;
    }

    // Correct reorder — for external callers
    private void ReorderTab(TabViewModel source, TabViewModel target) => DoTabReorder(source, target);
    private void DoTabReorder(TabViewModel source, TabViewModel target)
    {
        int from = _allTabs.IndexOf(source);
        int to   = _allTabs.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        _allTabs.RemoveAt(from);
        if (to > from) to--;   // adjust for the removal
        _allTabs.Insert(to, source);
        ReflowTabs();
        // Keep selection
        if (Tabs.Contains(source))         ListTabs.SelectedItem = source;
        else if (OverflowTabs.Contains(source)) ListOverflowTabs.SelectedItem = source;
    }

    // ────────────────────────────────────────────────────────────────
    // TAB CONTEXT MENU
    // ────────────────────────────────────────────────────────────────

    private TabViewModel? GetTabFromContextMenuSender(object sender)
    {
        if (sender is MenuItem mi)
        {
            // Walk up: MenuItem → ContextMenu → PlacementTarget (Grid or Border)
            if (mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement el)
                return el.DataContext as TabViewModel;
            // Also handle sub-menu items whose parent is another MenuItem
            if (mi.Parent is MenuItem parentMi)
            {
                if (parentMi.Parent is ContextMenu cm2 && cm2.PlacementTarget is FrameworkElement el2)
                    return el2.DataContext as TabViewModel;
            }
        }
        return null;
    }

    public void TabMenu_Duplicate(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromContextMenuSender(sender);
        if (tab == null) return;

        string currentUrl = tab.Url;
        CreateNewTab(currentUrl);

        // Move the new tab right after the original
        var newTab = _allTabs.LastOrDefault();
        if (newTab != null && newTab != tab)
        {
            int from = _allTabs.IndexOf(newTab);
            int to   = _allTabs.IndexOf(tab) + 1;
            if (from >= 0 && to <= _allTabs.Count && from != to)
            {
                _allTabs.RemoveAt(from);
                _allTabs.Insert(Math.Min(to, _allTabs.Count), newTab);
            }
            ReflowTabs();
        }
    }

    public void TabMenu_Rename(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromContextMenuSender(sender);
        if (tab == null) return;

        string? newTitle = ShowInputDialog("Rename Tab", "Enter new tab name:", tab.Title);
        if (newTitle != null && newTitle.Trim().Length > 0)
        {
            tab.Title = newTitle.Trim();
            tab.HasCustomTitle = true; // Prevent automatic page-title overwrites
        }
    }

    public void TabMenu_ChangePosition(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromContextMenuSender(sender);
        if (tab == null) return;

        int total    = _allTabs.Count;
        int current  = _allTabs.IndexOf(tab) + 1;

        string? input = ShowInputDialog("Change Tab Position",
            $"Enter position (1–{total}), or 'row,col' (e.g. 2,3)\n" +
            $"Row×Col takes priority over flat position.\n" +
            $"Current position: {current}",
            current.ToString());

        if (input == null) return;
        input = input.Trim();

        int targetIdx; // 0-based

        if (input.Contains(','))
        {
            var parts = input.Split(',');
            if (parts.Length >= 2 &&
                int.TryParse(parts[0].Trim(), out int row) &&
                int.TryParse(parts[1].Trim(), out int col))
            {
                int tabsPerRow = SettingsService.Current.TabsPerRow;
                targetIdx = Math.Clamp(((row - 1) * tabsPerRow) + (col - 1), 0, total - 1);
            }
            else return;
        }
        else if (int.TryParse(input, out int pos))
        {
            targetIdx = Math.Clamp(pos - 1, 0, total - 1);
        }
        else return;

        int from = _allTabs.IndexOf(tab);
        if (from == targetIdx) return;

        _allTabs.RemoveAt(from);
        _allTabs.Insert(targetIdx, tab);
        ReflowTabs();

        if (Tabs.Contains(tab))              ListTabs.SelectedItem = tab;
        else if (OverflowTabs.Contains(tab)) ListOverflowTabs.SelectedItem = tab;
    }

    public void TabMenu_BringToTop(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromContextMenuSender(sender);
        if (tab == null) return;
        int from = _allTabs.IndexOf(tab);
        if (from <= 0) return;
        _allTabs.RemoveAt(from);
        _allTabs.Insert(0, tab);
        ReflowTabs();
        if (Tabs.Contains(tab))              ListTabs.SelectedItem = tab;
        else if (OverflowTabs.Contains(tab)) ListOverflowTabs.SelectedItem = tab;
    }

    // ── Multi-tab actions ──────────────────────────────────────────────────────

    private List<TabViewModel> GetMultiTargets()
    {
        // If ≥2 multi-selected, operate on those; otherwise fall back to the active tab
        if (_multiSelectedTabs.Count >= 2)
            return _multiSelectedTabs.OrderBy(t => _allTabs.IndexOf(t)).ToList();
        if (GetCurrentTabViewModel() is TabViewModel cur)
            return new List<TabViewModel> { cur };
        return new List<TabViewModel>();
    }

    public void MultiTab_Duplicate(object sender, RoutedEventArgs e)
    {
        MultiTabContextPopup.IsOpen = false;
        foreach (var tab in GetMultiTargets())
        {
            CreateNewTab(tab.Url);
            var newTab = _allTabs.LastOrDefault();
            if (newTab != null && newTab != tab)
            {
                int from = _allTabs.IndexOf(newTab);
                int to   = _allTabs.IndexOf(tab) + 1;
                if (from >= 0 && to <= _allTabs.Count && from != to)
                {
                    _allTabs.RemoveAt(from);
                    _allTabs.Insert(Math.Min(to, _allTabs.Count), newTab);
                }
            }
        }
        ReflowTabs();
    }

    public void MultiTab_Rename(object sender, RoutedEventArgs e)
    {
        MultiTabContextPopup.IsOpen = false;
        var targets = GetMultiTargets();
        if (targets.Count == 0) return;
        string? newTitle = ShowInputDialog("Rename All Selected Tabs",
            $"Enter a name for all {targets.Count} selected tabs:", targets[0].Title);
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        foreach (var tab in targets)
        {
            tab.Title = newTitle.Trim();
            tab.HasCustomTitle = true;
        }
    }

    public void MultiTab_BringToTop(object sender, RoutedEventArgs e)
    {
        MultiTabContextPopup.IsOpen = false;
        // Move all selected tabs to the front, preserving their relative order
        var targets = GetMultiTargets();
        foreach (var tab in targets) _allTabs.Remove(tab);
        for (int i = targets.Count - 1; i >= 0; i--)
            _allTabs.Insert(0, targets[i]);
        ReflowTabs();
    }

    public void MultiTab_SendToEnd(object sender, RoutedEventArgs e)
    {
        MultiTabContextPopup.IsOpen = false;
        var targets = GetMultiTargets();
        foreach (var tab in targets) _allTabs.Remove(tab);
        foreach (var tab in targets) _allTabs.Add(tab);
        ReflowTabs();
    }

    public void MultiTab_Group(object sender, RoutedEventArgs e)
    {
        MultiTabContextPopup.IsOpen = false;
        var targets = GetMultiTargets();
        if (targets.Count < 2) return;

        // Gather current group name if all tabs share one (prefix "[ name ] ")
        string? existingGroup = null;
        if (targets.All(t => t.Title.StartsWith("[") && t.Title.Contains("] ")))
        {
            var gname = targets[0].Title[1..targets[0].Title.IndexOf("] ")].Trim();
            if (targets.All(t => t.Title.StartsWith($"[{gname}] ")))
                existingGroup = gname;
        }

        string? groupName = ShowInputDialog("Group Tabs",
            $"Enter a group name for {targets.Count} tabs:",
            existingGroup ?? "Group");
        if (string.IsNullOrWhiteSpace(groupName)) return;

        // Move all grouped tabs together (right after the first one) then prefix titles
        var anchor = targets[0];
        int insertIdx = _allTabs.IndexOf(anchor) + 1;
        for (int i = 1; i < targets.Count; i++)
        {
            _allTabs.Remove(targets[i]);
            _allTabs.Insert(Math.Min(insertIdx++, _allTabs.Count), targets[i]);
        }

        foreach (var tab in targets)
        {
            // Strip any existing group prefix first
            string baseTitle = tab.Title;
            if (baseTitle.StartsWith("[") && baseTitle.Contains("] "))
                baseTitle = baseTitle[(baseTitle.IndexOf("] ") + 2)..];
            tab.Title = $"[{groupName.Trim()}] {baseTitle}";
            tab.HasCustomTitle = true;
        }

        ReflowTabs();
    }

    public void MultiTab_SleepAll(object sender, RoutedEventArgs e)
    {
        MultiTabContextPopup.IsOpen = false;
        foreach (var tab in GetMultiTargets().ToList())
            if (!tab.IsSleeping && !tab.NeverSleep) SleepTab(tab);
    }

    public void MultiTab_WakeAll(object sender, RoutedEventArgs e)
    {
        MultiTabContextPopup.IsOpen = false;
        foreach (var tab in GetMultiTargets().ToList())
            if (tab.IsSleeping) WakeTab(tab);
    }

    public void MultiTab_Cancel(object sender, RoutedEventArgs e)
        => MultiTabContextPopup.IsOpen = false;

    public void MultiTab_CloseAll(object sender, RoutedEventArgs e)
    {
        MultiTabContextPopup.IsOpen = false;
        var targets = GetMultiTargets().ToList();
        foreach (var tab in targets) CloseTab(tab);
    }

    private void ApplyKeyboardLayout(string langTag)
    {
        if (string.IsNullOrEmpty(langTag)) return;
        try
        {
            var mgr = System.Windows.Input.InputLanguageManager.Current;
            foreach (System.Globalization.CultureInfo ci in mgr.AvailableInputLanguages)
            {
                if (ci.IetfLanguageTag.Equals(langTag, StringComparison.OrdinalIgnoreCase) ||
                    ci.IetfLanguageTag.StartsWith(langTag + "-", StringComparison.OrdinalIgnoreCase) ||
                    ci.TwoLetterISOLanguageName.Equals(langTag, StringComparison.OrdinalIgnoreCase))
                {
                    mgr.CurrentInputLanguage = ci;
                    return;
                }
            }
        }
        catch { }
    }

    public void TabMenu_SetLanguage(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromContextMenuSender(sender);
        if (tab == null || sender is not MenuItem mi) return;
        string lang = mi.Tag?.ToString() ?? SettingsService.Current.DefaultLanguage;
        tab.Language = lang;

        // Apply immediately to the live browser view
        if (_tabViews.TryGetValue(tab, out var browser))
            _ = browser.ApplyLanguageAsync(lang);

        TrySwitchKeyboardLayout(lang);
    }

    /// <summary>
    /// Activates the Windows keyboard layout matching the given IETF language tag.
    /// Only switches if the language is installed as a Windows input language.
    /// </summary>
    private void TrySwitchKeyboardLayout(string languageTag)
    {
        try
        {
            if (string.IsNullOrEmpty(languageTag)) return;
            string rootTag = languageTag.Split('-')[0];
            foreach (System.Globalization.CultureInfo culture in
                System.Windows.Input.InputLanguageManager.Current.AvailableInputLanguages)
            {
                if (culture.TwoLetterISOLanguageName.Equals(rootTag, StringComparison.OrdinalIgnoreCase) ||
                    culture.IetfLanguageTag.Equals(languageTag, StringComparison.OrdinalIgnoreCase))
                {
                    var hkl = LoadKeyboardLayout(culture.LCID.ToString("X8"), 1); // KLF_ACTIVATE
                    if (hkl != IntPtr.Zero) ActivateKeyboardLayout(hkl, 0);
                    return;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Builds a language submenu populated from the user's installed Windows input languages,
    /// with any extras from a curated fallback list that aren't already installed.
    /// </summary>
    private List<MenuItem> BuildLanguageMenuItems()
    {
        // Returns parentless MenuItems — callers add them directly to avoid logical-parent conflicts.
        var installed = new List<(string DisplayName, string Tag)>();
        try
        {
            foreach (System.Globalization.CultureInfo lang in System.Windows.Input.InputLanguageManager.Current.AvailableInputLanguages)
            {
                string ietf = lang.IetfLanguageTag;
                string name = lang.DisplayName;
                if (!installed.Any(x => x.Tag == ietf))
                    installed.Add((name, ietf));
            }
        }
        catch { /* fallback to curated only */ }

        var curated = new (string Name, string Tag)[]
        {
            ("English",   "en"), ("Deutsch",   "de"), ("Français",  "fr"),
            ("Español",   "es"), ("Italiano",  "it"), ("Polski",    "pl"),
            ("Português", "pt"), ("Русский",   "ru"), ("中文",        "zh"),
            ("日本語",      "ja"), ("한국어",      "ko"), ("Български", "bg"),
            ("العربية",   "ar"), ("Türkçe",    "tr"), ("Nederlands","nl"),
        };

        var all = new List<(string DisplayName, string Tag)>(installed);
        foreach (var (name, tag) in curated)
        {
            if (!all.Any(x => x.Tag.StartsWith(tag, StringComparison.OrdinalIgnoreCase)))
                all.Add((name, tag));
        }

        all = all
            .OrderByDescending(x => installed.Any(y => y.Tag == x.Tag))
            .ThenBy(x => x.DisplayName)
            .ToList();

        var items = new List<MenuItem>();
        foreach (var (displayName, tag) in all)
        {
            var mi = new MenuItem { Header = displayName, Tag = tag };
            mi.Click += TabMenu_SetLanguage;
            items.Add(mi);
        }
        return items;
    }

    private void TabContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;

        // If ≥2 tabs are selected, suppress the normal menu and show the multi-tab popup instead
        if (_multiSelectedTabs.Count >= 2)
        {
            cm.IsOpen = false;
            OpenMultiTabContextPopup();
            return;
        }

        foreach (var itemObj in cm.Items)
        {
            if (itemObj is MenuItem miCheck)
            {
                if (miCheck.Tag?.ToString() == "multi")
                    miCheck.Visibility = _multiSelectedTabs.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
                if (miCheck.Tag?.ToString() == "__lang_parent__" && miCheck.Items.Count == 0)
                {
                    foreach (var child in BuildLanguageMenuItems())
                        miCheck.Items.Add(child);
                }
            }
        }

        // ── Sleep options (re-built every open so checkmarks/labels stay in sync) ─
        var sleepOldItems = cm.Items.OfType<FrameworkElement>()
                                    .Where(x => x.Tag?.ToString() == "__sleepopt__")
                                    .ToList();
        foreach (var r in sleepOldItems) cm.Items.Remove(r);

        var sleepTab = (cm.PlacementTarget as FrameworkElement)?.DataContext as TabViewModel;
        if (SettingsService.Current.SleepingTabsEnabled)
        {
            // Insert just before the "__lang_parent__" item so sleep options sit
            // between the standard tab actions and the language submenu.
            int langIdx = -1;
            for (int si = 0; si < cm.Items.Count; si++)
                if (cm.Items[si] is MenuItem smi && smi.Tag?.ToString() == "__lang_parent__")
                { langIdx = si; break; }
            int insertAt = langIdx >= 0 ? langIdx : cm.Items.Count;

            var miSleep = new MenuItem { Header = "💤 Sleep Settings", Tag = "__sleepopt__" };

            // ── Sleep Now ────────────────────────────────────────────────────────
            bool isAlreadySleeping = sleepTab?.IsSleeping == true;
            var miSleepNow = new MenuItem
            {
                Header    = isAlreadySleeping ? "☀️ Wake Tab Now" : "💤 Sleep Tab Now",
                IsEnabled = sleepTab != null && (isAlreadySleeping || (sleepTab?.NeverSleep == false && sleepTab?.IsPlayingAudio == false))
            };
            miSleepNow.Click += (s2, e2) =>
            {
                if (sleepTab == null) return;
                if (sleepTab.IsSleeping) WakeTab(sleepTab);
                else SleepTab(sleepTab);
            };
            miSleep.Items.Add(miSleepNow);
            miSleep.Items.Add(new Separator());

            // ── Never Sleep toggle ───────────────────────────────────────────────
            bool isNeverSleep = sleepTab?.NeverSleep == true;
            var miNever = new MenuItem
            {
                Header    = (isNeverSleep ? "✓ " : "    ") + "Never Sleep This Tab",
                IsEnabled = sleepTab != null
            };
            miNever.Click += (s2, e2) =>
            {
                if (sleepTab == null) return;
                sleepTab.NeverSleep = !sleepTab.NeverSleep;
                if (sleepTab.NeverSleep && sleepTab.IsSleeping) WakeTab(sleepTab);
            };
            miSleep.Items.Add(miNever);
            miSleep.Items.Add(new Separator());

            // ── Custom idle timeout ──────────────────────────────────────────────
            string idleLabel = sleepTab?.SleepIdleMinutesOverride.HasValue == true
                ? $"⏱ Idle Timeout: {sleepTab.SleepIdleMinutesOverride} min  (custom)"
                : $"⏱ Idle Timeout: {SettingsService.Current.SleepingTabsMinutes} min  (global)";
            var miIdle = new MenuItem { Header = idleLabel, IsEnabled = sleepTab != null };
            miIdle.Click += (s2, e2) =>
            {
                if (sleepTab == null) return;
                string? val = ShowInputDialog("Custom Idle Timeout",
                    "Minutes of idle time before this tab sleeps (1–240).\n" +
                    "Leave blank to revert to the global setting.",
                    sleepTab.SleepIdleMinutesOverride?.ToString()
                        ?? SettingsService.Current.SleepingTabsMinutes.ToString());
                if (val == null) return;
                if (string.IsNullOrWhiteSpace(val)) { sleepTab.SleepIdleMinutesOverride = null; return; }
                if (int.TryParse(val, out int mins) && mins >= 1 && mins <= 240)
                    sleepTab.SleepIdleMinutesOverride = mins;
            };
            miSleep.Items.Add(miIdle);

            // ── Custom RAM threshold ─────────────────────────────────────────────
            string ramLabel = sleepTab?.SleepRamThresholdMbOverride.HasValue == true
                ? $"📊 RAM Threshold: {sleepTab.SleepRamThresholdMbOverride} MB  (custom)"
                : $"📊 RAM Threshold: global  ({SLEEP_RAM_MODERATE_MB} MB)";
            var miRam = new MenuItem { Header = ramLabel, IsEnabled = sleepTab != null };
            miRam.Click += (s2, e2) =>
            {
                if (sleepTab == null) return;
                string? val = ShowInputDialog("Custom RAM Threshold",
                    "Sleep this tab only when app RAM (MB) is above this value.\n" +
                    "Leave blank to revert to the global setting.",
                    sleepTab.SleepRamThresholdMbOverride?.ToString() ?? SLEEP_RAM_MODERATE_MB.ToString());
                if (val == null) return;
                if (string.IsNullOrWhiteSpace(val)) { sleepTab.SleepRamThresholdMbOverride = null; return; }
                if (long.TryParse(val, out long rmb) && rmb >= 100 && rmb <= 32768)
                    sleepTab.SleepRamThresholdMbOverride = rmb;
            };
            miSleep.Items.Add(miRam);
            miSleep.Items.Add(new Separator());

            // ── Apply rule to a tab index range ──────────────────────────────────
            var miRange = new MenuItem
            {
                Header    = "↩ Apply Rule to Tab Range…",
                IsEnabled = sleepTab != null && _allTabs.Count > 1
            };
            miRange.Click += (s2, e2) =>
            {
                if (sleepTab == null) return;
                int total = _allTabs.Count;
                string? val = ShowInputDialog("Apply Rule to Tab Range",
                    $"Apply this tab's sleep settings to a range of tab positions.\n" +
                    $"Format: 'start-end' (e.g. '2-6'), or a single number.\n" +
                    $"Total tabs open: {total}",
                    $"1-{total}");
                if (string.IsNullOrWhiteSpace(val)) return;
                int rfrom = 1, rto = total;
                if (val.Contains('-'))
                {
                    var rp = val.Split('-');
                    if (rp.Length == 2
                        && int.TryParse(rp[0].Trim(), out int ra)
                        && int.TryParse(rp[1].Trim(), out int rb))
                    { rfrom = Math.Clamp(ra, 1, total); rto = Math.Clamp(rb, 1, total); }
                }
                else if (int.TryParse(val.Trim(), out int rs))
                    rfrom = rto = Math.Clamp(rs, 1, total);
                for (int ri = rfrom - 1; ri < rto && ri < _allTabs.Count; ri++)
                {
                    _allTabs[ri].NeverSleep                = sleepTab.NeverSleep;
                    _allTabs[ri].SleepIdleMinutesOverride  = sleepTab.SleepIdleMinutesOverride;
                    _allTabs[ri].SleepRamThresholdMbOverride = sleepTab.SleepRamThresholdMbOverride;
                }
            };
            miSleep.Items.Add(miRange);
            miSleep.Items.Add(new Separator());

            // ── Reset / Save ─────────────────────────────────────────────────────
            var miReset = new MenuItem { Header = "↺ Reset to Default", IsEnabled = sleepTab != null };
            miReset.Click += (s2, e2) =>
            {
                if (sleepTab == null) return;
                sleepTab.NeverSleep                = false;
                sleepTab.SleepIdleMinutesOverride  = null;
                sleepTab.SleepRamThresholdMbOverride = null;
                TabSleepRulesService.RemoveRule(sleepTab.Url);
            };
            miSleep.Items.Add(miReset);

            string? sleepDomain = null;
            try
            {
                if (sleepTab != null && Uri.TryCreate(sleepTab.Url, UriKind.Absolute, out var su))
                    sleepDomain = su.Host;
            }
            catch { }
            if (!string.IsNullOrEmpty(sleepDomain))
            {
                var miSave = new MenuItem { Header = $"💾 Save Rule for '{sleepDomain}'" };
                miSave.Click += (s2, e2) =>
                {
                    if (sleepTab == null) return;
                    TabSleepRulesService.SetRule(sleepTab.Url, new TabSleepRulesService.DomainRule
                    {
                        NeverSleep     = sleepTab.NeverSleep,
                        IdleMinutes    = sleepTab.SleepIdleMinutesOverride,
                        RamThresholdMb = sleepTab.SleepRamThresholdMbOverride
                    });
                };
                miSleep.Items.Add(miSave);
            }

            cm.Items.Insert(insertAt, miSleep);
        }

        // ── Media-tab options (re-built every open so labels stay in sync) ──────
        var toRemove = cm.Items.OfType<FrameworkElement>()
                                .Where(x => x.Tag?.ToString() == "__mediaopt__")
                                .ToList();
        foreach (var r in toRemove) cm.Items.Remove(r);

        var tab = (cm.PlacementTarget as FrameworkElement)?.DataContext as TabViewModel;
        if (tab?.HasEverPlayedAudio == true)
        {
            cm.Items.Add(new Separator { Tag = "__mediaopt__" });

            // Tab widening removed

            // ── Marquee toggle ──────────────────────────────────────────────────
            var miMarquee = new MenuItem
            {
                Header = tab.IsMarqueeEnabled ? "Disable Marquee Text"
                                              : "Enable Marquee Text",
                Tag    = "__mediaopt__"
            };
            miMarquee.Click += (s2, e2) => { tab.IsMarqueeEnabled = !tab.IsMarqueeEnabled; };
            cm.Items.Add(miMarquee);
        }
    }

    public void TabMenu_OpenMultiContext(object sender, RoutedEventArgs e)
        => OpenMultiTabContextPopup();

    private void OpenMultiTabContextPopup()
    {
        int count = Math.Max(_multiSelectedTabs.Count, 2);
        TxtMultiTabHeader.Text = $"{count} TABS SELECTED";
        MultiTabContextPopup.IsOpen = true;
    }

    private string? ShowInputDialog(string title, string prompt, string defaultValue = "")
    {
        var dlg    = new Window
        {
            Title                 = title,
            Width                 = 360,
            SizeToContent         = SizeToContent.Height,
            ResizeMode            = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner                 = this,
            Background            = System.Windows.Media.Brushes.Black,
            WindowStyle           = WindowStyle.ToolWindow
        };

        var panel  = new StackPanel { Margin = new Thickness(16) };
        var lbl    = new TextBlock { Text = prompt, Foreground = System.Windows.Media.Brushes.White,
                                     TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var box    = new TextBox  { Text = defaultValue, Background = new System.Windows.Media.SolidColorBrush(
                                        System.Windows.Media.Color.FromRgb(40, 40, 40)),
                                    Foreground = System.Windows.Media.Brushes.White,
                                    BorderThickness = new Thickness(1),
                                    BorderBrush = System.Windows.Media.Brushes.Gray,
                                    Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12) };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
                                      HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk  = new Button { Content = "OK",     Width = 70, Margin = new Thickness(0, 0, 8, 0),
                                   IsDefault = true };
        var btnCan = new Button { Content = "Cancel", Width = 70, IsCancel = true };

        string? result = null;
        btnOk.Click  += (s, e2) => { result = box.Text; dlg.DialogResult = true; };

        btnRow.Children.Add(btnOk);
        btnRow.Children.Add(btnCan);
        panel.Children.Add(lbl);
        panel.Children.Add(box);
        panel.Children.Add(btnRow);
        dlg.Content = panel;

        box.SelectAll();
        box.Focus();

        dlg.ShowDialog();
        return result;
    }

    private void MediaTab_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Hover tab widening removed
    }

    private void MediaTab_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Hover tab widening removed
    }

    private void MarqueeBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border && border.DataContext is TabViewModel tab)
        {
            if (border.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb)
            {
                // Marquee only if text is wider than the available space in the expanded tab
                // Available space ≈ expanded width (181) minus controls column (≈130) minus margins
                tab.IsMarqueeNeeded = tb.ActualWidth > border.ActualWidth;
            }
        }
    }

    private void HoverMarquee_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is TextBlock tb && tb.DataContext is TabViewModel tab)
        {
            if (VisualTreeHelper.GetParent(tb) is StackPanel sp &&
                VisualTreeHelper.GetParent(sp) is Border border)
            {
                tab.IsMarqueeNeeded = tb.ActualWidth > border.ActualWidth;
            }
        }
    }
    // Web App Install
    private void UpdateInstallButtonVisibility(string url)
    {
        bool canInstall =
            !string.IsNullOrEmpty(url) &&
            (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             url.StartsWith("http://",  StringComparison.OrdinalIgnoreCase));

        BtnInstallWebApp.Visibility = canInstall ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnInstallWebApp_Click(object sender, RoutedEventArgs e)
    {
        var browser = CurrentBrowser;
        if (browser?.MainWebView?.CoreWebView2 == null) return;

        string url   = browser.MainWebView.Source?.ToString() ?? "";
        string title = browser.MainWebView.CoreWebView2.DocumentTitle;
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http")) return;

        try
        {
            string host = new Uri(url).Host.Replace("www.", "");
            string appName = !string.IsNullOrWhiteSpace(title)
                ? title.Split(new char[]{'-','|'})[0].Trim()
                : host;
            if (appName.Length > 40) appName = appName.Substring(0, 40).Trim();

            string appsRoot = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Horizon_Browser", "WebApps");
            System.IO.Directory.CreateDirectory(appsRoot);

            string horizonExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeCandidate = System.IO.Path.ChangeExtension(horizonExe, ".exe");
            if (!System.IO.File.Exists(exeCandidate)) exeCandidate = horizonExe;

            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string lnkPath    = System.IO.Path.Combine(desktopDir, appName + ".lnk");
            string workDir    = System.IO.Path.GetDirectoryName(exeCandidate) ?? "";

            // Write a .ps1 script to a temp file and run it.
            // This avoids all command-line quoting issues with special chars in paths.
            string ps1 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "horizon_webapp_" + Guid.NewGuid().ToString("N") + ".ps1");
            string psLines =
                "$ws = New-Object -ComObject WScript.Shell" + Environment.NewLine +
                "$s  = $ws.CreateShortcut(@'" + Environment.NewLine + lnkPath + Environment.NewLine + "'@)" + Environment.NewLine +
                "$s.TargetPath = @'" + Environment.NewLine + exeCandidate + Environment.NewLine + "'@" + Environment.NewLine +
                "$s.Arguments = @'" + Environment.NewLine + url + Environment.NewLine + "'@" + Environment.NewLine +
                "$s.WorkingDirectory = @'" + Environment.NewLine + workDir + Environment.NewLine + "'@" + Environment.NewLine +
                "$s.Save()";
            System.IO.File.WriteAllText(ps1, psLines, System.Text.Encoding.UTF8);

            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + ps1 + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);
            try { System.IO.File.Delete(ps1); } catch { }

            bool ok = System.IO.File.Exists(lnkPath);
            string msg = ok
                ? "'" + appName + "' installed as a Web App!\n\nShortcut added to Desktop."
                : "Web App files saved to:\n" + appsRoot + "\n\nNote: Desktop shortcut could not be created.";

            MessageBox.Show(msg, "Web App Installed", MessageBoxButton.OK, MessageBoxImage.Information);
            LogService.Write("WEBAPP", "Installed: " + appName + " -> " + url);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "InstallWebApp");
            MessageBox.Show("Failed to install Web App: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HEADER WIDGET
    // ══════════════════════════════════════════════════════════════════════════

    private void InitWidget()
    {
        var modes = SettingsService.Current.WidgetModes;
        int mediaIdx = modes.IndexOf("Media");
        if (mediaIdx >= 0) { modes[mediaIdx] = "Music"; SettingsService.Save(); }

        SyncWidgetCheckboxes();
        SliderWidgetCycle.Value    = SettingsService.Current.WidgetCycleSecs;
        TxtWidgetCycleSecs.Text    = SettingsService.Current.WidgetCycleSecs == 0
                                     ? "Off" : $"{SettingsService.Current.WidgetCycleSecs}s";
        TxtWeatherCity.Text        = SettingsService.Current.WidgetWeatherCity;
        PnlWeatherCity.Visibility  = SettingsService.Current.WidgetModes.Contains("Weather")
                                     ? Visibility.Visible : Visibility.Collapsed;

        _clockTimer.Tick += (s, e) => RefreshWidgetDisplay();
        _clockTimer.Start();
        _clockMode = SettingsService.Current.ClockMode;

        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        _cpuLastTotal = proc.TotalProcessorTime;
        _cpuLastCheck = DateTime.UtcNow;

        if (SettingsService.Current.WidgetModes.Contains("Weather") &&
            !string.IsNullOrWhiteSpace(SettingsService.Current.WidgetWeatherCity))
        {
            string _initCity = SettingsService.Current.WidgetWeatherCity;
            _ = Task.Delay(1800).ContinueWith(_ =>
                Dispatcher.Invoke(() => _ = FetchWeatherAsync(_initCity)));
            StartWeatherRefreshTimer(_initCity);
        }

        RestartWidgetCycleTimer();
        RefreshWidgetDisplay();

        _videoWidgetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _videoWidgetTimer.Tick += VideoWidgetTimer_Tick;
        _videoWidgetTimer.Start();
    }
    
    private async void VideoWidgetTimer_Tick(object? sender, EventArgs e)
    {
        string mode = CurrentWidgetMode();
        bool isMedia = mode is "Media" or "Music" or "Video";

        if (!isMedia)
        {
            WidgetVideoImage.Visibility = Visibility.Collapsed;
            return;
        }

        var vidTab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio && t.HasVideo && !t.IsAudioOnlyMode);

        if (vidTab != null)
        {
            if (_tabViews.TryGetValue(vidTab, out var vidBv) && vidBv.MainWebView?.CoreWebView2 != null)
            {
                try
                {
                    const string vidScript = @"(() => {
                        const yt = document.querySelector('h1.style-scope.ytd-watch-metadata');
                        if (yt) return yt.innerText;
                        const og = document.querySelector('meta[property=""og:title""]');
                        if (og) return og.content;
                        return document.title || null;
                    })()";
                    string vidRaw = await vidBv.MainWebView.CoreWebView2.ExecuteScriptAsync(vidScript);
                    if (!string.IsNullOrEmpty(vidRaw) && vidRaw != "null")
                    {
                        string vidParsed = System.Text.Json.JsonSerializer.Deserialize<string>(vidRaw) ?? "";
                        if (!string.IsNullOrEmpty(vidParsed)) vidTab.MediaTitle = vidParsed;
                    }
                }
                catch { }
            }
            string currentTitle = vidTab.CleanMediaTitle ?? vidTab.Title;
            WidgetVideoImage.Visibility = Visibility.Collapsed;
            TxtWidget.Visibility        = Visibility.Visible;
            WidgetVizCanvas.Visibility  = Visibility.Collapsed;
            TxtWidget.ToolTip           = currentTitle;
            return;
        }

        // Refresh MediaTitle for audio-only tabs — title changes on services like Spotify
        // don't reliably fire DocumentTitleChanged, so we poll here every 4 s.
        var _aoTab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio && t.IsAudioOnlyMode);
        if (_aoTab != null && _tabViews.TryGetValue(_aoTab, out var _aoBv) && _aoBv.MainWebView?.CoreWebView2 != null)
        {
            try
            {
                const string _aoScript = @"(() => {
                    const m = document.querySelector('video, audio');
                    if (m && m.title) return m.title;
                    const yt = document.querySelector('h1.style-scope.ytd-watch-metadata');
                    if (yt) return yt.innerText;
                    const og = document.querySelector('meta[property=""og:title""]');
                    if (og) return og.content;
                    return document.title || null;
                })()";
                string _aoRaw = await _aoBv.MainWebView.CoreWebView2.ExecuteScriptAsync(_aoScript);
                if (!string.IsNullOrEmpty(_aoRaw) && _aoRaw != "null")
                {
                    string _aoParsed = System.Text.Json.JsonSerializer.Deserialize<string>(_aoRaw) ?? "";
                    if (!string.IsNullOrEmpty(_aoParsed)) _aoTab.MediaTitle = _aoParsed;
                }
            }
            catch { }
        }

        WidgetVideoImage.Visibility = Visibility.Collapsed;
        TxtWidget.Visibility        = Visibility.Visible;
        var audTab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio);
        TxtWidget.ToolTip = audTab != null
            ? (!string.IsNullOrEmpty(audTab.CleanMediaTitle) ? audTab.CleanMediaTitle : audTab.Title)
            : null;
    }
    
    private void SyncWidgetCheckboxes()
    {
        var m = SettingsService.Current.WidgetModes;

        // Enabled widgets appear first in their saved cycle order; disabled ones follow in canonical order
        var enabled  = m.Where(k => _allWidgetDefs.Any(d => d.Key == k)).ToList();
        var disabled = _allWidgetDefs.Where(d => !enabled.Contains(d.Key)).Select(d => d.Key);

        LstWidgetOrder.Items.Clear();
        foreach (var key in enabled.Concat(disabled))
        {
            var entry    = _allWidgetDefs.FirstOrDefault(d => d.Key == key);
            string label = entry.Label ?? key;
            bool isOn    = m.Contains(key) || (key == "Media" && (m.Contains("Music") || m.Contains("Video")));
            LstWidgetOrder.Items.Add(BuildWidgetListItem(key, label, isOn));
        }

        PnlWeatherCity.Visibility = m.Contains("Weather") ? Visibility.Visible : Visibility.Collapsed;
        ChkWidgetDisableCycle.IsChecked = SettingsService.Current.WidgetDisableCycle;
        PnlWidgetCycle.Visibility = (m.Count > 1 && !SettingsService.Current.WidgetDisableCycle)
                                    ? Visibility.Visible : Visibility.Collapsed;
    }

    private ListBoxItem BuildWidgetListItem(string key, string label, bool isChecked)
    {
        var handle = new TextBlock
        {
            Text              = "≡",
            Foreground        = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize          = 14,
            Margin            = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor            = Cursors.SizeNS,
            ToolTip           = "Drag to reorder"
        };

        var cb = new CheckBox
        {
            Content           = label,
            Tag               = key,
            IsChecked         = isChecked,
            Foreground        = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cb.Click += WidgetModeCheck_Click;

        var grid = new Grid { Margin = new Thickness(2, 2, 2, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(handle, 0);
        Grid.SetColumn(cb, 1);
        grid.Children.Add(handle);
        grid.Children.Add(cb);

        return new ListBoxItem { Content = grid, Tag = key, Background = Brushes.Transparent };
    }

    // Persist the current list order → WidgetModes
    private void SaveWidgetOrder()
    {
        var modes = SettingsService.Current.WidgetModes;
        modes.Clear();
        foreach (ListBoxItem lbi in LstWidgetOrder.Items)
            if (lbi.Content is Grid g &&
                g.Children.OfType<CheckBox>().FirstOrDefault() is CheckBox cb &&
                cb.IsChecked == true && cb.Tag is string key)
                modes.Add(key);

        if (modes.Count == 0) modes.Add("Clock");
        if (_widgetModeIndex >= modes.Count) _widgetModeIndex = 0;
        SettingsService.Save();
        RestartWidgetCycleTimer();
        RefreshWidgetDisplay();
        PnlWidgetCycle.Visibility = (modes.Count > 1 && !SettingsService.Current.WidgetDisableCycle)
                                    ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnWidgetMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (LstWidgetOrder.SelectedItem is not ListBoxItem lbi) return;
        int idx = LstWidgetOrder.Items.IndexOf(lbi);
        if (idx <= 0) return;
        LstWidgetOrder.Items.Remove(lbi);
        LstWidgetOrder.Items.Insert(idx - 1, lbi);
        LstWidgetOrder.SelectedItem = lbi;
        LstWidgetOrder.ScrollIntoView(lbi);
        SaveWidgetOrder();
    }

    private void BtnWidgetMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (LstWidgetOrder.SelectedItem is not ListBoxItem lbi) return;
        int idx = LstWidgetOrder.Items.IndexOf(lbi);
        if (idx >= LstWidgetOrder.Items.Count - 1) return;
        LstWidgetOrder.Items.Remove(lbi);
        LstWidgetOrder.Items.Insert(idx + 1, lbi);
        LstWidgetOrder.SelectedItem = lbi;
        LstWidgetOrder.ScrollIntoView(lbi);
        SaveWidgetOrder();
    }

    // ── Widget list drag-drop reorder ─────────────────────────────────────────

    private void WidgetList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _widgetDragOrigin = e.GetPosition(null);
        _widgetDragging   = false;
    }

    private void WidgetList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _widgetDragging) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _widgetDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _widgetDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (LstWidgetOrder.SelectedItem is ListBoxItem lbi)
        {
            _widgetDragging = true;
            DragDrop.DoDragDrop(LstWidgetOrder, new DataObject(typeof(ListBoxItem), lbi), DragDropEffects.Move);
            _widgetDragging = false;
        }
    }

    private void WidgetList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ListBoxItem)) is not ListBoxItem dragged) return;
        var target = WidgetListFindAncestorItem((DependencyObject)e.OriginalSource);
        if (target == null || ReferenceEquals(target, dragged)) return;

        int fromIdx = LstWidgetOrder.Items.IndexOf(dragged);
        int toIdx   = LstWidgetOrder.Items.IndexOf(target);
        if (fromIdx < 0 || toIdx < 0) return;

        LstWidgetOrder.Items.Remove(dragged);
        LstWidgetOrder.Items.Insert(toIdx, dragged);
        LstWidgetOrder.SelectedItem = dragged;
        SaveWidgetOrder();
    }

    private static ListBoxItem? WidgetListFindAncestorItem(DependencyObject? obj)
    {
        while (obj != null)
        {
            if (obj is ListBoxItem lbi) return lbi;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private void RestartWidgetCycleTimer()
    {
        _widgetCycleTimer?.Stop();
        _widgetCycleTimer = null;
        if (SettingsService.Current.WidgetDisableCycle) return; // user disabled auto-cycling
        if (SettingsService.Current.WidgetModes.Count <= 1) return;
        int secs = SettingsService.Current.WidgetCycleSecs;
        if (secs <= 0) return; // 0 = auto-cycling disabled
        _widgetCycleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
        _widgetCycleTimer.Tick += (s, e) => CycleWidgetMode(+1);
        _widgetCycleTimer.Start();
    }

    private void CycleWidgetMode(int direction)
    {
        var modes = SettingsService.Current.WidgetModes;
        if (modes.Count == 0) return;
        _widgetModeIndex = (_widgetModeIndex + direction + modes.Count) % modes.Count;
        FadeWidgetContent(GetWidgetText());
    }

    private string CurrentWidgetMode()
    {
        var m = SettingsService.Current.WidgetModes;
        if (m.Count == 0) return "Clock";
        return m[Math.Clamp(_widgetModeIndex, 0, m.Count - 1)];
    }

    private string GetWidgetText() => CurrentWidgetMode() switch
    {
        "Clock"      => GetClockText(),
        "CPU"        => $"CPU  {GetCpuPercent():F1}%",
        "RAM"        => $"RAM  {GetCachedRamMb()} MB",
        "Media"      => GetWidgetMediaText(),
        "Music"      => GetWidgetMediaText(),   // legacy alias
        "Weather"    => _weatherCache,
        "Notes"      => WidgetTruncate(SettingsService.Current.WidgetNotes, "📝 (empty)"),
        "Calculator" => "🧮  Calculator",
        "Converter"  => "⇄   Converter",
        "Calendar"   => DateTime.Now.ToString("dd MMM"),
        "Navigation" => GetNavWidgetText(),
        "Notifications" => GetNotificationsWidgetText(),
        _            => "--"
    };

    private string GetNotificationsWidgetText()
    {
        int unread = Services.NotificationCenterService.UnreadCount;
        if (unread == 0) return "🔔  --";
        var latest = Services.NotificationCenterService.History.Count > 0
            ? Services.NotificationCenterService.History[0]
            : null;
        string label = latest != null ? WidgetTruncate(latest.Title, "New") : "New";
        return unread > 1 ? $"🔔  {label}  (+{unread - 1})" : $"🔔  {label}";
    }

    private void InitNotificationsWidget()
    {
        Services.NotificationCenterService.NotificationAdded += entry =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                RefreshWidgetDisplay();
                ShowNotificationToast(entry);
            });
        };
    }

    private void ShowNotificationToast(Services.NotificationEntry entry)
    {
        try
        {
            var toast = new Views.NotificationToast(entry.Origin, entry.Title, entry.Body);
            toast.Show();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ShowNotificationToast");
        }
    }

    private void OpenNotificationsListPopup()
    {
        var win = MakeToolWindow("Notifications", 320);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 480
        };
        var root = new StackPanel { Margin = new Thickness(12) };
        scroll.Content = root;
        win.Content = scroll;

        root.Children.Add(SectionLabel("NOTIFICATIONS"));

        if (Services.NotificationCenterService.History.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "No notifications yet",
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                Margin = new Thickness(4, 8, 4, 8)
            });
        }
        else
        {
            foreach (var n in Services.NotificationCenterService.History)
            {
                var item = new StackPanel { Margin = new Thickness(4, 4, 4, 8) };
                item.Children.Add(new TextBlock
                {
                    Text = $"{n.Origin}  ·  {n.Time:HH:mm}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
                });
                item.Children.Add(new TextBlock
                {
                    Text = n.Title,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });
                if (!string.IsNullOrEmpty(n.Body))
                {
                    item.Children.Add(new TextBlock
                    {
                        Text = n.Body,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                root.Children.Add(item);
            }
        }

        var clearBtn = MenuButton("🗑  Clear all", false);
        clearBtn.Click += (s, e) =>
        {
            Services.NotificationCenterService.Clear();
            RefreshWidgetDisplay();
            win.Close();
        };
        root.Children.Add(clearBtn);

        Services.NotificationCenterService.MarkAllRead();
        RefreshWidgetDisplay();

        win.Show();
    }

    // Combined media text: prefers video then audio
    private long GetCachedRamMb()
    {
        if ((DateTime.UtcNow - _ramCacheTime).TotalSeconds >= 5)
        {
            _cachedRamMb  = GetProcessTreeWorkingSetBytes() / 1048576;
            _ramCacheTime = DateTime.UtcNow;
        }
        return _cachedRamMb;
    }

    private string GetWidgetMediaText()
    {
        // Video playing?
        var vidTab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio && t.HasVideo && !t.IsAudioOnlyMode)
                  ?? _allTabs.FirstOrDefault(t => t.HasVideo && !t.IsAudioOnlyMode);
        if (vidTab != null)
            return WidgetTruncate("🎬 " + (vidTab.CleanMediaTitle ?? vidTab.Title), "🎬 No Video");

        // Audio only?
        var audTab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio && (!t.HasVideo || t.IsAudioOnlyMode) && !string.IsNullOrEmpty(t.CleanMediaTitle))
                  ?? _allTabs.FirstOrDefault(t => (!t.HasVideo || t.IsAudioOnlyMode) && !string.IsNullOrEmpty(t.CleanMediaTitle));
        if (audTab != null)
            return WidgetTruncate("🎵 " + audTab.CleanMediaTitle, "🎵 No Audio");

        return "🎵 No Media";
    }

    private static string WidgetTruncate(string text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        string first = text.Split('\n')[0].Trim();
        return first.Length > 18 ? first[..15] + "…" : first;
    }

    private void RefreshWidgetDisplay()
    {
        string mode = CurrentWidgetMode();
        WidgetVizCanvas.Visibility = Visibility.Collapsed;

        bool wantMarquee = (mode == "Weather" && SettingsService.Current.WeatherWidgetMarquee)
                        || ((mode == "Media" || mode == "Music") && SettingsService.Current.MediaWidgetMarquee);

        if (!wantMarquee)
        {
            if (_isMarqueeRunning) StopWidgetMarquee();
            if (!_widgetFading) TxtWidget.Text = GetWidgetText();
        }
        else
        {
            string marqueeText = mode == "Weather" ? _weatherCache : GetWidgetMediaTextFull();
            if (!_isMarqueeRunning || TxtWidget.Text != marqueeText)
            {
                StopWidgetMarquee();
                if (!_widgetFading) StartWidgetMarquee(marqueeText);
            }
        }

        if (TxtWidgetSub != null)
            TxtWidgetSub.Text = mode == "Clock" ? GetClockSubText() : "";
        if (mode == "Weather" && _weatherWmoCode >= 0)
            StartWeatherWidgetAnimation();
        else if (mode != "Weather")
            StopWeatherWidgetAnimation();

        if (mode == "Weather")
        {
            if (SettingsService.Current.WeatherWidgetMarquee || SettingsService.Current.WeatherWidgetHoverWiden)
                AnimateWidgetWidth(_widgetDefaultWidth);
            else
                UpdateWeatherWidgetWidth();
        }
        else
            AnimateWidgetWidth(_widgetDefaultWidth);
    }

    // ── Mini visualizer bars drawn directly onto WidgetVizCanvas ─────────────
    private double _sidebarScrollTarget = 0.0;
    private double _widgetVizPhase = 0.0;

    private void DrawWidgetVisualizer()
    {
        WidgetVizCanvas.Children.Clear();
        double w = WidgetVizCanvas.ActualWidth;
        double h = WidgetVizCanvas.ActualHeight;
        if (w < 4 || h < 4) return;

        // Pull palette from active audio tab if available
        var audTab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio);
        var palette = audTab?.PaletteColors;

        _widgetVizPhase += 0.08;
        const int bars = 18;
        double barW = w / bars - 1;

        for (int i = 0; i < bars; i++)
        {
            double amp = (Math.Sin(_widgetVizPhase + i * 0.55) + 1.0) * 0.5
                       * (0.55 + 0.45 * Math.Sin(_widgetVizPhase * 1.3 + i * 0.3));
            double barH = Math.Max(3, amp * (h - 4));
            double x    = i * (barW + 1);
            double y    = h - barH;

            System.Windows.Media.Color col;
            if (palette != null && palette.Count >= 2)
            {
                int pi = i % palette.Count;
                col = ModulateVisualizerColor(palette[pi], amp);
            }
            else
            {
                byte g = (byte)(60 + amp * 195);
                col = System.Windows.Media.Color.FromRgb(0, g, (byte)(g / 3));
            }

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(1, barW),
                Height = barH,
                Fill = new SolidColorBrush(col),
                RadiusX = 1, RadiusY = 1
            };
            System.Windows.Controls.Canvas.SetLeft(rect, x);
            System.Windows.Controls.Canvas.SetTop(rect, y);
            WidgetVizCanvas.Children.Add(rect);
        }
    }

    private void FadeWidgetContent(string newText)
    {
        if (_widgetFading) { TxtWidget.Text = newText; return; }
        _widgetFading = true;
        var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(300)));
        fadeOut.Completed += (s, e) =>
        {
            TxtWidget.Text = newText;
            var fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(300)));
            fadeIn.Completed += (s2, e2) => _widgetFading = false;
            TxtWidget.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        TxtWidget.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private double GetCpuPercent()
    {
        var now  = DateTime.UtcNow;
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        double elapsedMs = (now - _cpuLastCheck).TotalMilliseconds;
        if (elapsedMs < 300) return _cpuLastPct;
        double cpuMs   = (proc.TotalProcessorTime - _cpuLastTotal).TotalMilliseconds;
        _cpuLastCheck  = now;
        _cpuLastTotal  = proc.TotalProcessorTime;
        _cpuLastPct    = Math.Clamp(
            Math.Round(cpuMs / elapsedMs / Environment.ProcessorCount * 100.0, 1), 0, 100);
        return _cpuLastPct;
    }


    // ── Weather: shared HTTP client + resilient cache ─────────────────────────
    private static readonly HttpClient _widgetHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string _weatherLastGood = "";
    private DispatcherTimer? _weatherRefreshTimer;

    private void StartWeatherRefreshTimer(string city)
    {
        _weatherRefreshTimer?.Stop();
        _weatherWidgetAnimTimer?.Stop();
        _weatherRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _weatherRefreshTimer.Tick += (s, e) => _ = FetchWeatherAsync(city);
        _weatherRefreshTimer.Start();
    }

    private async Task FetchWeatherAsync(string city)
    {
        // ── Provider 1: wttr.in (compact format) ─────────────────────────────
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string raw = await _widgetHttp.GetStringAsync(
                    $"https://wttr.in/{Uri.EscapeDataString(city)}?format=3");
                raw = raw.Trim();
                if (raw.Length > 0 && !raw.StartsWith("<") && !raw.StartsWith("Unknown"))
                {
                    _weatherCache    = raw;
                    _weatherLastGood = _weatherCache;
                    Dispatcher.Invoke(RefreshWidgetDisplay);
                    return;
                }
            }
            catch { if (attempt == 0) await Task.Delay(2500); }
        }

        // ── Provider 2: Open-Meteo (free, no key, very reliable) ─────────────
        try
        {
            // Step 1 — geocode
            string geoJson = await _widgetHttp.GetStringAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json");
            using var geoDoc = JsonDocument.Parse(geoJson);
            if (geoDoc.RootElement.TryGetProperty("results", out var geoResults) && geoResults.GetArrayLength() > 0)
            {
                var loc     = geoResults[0];
                double lat  = loc.GetProperty("latitude").GetDouble();
                double lon  = loc.GetProperty("longitude").GetDouble();
                string name = loc.TryGetProperty("name", out var nm) ? nm.GetString() ?? city : city;

                // Step 2 — current conditions
                string wxJson = await _widgetHttp.GetStringAsync(
                    $"https://api.open-meteo.com/v1/forecast" +
                    $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                    $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
                    $"&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,relative_humidity_2m" +
                    $"&wind_speed_unit=kmh&timezone=auto&forecast_days=1");
                using var wxDoc  = JsonDocument.Parse(wxJson);
                var cur          = wxDoc.RootElement.GetProperty("current");
                double temp      = cur.GetProperty("temperature_2m").GetDouble();
                int    wmoCode   = cur.GetProperty("weather_code").GetInt32();
                string icon      = WmoCodeToIcon(wmoCode);
                string result    = $"{name}: {icon} {temp:F0}°C";
                _weatherCache    = result;
                _weatherLastGood = _weatherCache;
                _weatherWmoCode  = wmoCode;
                Dispatcher.Invoke(() => { RefreshWidgetDisplay(); UpdateWeatherWidgetAnimation(); });
                return;
            }
        }
        catch { }

        // ── Provider 3: wttr.in JSON fallback (different endpoint) ───────────
        try
        {
            string raw2 = await _widgetHttp.GetStringAsync(
                $"https://wttr.in/{Uri.EscapeDataString(city)}?format=%l:+%C+%t");
            raw2 = raw2.Trim();
            if (raw2.Length > 0 && !raw2.StartsWith("<") && !raw2.StartsWith("Unknown"))
            {
                _weatherCache    = raw2;
                _weatherLastGood = _weatherCache;
                Dispatcher.Invoke(RefreshWidgetDisplay);
                return;
            }
        }
        catch { }

        // ── All providers failed ──────────────────────────────────────────────
        _weatherCache = !string.IsNullOrEmpty(_weatherLastGood) ? _weatherLastGood + " ⚠" : "⛅ N/A";
        Dispatcher.Invoke(() =>
        {
            if (CurrentWidgetMode() == "Weather") FadeWidgetContent(_weatherCache);
        });
    }

    private static string WmoCodeToDescription(int code) => code switch
    {
        0                         => "Clear sky",
        1                         => "Mainly clear",
        2                         => "Partly cloudy",
        3                         => "Overcast",
        45 or 48                  => "Fog",
        51 or 53 or 55            => "Drizzle",
        61                        => "Slight rain",
        63                        => "Moderate rain",
        65                        => "Heavy rain",
        71                        => "Slight snow",
        73                        => "Moderate snow",
        75                        => "Heavy snow",
        77                        => "Snow grains",
        80 or 81 or 82            => "Rain showers",
        85 or 86                  => "Snow showers",
        95                        => "Thunderstorm",
        96 or 99                  => "Thunderstorm + hail",
        _                         => "Unknown"
    };

    private static string WindDirectionToString(int degrees)
    {
        string[] compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return compass[((degrees + 22) / 45) % 8];
    }

    private static string WmoCodeToIcon(int code) => code switch
    {
        0                         => "☀️",
        1 or 2                    => "🌤",
        3                         => "☁️",
        45 or 48                  => "🌫",
        51 or 53 or 55            => "🌦",
        61 or 63 or 65            => "🌧",
        71 or 73 or 75 or 77      => "❄️",
        80 or 81 or 82            => "🌧",
        85 or 86                  => "🌨",
        95                        => "⛈",
        96 or 99                  => "⛈",
        _                         => "⛅"
    };

    private static System.Windows.Media.Color WmoCodeToIconColor(int code) => code switch
    {
        0                                            => Color.FromRgb(0xff, 0xcc, 0x00), // sunny gold
        1 or 2                                       => Color.FromRgb(0x88, 0xcc, 0xff), // partly-cloudy sky blue
        3                                            => Color.FromRgb(0xaa, 0xaa, 0xcc), // overcast muted blue-grey
        45 or 48                                     => Color.FromRgb(0xcc, 0xcc, 0xaa), // fog warm grey
        51 or 53 or 55                               => Color.FromRgb(0x77, 0xcc, 0xff), // drizzle light blue
        61 or 63 or 65 or 80 or 81 or 82             => Color.FromRgb(0x44, 0x99, 0xff), // rain blue
        71 or 73 or 75 or 77 or 85 or 86             => Color.FromRgb(0xcc, 0xee, 0xff), // snow icy white-blue
        95 or 96 or 99                               => Color.FromRgb(0xcc, 0x88, 0xff), // thunderstorm violet
        _                                            => Color.FromRgb(0x88, 0xbb, 0xdd),
    };

    // Returns a color-emoji-font TextBlock for a WMO weather icon
    private static TextBlock WeatherIconBlock(int wmoCode, double fontSize, Thickness? margin = null) =>
        new TextBlock
        {
            Text             = WmoCodeToIcon(wmoCode),
            FontSize         = fontSize,
            FontFamily       = new FontFamily("Segoe UI Emoji"),
            Foreground       = new SolidColorBrush(WmoCodeToIconColor(wmoCode)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin           = margin ?? new Thickness(0),
        };

    // ── Current-conditions row helpers ────────────────────────────────────────
    private static string WxHumidityComfort(int h) =>
        h < 25 ? "Very dry — may irritate airways" :
        h < 45 ? "Comfortable" :
        h < 65 ? "Slightly humid — comfortable" :
        h < 80 ? "Humid — may feel muggy" : "Very humid — oppressive";

    private static string WxBeaufort(double kmh) =>
        kmh < 2   ? "Beaufort 0 — Calm" :
        kmh < 12  ? "Beaufort 1-2 — Light air / breeze" :
        kmh < 29  ? "Beaufort 3-4 — Gentle to moderate breeze" :
        kmh < 50  ? "Beaufort 5-6 — Fresh to strong breeze" :
        kmh < 75  ? "Beaufort 7-8 — Near gale / Gale" :
        kmh < 103 ? "Beaufort 9-10 — Severe / Storm" : "Beaufort 11-12 — Violent storm / Hurricane";

    private static string WxPressureTip(double hpa) =>
        hpa > 1022 ? "High pressure — fair weather likely" :
        hpa < 1000 ? "Low pressure — rain / unsettled likely" : "Normal pressure";

    private static string WxUvLabel(double uv) =>
        uv < 3  ? $"UV {uv:F0}  Low" :
        uv < 6  ? $"UV {uv:F0}  Moderate" :
        uv < 8  ? $"UV {uv:F0}  High" :
        uv < 11 ? $"UV {uv:F0}  Very High" : $"UV {uv:F0}  Extreme";

    private static string WxUvProtection(double uv) =>
        uv < 3  ? "No protection needed" :
        uv < 6  ? "Wear sunscreen SPF 30+" :
        uv < 8  ? "SPF 50+, hat recommended" :
                  "Avoid direct sun 10–16h — full protection";

    // Builds the Now-tab structured rows from cached structured fields
    private IEnumerable<UIElement> BuildCurrentConditionsRows()
    {
        if (_weatherWmoCode < 0 || string.IsNullOrEmpty(_cachedWxLoc))
        {
            yield return new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_weatherDetailCache) ? _weatherCache : _weatherDetailCache,
                Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap
            };
            yield break;
        }

        var defs = new (string iconStr, int? wmo, string label, string value, string detail)[]
        {
            ("📍", null, "",             _cachedWxLoc, ""),
            (WmoCodeToIcon(_weatherWmoCode), _weatherWmoCode, "Condition",
                WmoCodeToDescription(_weatherWmoCode), ""),
            ("🌡", null, "Temperature",
                $"{_cachedWxTemp:F1}°C  (feels {_cachedWxFeelsLike:F1}°C)",
                $"Hi {_cachedWxTempMax:F1}°C  /  Lo {_cachedWxTempMin:F1}°C"),
            ("💧", null, "Humidity",
                $"{_cachedWxHumidity}%",
                WxHumidityComfort(_cachedWxHumidity)),
            ("🌬", null, "Wind",
                $"{_cachedWxWindSpd:F0} km/h {WindDirectionToString(_cachedWxWindDir)}" +
                    (_cachedWxWindGust > _cachedWxWindSpd + 5 ? $"  (gusts {_cachedWxWindGust:F0})" : ""),
                WxBeaufort(_cachedWxWindSpd)),
            ("🌧", null, "Precip",
                $"{_cachedWxPrecip:F1} mm  ·  {_cachedWxDailyPrecip:F1} mm today",
                $"Rain chance: {_cachedWxRainChance:F0}%"),
            ("🔍", null, "Visibility",
                _cachedWxVisM >= 1000 ? $"{_cachedWxVisM / 1000.0:F1} km" : $"{_cachedWxVisM:F0} m",
                ""),
            ("⏱", null, "Pressure",
                $"{_cachedWxPressure:F0} hPa",
                WxPressureTip(_cachedWxPressure)),
            ("☀", null, "UV",
                WxUvLabel(_cachedWxUv),
                WxUvProtection(_cachedWxUv)),
            ("🌅", null, "Sunrise",
                _cachedWxSunrise,
                $"Sunset: {_cachedWxSunset}"),
        };

        bool alt = false;
        foreach (var (iconStr, wmo, label, value, detail) in defs)
        {
            bool hasDetail = !string.IsNullOrEmpty(detail);
            var bgBase = new SolidColorBrush(alt
                ? Color.FromRgb(0x0a, 0x12, 0x22)
                : Color.FromRgb(0x10, 0x1e, 0x34));
            alt = !alt;

            var row = new Border
            {
                Background      = bgBase,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1a, 0x2e, 0x4a)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(6, 5, 6, 5),
                Cursor          = hasDetail ? Cursors.Hand : Cursors.Arrow,
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (hasDetail)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });

            var iconTb = wmo.HasValue
                ? WeatherIconBlock(wmo.Value, 13, new Thickness(0, 0, 4, 0))
                : new TextBlock
                {
                    Text = iconStr, FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
            var labelTb = new TextBlock
            {
                Text = label, FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x88, 0xaa)),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas")
            };
            var valueTb = new TextBlock
            {
                Text = value, FontSize = 11, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
            };

            Grid.SetColumn(iconTb, 0); Grid.SetColumn(labelTb, 1); Grid.SetColumn(valueTb, 2);
            g.Children.Add(iconTb); g.Children.Add(labelTb); g.Children.Add(valueTb);

            var rowStack = new StackPanel();
            rowStack.Children.Add(g);

            if (hasDetail)
            {
                var arrowTb = new TextBlock
                {
                    Text = "›", FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0x88)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(arrowTb, 3); g.Children.Add(arrowTb);

                var detailTb = new TextBlock
                {
                    Text = detail, FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xcc, 0xff)),
                    Margin = new Thickness(24, 2, 0, 2), TextWrapping = TextWrapping.Wrap,
                    Visibility = Visibility.Collapsed
                };
                rowStack.Children.Add(detailTb);

                bool expanded = false;
                row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x28, 0x44));
                row.MouseLeave += (_, _) => row.Background = bgBase;
                row.MouseLeftButtonUp += (_, _) =>
                {
                    expanded = !expanded;
                    detailTb.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    arrowTb.Text = expanded ? "⌄" : "›";
                };
            }

            row.Child = rowStack;
            yield return row;
        }
    }

    // ── Widget event handlers ─────────────────────────────────────────────────

    public void HeaderWidget_LeftClick(object sender, MouseButtonEventArgs e)
    {
        switch (CurrentWidgetMode())
        {
            case "Clock":      OpenClockModeMenu();        break;
            case "Notes":      OpenNotesWindow();          break;
            case "Calculator": OpenCalculatorWindow();     break;
            case "Converter":  OpenConverterWindow();      break;
            case "Calendar":   OpenCalendarWindow();       break;
            case "Weather":    OpenWeatherDetailPopup();   break;
            case "Media":
            case "Music":
            case "Video":      OpenMusicControlsPopup();   break;
            case "CPU":
            case "RAM":        OpenSystemDetailPopup();    break;
            case "Navigation": if (_navActive) OpenNavHudWindow(); else OpenNavigationWindow(); break;
            case "Notifications": OpenNotificationsListPopup(); break;
            
        }
    }

    private void OpenSystemDetailPopup()
    {
        var proc = Process.GetCurrentProcess(); proc.Refresh();
        string info = $"CPU:     {GetCpuPercent():F1}%  ({Environment.ProcessorCount} cores)\n" +
                      $"RAM:     {GetProcessTreeWorkingSetBytes() / 1048576:F0} MB  (incl. WebView2)\n" +
                      $"Threads: {proc.Threads.Count}";
        MessageBox.Show(info, "System Info", MessageBoxButton.OK, MessageBoxImage.None);
    }

    // ── XAML Popup stubs (Music/Video widget inline popup) ───────────────────
    // These forward to the same full-featured Window-based controls popup.
    private void MusicPopupBack_Click(object sender, RoutedEventArgs e)    { e.Handled = true; OpenMusicControlsPopup(); }
    private void MusicPopupPlay_Click(object sender, RoutedEventArgs e)    { e.Handled = true; OpenMusicControlsPopup(); }
    private void MusicPopupForward_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenMusicControlsPopup(); }
    private void MusicPopupMute_Click(object sender, RoutedEventArgs e)    { e.Handled = true; OpenMusicControlsPopup(); }
    private void MusicPopupGoToTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var tab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio) ?? _allTabs.FirstOrDefault(t => t.HasEverPlayedAudio);
        if (tab == null) return;
        if (Tabs.Contains(tab))              ListTabs.SelectedItem         = tab;
        else if (OverflowTabs.Contains(tab)) ListOverflowTabs.SelectedItem = tab;
    }

    // ── Widget swipe / click dispatcher ──────────────────────────────────────
    // MouseDown records the press origin; MouseMove arms the swipe flag once
    // movement exceeds the threshold; MouseUp dispatches:
    //   • No movement          → left-click action  (open widget control)
    //   • Horizontal swipe →   → next widget mode
    //   • Horizontal swipe ←   → previous widget mode
    //   • Vertical   swipe ↑   → open widget control (same as click)
    //   • Vertical   swipe ↓   → open settings popup (same as right-click)

    public void HeaderWidget_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement el) return;
        _widgetSwipeStart = e.GetPosition(el);
        _widgetSwiping    = false;
        el.CaptureMouse();
        e.Handled = true;
    }

    public void HeaderWidget_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not UIElement el) return;
        var pos = e.GetPosition(el);
        if (Math.Abs(pos.X - _widgetSwipeStart.X) > _widgetSwipePx ||
            Math.Abs(pos.Y - _widgetSwipeStart.Y) > _widgetSwipePx)
            _widgetSwiping = true;
    }

    public void HeaderWidget_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement el) return;
        el.ReleaseMouseCapture();

        var pos = e.GetPosition(el);
        double dx = pos.X - _widgetSwipeStart.X;
        double dy = pos.Y - _widgetSwipeStart.Y;

        if (!_widgetSwiping)
        {
            // Short tap — open the widget's control window
            HeaderWidget_LeftClick(sender, e);
        }
        else if (Math.Abs(dx) >= Math.Abs(dy))
        {
            // Horizontal swipe: right→ = previous (−1), left← = next (+1)
            CycleWidgetMode(dx < 0 ? +1 : -1);
        }
        else
        {
            // Vertical swipe: up↑ = open control, down↓ = settings popup
            if (dy < 0) HeaderWidget_LeftClick(sender, e);
            else        HeaderWidget_RightClick(sender, e);
        }

        _widgetSwiping = false;
        e.Handled = true;
    }

    public void HeaderWidget_RightClick(object sender, MouseButtonEventArgs e)
    {
        SyncWidgetCheckboxes();
        SliderWidgetCycle.Value   = SettingsService.Current.WidgetCycleSecs;
        TxtWidgetCycleSecs.Text   = $"{SettingsService.Current.WidgetCycleSecs}s";
        TxtWeatherCity.Text       = SettingsService.Current.WidgetWeatherCity;
        PnlWeatherCity.Visibility = SettingsService.Current.WidgetModes.Contains("Weather")
                                    ? Visibility.Visible : Visibility.Collapsed;
        WidgetContextPopup.IsOpen = true;
        e.Handled = true;
    }

    public void HeaderWidget_MouseWheel(object sender, MouseWheelEventArgs e)
        => CycleWidgetMode(e.Delta > 0 ? -1 : +1);

    private void BtnWidgetPrev_Click(object sender, RoutedEventArgs e)
        => CycleWidgetMode(-1);

    private void BtnWidgetNext_Click(object sender, RoutedEventArgs e)
        => CycleWidgetMode(+1);

    private void BtnWidgetNav_RightClick(object sender, MouseButtonEventArgs e)
    {
        WidgetContextPopup.IsOpen = true;
        e.Handled = true;
    }

    private void WidgetModeCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        // DisableCycle is a special toggle, not a widget mode
        if (cb.Name == "ChkWidgetDisableCycle")
        {
            SettingsService.Current.WidgetDisableCycle = cb.IsChecked == true;
            SettingsService.Save();
            RestartWidgetCycleTimer();
            SyncWidgetCheckboxes();
            return;
        }
        if (cb.Tag is not string mode) return;
        var modes = SettingsService.Current.WidgetModes;

        if (cb.IsChecked == true)
        {
            if (!modes.Contains(mode)) modes.Add(mode);
            if (mode == "Weather")
            {
                PnlWeatherCity.Visibility = Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(SettingsService.Current.WidgetWeatherCity))
                    _ = FetchWeatherAsync(SettingsService.Current.WidgetWeatherCity);
            }
        }
        else
        {
            modes.Remove(mode);
            if (mode == "Weather") PnlWeatherCity.Visibility = Visibility.Collapsed;
            // Always keep at least Clock
            if (modes.Count == 0)
            {
                modes.Add("Clock");
                foreach (ListBoxItem lbi in LstWidgetOrder.Items)
                    if (lbi.Tag is string k && k == "Clock" && lbi.Content is Grid g)
                        if (g.Children.OfType<CheckBox>().FirstOrDefault() is CheckBox ck)
                            ck.IsChecked = true;
            }
            if (_widgetModeIndex >= modes.Count) _widgetModeIndex = 0;
        }

        SettingsService.Save();
        RestartWidgetCycleTimer();
        RefreshWidgetDisplay();
        PnlWidgetCycle.Visibility = (modes.Count > 1 && !SettingsService.Current.WidgetDisableCycle)
                                    ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SliderWidgetCycle_Changed(object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        int secs = (int)Math.Round(e.NewValue);
        SettingsService.Current.WidgetCycleSecs = secs;
        if (TxtWidgetCycleSecs != null)
            TxtWidgetCycleSecs.Text = secs == 0 ? "Off" : $"{secs}s";
        SettingsService.Save();
        RestartWidgetCycleTimer();
    }

    private void TxtWeatherCity_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return) return;
        string city = TxtWeatherCity.Text.Trim();
        SettingsService.Current.WidgetWeatherCity = city;
        SettingsService.Save();
        if (!string.IsNullOrEmpty(city)) _ = FetchWeatherAsync(city);
        WidgetContextPopup.IsOpen = false;
    }

    private void TxtWeatherCity_LostFocus(object sender, RoutedEventArgs e)
    {
        string city = TxtWeatherCity.Text.Trim();
        if (city == SettingsService.Current.WidgetWeatherCity) return;
        SettingsService.Current.WidgetWeatherCity = city;
        SettingsService.Save();
        if (!string.IsNullOrEmpty(city)) _ = FetchWeatherAsync(city);
    }

    // ── Music Controls popup (left-click on Music/Video widget) ──────────────

    private void OpenMusicControlsPopup()
    {
        if (_mediaWidgetWindow != null)
        {
            _mediaWidgetWindow.ShowInTaskbar = false;
            _mediaWidgetWindow.WindowState    = WindowState.Normal;
            _mediaWidgetWindow.Activate();
            return;
        }

        // Find the best candidate tab (currently playing, or last that played)
        var tab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio)
               ?? _allTabs.FirstOrDefault(t => t.HasEverPlayedAudio);

        var win = new Window
        {
            Title            = "Music Playing",
            Width            = 320,
            SizeToContent    = SizeToContent.Height,
            Background       = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
            WindowStyle      = WindowStyle.ToolWindow,
            ResizeMode       = ResizeMode.NoResize,
            Owner            = null,
            ShowInTaskbar    = false,
            Topmost          = true,
        };
        _mediaWidgetWindow = win;

        var root = new StackPanel { Margin = new Thickness(12) };

        // Track title
        var titleBlock = new TextBlock
        {
            Text         = tab != null ? (tab.CleanMediaTitle ?? tab.Title) : "Nothing playing",
            Foreground   = new SolidColorBrush(Color.FromRgb(0xee, 0xee, 0xee)),
            FontSize     = 14,
            FontWeight   = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth     = 280,
            Margin       = new Thickness(0, 0, 0, 4),
        };
        root.Children.Add(titleBlock);

        // Tab name (artist/source)
        var sourceBlock = new TextBlock
        {
            Text       = tab != null ? tab.Title : "",
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize   = 11,
            Margin     = new Thickness(0, 0, 0, 10),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        root.Children.Add(sourceBlock);

        // Controls row
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };

        System.Windows.Controls.Button MakeCtrlBtn(string symbol, string tooltip)
        {
            var b = new Button
            {
                Content          = symbol,
                Width            = 38, Height = 34, FontSize = 15,
                Background       = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
                Foreground       = System.Windows.Media.Brushes.White,
                BorderBrush      = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness  = new Thickness(1),
                Margin           = new Thickness(3, 0, 3, 0),
                Cursor           = System.Windows.Input.Cursors.Hand,
                ToolTip          = tooltip,
            };
            return b;
        }

        var btnPrev    = MakeCtrlBtn("⏮", "Previous Track");
        var btnBack    = MakeCtrlBtn("⏪", "-10s");
        var btnPP      = MakeCtrlBtn(tab?.IsPlayingAudio == true ? "⏸" : "▶", "Play / Pause");
        var btnFwd     = MakeCtrlBtn("⏩", "+10s");
        var btnNext    = MakeCtrlBtn("⏭", "Next Track");
        var btnMute    = MakeCtrlBtn(tab?.IsMuted == true ? "🔇" : "🔊", "Toggle Mute");

        if (tab != null && _tabViews.TryGetValue(tab, out var browser))
        {
            btnPrev.Click += async (s, e) =>
            {
                e.Handled = true;
                await (browser.MainWebView?.CoreWebView2?.ExecuteScriptAsync(
                    "(() => { const b = document.querySelector('.ytp-prev-button,[data-testid=\"previous-button\"],.skipControl__previous'); if(b){b.click();}else{const m=document.querySelector('video,audio');if(m)m.currentTime=0;}})()") ?? Task.CompletedTask);
            };
            btnBack.Click += async (s, e) =>
            {
                e.Handled = true;
                await (browser.MainWebView?.CoreWebView2?.ExecuteScriptAsync(
                    "(() => { const v=document.querySelector('video,audio'); if(v) v.currentTime=Math.max(0,v.currentTime-10); })()") ?? Task.CompletedTask);
            };
            btnPP.Click += async (s, e) =>
            {
                e.Handled = true;
                await (browser.MainWebView?.CoreWebView2?.ExecuteScriptAsync(
                    "(() => { const m=document.querySelector('video,audio'); if(m){if(m.paused)m.play().catch(()=>{});else m.pause();} })()") ?? Task.CompletedTask);
                btnPP.Content = tab.IsPlayingAudio ? "⏸" : "▶";
            };
            btnFwd.Click += async (s, e) =>
            {
                e.Handled = true;
                await (browser.MainWebView?.CoreWebView2?.ExecuteScriptAsync(
                    "(() => { const v=document.querySelector('video,audio'); if(v) v.currentTime=Math.min(v.duration||v.currentTime,v.currentTime+10); })()") ?? Task.CompletedTask);
            };
            btnNext.Click += async (s, e) =>
            {
                e.Handled = true;
                await (browser.MainWebView?.CoreWebView2?.ExecuteScriptAsync(
                    "(() => { const b=document.querySelector('.ytp-next-button,[data-testid=\"next-button\"],.skipControl__next'); if(b){b.click();}else{const m=document.querySelector('video,audio');if(m&&isFinite(m.duration))m.currentTime=m.duration-0.1;}})()") ?? Task.CompletedTask);
            };
            btnMute.Click += (s, e) =>
            {
                e.Handled = true;
                if (browser.MainWebView?.CoreWebView2 != null)
                {
                    browser.MainWebView.CoreWebView2.IsMuted = !browser.MainWebView.CoreWebView2.IsMuted;
                    tab.IsMuted = browser.MainWebView.CoreWebView2.IsMuted;
                    btnMute.Content = tab.IsMuted ? "🔇" : "🔊";
                }
            };
        }
        else
        {
            foreach (var b in new[] { btnPrev, btnBack, btnPP, btnFwd, btnNext, btnMute })
                b.IsEnabled = false;
        }

        foreach (var b in new[] { btnPrev, btnBack, btnPP, btnFwd, btnNext, btnMute })
            btnRow.Children.Add(b);
        root.Children.Add(btnRow);

        // Volume row
        var volRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        var volLabel = new TextBlock { Text = "🔉", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = System.Windows.Media.Brushes.White };
        var volSlider = new Slider { Minimum = 0, Maximum = 100, Value = tab != null ? tab.Volume * 100 : 100, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        var volHigh = new TextBlock { Text = "🔊", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Foreground = System.Windows.Media.Brushes.White };
        if (tab != null && _tabViews.TryGetValue(tab, out var volBrowser))
        {
            volSlider.ValueChanged += (s, ev) =>
            {
                tab.Volume = volSlider.Value / 100.0;
                _ = volBrowser.MainWebView?.CoreWebView2?.ExecuteScriptAsync(
                    $"(() => {{ const v=document.querySelector('video,audio'); if(v) v.volume={tab.Volume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}; }})()");
            };
        }
        volRow.Children.Add(volLabel);
        volRow.Children.Add(volSlider);
        volRow.Children.Add(volHigh);
        root.Children.Add(volRow);

        // Audio Only toggle + Mini-player (PiP)
        if (tab != null && _tabViews.TryGetValue(tab, out var aoBrowser))
        {
            var aoRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var aoBtn = new Button
            {
                Content         = tab.IsAudioOnlyMode ? "🎵 Audio Only: ON" : "🎵 Audio Only: OFF",
                Background      = new SolidColorBrush(tab.IsAudioOnlyMode ? Color.FromRgb(0x1a, 0x44, 0x1a) : Color.FromRgb(0x28, 0x28, 0x28)),
                Foreground      = System.Windows.Media.Brushes.White,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(10, 4, 10, 4),
                Margin          = new Thickness(0, 4, 3, 0),
                Cursor          = System.Windows.Input.Cursors.Hand,
            };
            aoBtn.Click += async (s, e) =>
            {
                tab.IsAudioOnlyMode = !tab.IsAudioOnlyMode;
                aoBtn.Content    = tab.IsAudioOnlyMode ? "🎵 Audio Only: ON" : "🎵 Audio Only: OFF";
                aoBtn.Background = new SolidColorBrush(tab.IsAudioOnlyMode ? Color.FromRgb(0x1a, 0x44, 0x1a) : Color.FromRgb(0x28, 0x28, 0x28));
                await (aoBrowser.MainWebView?.CoreWebView2?.ExecuteScriptAsync(
                    "(() => { document.querySelectorAll('video').forEach(v => v.style.opacity = v.style.opacity === '0' ? '1' : '0'); })()") ?? Task.CompletedTask);
            };

            var pipBtn = new Button
            {
                Content         = "🗔  Mini Player",
                Background      = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
                Foreground      = System.Windows.Media.Brushes.White,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(10, 4, 10, 4),
                Margin          = new Thickness(3, 4, 0, 0),
                Cursor          = System.Windows.Input.Cursors.Hand,
            };
            pipBtn.Click += async (s, e) =>
            {
                var core = aoBrowser.MainWebView?.CoreWebView2;
                if (core == null) return;
                string hideOpacity = tab.IsAudioOnlyMode ? "'0'" : "'1'";
                await core.ExecuteScriptAsync(
                    $"(() => {{ const v = document.querySelector('video'); if(!v) return; " +
                    $"v.style.opacity = {hideOpacity}; " +
                    "document.pictureInPictureElement ? document.exitPictureInPicture() : v.requestPictureInPicture().catch(()=>{}); })()");
            };

            aoRow.Children.Add(aoBtn);
            aoRow.Children.Add(pipBtn);
            root.Children.Add(aoRow);
        }

        var minRow = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };

        var minBtn = new Button
        {
            Content         = "🗕  Minimize",
            Background      = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
            Foreground      = System.Windows.Media.Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(0, 4, 0, 4),
            Margin          = new Thickness(0, 0, 3, 0),
            Cursor          = System.Windows.Input.Cursors.Hand,
        };
        minBtn.Click += (s2, e2) =>
        {
            win.ShowInTaskbar = true;
            win.WindowState   = WindowState.Minimized;
        };

        var gotoBtn = new Button
        {
            Content         = "↪  Go to Tab",
            Background      = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
            Foreground      = System.Windows.Media.Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(0, 4, 0, 4),
            Margin          = new Thickness(3, 0, 0, 0),
            Cursor          = System.Windows.Input.Cursors.Hand,
        };
        gotoBtn.Click += (s2, e2) => GoToMediaTabAndFocus(tab);

        minRow.Children.Add(minBtn);
        minRow.Children.Add(gotoBtn);
        root.Children.Add(minRow);

        win.SourceInitialized += (s2, e2) => SetupMediaWidgetTaskbarThumb(win, tab);
        win.Closed += (s2, e2) =>
        {
            _mediaWidgetWindow  = null;
            _mediaWidgetTaskbar = null;
            Dispatcher.BeginInvoke(new Action(() => { try { if (WindowState != WindowState.Minimized) Activate(); } catch { } }));
        };
        win.Content = root;
        win.Show();
    }

    private void SetupMediaWidgetTaskbarThumb(Window win, TabViewModel? tab)
    {
        try
        {
            var hwnd = new WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Distinct AppUserModelID so Windows stops grouping this window under
            // the browser's taskbar button — without this, same-process windows
            // are combined into one button/thumbnail flyout regardless of icon.
            SetWindowAppUserModelId(hwnd, "Horizon.Stealth.MediaWidget");

            // Give the taskbar button its own identity (icon) instead of inheriting
            // the browser window's icon, so it reads as a separate app entry.
            bool isVideo = tab != null && tab.HasVideo && !tab.IsAudioOnlyMode;
            ApplyMediaWidgetIcon(hwnd, isVideo);

            BuildMediaWidgetSystemMenu(hwnd, tab);
            BuildMediaWidgetJumpList(tab);

            _mediaWidgetTaskbar = (ITaskbarList3)new TaskbarInstance();
            _mediaWidgetTaskbar.HrInit();

            bool enabled = tab != null;
            var buttons = new[]
            {
                new THUMBBUTTON { iId = 1, szTip = "Previous",     dwMask = ThumbButtonMask.Tooltip | ThumbButtonMask.Flags, dwFlags = enabled ? ThumbButtonFlags.Enabled : ThumbButtonFlags.Disabled },
                new THUMBBUTTON { iId = 2, szTip = "Play / Pause", dwMask = ThumbButtonMask.Tooltip | ThumbButtonMask.Flags, dwFlags = enabled ? ThumbButtonFlags.Enabled : ThumbButtonFlags.Disabled },
                new THUMBBUTTON { iId = 3, szTip = "Next",         dwMask = ThumbButtonMask.Tooltip | ThumbButtonMask.Flags, dwFlags = enabled ? ThumbButtonFlags.Enabled : ThumbButtonFlags.Disabled },
                new THUMBBUTTON { iId = 4, szTip = "Mute",         dwMask = ThumbButtonMask.Tooltip | ThumbButtonMask.Flags, dwFlags = enabled ? ThumbButtonFlags.Enabled : ThumbButtonFlags.Disabled },
            };
            _mediaWidgetTaskbar.ThumbBarAddButtons(hwnd, (uint)buttons.Length, buttons);

            var src = HwndSource.FromHwnd(hwnd);
            src?.AddHook((IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                const int WM_COMMAND    = 0x0111;
                const int WM_SYSCOMMAND = 0x0112;

                if (msg == WM_COMMAND && tab != null && _tabViews.TryGetValue(tab, out var browser)
                    && browser.MainWebView?.CoreWebView2 != null)
                {
                    var core = browser.MainWebView.CoreWebView2;
                    int btnId = (int)(wParam.ToInt64() & 0xFFFF);
                    switch (btnId)
                    {
                        case 1:
                            _ = core.ExecuteScriptAsync("(() => { const b=document.querySelector('.ytp-prev-button,[data-testid=\"previous-button\"],.skipControl__previous'); if(b){b.click();}else{const m=document.querySelector('video,audio');if(m)m.currentTime=0;}})()");
                            handled = true; break;
                        case 2:
                            _ = core.ExecuteScriptAsync("(() => { const m=document.querySelector('video,audio'); if(m){if(m.paused)m.play().catch(()=>{});else m.pause();} })()");
                            handled = true; break;
                        case 3:
                            _ = core.ExecuteScriptAsync("(() => { const b=document.querySelector('.ytp-next-button,[data-testid=\"next-button\"],.skipControl__next'); if(b){b.click();}else{const m=document.querySelector('video,audio');if(m&&isFinite(m.duration))m.currentTime=m.duration-0.1;}})()");
                            handled = true; break;
                        case 4:
                            core.IsMuted = !core.IsMuted;
                            tab.IsMuted  = core.IsMuted;
                            handled = true; break;
                    }
                }
                else if (msg == WM_SYSCOMMAND)
                {
                    uint cmd = (uint)(wParam.ToInt64() & 0xFFF0);
                    if (cmd == SC_MEDIA_RETURNTAB)
                    {
                        GoToMediaTabAndFocus(tab);
                        handled = true;
                    }
                    else if (tab != null && _tabViews.TryGetValue(tab, out var browser2) && browser2.MainWebView?.CoreWebView2 != null)
                    {
                        var core2 = browser2.MainWebView.CoreWebView2;
                        if (cmd == SC_MEDIA_PLAYPAUSE)
                        {
                            _ = core2.ExecuteScriptAsync("(() => { const m=document.querySelector('video,audio'); if(m){if(m.paused)m.play().catch(()=>{});else m.pause();} })()");
                            handled = true;
                        }
                        else if (cmd == SC_MEDIA_PREV)
                        {
                            _ = core2.ExecuteScriptAsync("(() => { const b=document.querySelector('.ytp-prev-button,[data-testid=\"previous-button\"],.skipControl__previous'); if(b){b.click();}else{const m=document.querySelector('video,audio');if(m)m.currentTime=0;}})()");
                            handled = true;
                        }
                        else if (cmd == SC_MEDIA_NEXT)
                        {
                            _ = core2.ExecuteScriptAsync("(() => { const b=document.querySelector('.ytp-next-button,[data-testid=\"next-button\"],.skipControl__next'); if(b){b.click();}else{const m=document.querySelector('video,audio');if(m&&isFinite(m.duration))m.currentTime=m.duration-0.1;}})()");
                            handled = true;
                        }
                        else if (cmd == SC_MEDIA_MUTE)
                        {
                            core2.IsMuted = !core2.IsMuted;
                            tab.IsMuted   = core2.IsMuted;
                            handled = true;
                        }
                        else if (cmd == SC_MEDIA_AUDIOONLY)
                        {
                            tab.IsAudioOnlyMode = !tab.IsAudioOnlyMode;
                            _ = core2.ExecuteScriptAsync("(() => { document.querySelectorAll('video').forEach(v => v.style.opacity = v.style.opacity === '0' ? '1' : '0'); })()");
                            ApplyMediaWidgetIcon(hwnd, tab.HasVideo && !tab.IsAudioOnlyMode);
                            handled = true;
                        }
                    }
                }
                return IntPtr.Zero;
            });
        }
        catch { }
    }

    /// <summary>
    /// Shared entry point for a media-control command, regardless of whether it
    /// came from the widget popup, the taskbar thumbnail buttons, the system-menu
    /// right-click items, or a Jump List task relaunch relayed over the pipe.
    /// Always resolves the tab fresh, since the widget window may not be open.
    /// </summary>
    private void ExecuteMediaWidgetCommand(string cmd)
    {
        var tab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio)
               ?? _allTabs.FirstOrDefault(t => t.HasEverPlayedAudio);

        if (cmd == "RETURNTAB")
        {
            GoToMediaTabAndFocus(tab);
            return;
        }

        if (tab == null || !_tabViews.TryGetValue(tab, out var browser) || browser.MainWebView?.CoreWebView2 == null)
            return;

        var core = browser.MainWebView.CoreWebView2;
        switch (cmd)
        {
            case "PLAYPAUSE":
                _ = core.ExecuteScriptAsync("(() => { const m=document.querySelector('video,audio'); if(m){if(m.paused)m.play().catch(()=>{});else m.pause();} })()");
                break;
            case "PREV":
                _ = core.ExecuteScriptAsync("(() => { const b=document.querySelector('.ytp-prev-button,[data-testid=\"previous-button\"],.skipControl__previous'); if(b){b.click();}else{const m=document.querySelector('video,audio');if(m)m.currentTime=0;}})()");
                break;
            case "NEXT":
                _ = core.ExecuteScriptAsync("(() => { const b=document.querySelector('.ytp-next-button,[data-testid=\"next-button\"],.skipControl__next'); if(b){b.click();}else{const m=document.querySelector('video,audio');if(m&&isFinite(m.duration))m.currentTime=m.duration-0.1;}})()");
                break;
            case "MUTE":
                core.IsMuted = !core.IsMuted;
                tab.IsMuted  = core.IsMuted;
                break;
            case "AUDIOONLY":
                tab.IsAudioOnlyMode = !tab.IsAudioOnlyMode;
                _ = core.ExecuteScriptAsync("(() => { document.querySelectorAll('video').forEach(v => v.style.opacity = v.style.opacity === '0' ? '1' : '0'); })()");
                break;
        }
    }

    private void GoToMediaTabAndFocus(TabViewModel? tab)
    {
        if (tab == null) return;
        if (Tabs.Contains(tab))              ListTabs.SelectedItem         = tab;
        else if (OverflowTabs.Contains(tab)) ListOverflowTabs.SelectedItem = tab;

        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void BuildMediaWidgetJumpList(TabViewModel? tab)
    {
        try
        {
            var dl = (ICustomDestinationList)new CustomDestinationList();
            dl.SetAppID("Horizon.Stealth.MediaWidget");

            var removedIid = typeof(IObjectArray).GUID;
            dl.BeginList(out _, ref removedIid, out _);

            var collection = (IObjectCollection)new EnumerableObjectCollection();

            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                             ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

            void AddTask(string title, string cmd)
            {
                var link = (IShellLinkW)new ShellLink();
                link.SetPath(exePath);
                link.SetArguments($"--media-cmd={cmd}");
                link.SetIconLocation(exePath, 0);

                var pps = (IPropertyStore)link;
                var key = PKEY_Title;
                var pv  = new PropVariant { vt = 31, pointerValue = Marshal.StringToCoTaskMemUni(title) }; // VT_LPWSTR
                try { pps.SetValue(ref key, ref pv); pps.Commit(); }
                finally { PropVariantClear(ref pv); }

                collection.AddObject(link);
            }

            AddTask(tab?.IsPlayingAudio == true ? "Pause" : "Play", "PLAYPAUSE");
            AddTask("Previous Track", "PREV");
            AddTask("Next Track", "NEXT");
            AddTask(tab?.IsMuted == true ? "Unmute" : "Mute", "MUTE");
            AddTask(tab?.IsAudioOnlyMode == true ? "Audio Only: Off" : "Audio Only: On", "AUDIOONLY");
            AddTask("Return to Media Tab", "RETURNTAB");

            dl.AddUserTasks((IObjectArray)collection);
            dl.CommitList();
        }
        catch { }
    }

    private static readonly PROPERTYKEY PKEY_Title = new PROPERTYKEY
    {
        fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"),
        pid   = 2
    };

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint cMaxSlots, ref Guid riid, out IObjectArray? ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray poa);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, out object ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class CustomDestinationList { }

    [ComImport, Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint cObjects);
        void GetAt(uint uiIndex, ref Guid riid, out object ppv);
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint cObjects);
        void GetAt(uint uiIndex, ref Guid riid, out object ppv);
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object punk);
        void AddFromArray(IObjectArray poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class EnumerableObjectCollection { }

    [ComImport, Guid("000214f9-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("00021401-0000-0000-c000-000000000046")]
    private class ShellLink { }

    private void BuildMediaWidgetSystemMenu(IntPtr hwnd, TabViewModel? tab)
    {
        var hMenu = GetSystemMenu(hwnd, false);
        if (hMenu == IntPtr.Zero) return;

        AppendMenu(hMenu, MF_SEPARATOR, 0, "");
        AppendMenu(hMenu, MF_STRING, SC_MEDIA_RETURNTAB,  "Return to Media Tab");
        AppendMenu(hMenu, MF_STRING, SC_MEDIA_PLAYPAUSE,  tab?.IsPlayingAudio == true ? "Pause" : "Play");
        AppendMenu(hMenu, MF_STRING, SC_MEDIA_PREV,       "Previous Track");
        AppendMenu(hMenu, MF_STRING, SC_MEDIA_NEXT,       "Next Track");
        AppendMenu(hMenu, MF_STRING, SC_MEDIA_MUTE,       tab?.IsMuted == true ? "Unmute" : "Mute");
        AppendMenu(hMenu, MF_STRING, SC_MEDIA_AUDIOONLY,  tab?.IsAudioOnlyMode == true ? "Audio Only: ON" : "Audio Only: OFF");
    }

    private void ApplyMediaWidgetIcon(IntPtr hwnd, bool isVideo)
    {
        try
        {
            using var icon = CreateMediaWidgetIcon(isVideo);
            const int WM_SETICON = 0x0080;
            const int ICON_SMALL = 0;
            const int ICON_BIG   = 1;
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, icon.Handle);
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG,   icon.Handle);
        }
        catch { }
    }

    private static System.Drawing.Icon CreateMediaWidgetIcon(bool isVideo)
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            var bg = isVideo
                ? System.Drawing.Color.FromArgb(255, 0x2e, 0x6a, 0xa0)
                : System.Drawing.Color.FromArgb(255, 0x2a, 0x6a, 0x2a);
            using var bgBrush = new System.Drawing.SolidBrush(bg);
            g.FillEllipse(bgBrush, 1, 1, 30, 30);

            using var fgPen   = new System.Drawing.Pen(System.Drawing.Color.White, 2.2f);
            using var fgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);

            if (isVideo)
            {
                var pts = new[]
                {
                    new System.Drawing.Point(12, 9),
                    new System.Drawing.Point(12, 23),
                    new System.Drawing.Point(24, 16),
                };
                g.FillPolygon(fgBrush, pts);
            }
            else
            {
                g.FillEllipse(fgBrush, 9, 18, 7, 6);
                g.FillEllipse(fgBrush, 17, 15, 7, 6);
                g.DrawLine(fgPen, 15.5f, 8, 15.5f, 21);
                g.DrawLine(fgPen, 23.5f, 8, 23.5f, 18);
                g.DrawLine(fgPen, 15.5f, 8, 23.5f, 10.5f);
            }
        }

        var hIcon = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }

    [ComImport, Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    private class TaskbarInstance { }

    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, int tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, int tbatFlags);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButtons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButtons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, ref RECT prcClip);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct THUMBBUTTON
    {
        public ThumbButtonMask dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTip;
        public ThumbButtonFlags dwFlags;
    }

    [Flags]
    private enum ThumbButtonMask : uint { Bitmap = 0x1, Icon = 0x2, Tooltip = 0x4, Flags = 0x8 }

    [Flags]
    private enum ThumbButtonFlags : uint { Enabled = 0, Disabled = 0x1, DismissOnClick = 0x2, NoBackground = 0x4, Hidden = 0x8, NonInteractive = 0x10 }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const uint MF_STRING    = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint SC_MEDIA_PLAYPAUSE = 0x1000;
    private const uint SC_MEDIA_PREV      = 0x1010;
    private const uint SC_MEDIA_NEXT      = 0x1020;
    private const uint SC_MEDIA_MUTE      = 0x1030;
    private const uint SC_MEDIA_AUDIOONLY = 0x1040;
    private const uint SC_MEDIA_RETURNTAB = 0x1050;

    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new PROPERTYKEY
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid   = 5
    };

    private static void SetWindowAppUserModelId(IntPtr hwnd, string aumid)
    {
        try
        {
            var riid = typeof(IPropertyStore).GUID;
            int hr = SHGetPropertyStoreForWindow(hwnd, ref riid, out IPropertyStore? store);
            if (hr != 0 || store == null) return;

            var pkey = PKEY_AppUserModel_ID;
            var pv = new PropVariant { vt = 31, pointerValue = Marshal.StringToCoTaskMemUni(aumid) }; // VT_LPWSTR
            try
            {
                store.SetValue(ref pkey, ref pv);
                store.Commit();
            }
            finally
            {
                PropVariantClear(ref pv);
            }
            Marshal.ReleaseComObject(store);
        }
        catch { }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IPropertyStore? propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PropVariant pv);
        int SetValue(ref PROPERTYKEY key, ref PropVariant pv);
        int Commit();
    }

    // ── Weather Detail popup (left-click on Weather widget) ──────────────────

    private void OpenWeatherDetailPopup()
    {
        var win = new Window
        {
            Title = "Weather", Width = 400, MinHeight = 400, MaxHeight = 860,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.CanResizeWithGrip,
            Owner = this, ShowInTaskbar = false, Topmost = true,
        };

        var animBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Colors.Transparent, 0.0),
                new GradientStop(Colors.Transparent, 0.5),
                new GradientStop(Colors.Transparent, 1.0),
            }
        };
        var bgGrid = new Grid();
        bgGrid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x0e, 0x1a, 0x2e)) });
        var animBgBorder = new Border { Background = animBrush };
        bgGrid.Children.Add(animBgBorder);

        double popupFade = 0, popupT = 0, popupViz = 0;
        int    popupPhase = 0;
        double[] popupAmps = { 0, 0, 0 };
        DispatcherTimer? popupAnimTimer = null;

        void StartPopupAnim()
        {
            if (_weatherWmoCode < 0) return;
            var pal = GetWeatherPalette(_weatherWmoCode);
            if (pal.Count < 3) return;
            double fadeStep = 1.0 / (1500.0 / PaletteTickMs);
            popupAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PaletteTickMs) };
            popupAnimTimer.Tick += (_, _) =>
            {
                var p = GetWeatherPalette(_weatherWmoCode);
                int n = p.Count; if (n < 3) return;
                popupT += 0.004;
                if (popupT >= 1.0) { popupT = 0.0; popupPhase = (popupPhase + 1) % n; }
                popupViz += 1.0;
                if (popupFade < 1.0) popupFade = Math.Min(1.0, popupFade + fadeStep);
                double fa = popupFade; int ph = popupPhase;
                var c0 = LerpColor(p[ph % n],       p[(ph+1) % n], popupT);
                var c1 = LerpColor(p[(ph+1) % n],   p[(ph+2) % n], popupT);
                var c2 = LerpColor(p[(ph+2) % n],   p[ph    % n],  popupT);
                double a0 = ComputeBandAmplitude(popupViz, _bandFreq[0], _bandPhase[0]);
                double a1 = ComputeBandAmplitude(popupViz, _bandFreq[1], _bandPhase[1]);
                double a2 = ComputeBandAmplitude(popupViz, _bandFreq[2], _bandPhase[2]);
                const double sm = 0.88;
                popupAmps[0] += (a0 - popupAmps[0]) * sm;
                popupAmps[1] += (a1 - popupAmps[1]) * sm;
                popupAmps[2] += (a2 - popupAmps[2]) * sm;
                c0 = ModulateVisualizerColor(c0, popupAmps[0]); c1 = ModulateVisualizerColor(c1, popupAmps[1]); c2 = ModulateVisualizerColor(c2, popupAmps[2]);
                c0.A = (byte)(150 * fa); c1.A = (byte)(110 * fa); c2.A = (byte)(150 * fa);
                var freshPopupBrush = new LinearGradientBrush
                {
                    StartPoint    = new Point(0, 0),
                    EndPoint      = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(c0, 0.0),
                        new GradientStop(c1, 0.25 + 0.5 * ((Math.Sin(popupViz * 0.031) * 0.5) + 0.5)),
                        new GradientStop(c2, 1.0),
                    }
                };
                animBgBorder.Background = freshPopupBrush;
            };
            popupAnimTimer.Start();
        }

        win.Loaded += (_, _) => StartPopupAnim();
        win.Closed += (_, _) => { popupAnimTimer?.Stop(); Dispatcher.BeginInvoke(new Action(() => { try { if (WindowState != WindowState.Minimized) Activate(); } catch { } })); };

        var outerDock = new DockPanel();
        bgGrid.Children.Add(outerDock);

        var tabBar = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromArgb(200, 0x05, 0x10, 0x22)) };
        DockPanel.SetDock(tabBar, Dock.Top);
        outerDock.Children.Add(tabBar);

        string city = SettingsService.Current.WidgetWeatherCity;

        // ── Current tab ──────────────────────────────────────────────────────
        var panelCurrent = new Grid();
        var leftCol  = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var rightCol = new ColumnDefinition { Width = new GridLength(0) };
        panelCurrent.ColumnDefinitions.Add(leftCol);
        panelCurrent.ColumnDefinitions.Add(rightCol);

        var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(leftScroll, 0);
        panelCurrent.Children.Add(leftScroll);

        var hourlyScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)),
            BorderThickness = new Thickness(1, 0, 0, 0)
        };
        Grid.SetColumn(hourlyScroll, 1);
        panelCurrent.Children.Add(hourlyScroll);

        var currentInner = new StackPanel { Margin = new Thickness(12) };
        leftScroll.Content = currentInner;

        var cityInput = new TextBox
        {
            Text = city, Background = new SolidColorBrush(Color.FromArgb(180, 0x10, 0x10, 0x10)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1), Padding = new Thickness(4, 3, 4, 3),
            FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
        };
        var conditionsHolder = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        void ShowLoading()
        {
            conditionsHolder.Children.Clear();
            conditionsHolder.Children.Add(new TextBlock
            {
                Text = "Loading…",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xaa)),
                FontSize = 11, Margin = new Thickness(0, 4, 0, 4)
            });
        }
        void RebuildConditions()
        {
            conditionsHolder.Children.Clear();
            foreach (var row in BuildCurrentConditionsRows())
                conditionsHolder.Children.Add(row);
        }
        RebuildConditions();

        var refreshBtn = AccentButton("↻  Refresh", Color.FromRgb(0x1a, 0x34, 0x60), Color.FromRgb(0x2e, 0x5a, 0x8f), 110);

        var hourlyHolder = new StackPanel { Margin = new Thickness(8) };
        hourlyScroll.Content = hourlyHolder;
        bool hourlyLoaded = false;
        bool hourlyExpanded = false;

        async Task LoadTodayHourly()
        {
            hourlyHolder.Children.Clear();
            await EnsureWeatherGeoAsync(cityInput.Text.Trim());
            var todayHourly = await FetchWeatherHourlyAsync(DateTime.Today);
            hourlyHolder.Children.Clear();
            if (todayHourly.Count == 0) return;

            hourlyHolder.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)),
                Margin = new Thickness(0, 4, 0, 6)
            });
            hourlyHolder.Children.Add(SectionLabel("TODAY — HOURLY"));
            var graphCanvas = BuildHourlyGraph(todayHourly, 360, 70);
            hourlyHolder.Children.Add(graphCanvas);
            hourlyHolder.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)),
                Margin = new Thickness(0, 4, 0, 6)
            });
            bool altH = false;
            foreach (var (time, temp, prec, wind, hWmo, hum) in todayHourly)
            {
                var hRow = new Border
                {
                    Background      = new SolidColorBrush(altH ? Color.FromArgb(120, 0x08, 0x10, 0x20) : Color.FromArgb(120, 0x0e, 0x1a, 0x2e)),
                    BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(6, 3, 6, 3)
                };
                altH = !altH;
                var hg = new Grid();
                hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

                var tLbl = new TextBlock { Text = time.ToString("HH:mm"), FontSize = 10, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xaa, 0xcc)), VerticalAlignment = VerticalAlignment.Center };
                var iLbl = WeatherIconBlock(hWmo, 12, new Thickness(2, 0, 2, 0));
                var teTx = new TextBlock { Text = $"{temp:F1}°C", FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
                var pTx  = new TextBlock { Text = prec > 0 ? $"💧{prec:F1}mm" : "", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xaa, 0xdd)), VerticalAlignment = VerticalAlignment.Center };
                var wTx  = new TextBlock { Text = $"💨{wind:F0}", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xcc, 0x88)), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };

                Grid.SetColumn(tLbl, 0); Grid.SetColumn(iLbl, 1); Grid.SetColumn(teTx, 2); Grid.SetColumn(pTx, 3); Grid.SetColumn(wTx, 4);
                hg.Children.Add(tLbl); hg.Children.Add(iLbl); hg.Children.Add(teTx); hg.Children.Add(pTx); hg.Children.Add(wTx);

                var captureHWmo = hWmo; var captureHum = hum;
                var capturePrec = prec; var captureWind = wind;
                var hDetailTb = new TextBlock
                {
                    Text = $"{WmoCodeToDescription(captureHWmo)}  ·  💧 {captureHum}%  ·  💨 {captureWind:F0} km/h  ·  🌧 {capturePrec:F1} mm",
                    FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xcc, 0xff)),
                    Margin = new Thickness(40, 2, 6, 2), TextWrapping = TextWrapping.Wrap,
                    Visibility = Visibility.Collapsed
                };
                var hRowStack = new StackPanel();
                hRowStack.Children.Add(hg);
                hRowStack.Children.Add(hDetailTb);
                hRow.Child = hRowStack;
                hRow.Cursor = Cursors.Hand;
                var hBgOrig = hRow.Background;
                bool hrExpanded = false;
                hRow.MouseEnter += (_, _) => hRow.Background = new SolidColorBrush(Color.FromArgb(180, 0x14, 0x24, 0x3c));
                hRow.MouseLeave += (_, _) => hRow.Background = hBgOrig;
                hRow.MouseLeftButtonUp += (_, _) =>
                {
                    hrExpanded = !hrExpanded;
                    hDetailTb.Visibility = hrExpanded ? Visibility.Visible : Visibility.Collapsed;
                };
                hourlyHolder.Children.Add(hRow);
            }
        }

        cityInput.KeyDown += async (s, e) =>
        {
            if (e.Key == Key.Return)
            {
                string nc = cityInput.Text.Trim();
                SettingsService.Current.WidgetWeatherCity = nc;
                SettingsService.Save();
                ShowLoading();
                await FetchWeatherDetailAsync(nc);
                RebuildConditions();
                _ = FetchWeatherAsync(nc);
                hourlyLoaded = false;
                if (hourlyExpanded) { hourlyLoaded = true; await LoadTodayHourly(); }
            }
        };
        refreshBtn.Click += async (s, e) =>
        {
            string rc = cityInput.Text.Trim();
            if (!string.IsNullOrEmpty(rc))
            {
                refreshBtn.IsEnabled = false; refreshBtn.Content = "Loading…";
                ShowLoading();
                await FetchWeatherDetailAsync(rc);
                RebuildConditions();
                if (hourlyExpanded) { hourlyLoaded = true; await LoadTodayHourly(); }
                refreshBtn.Content = "↻  Refresh"; refreshBtn.IsEnabled = true;
            }
        };
        currentInner.Children.Add(cityInput);
        currentInner.Children.Add(conditionsHolder);
        currentInner.Children.Add(refreshBtn);

        var hourlyToggleBtn = new Button
        {
            Content = ">  Today — Hourly",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(100, 0x0a, 0x16, 0x28)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xaa, 0xdd)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)),
            FontSize = 11, FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(6, 8, 6, 8),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 10, 0, 0)
        };
        hourlyToggleBtn.Click += async (s, e) =>
        {
            hourlyExpanded = !hourlyExpanded;
            if (hourlyExpanded)
            {
                rightCol.Width = new GridLength(340);
                hourlyScroll.Visibility = Visibility.Visible;
                win.Width = 740;
                hourlyToggleBtn.Content = "<  Today — Hourly";
            }
            else
            {
                hourlyScroll.Visibility = Visibility.Collapsed;
                rightCol.Width = new GridLength(0);
                win.Width = 400;
                hourlyToggleBtn.Content = ">  Today — Hourly";
            }
            if (hourlyExpanded && !hourlyLoaded)
            {
                hourlyLoaded = true;
                await LoadTodayHourly();
            }
        };
        currentInner.Children.Add(hourlyToggleBtn);

        // ── Forecast tab builder ─────────────────────────────────────────────
        ScrollViewer MakeForecastTab()
        {
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
            sv.Content = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            return sv;
        }

        var panel7   = MakeForecastTab();
        var panel16  = MakeForecastTab();
        var panelMon = MakeForecastTab();
        var panelYr  = MakeForecastTab();

        async Task LoadForecastTab(ScrollViewer sv, int days)
        {
            var sp = (StackPanel)sv.Content;
            sp.Children.Clear();
            sp.Children.Add(new TextBlock { Text = "Loading…", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xaa)), FontSize = 11, Margin = new Thickness(0, 6, 0, 6) });
            string fc = cityInput.Text.Trim();
            if (string.IsNullOrEmpty(fc)) return;
            var data = await FetchWeatherForecastAsync(fc, days);
            sp.Children.Clear();
            if (data.Count > 1)
            {
                var graph = BuildTemperatureGraph(data, 370, 80);
                sp.Children.Add(graph);
                sp.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)), Margin = new Thickness(0, 4, 0, 6) });
            }
            BuildForecastRows(sp, data);
        }

        async Task LoadMonthlyTab(ScrollViewer sv)
        {
            var sp = (StackPanel)sv.Content;
            sp.Children.Clear();
            sp.Children.Add(new TextBlock { Text = "Loading…", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xaa)), FontSize = 11, Margin = new Thickness(0, 6, 0, 6) });
            string fc = cityInput.Text.Trim();
            if (string.IsNullOrEmpty(fc)) return;
            await EnsureWeatherGeoAsync(fc);
            var today = DateTime.Today;
            var cur = new DateTime(today.Year, today.Month, 1);
            await BuildMonthCalendarView(sp, fc, cur);
        }

        async Task LoadYearlyTab(ScrollViewer sv)
        {
            var sp = (StackPanel)sv.Content;
            sp.Children.Clear();
            sp.Children.Add(new TextBlock { Text = "Loading…", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xaa)), FontSize = 11, Margin = new Thickness(0, 6, 0, 6) });
            string fc = cityInput.Text.Trim();
            if (string.IsNullOrEmpty(fc)) return;
            await EnsureWeatherGeoAsync(fc);
            await BuildYearlyView(sp, fc, DateTime.Today.Year);
        }

        // ── Tab management ───────────────────────────────────────────────────
        UIElement[] panels = { panelCurrent, panel7, panel16, panelMon, panelYr };
        string[]    labels = { "⛅ Now", "📅 7d", "📅 16d", "🗓 Month", "📆 Year" };
        var tabBtns = new Button[panels.Length];
        bool[] loaded = new bool[panels.Length];

        var contentHolder = new Grid();
        foreach (var p in panels) contentHolder.Children.Add(p);
        outerDock.Children.Add(contentHolder);

        void SelectTab(int idx)
        {
            for (int i = 0; i < panels.Length; i++)
            {
                panels[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
                TabButtonActive(tabBtns[i], i == idx);
            }
        }

        for (int i = 0; i < labels.Length; i++)
        {
            var btn = TabButton(labels[i], i == 0);
            btn.Padding = new Thickness(9, 0, 9, 0);
            tabBtns[i] = btn;
            var ci = i;
            btn.Click += async (s, e) =>
            {
                SelectTab(ci);
                if (loaded[ci]) return;
                loaded[ci] = true;
                if      (ci == 1) await LoadForecastTab(panel7,   7);
                else if (ci == 2) await LoadForecastTab(panel16, 16);
                else if (ci == 3) await LoadMonthlyTab(panelMon);
                else if (ci == 4) await LoadYearlyTab(panelYr);
            };
            tabBar.Children.Add(btn);
        }
        panelCurrent.Visibility = Visibility.Visible;
        loaded[0] = true;

        win.Content = bgGrid;
        win.Show();

        if (!string.IsNullOrEmpty(city))
        {
            ShowLoading();
            _ = FetchWeatherDetailAsync(city).ContinueWith(_ =>
                Dispatcher.Invoke(() =>
                {
                    RebuildConditions();
                }));
        }
    }

    private async Task EnsureWeatherGeoAsync(string city)
    {
        if (_cachedWeatherCity == city && _cachedWeatherLat != 0) return;
        try
        {
            string geoJson = await _widgetHttp.GetStringAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json");
            using var doc = JsonDocument.Parse(geoJson);
            if (doc.RootElement.TryGetProperty("results", out var res) && res.GetArrayLength() > 0)
            {
                var loc = res[0];
                _cachedWeatherLat  = loc.GetProperty("latitude").GetDouble();
                _cachedWeatherLon  = loc.GetProperty("longitude").GetDouble();
                _cachedWeatherTz   = loc.TryGetProperty("timezone", out var tz) ? tz.GetString() ?? "auto" : "auto";
                _cachedWeatherCity = city;
            }
        }
        catch { }
    }

    private async Task<List<(DateTime Date, int WmoCode, double Max, double Min, double RainPct, double PrecipMm)>>
        FetchWeatherRangeAsync(DateTime startDate, DateTime endDate)
    {
        var result = new List<(DateTime, int, double, double, double, double)>();
        if (_cachedWeatherLat == 0) return result;

        var today = DateTime.Today;
        string latS = _cachedWeatherLat.ToString(CultureInfo.InvariantCulture);
        string lonS = _cachedWeatherLon.ToString(CultureInfo.InvariantCulture);
        string tz   = Uri.EscapeDataString(_cachedWeatherTz);
        string fields = "weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum";

        async Task<List<(DateTime, int, double, double, double, double)>> FetchPart(string baseUrl, string timeParams)
        {
            var r = new List<(DateTime, int, double, double, double, double)>();
            try
            {
                string url = $"{baseUrl}?latitude={latS}&longitude={lonS}&daily={fields}&timezone={tz}{timeParams}";
                string json = await _widgetHttp.GetStringAsync(url);
                using var doc   = JsonDocument.Parse(json);
                var daily  = doc.RootElement.GetProperty("daily");
                var dates  = daily.GetProperty("time");
                var codes  = daily.GetProperty("weather_code");
                var maxes  = daily.GetProperty("temperature_2m_max");
                var mines  = daily.GetProperty("temperature_2m_min");
                var rainP  = daily.TryGetProperty("precipitation_probability_max", out var rp) ? rp : daily.GetProperty("weather_code");
                var prec   = daily.GetProperty("precipitation_sum");
                bool hasRain = daily.TryGetProperty("precipitation_probability_max", out _);
                int count = dates.GetArrayLength();
                for (int i = 0; i < count; i++)
                {
                    if (!DateTime.TryParse(dates[i].GetString(), out var d)) continue;
                    r.Add((d, codes[i].GetInt32(), maxes[i].GetDouble(), mines[i].GetDouble(),
                        hasRain && rainP[i].ValueKind == JsonValueKind.Number ? rainP[i].GetDouble() : 0,
                        prec[i].ValueKind == JsonValueKind.Number ? prec[i].GetDouble() : 0));
                }
            }
            catch { }
            return r;
        }

        if (startDate < today)
        {
            var archiveEnd = endDate < today.AddDays(-1) ? endDate : today.AddDays(-1);
            var past = await FetchPart("https://archive-api.open-meteo.com/v1/archive", $"&start_date={startDate:yyyy-MM-dd}&end_date={archiveEnd:yyyy-MM-dd}");
            result.AddRange(past);
        }
        if (endDate >= today)
        {
            var fcastStart = startDate > today ? startDate : today;
            int fcastDays  = (endDate - fcastStart).Days + 1;
            if (fcastDays > 0)
            {
                var future = await FetchPart("https://api.open-meteo.com/v1/forecast", $"&forecast_days={Math.Min(fcastDays, 16)}");
                result.AddRange(future);
            }
        }

        return result.OrderBy(x => x.Item1).DistinctBy(x => x.Item1.Date).ToList();
    }

    private async Task<List<(DateTime Time, double Temp, double Precip, double WindKmh, int Wmo, int Humidity)>>
        FetchWeatherHourlyAsync(DateTime date)
    {
        var result = new List<(DateTime, double, double, double, int, int)>();
        if (_cachedWeatherLat == 0) return result;
        string latS   = _cachedWeatherLat.ToString(CultureInfo.InvariantCulture);
        string lonS   = _cachedWeatherLon.ToString(CultureInfo.InvariantCulture);
        string tz     = Uri.EscapeDataString(_cachedWeatherTz);
        string fields = "temperature_2m,precipitation,wind_speed_10m,weather_code,relative_humidity_2m";
        string baseUrl = date < DateTime.Today
            ? "https://archive-api.open-meteo.com/v1/archive"
            : "https://api.open-meteo.com/v1/forecast";
        try
        {
            string url  = baseUrl +
                $"?latitude={latS}&longitude={lonS}&hourly={fields}" +
                $"&timezone={tz}&start_date={date:yyyy-MM-dd}&end_date={date:yyyy-MM-dd}";
            string json = await _widgetHttp.GetStringAsync(url);
            using var doc   = JsonDocument.Parse(json);
            var hourly = doc.RootElement.GetProperty("hourly");
            var times  = hourly.GetProperty("time");
            var temps  = hourly.GetProperty("temperature_2m");
            var prec   = hourly.GetProperty("precipitation");
            var wind   = hourly.GetProperty("wind_speed_10m");
            var codes  = hourly.GetProperty("weather_code");
            var hum    = hourly.GetProperty("relative_humidity_2m");
            int cnt    = times.GetArrayLength();
            for (int i = 0; i < cnt; i++)
            {
                if (!DateTime.TryParse(times[i].GetString(), out var t)) continue;
                result.Add((t,
                    temps[i].ValueKind == JsonValueKind.Number ? temps[i].GetDouble() : 0,
                    prec[i].ValueKind  == JsonValueKind.Number ? prec[i].GetDouble()  : 0,
                    wind[i].ValueKind  == JsonValueKind.Number ? wind[i].GetDouble()  : 0,
                    codes[i].ValueKind == JsonValueKind.Number ? codes[i].GetInt32()  : 0,
                    hum[i].ValueKind   == JsonValueKind.Number ? hum[i].GetInt32()    : 0));
            }
        }
        catch { }
        return result;
    }

    private Canvas BuildTemperatureGraph(
        List<(DateTime Date, int WmoCode, double Max, double Min, double RainPct, double PrecipMm)> days,
        double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height, Margin = new Thickness(0, 4, 0, 4) };
        if (days.Count < 2) return canvas;

        double allMax = days.Max(d => d.Max);
        double allMin = days.Min(d => d.Min);
        double range  = allMax - allMin;
        if (range < 1) range = 1;

        double barW   = (width - 4) / days.Count;
        double padTop = 14, padBot = 18;
        double graphH = height - padTop - padBot;

        for (int i = 0; i < days.Count; i++)
        {
            var (date, code, max, min, rain, _) = days[i];
            double x     = 2 + i * barW;
            double yMax  = padTop + (1 - (max - allMin) / range) * graphH;
            double yMin  = padTop + (1 - (min - allMin) / range) * graphH;
            double barH  = Math.Max(3, yMin - yMax);

            byte alpha = (byte)(rain > 0 ? 220 : 180);
            var fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(alpha, 0xff, 0x88, 0x44), 0),
                    new GradientStop(Color.FromArgb(alpha, 0x44, 0x88, 0xff), 1),
                }
            };

            var bar = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(2, barW - 2), Height = barH,
                Fill = fill, RadiusX = 2, RadiusY = 2
            };
            Canvas.SetLeft(bar, x); Canvas.SetTop(bar, yMax);
            canvas.Children.Add(bar);

            if (rain > 20)
            {
                var rainBar = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(2, barW - 2), Height = Math.Max(2, (rain / 100.0) * graphH * 0.3),
                    Fill = new SolidColorBrush(Color.FromArgb(140, 0x44, 0xaa, 0xff)),
                    RadiusX = 1, RadiusY = 1
                };
                Canvas.SetLeft(rainBar, x); Canvas.SetTop(rainBar, height - padBot - rainBar.Height);
                canvas.Children.Add(rainBar);
            }

            var maxLbl = new TextBlock
            {
                Text = $"{max:F0}°", FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xaa, 0x66)),
                TextAlignment = TextAlignment.Center, Width = barW
            };
            Canvas.SetLeft(maxLbl, x); Canvas.SetTop(maxLbl, Math.Max(0, yMax - 13));
            canvas.Children.Add(maxLbl);

            if (i % Math.Max(1, days.Count / 8) == 0 || i == days.Count - 1)
            {
                var dateLbl = new TextBlock
                {
                    Text = date.ToString("M/d"), FontSize = 7.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x88, 0xaa)),
                    TextAlignment = TextAlignment.Center, Width = barW * 2
                };
                Canvas.SetLeft(dateLbl, x - barW * 0.5); Canvas.SetTop(dateLbl, height - padBot + 2);
                canvas.Children.Add(dateLbl);
            }
        }

        var border = new System.Windows.Shapes.Rectangle
        {
            Width = width, Height = height,
            Stroke = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)),
            StrokeThickness = 1, Fill = Brushes.Transparent, RadiusX = 4, RadiusY = 4
        };
        Canvas.SetLeft(border, 0); Canvas.SetTop(border, 0);
        canvas.Children.Add(border);

        return canvas;
    }

    private void BuildForecastRows(StackPanel panel,
        List<(DateTime Date, int WmoCode, double Max, double Min, double RainPct, double PrecipMm)> days)
    {
        panel.Children.Clear();
        if (days.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Could not load forecast.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x88, 0x99)),
                FontSize = 11, Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }
        bool alt = false;
        foreach (var item in days)
        {
            var (date, code, max, min, rainPct, precipMm) = item;
            var row = new Border
            {
                Background      = new SolidColorBrush(alt
                    ? Color.FromRgb(0x0a, 0x12, 0x22)
                    : Color.FromRgb(0x10, 0x1e, 0x34)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1a, 0x2e, 0x4a)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(8, 5, 8, 5),
                Cursor = Cursors.Hand
            };
            alt = !alt;

            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x28, 0x44));
            row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(alt ? Color.FromRgb(0x10, 0x1e, 0x34) : Color.FromRgb(0x0a, 0x12, 0x22));

            var captureDate = date; var captureCode = code;
            var captureMax  = max;  var captureMin  = min;
            row.MouseLeftButtonUp += (_, _) => _ = OpenWeatherDayDetailAsync(captureDate, captureCode, captureMax, captureMin);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

            var dayLbl = new TextBlock
            {
                Text = date.ToString("ddd dd"), FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xcc, 0xee)),
                VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas")
            };
            var iconLbl = WeatherIconBlock(code, 14, new Thickness(2, 0, 2, 0));
            var tempLbl = new TextBlock
            {
                Text = $"{max:F0}° / {min:F0}°", FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd)),
                VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas")
            };
            var rainLbl = new TextBlock
            {
                Text = rainPct > 0 ? $"💧{rainPct:F0}%" : "",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xaa, 0xdd)),
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right
            };
            var arrow = new TextBlock
            {
                Text = "›", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0x88)),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(dayLbl,  0); Grid.SetColumn(iconLbl, 1); Grid.SetColumn(tempLbl, 2);
            Grid.SetColumn(rainLbl, 3); Grid.SetColumn(arrow,   4);
            g.Children.Add(dayLbl); g.Children.Add(iconLbl); g.Children.Add(tempLbl);
            g.Children.Add(rainLbl); g.Children.Add(arrow);
            row.Child = g;
            panel.Children.Add(row);
        }
    }

    private async Task OpenWeatherDayDetailAsync(DateTime date, int wmoCode, double max, double min)
    {
        var popup = new Window
        {
            Title = date.ToString("dddd, d MMMM yyyy"), Width = 400, MinHeight = 280, MaxHeight = 780,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.CanResizeWithGrip,
            Owner = this, ShowInTaskbar = false, Topmost = true,
        };
        popup.Closing += (_, _) => { popup.Owner = null; };
        popup.Closed  += (_, _) => Dispatcher.BeginInvoke(new Action(() => { try { if (WindowState != WindowState.Minimized) Activate(); } catch { } }));

        var animBrush2 = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Colors.Transparent, 0.0),
                new GradientStop(Colors.Transparent, 0.5),
                new GradientStop(Colors.Transparent, 1.0),
            }
        };
        var bg2 = new Grid();
        bg2.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x0a, 0x14, 0x26)) });
        bg2.Children.Add(new Border { Background = animBrush2 });

        double pFade = 0, pT = 0, pViz = 0; int pPhase = 0; double[] pAmps = { 0, 0, 0 };
        DispatcherTimer? pTimer = null;
        var pal2 = GetWeatherPalette(wmoCode);
        if (pal2.Count >= 3)
        {
            double fs = 1.0 / (1500.0 / PaletteTickMs);
            pTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PaletteTickMs) };
            pTimer.Tick += (_, _) =>
            {
                var p = GetWeatherPalette(wmoCode); int n = p.Count; if (n < 3) return;
                pT += 0.004; if (pT >= 1.0) { pT = 0.0; pPhase = (pPhase + 1) % n; }
                pViz += 1.0; if (pFade < 1.0) pFade = Math.Min(1.0, pFade + fs);
                int ph = pPhase;
                var c0 = LerpColor(p[ph%n], p[(ph+1)%n], pT); var c1 = LerpColor(p[(ph+1)%n], p[(ph+2)%n], pT); var c2 = LerpColor(p[(ph+2)%n], p[ph%n], pT);
                double a0 = ComputeBandAmplitude(pViz,_bandFreq[0],_bandPhase[0]); double a1 = ComputeBandAmplitude(pViz,_bandFreq[1],_bandPhase[1]); double a2 = ComputeBandAmplitude(pViz,_bandFreq[2],_bandPhase[2]);
                const double sm = 0.88; pAmps[0]+=(a0-pAmps[0])*sm; pAmps[1]+=(a1-pAmps[1])*sm; pAmps[2]+=(a2-pAmps[2])*sm;
                c0=ModulateVisualizerColor(c0,pAmps[0]); c1=ModulateVisualizerColor(c1,pAmps[1]); c2=ModulateVisualizerColor(c2,pAmps[2]);
                c0.A=(byte)(140*pFade); c1.A=(byte)(100*pFade); c2.A=(byte)(140*pFade);
                animBrush2.GradientStops[0].Color=c0; animBrush2.GradientStops[1].Color=c1; animBrush2.GradientStops[2].Color=c2;
                animBrush2.GradientStops[1].Offset=0.25+0.5*((Math.Sin(pViz*0.031)*0.5)+0.5);
            };
            pTimer.Start();
        }
        popup.Closed += (_, _) => pTimer?.Stop();

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root   = new StackPanel { Margin = new Thickness(14) };
        scroll.Content = root;
        bg2.Children.Add(scroll);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(WeatherIconBlock(wmoCode, 32, new Thickness(0, 0, 10, 0)));
        var hInfo = new StackPanel();
        hInfo.Children.Add(new TextBlock { Text = WmoCodeToDescription(wmoCode), Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold });
        hInfo.Children.Add(new TextBlock { Text = $"Hi {max:F1}°C  /  Lo {min:F1}°C", Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xdd, 0xff)), FontSize = 12, FontFamily = new FontFamily("Consolas") });
        header.Children.Add(hInfo);
        root.Children.Add(header);

        var loadingLbl = new TextBlock { Text = "Loading hourly data…", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xaa)), FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(loadingLbl);
        popup.Content = bg2;
        popup.Show();

        var hourly = await FetchWeatherHourlyAsync(date);
        root.Children.Remove(loadingLbl);

        if (hourly.Count > 0)
        {
            var graphCanvas = BuildHourlyGraph(hourly, 360, 70);
            root.Children.Add(graphCanvas);
            root.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)), Margin = new Thickness(0, 6, 0, 6) });
        }

        root.Children.Add(SectionLabel("HOURLY BREAKDOWN"));
        bool altH = false;
        foreach (var (time, temp, prec, wind, hWmo, hum) in hourly)
        {
            var hRow = new Border
            {
                Background      = new SolidColorBrush(altH ? Color.FromArgb(120, 0x08, 0x10, 0x20) : Color.FromArgb(120, 0x0e, 0x1a, 0x2e)),
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(6, 3, 6, 3)
            };
            altH = !altH;
            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

            var tLbl = new TextBlock { Text = time.ToString("HH:mm"), FontSize = 10, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xaa, 0xcc)), VerticalAlignment = VerticalAlignment.Center };
            var iLbl = WeatherIconBlock(hWmo, 12, new Thickness(2, 0, 2, 0));
            var teTx = new TextBlock { Text = $"{temp:F1}°C", FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            var pTx  = new TextBlock { Text = prec > 0 ? $"💧{prec:F1}mm" : "", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xaa, 0xdd)), VerticalAlignment = VerticalAlignment.Center };
            var wTx  = new TextBlock { Text = $"💨{wind:F0}", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xcc, 0x88)), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };

            Grid.SetColumn(tLbl, 0); Grid.SetColumn(iLbl, 1); Grid.SetColumn(teTx, 2); Grid.SetColumn(pTx, 3); Grid.SetColumn(wTx, 4);
            hg.Children.Add(tLbl); hg.Children.Add(iLbl); hg.Children.Add(teTx); hg.Children.Add(pTx); hg.Children.Add(wTx);
            var captureHWmo = hWmo; var captureHum = hum;
            var capturePrec = prec; var captureWind = wind;
            var hDetailTb = new TextBlock
            {
                Text = $"{WmoCodeToDescription(captureHWmo)}  ·  💧 {captureHum}%  ·  💨 {captureWind:F0} km/h  ·  🌧 {capturePrec:F1} mm",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xcc, 0xff)),
                Margin = new Thickness(40, 2, 6, 2), TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            var hRowStack = new StackPanel();
            hRowStack.Children.Add(hg);
            hRowStack.Children.Add(hDetailTb);
            hRow.Child = hRowStack;
            hRow.Cursor = Cursors.Hand;
            var hBgOrig = hRow.Background;
            bool hrExpanded = false;
            hRow.MouseEnter += (_, _) => hRow.Background = new SolidColorBrush(Color.FromArgb(180, 0x14, 0x24, 0x3c));
            hRow.MouseLeave += (_, _) => hRow.Background = hBgOrig;
            hRow.MouseLeftButtonUp += (_, _) =>
            {
                hrExpanded = !hrExpanded;
                hDetailTb.Visibility = hrExpanded ? Visibility.Visible : Visibility.Collapsed;
            };
            root.Children.Add(hRow);
        }

        if (hourly.Count == 0)
        {
            root.Children.Add(new TextBlock { Text = "No hourly data available.", Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x88, 0x99)), FontSize = 11 });
        }
    }

    private static Canvas BuildHourlyGraph(
        List<(DateTime Time, double Temp, double Precip, double WindKmh, int Wmo, int Humidity)> hourly,
        double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height, Margin = new Thickness(0, 4, 0, 4) };
        if (hourly.Count < 2) return canvas;

        double allMax = hourly.Max(h => h.Temp);
        double allMin = hourly.Min(h => h.Temp);
        double range  = allMax - allMin; if (range < 1) range = 1;
        double padTop = 10, padBot = 14;
        double graphH = height - padTop - padBot;
        double segW   = (width - 4) / (hourly.Count - 1);

        var points = new PointCollection();
        for (int i = 0; i < hourly.Count; i++)
        {
            double x = 2 + i * segW;
            double y = padTop + (1 - (hourly[i].Temp - allMin) / range) * graphH;
            points.Add(new Point(x, y));
        }

        var poly = new System.Windows.Shapes.Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.FromRgb(0xff, 0x99, 0x44)),
            StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round
        };
        canvas.Children.Add(poly);

        for (int i = 0; i < hourly.Count; i += 3)
        {
            double x = 2 + i * segW;
            var lbl = new TextBlock
            {
                Text = hourly[i].Time.ToString("HH"), FontSize = 7,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x88, 0xaa))
            };
            Canvas.SetLeft(lbl, x - 6); Canvas.SetTop(lbl, height - padBot + 2);
            canvas.Children.Add(lbl);
        }

        var border = new System.Windows.Shapes.Rectangle
        {
            Width = width, Height = height,
            Stroke = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)),
            StrokeThickness = 1, Fill = Brushes.Transparent, RadiusX = 3, RadiusY = 3
        };
        Canvas.SetLeft(border, 0); Canvas.SetTop(border, 0);
        canvas.Children.Add(border);
        return canvas;
    }

    private async Task BuildMonthCalendarView(StackPanel sp, string city, DateTime month)
    {
        sp.Children.Clear();
        sp.Children.Add(new TextBlock { Text = "Loading…", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xaa)), FontSize = 11, Margin = new Thickness(0, 6, 0, 6) });

        int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var startDate   = new DateTime(month.Year, month.Month, 1);
        var endDate     = startDate.AddDays(daysInMonth - 1);
        var data        = await FetchWeatherRangeAsync(startDate, endDate);
        var dataDict    = data.ToDictionary(d => d.Date.Date, d => d);

        sp.Children.Clear();

        // Nav row
        var nav = new Grid { Margin = new Thickness(0, 2, 0, 8) };
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var prevBtn = NavBtn("‹"); var nextBtn = NavBtn("›");
        var monthLbl = new TextBlock
        {
            Text = month.ToString("MMMM yyyy"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold
        };
        Grid.SetColumn(prevBtn, 0); Grid.SetColumn(monthLbl, 1); Grid.SetColumn(nextBtn, 2);
        nav.Children.Add(prevBtn); nav.Children.Add(monthLbl); nav.Children.Add(nextBtn);
        sp.Children.Add(nav);

        // DOW headers
        var dowRow = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (d, wk) in new[] { ("Mo", false), ("Tu", false), ("We", false), ("Th", false), ("Fr", false), ("Sa", true), ("Su", true) })
            dowRow.Children.Add(new TextBlock { Text = d, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(wk ? Color.FromRgb(0x55, 0x88, 0xff) : Color.FromRgb(0x55, 0x55, 0x55)), FontSize = 10, FontWeight = FontWeights.Bold });
        sp.Children.Add(dowRow);

        // Day grid
        var dayGrid = new UniformGrid { Rows = 6, Columns = 7 };
        int startDow = ((int)startDate.DayOfWeek + 6) % 7;
        for (int i = 0; i < 42; i++)
        {
            int dn = i - startDow + 1;
            var cell = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(4) };
            if (dn >= 1 && dn <= daysInMonth)
            {
                var d2    = startDate.AddDays(dn - 1);
                bool isTd = d2.Date == DateTime.Today;
                bool isWk = d2.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                cell.Background      = new SolidColorBrush(isTd ? Color.FromRgb(0x1a, 0x44, 0x72) : Color.FromRgb(0x10, 0x1a, 0x2e));
                cell.BorderBrush     = new SolidColorBrush(isTd ? Color.FromRgb(0x2e, 0x6a, 0xa0) : Color.FromRgb(0x1e, 0x2e, 0x44));
                cell.BorderThickness = new Thickness(1);
                cell.Cursor          = Cursors.Hand;

                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                content.Children.Add(new TextBlock
                {
                    Text = dn.ToString(), FontSize = 11, FontWeight = isTd ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(isTd ? Color.FromRgb(0xaa, 0xdd, 0xff) : isWk ? Color.FromRgb(0x55, 0x88, 0xff) : Color.FromRgb(0xcc, 0xcc, 0xcc))
                });
                if (dataDict.TryGetValue(d2.Date, out var dd))
                {
                    content.Children.Add(new TextBlock { Text = WmoCodeToIcon(dd.WmoCode), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
                    content.Children.Add(new TextBlock
                    {
                        Text = $"{dd.Max:F0}/{dd.Min:F0}", FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0xbb, 0xdd))
                    });
                }
                cell.Child = content;

                var captureDate = d2;
                int captureCode = dataDict.TryGetValue(d2.Date, out var cd) ? cd.WmoCode : 1;
                double captureMax = dataDict.TryGetValue(d2.Date, out var cm) ? cm.Max : 0;
                double captureMin = dataDict.TryGetValue(d2.Date, out var cmin) ? cmin.Min : 0;

                cell.MouseEnter += (_, _) => cell.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x28, 0x44));
                cell.MouseLeave += (_, _) => cell.Background = new SolidColorBrush(isTd ? Color.FromRgb(0x1a, 0x44, 0x72) : Color.FromRgb(0x10, 0x1a, 0x2e));
                cell.MouseLeftButtonUp += (_, _) => _ = OpenWeatherDayDetailAsync(captureDate, captureCode, captureMax, captureMin);
            }
            else
            {
                cell.Background = Brushes.Transparent;
            }
            dayGrid.Children.Add(cell);
        }
        sp.Children.Add(dayGrid);

        prevBtn.Click += async (_, _) => await BuildMonthCalendarView(sp, city, month.AddMonths(-1));
        nextBtn.Click += async (_, _) => await BuildMonthCalendarView(sp, city, month.AddMonths(1));
    }

    private async Task BuildYearlyView(StackPanel sp, string city, int year)
    {
        sp.Children.Clear();

        var yearNav = new Grid { Margin = new Thickness(0, 2, 0, 10) };
        yearNav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        yearNav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        yearNav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var yPrev = NavBtn("‹"); var yNext = NavBtn("›");
        var yLbl  = new TextBlock
        {
            Text = year.ToString(), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold
        };
        Grid.SetColumn(yPrev, 0); Grid.SetColumn(yLbl, 1); Grid.SetColumn(yNext, 2);
        yearNav.Children.Add(yPrev); yearNav.Children.Add(yLbl); yearNav.Children.Add(yNext);
        sp.Children.Add(yearNav);

        sp.Children.Add(new TextBlock { Text = "Loading yearly data…", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xaa)), FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });

        var startY = new DateTime(year, 1, 1);
        var endY   = new DateTime(year, 12, 31);
        var yearData = await FetchWeatherRangeAsync(startY, endY);
        var byMonth  = yearData.GroupBy(d => d.Date.Month).ToDictionary(g => g.Key, g => g.ToList());

        var loadingTx = sp.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text.StartsWith("Loading yearly"));
        if (loadingTx != null) sp.Children.Remove(loadingTx);

        string[] monthNames = { "January","February","March","April","May","June","July","August","September","October","November","December" };
        for (int m = 1; m <= 12; m++)
        {
            var mMonth = m;
            var mPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
            var mBorder = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x0e, 0x18, 0x2e)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1e, 0x2e, 0x44)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand
            };

            var mg = new Grid();
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

            bool hasData = byMonth.TryGetValue(m, out var mDays) && mDays.Count > 0;
            int  domCode = hasData ? mDays!.GroupBy(d => d.WmoCode).OrderByDescending(g => g.Count()).First().Key : 1;
            double avgHi = hasData ? mDays!.Average(d => d.Max) : 0;
            double avgLo = hasData ? mDays!.Average(d => d.Min) : 0;

            var mNameLbl = new TextBlock { Text = monthNames[m - 1], Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var mIconLbl = new TextBlock { Text = WmoCodeToIcon(domCode), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            var mTempLbl = new TextBlock
            {
                Text = hasData ? $"avg {avgHi:F0}° / {avgLo:F0}°" : "No data",
                Foreground = new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd)), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas")
            };
            var mDaysLbl = new TextBlock
            {
                Text = hasData ? $"{mDays!.Count} days" : "",
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x88, 0xaa)), FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right
            };
            var mArrow = new TextBlock { Text = "›", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0x88)), VerticalAlignment = VerticalAlignment.Center };

            Grid.SetColumn(mNameLbl, 0); Grid.SetColumn(mIconLbl, 1); Grid.SetColumn(mTempLbl, 2); Grid.SetColumn(mDaysLbl, 3); Grid.SetColumn(mArrow, 4);
            mg.Children.Add(mNameLbl); mg.Children.Add(mIconLbl); mg.Children.Add(mTempLbl); mg.Children.Add(mDaysLbl); mg.Children.Add(mArrow);
            mBorder.Child = mg;
            mPanel.Children.Add(mBorder);

            bool expanded = false;
            mBorder.MouseEnter += (_, _) => mBorder.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x24, 0x3e));
            mBorder.MouseLeave += (_, _) => mBorder.Background = new SolidColorBrush(Color.FromRgb(0x0e, 0x18, 0x2e));
            mBorder.MouseLeftButtonUp += async (_, _) =>
            {
                expanded = !expanded;
                mArrow.Text = expanded ? "⌄" : "›";
                if (expanded)
                {
                    var subSv  = new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(4, 4, 4, 0) };
                    var subSp  = new StackPanel();
                    subSv.Content = subSp;
                    mPanel.Children.Add(subSv);
                    subSp.Children.Add(new TextBlock { Text = "Loading…", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xaa)), FontSize = 11 });
                    var monthDate = new DateTime(year, mMonth, 1);
                    await BuildMonthCalendarView(subSp, city, monthDate);
                }
                else
                {
                    if (mPanel.Children.Count > 1) mPanel.Children.RemoveAt(1);
                }
            };

            sp.Children.Add(mPanel);
        }

        yPrev.Click += async (_, _) => await BuildYearlyView(sp, city, year - 1);
        yNext.Click += async (_, _) => await BuildYearlyView(sp, city, year + 1);
    }

    private async Task FetchWeatherDetailAsync(string city)
    {
        // ── Primary: Open-Meteo geocoding + comprehensive forecast ────────────
        try
        {
            string geoJson = await _widgetHttp.GetStringAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json");
            using var geoDoc = JsonDocument.Parse(geoJson);

            if (geoDoc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var    loc     = results[0];
                double lat     = loc.GetProperty("latitude").GetDouble();
                double lon     = loc.GetProperty("longitude").GetDouble();
                string name    = loc.TryGetProperty("name",    out var nm) ? nm.GetString() ?? city : city;
                string country = loc.TryGetProperty("country", out var ct) ? ct.GetString() ?? ""   : "";
                string tz      = loc.TryGetProperty("timezone", out var tz2) ? tz2.GetString() ?? "auto" : "auto";

                string wxJson = await _widgetHttp.GetStringAsync(
                    $"https://api.open-meteo.com/v1/forecast" +
                    $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                    $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
                    $"&current=temperature_2m,apparent_temperature,weather_code," +
                    $"wind_speed_10m,wind_direction_10m,wind_gusts_10m," +
                    $"relative_humidity_2m,precipitation,surface_pressure,visibility,uv_index,is_day" +
                    $"&daily=sunrise,sunset,temperature_2m_max,temperature_2m_min," +
                    $"precipitation_sum,wind_speed_10m_max,uv_index_max,precipitation_probability_max" +
                    $"&wind_speed_unit=kmh&timezone={Uri.EscapeDataString(tz)}&forecast_days=1");

                using var wxDoc = JsonDocument.Parse(wxJson);
                var cur   = wxDoc.RootElement.GetProperty("current");
                var daily = wxDoc.RootElement.GetProperty("daily");

                double temp      = cur.GetProperty("temperature_2m").GetDouble();
                double feelsLike = cur.GetProperty("apparent_temperature").GetDouble();
                int    wmoCode   = cur.GetProperty("weather_code").GetInt32();
                double windSpd   = cur.GetProperty("wind_speed_10m").GetDouble();
                int    windDir   = (int)cur.GetProperty("wind_direction_10m").GetDouble();
                double windGust  = cur.TryGetProperty("wind_gusts_10m",       out var wg)  ? wg.GetDouble()  : 0;
                int    humidity  = (int)cur.GetProperty("relative_humidity_2m").GetDouble();
                double pressure  = cur.GetProperty("surface_pressure").GetDouble();
                double precip    = cur.GetProperty("precipitation").GetDouble();
                double uv        = cur.TryGetProperty("uv_index",   out var uvp)  ? uvp.GetDouble()  : 0;
                double visM      = cur.TryGetProperty("visibility",  out var visp) ? visp.GetDouble() : 0;

                string sunriseRaw = "", sunsetRaw = "";
                double tempMax = 0, tempMin = 0, dailyPrecip = 0, rainChance = 0, uvMax = 0;
                try
                {
                    sunriseRaw  = daily.GetProperty("sunrise")[0].GetString()               ?? "";
                    sunsetRaw   = daily.GetProperty("sunset")[0].GetString()                ?? "";
                    tempMax     = daily.GetProperty("temperature_2m_max")[0].GetDouble();
                    tempMin     = daily.GetProperty("temperature_2m_min")[0].GetDouble();
                    dailyPrecip = daily.GetProperty("precipitation_sum")[0].GetDouble();
                    uvMax       = daily.TryGetProperty("uv_index_max", out var uvmx)                         ? uvmx[0].GetDouble() : uv;
                    rainChance  = daily.TryGetProperty("precipitation_probability_max", out var rcp) ? rcp[0].GetDouble()  : 0;
                }
                catch { }

                string sunriseStr = sunriseRaw.Length >= 5 ? sunriseRaw[^5..] : sunriseRaw;
                string sunsetStr  = sunsetRaw.Length  >= 5 ? sunsetRaw[^5..]  : sunsetRaw;
                string visStr     = visM >= 1000 ? $"{visM / 1000.0:F1} km" : $"{visM:F0} m";
                string windLine   = $"{windSpd:F0} km/h {WindDirectionToString(windDir)}" +
                                    (windGust > windSpd + 5 ? $"  (gusts {windGust:F0})" : "");
                string uvLine     = uvMax >= 8 ? $"{uvMax:F0}  ⚠ Very High" :
                                    uvMax >= 6 ? $"{uvMax:F0}  High"         :
                                    uvMax >= 3 ? $"{uvMax:F0}  Moderate"     :
                                                 $"{uvMax:F0}  Low";
                string loc2Line   = name + (string.IsNullOrEmpty(country) ? "" : $", {country}");

                _cachedWeatherLat  = lat;
                _cachedWeatherLon  = lon;
                _cachedWeatherTz   = tz;
                _cachedWeatherCity = city;
                _cachedWxTemp = temp; _cachedWxFeelsLike = feelsLike;
                _cachedWxTempMax = tempMax; _cachedWxTempMin = tempMin;
                _cachedWxHumidity = humidity; _cachedWxWindDir = windDir;
                _cachedWxWindSpd = windSpd; _cachedWxWindGust = windGust;
                _cachedWxPrecip = precip; _cachedWxDailyPrecip = dailyPrecip; _cachedWxRainChance = rainChance;
                _cachedWxPressure = pressure; _cachedWxUv = uvMax; _cachedWxVisM = visM;
                _cachedWxSunrise = sunriseStr; _cachedWxSunset = sunsetStr;
                _cachedWxLoc = name + (string.IsNullOrEmpty(country) ? "" : $", {country}");
                _weatherDetailCache =
                    $"📍 {loc2Line}\n" +
                    $"{WmoCodeToIcon(wmoCode)} {WmoCodeToDescription(wmoCode)}\n\n" +
                    $"🌡  Temp       {temp:F1}°C  (feels {feelsLike:F1}°C)\n" +
                    $"   Hi / Lo    {tempMax:F1}°C  /  {tempMin:F1}°C\n" +
                    $"💧  Humidity   {humidity}%\n" +
                    $"🌬  Wind       {windLine}\n" +
                    $"🌧  Precip     {precip:F1} mm now  |  {dailyPrecip:F1} mm today  ({rainChance:F0}% chance)\n" +
                    $"🔍  Visibility {visStr}\n" +
                    $"⏱  Pressure   {pressure:F0} hPa\n" +
                    $"☀  UV Index   {uvLine}\n" +
                    $"🌅  Sunrise    {sunriseStr}     🌇 Sunset  {sunsetStr}";
                return;
            }
        }
        catch { }

        // ── Fallback: wttr.in (simplified format avoids URL encoding issues) ─
        try
        {
            string raw = await _widgetHttp.GetStringAsync(
                $"https://wttr.in/{Uri.EscapeDataString(city)}?format=4");
            raw = raw.Trim();
            if (raw.Length > 0 && !raw.StartsWith("<") && !raw.StartsWith("Unknown"))
            { _weatherDetailCache = raw; return; }
        }
        catch { }

        _weatherDetailCache = _weatherCache;
    }

    private async Task<List<(DateTime Date, int WmoCode, double Max, double Min, double RainPct, double PrecipMm)>>
        FetchWeatherForecastAsync(string city, int days)
    {
        await EnsureWeatherGeoAsync(city);
        if (_cachedWeatherLat == 0) return new();
        var end = DateTime.Today.AddDays(Math.Clamp(days, 1, 16) - 1);
        return await FetchWeatherRangeAsync(DateTime.Today, end);
    }

    private static List<System.Windows.Media.Color> GetWeatherPalette(int wmoCode)
    {
        if (wmoCode == 0)
            return new() { C(0x2a1200), C(0xffcc00), C(0xff7700), C(0xcc2200), C(0x1e0e00) };
        if (wmoCode is 1 or 2)
            return new() { C(0x0a1e38), C(0x3a9ae0), C(0xffe890), C(0x1a5080) };
        if (wmoCode == 3)
            return new() { C(0x141820), C(0x607088), C(0x404858), C(0x141820) };
        if (wmoCode is 45 or 48)
            return new() { C(0x1e1a10), C(0xb0a078), C(0x706050), C(0x1e1a10) };
        if (wmoCode is 51 or 53 or 55)
            return new() { C(0x0a1828), C(0x3888b8), C(0x204458), C(0x0a1828) };
        if (wmoCode is 61 or 63 or 65 or 80 or 81 or 82)
            return new() { C(0x040c1c), C(0x1a6cd4), C(0x0e2e5c), C(0x2a88e8), C(0x060e20) };
        if (wmoCode is 71 or 73 or 75 or 77 or 85 or 86)
            return new() { C(0x101828), C(0x88b8d8), C(0xc8dce8), C(0x304060), C(0x101828) };
        if (wmoCode is 95 or 96 or 99)
            return new() { C(0x04041a), C(0x180650), C(0xe8de00), C(0x0a0430), C(0x28087a) };
        return new() { C(0x141820), C(0x3a4860), C(0x141820) };
    }

    private void UpdateWeatherWidgetAnimation()
    {
        if (CurrentWidgetMode() != "Weather") return;
        if (_weatherWmoCode < 0) return;
        StopWeatherWidgetAnimation(fadeOut: false);
        StartWeatherWidgetAnimation();
    }

    private void StartWeatherWidgetAnimation()
    {
        if (_weatherWidgetAnimTimer != null) return;
        if (_weatherWmoCode < 0) return;

        var palette = GetWeatherPalette(_weatherWmoCode);
        if (palette.Count < 3) return;

        _weatherWidgetT       = 0.0;
        _weatherWidgetPhase   = 0;
        _weatherWidgetVizTime = 0.0;
        _weatherWidgetAmps    = new double[3];
        _weatherWidgetFade    = 0.0;

        _weatherWidgetBrush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint   = new System.Windows.Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Colors.Transparent, 0.0),
                new GradientStop(Colors.Transparent, 0.5),
                new GradientStop(Colors.Transparent, 1.0),
            }
        };
        HeaderWidgetBorder.Background = _weatherWidgetBrush;

        double fadeStep = 1.0 / (1500.0 / PaletteTickMs);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PaletteTickMs) };
        timer.Tick += (s, e) =>
        {
            var pal = GetWeatherPalette(_weatherWmoCode);
            int n   = pal.Count;
            if (n < 3) return;

            _weatherWidgetT += 0.0018;
            if (_weatherWidgetT >= 1.0)
            {
                _weatherWidgetT = 0.0;
                _weatherWidgetPhase = (_weatherWidgetPhase + 1) % n;
            }
            _weatherWidgetVizTime += 1.0;

            if (_weatherWidgetFade < 1.0)
                _weatherWidgetFade = Math.Min(1.0, _weatherWidgetFade + fadeStep);
            double fa = _weatherWidgetFade;

            double t  = _weatherWidgetT;
            double vt = _weatherWidgetVizTime;
            int    p  = _weatherWidgetPhase;

            var c0 = LerpColor(pal[p % n],         pal[(p + 1) % n], t);
            var c1 = LerpColor(pal[(p + 1) % n],   pal[(p + 2) % n], t);
            var c2 = LerpColor(pal[(p + 2) % n],   pal[p % n],       t);

            double amp1 = ComputeBandAmplitude(vt, _bandFreq[1], _bandPhase[1]);
            const double sa = 0.88;
            _weatherWidgetAmps[1] += (amp1 - _weatherWidgetAmps[1]) * sa;
            amp1 = _weatherWidgetAmps[1];

            c0.A = (byte)(230 * fa);
            c1.A = (byte)((185 + (int)(amp1 * 70)) * fa);
            c2.A = (byte)(220 * fa);

            var freshBrush = new LinearGradientBrush
            {
                StartPoint    = new System.Windows.Point(0, 0),
                EndPoint      = new System.Windows.Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(c0, 0.0),
                    new GradientStop(c1, 0.25 + 0.5 * ((Math.Sin(vt * 0.031) * 0.5) + 0.5)),
                    new GradientStop(c2, 1.0),
                }
            };
            _weatherWidgetBrush = freshBrush;
            HeaderWidgetBorder.Background = freshBrush;
        };

        timer.Start();
        _weatherWidgetAnimTimer = timer;
    }

    private void StopWeatherWidgetAnimation(bool fadeOut = false)
    {
        if (_weatherWidgetAnimTimer == null) return;

        if (!fadeOut)
        {
            _weatherWidgetAnimTimer.Stop();
            _weatherWidgetAnimTimer = null;
            HeaderWidgetBorder.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
            _weatherWidgetBrush = null;
            return;
        }

        var timerRef = _weatherWidgetAnimTimer;
        var brushRef = _weatherWidgetBrush;
        _weatherWidgetAnimTimer = null;
        _weatherWidgetBrush     = null;
        timerRef.Stop();

        if (brushRef == null)
        {
            HeaderWidgetBorder.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
            return;
        }

        int steps = 8; int step = 0;
        HeaderWidgetBorder.Background = brushRef;
        var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        fade.Tick += (s, e) =>
        {
            step++;
            double alpha = 1.0 - step / (double)steps;
            byte a = (byte)(alpha * 255);
            var fadedBrush = new LinearGradientBrush
            {
                StartPoint    = brushRef.StartPoint,
                EndPoint      = brushRef.EndPoint,
                GradientStops = new GradientStopCollection(brushRef.GradientStops.Select(gs =>
                    new GradientStop(Color.FromArgb(a, gs.Color.R, gs.Color.G, gs.Color.B), gs.Offset)))
            };
            HeaderWidgetBorder.Background = fadedBrush;
            if (step >= steps)
            {
                fade.Stop();
                HeaderWidgetBorder.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
            }
        };
        fade.Start();
    }

    // ── Weather widget: adaptive width animation ──────────────────────────────
    // ── Widget marquee & hover-widen ─────────────────────────────────────────
    private void StartWidgetMarquee(string fullText)
    {
        if (string.IsNullOrEmpty(fullText)) return;
        TxtWidget.Text = fullText;
        TxtWidget.HorizontalAlignment = HorizontalAlignment.Left;
        TxtWidget.Margin = new Thickness(6, 0, 0, 0);

        var ft = new System.Windows.Media.FormattedText(
            fullText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(TxtWidget.FontFamily, TxtWidget.FontStyle,
                         TxtWidget.FontWeight, TxtWidget.FontStretch),
            TxtWidget.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        _marqueeTextWidth = ft.Width;
        double containerW = _widgetDefaultWidth - 12;

        if (_marqueeTextWidth <= containerW)
        {
            TxtWidget.HorizontalAlignment = HorizontalAlignment.Center;
            TxtWidget.Margin = new Thickness(0);
            return;
        }

        TxtWidget.RenderTransform = new TranslateTransform(0, 0);
        _marqueeOffset    = -40;
        _isMarqueeRunning = true;

        if (_marqueeTimer == null)
        {
            _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _marqueeTimer.Tick += MarqueeTimer_Tick;
        }
        _marqueeTimer.Start();
    }

    private void MarqueeTimer_Tick(object? sender, EventArgs e)
    {
        _marqueeOffset += 1.5;
        double travel = _marqueeTextWidth - (_widgetDefaultWidth - 12);
        if (_marqueeOffset > travel + 50) _marqueeOffset = -40;
        if (TxtWidget?.RenderTransform is TranslateTransform tt)
            tt.X = -Math.Max(0, _marqueeOffset);
    }

    private void StopWidgetMarquee()
    {
        _marqueeTimer?.Stop();
        _isMarqueeRunning = false;
        _marqueeOffset    = 0;
        if (TxtWidget == null) return;
        TxtWidget.RenderTransform     = Transform.Identity;
        TxtWidget.HorizontalAlignment = HorizontalAlignment.Center;
        TxtWidget.Margin              = new Thickness(0);
    }

    private string GetWidgetMediaTextFull()
    {
        var vidTab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio && t.HasVideo && !t.IsAudioOnlyMode)
                  ?? _allTabs.FirstOrDefault(t => t.HasVideo && !t.IsAudioOnlyMode);
        if (vidTab != null)
        {
            string title = (vidTab.CleanMediaTitle ?? vidTab.Title ?? "").Split('\n')[0].Trim();
            return string.IsNullOrEmpty(title) ? "🎬 No Video" : "🎬 " + title;
        }
        var audTab = _allTabs.FirstOrDefault(t => t.IsPlayingAudio && (!t.HasVideo || t.IsAudioOnlyMode)
                                                   && !string.IsNullOrEmpty(t.CleanMediaTitle))
                  ?? _allTabs.FirstOrDefault(t => (!t.HasVideo || t.IsAudioOnlyMode)
                                                   && !string.IsNullOrEmpty(t.CleanMediaTitle));
        return audTab != null ? "🎵 " + audTab.CleanMediaTitle : "🎵 No Media";
    }

    private void UpdateMediaWidgetWidth()
    {
        if (HeaderWidgetBorder == null) return;
        string fullText = GetWidgetMediaTextFull();
        var ft = new System.Windows.Media.FormattedText(
            fullText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(TxtWidget.FontFamily, TxtWidget.FontStyle,
                         TxtWidget.FontWeight, TxtWidget.FontStretch),
            TxtWidget.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double target = Math.Max(_widgetDefaultWidth, Math.Min(ft.Width + 24, 320));
        AnimateWidgetWidth(target);
    }

    private void HeaderWidget_MouseEnter(object sender, MouseEventArgs e)
    {
        string mode = CurrentWidgetMode();
        if (mode == "Weather" && SettingsService.Current.WeatherWidgetHoverWiden)
            UpdateWeatherWidgetWidth();
        else if ((mode == "Media" || mode == "Music") && SettingsService.Current.MediaWidgetHoverWiden)
            UpdateMediaWidgetWidth();
    }

    private void HeaderWidget_MouseLeave(object sender, MouseEventArgs e)
    {
        string mode = CurrentWidgetMode();
        bool shouldCollapse = (mode == "Weather" && SettingsService.Current.WeatherWidgetHoverWiden)
                           || ((mode == "Media" || mode == "Music") && SettingsService.Current.MediaWidgetHoverWiden);
        if (shouldCollapse) AnimateWidgetWidth(_widgetDefaultWidth);
    }

    private void LstWidgetOrder_RightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);
        if (hit is not ListBoxItem lbi || lbi.Tag is not string key) return;
        if (key != "Weather" && key != "Media" && key != "Music") return;

        bool isWeather  = key == "Weather";
        bool marquee    = isWeather ? SettingsService.Current.WeatherWidgetMarquee    : SettingsService.Current.MediaWidgetMarquee;
        bool hoverWiden = isWeather ? SettingsService.Current.WeatherWidgetHoverWiden : SettingsService.Current.MediaWidgetHoverWiden;
        var  menuFg     = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));

        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
            Placement  = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        var miMarquee = new MenuItem
        {
            Header      = "Marquee text (no auto-widen)",
            IsCheckable = true,
            IsChecked   = marquee,
            Foreground  = menuFg,
            Background  = Brushes.Transparent
        };
        miMarquee.Click += (s, _) =>
        {
            bool v = ((MenuItem)s!).IsChecked;
            if (isWeather) { SettingsService.Current.WeatherWidgetMarquee = v; if (v) SettingsService.Current.WeatherWidgetHoverWiden = false; }
            else           { SettingsService.Current.MediaWidgetMarquee   = v; if (v) SettingsService.Current.MediaWidgetHoverWiden   = false; }
            SettingsService.Save();
            if (!v) StopWidgetMarquee();
            RefreshWidgetDisplay();
        };

        var miHover = new MenuItem
        {
            Header      = "Widen on hover only",
            IsCheckable = true,
            IsChecked   = hoverWiden,
            Foreground  = menuFg,
            Background  = Brushes.Transparent
        };
        miHover.Click += (s, _) =>
        {
            bool v = ((MenuItem)s!).IsChecked;
            if (isWeather) { SettingsService.Current.WeatherWidgetHoverWiden = v; if (v) SettingsService.Current.WeatherWidgetMarquee = false; }
            else           { SettingsService.Current.MediaWidgetHoverWiden   = v; if (v) SettingsService.Current.MediaWidgetMarquee   = false; }
            SettingsService.Save();
            StopWidgetMarquee();
            RefreshWidgetDisplay();
        };

        menu.Items.Add(miMarquee);
        menu.Items.Add(miHover);
        menu.IsOpen = true;
        e.Handled   = true;
    }

    private void AnimateWidgetWidth(double targetWidth)
    {
        if (HeaderWidgetBorder == null) return;
        if (Math.Abs(HeaderWidgetBorder.Width - targetWidth) < 1.0) return;
        var anim = new DoubleAnimation(targetWidth,
            new Duration(TimeSpan.FromMilliseconds(300)))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        HeaderWidgetBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void UpdateWeatherWidgetWidth()
    {
        if (HeaderWidgetBorder == null || string.IsNullOrEmpty(_weatherCache)) return;
        var ft = new System.Windows.Media.FormattedText(
            _weatherCache,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(TxtWidget.FontFamily, TxtWidget.FontStyle,
                         TxtWidget.FontWeight, TxtWidget.FontStretch),
            TxtWidget.FontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double needed = ft.Width + 24; // 12 px padding each side
        double target = Math.Max(_widgetDefaultWidth, Math.Min(needed, 320));
        AnimateWidgetWidth(target);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SLEEPING TABS  (Edge-style: suspend background tabs after inactivity)
    // ══════════════════════════════════════════════════════════════════════════

    private void InitSleepingTabs()
    {
        TabSleepRulesService.Load();
        _sleepTimer.Tick += (s, e) => CheckSleepingTabs();
        _sleepTimer.Start();
        // Mark the initial active tab as recently active
        if (GetCurrentTabViewModel() is TabViewModel t) _tabLastActive[t] = DateTime.UtcNow;
    }

    private HashSet<TabViewModel> GetProtectedTabs()
    {
        return _tabClickCount
            .OrderByDescending(kv => kv.Value)
            .Take(SLEEP_PROTECT_N)
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    private void CheckSleepingTabs()
    {
        if (!SettingsService.Current.SleepingTabsEnabled) return;

        long mb       = GetCachedRamMb();
        int  tabCount = _allTabs.Count;
        var  active   = GetCurrentTabViewModel();
        var  protect  = GetProtectedTabs();

        if (mb < SLEEP_RAM_MODERATE_MB && tabCount < SLEEP_MIN_TABS)
        {
            foreach (var tab in _allTabs)
                if (tab.IsSleeping && (tab == active || tab.IsPlayingAudio)) WakeTab(tab);
            CheckMemoryPressure();
            return;
        }

        double multiplier = mb >= SLEEP_RAM_HIGH_MB    ? 0.75
                          : mb >= SLEEP_RAM_MODERATE_MB ? 1.0
                          : 2.0;
        var threshold = TimeSpan.FromMinutes(Math.Max(1, SettingsService.Current.SleepingTabsMinutes) * multiplier);

        foreach (var tab in _allTabs)
        {
            if (tab == active || tab.IsPlayingAudio || tab.IsActiveDownload)
            {
                _tabLastActive[tab] = DateTime.UtcNow;
                WakeTab(tab);
                continue;
            }

            // NeverSleep: wake it if it somehow ended up sleeping, then always skip
            if (tab.NeverSleep)
            {
                if (tab.IsSleeping) WakeTab(tab);
                continue;
            }

            if (protect.Contains(tab))
            {
                if (!_tabLastActive.ContainsKey(tab)) _tabLastActive[tab] = DateTime.UtcNow;
                continue;
            }

            if (!_tabLastActive.TryGetValue(tab, out var lastActive))
            {
                _tabLastActive[tab] = DateTime.UtcNow;
                continue;
            }

            // Per-tab idle threshold: custom override takes priority over global + RAM multiplier
            double tabIdleMinutes = tab.SleepIdleMinutesOverride.HasValue
                ? tab.SleepIdleMinutesOverride.Value
                : Math.Max(1, SettingsService.Current.SleepingTabsMinutes) * multiplier;
            var tabThreshold = TimeSpan.FromMinutes(tabIdleMinutes);

            // Per-tab RAM threshold: skip sleep when app RAM is below the tab's custom value
            if (tab.SleepRamThresholdMbOverride.HasValue && mb < tab.SleepRamThresholdMbOverride.Value)
                continue;

            if (DateTime.UtcNow - lastActive > tabThreshold && !tab.IsSleeping)
                SleepTab(tab);
        }

        CheckMemoryPressure();
    }

    private void CheckMemoryPressure()
    {
        long mb = GetCachedRamMb();

        if (mb < SLEEP_RAM_HIGH_MB) return;

        int toSleep = Math.Min(4, (int)Math.Ceiling((mb - (SLEEP_RAM_HIGH_MB - 1)) / 200.0));

        var active  = GetCurrentTabViewModel();
        int total   = _allTabs.Count;
        var protect = GetProtectedTabs();

        var candidates = _allTabs
            .Select((tab, idx) =>
            {
                if (tab == active || tab.IsSleeping || tab.IsPlayingAudio || tab.IsActiveDownload || protect.Contains(tab) || tab.NeverSleep)
                    return (tab, score: -1.0);

                double idleMin = _tabLastActive.TryGetValue(tab, out var dt)
                               ? (DateTime.UtcNow - dt).TotalMinutes
                               : 999.0;

                double clickPenalty  = _tabClickCount.TryGetValue(tab, out int clicks) ? clicks * 0.5 : 0.0;
                double positionScore = total > 1 ? (double)idx / (total - 1) : 0.0;
                double score         = (idleMin * 3.0) + positionScore - clickPenalty;
                return (tab, score);
            })
            .Where(x => x.score >= 0)
            .OrderByDescending(x => x.score)
            .Take(toSleep)
            .Select(x => x.tab)
            .ToList();

        foreach (var tab in candidates)
            SleepTab(tab);

        if (candidates.Count > 0)
            LogService.Write("MEM", $"Pressure: {mb} MB — queued {candidates.Count} tab(s) for sleep");
    }

    private async void SleepTab(TabViewModel tab)
    {
        if (tab.IsSleeping || tab.NeverSleep) return;
        if (!_tabViews.TryGetValue(tab, out var bv)) return;
        try
        {
            if (bv.MainWebView?.CoreWebView2 != null)
            {
                _tabSleepUrls[tab] = tab.Url;
                tab.IsSleeping = true;

                // Attempt soft suspend first — freezes renderer in place,
                // page state is preserved and wake is instant (no reload).
                bool suspended = await bv.MainWebView.CoreWebView2.TrySuspendAsync();
                if (!suspended)
                {
                    // Page refused soft suspend (active WebSocket, service worker, etc.)
                    // Fall back to hard discard so memory is still freed.
                    bv.MainWebView.CoreWebView2.Navigate("about:blank");
                }

                LogService.Write("SLEEP", $"Tab sleeping ({(suspended ? "soft" : "hard")}): {tab.Title}");
            }
        }
        catch { }
    }
    
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }
    
    private void WakeTab(TabViewModel tab)
    {
        if (!tab.IsSleeping) return;
        if (!_tabViews.TryGetValue(tab, out var bv)) return;
        try
        {
            string wakeUrl = _tabSleepUrls.TryGetValue(tab, out var stored) ? stored : tab.Url;

            // Guard: if the stored URL is blank (tab was still initialising when
            // sleep fired, or was already on about:blank), fall back to the homepage
            // so the tab always wakes to something navigable.
            if (string.IsNullOrEmpty(wakeUrl) || wakeUrl == "about:blank")
                wakeUrl = SettingsService.Current.HomePage;

            _tabSleepUrls.Remove(tab);
            tab.IsSleeping = false;
            LogService.Write("SLEEP", $"Tab waking: {tab.Title}");

            if (bv.MainWebView?.CoreWebView2 != null)
            {
                if (bv.MainWebView.CoreWebView2.IsSuspended)
                {
                    // Soft-suspended: resume in place, page state intact, no reload
                    bv.MainWebView.CoreWebView2.Resume();
                    _tabSleepUrls.Remove(tab);
                    return;
                }
                // Hard-discarded: navigate back to original URL
                bv.MainWebView.CoreWebView2.Navigate(wakeUrl);
            }
            else
            {
                // CoreWebView2 is null — the process crashed while this tab was
                // sleeping (about:blank) so there was no visible crash page and
                // no ProcessFailed auto-recovery triggered a Reload.
                // Reinitialize the environment and the control before navigating.
                string capturedUrl = wakeUrl;
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var env3 = CurrentBrowser?.MainWebView?.CoreWebView2?.Environment;
                        await bv.MainWebView!.EnsureCoreWebView2Async(env3);
                        bv.Navigate(capturedUrl);
                    }
                    catch (Exception ex)
                    {
                        LogService.RecordCrash(ex, "WakeTab reinit");
                    }
                });
            }
        }
        catch { }
    }

    private void AttachSidebarSmoothScroll()
    {
        var sv = FindVisualChild<ScrollViewer>(RightSidebar);
        if (sv == null) return;
        sv.PreviewMouseWheel += (sender, e) =>
        {
            if (sender is not ScrollViewer s) return;
            double scrollMultiplier = SettingsService.Current.ScrollSpeedMultiplier;
            _sidebarScrollTarget = Math.Clamp(
                _sidebarScrollTarget - e.Delta * (0.610 * scrollMultiplier),
                0, s.ScrollableHeight);
            s.ScrollToVerticalOffset(_sidebarScrollTarget);
            e.Handled = true;
        };
    }
}