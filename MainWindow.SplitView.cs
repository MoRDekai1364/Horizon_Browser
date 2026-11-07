using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth;

public partial class MainWindow : Window
{
    private const double SplitPaneMinWidth = 260;
    private const double SplitPaneMinHeight = 140;
    private const double SplitDividerWidth = 6;
    private const double SplitIndicatorHeight = 3;
    private const int SplitMaxSlots = 6;

    private sealed class SplitCell
    {
        public int Col;
        public int ColSpan;
        public int Row;
        public int RowSpan;
        public int XA;
        public int XB;
        public int YA;
        public int YB;
    }

    private sealed class SplitHDivider
    {
        public GridSplitter Splitter = null!;
        public int Edge;
        public int ColInterval;
    }

    private bool _splitActive;
    private bool _splitPicking;
    private bool _splitAwaitNewTab;
    private readonly TabViewModel?[] _splitSlots = new TabViewModel?[SplitMaxSlots];
    private string _splitLayoutId = SplitLayoutCatalog.DefaultId;
    private SplitLayoutDef? _splitLayout;
    private SplitCell[] _splitCells = Array.Empty<SplitCell>();
    private double _splitMinWidth;
    private readonly List<GridSplitter> _splitVDividers = new();
    private readonly List<SplitHDivider> _splitHDividers = new();
    private Border?[] _splitIndicators = Array.Empty<Border?>();
    private readonly Border?[] _splitPlaceholders = new Border?[SplitMaxSlots];
    private Border? _splitPickerHost;
    private Window? _splitPickerWindow;
    private readonly HashSet<Controls.BrowserView> _splitFocusHooked = new();

    public bool IsSplitViewActive => _splitActive;

    private SplitMonitorShape SplitCurrentMonitorShape()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return SplitMonitorShape.Standard;
            var mi = new WMI { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(WMI)) };
            if (!GetMonitorInfo(monitor, ref mi)) return SplitMonitorShape.Standard;
            double w = mi.rcMonitor.right - mi.rcMonitor.left;
            double h = mi.rcMonitor.bottom - mi.rcMonitor.top;
            if (w <= 0 || h <= 0) return SplitMonitorShape.Standard;
            var shape = SplitLayoutCatalog.ShapeOf(w / h);
            LogService.Debug("split", () => $"monitor {w:F0}x{h:F0} shape={shape}");
            return shape;
        }
        catch (Exception ex)
        {
            LogService.Debug("split", () => $"monitor shape failed: {ex.Message}");
            return SplitMonitorShape.Standard;
        }
    }

    private IReadOnlyList<SplitLayoutDef> SplitLayoutsForCurrentMonitor()
    {
        return SplitLayoutCatalog.For(SplitCurrentMonitorShape());
    }

    private Brush SplitBrush(string key, Brush fallback)
    {
        return TryFindResource(key) as Brush ?? fallback;
    }

    private TabViewModel? SplitTabOf(Controls.BrowserView? view)
    {
        if (view == null) return null;
        foreach (var kv in _tabViews)
        {
            if (ReferenceEquals(kv.Value, view)) return kv.Key;
        }
        return null;
    }

    private void SplitSelectTab(TabViewModel tab)
    {
        if (Tabs.Contains(tab))
        {
            ListTabs.SelectedItem = tab;
            ListOverflowTabs.SelectedItem = null;
        }
        else if (OverflowTabs.Contains(tab))
        {
            ListOverflowTabs.SelectedItem = tab;
            ListTabs.SelectedItem = null;
        }
    }

    private void SplitHookFocus(Controls.BrowserView view)
    {
        if (!_splitFocusHooked.Add(view)) return;
        view.MainWebView.GotFocus += (_, _) =>
        {
            if (!_splitActive || _splitPicking) return;
            var tab = SplitTabOf(view);
            if (tab != null && !ReferenceEquals(tab, CurrentTab)) SplitSelectTab(tab);
        };
    }


    private int SplitSlotOf(TabViewModel? tab)
    {
        if (tab == null) return -1;
        for (int i = 0; i < SplitMaxSlots; i++)
        {
            if (ReferenceEquals(_splitSlots[i], tab)) return i;
        }
        return -1;
    }

    private int SplitFirstEmptySlot()
    {
        int n = _splitLayout?.Slots.Count ?? 0;
        for (int i = 0; i < n && i < SplitMaxSlots; i++)
        {
            if (_splitSlots[i] == null) return i;
        }
        return -1;
    }

    private void SplitPlaceElement(FrameworkElement element, int slot)
    {
        if (slot < 0 || slot >= _splitCells.Length) return;
        var c = _splitCells[slot];
        Grid.SetColumn(element, c.Col);
        Grid.SetColumnSpan(element, c.ColSpan);
        Grid.SetRow(element, c.Row);
        Grid.SetRowSpan(element, c.RowSpan);
    }

    private static List<double> SplitEdges(IEnumerable<double> values)
    {
        var list = new List<double>();
        foreach (var v in values.OrderBy(x => x))
        {
            if (list.Count == 0 || v - list[list.Count - 1] > 0.0001) list.Add(v);
        }
        return list;
    }

    private static int SplitEdgeIndex(List<double> edges, double v)
    {
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < edges.Count; i++)
        {
            double d = Math.Abs(edges[i] - v);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private static int SplitColumnCount(SplitLayoutDef def)
    {
        return SplitEdges(def.Slots.SelectMany(s => new[] { s.X, s.X + s.W })).Count - 1;
    }

    private void SplitClearGridChildren()
    {
        foreach (var d in _splitVDividers) ViewContainer.Children.Remove(d);
        _splitVDividers.Clear();
        foreach (var h in _splitHDividers) ViewContainer.Children.Remove(h.Splitter);
        _splitHDividers.Clear();
        foreach (var b in _splitIndicators)
        {
            if (b != null) ViewContainer.Children.Remove(b);
        }
        _splitIndicators = Array.Empty<Border?>();
        for (int i = 0; i < _splitPlaceholders.Length; i++)
        {
            var ph = _splitPlaceholders[i];
            if (ph == null) continue;
            ViewContainer.Children.Remove(ph);
            _splitPlaceholders[i] = null;
        }
    }

    private GridSplitter SplitCreateDivider(bool vertical, Brush background)
    {
        var sp = new GridSplitter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = false,
            Background = background
        };
        if (vertical)
        {
            sp.Width = SplitDividerWidth;
            sp.ResizeDirection = GridResizeDirection.Columns;
            sp.Cursor = Cursors.SizeWE;
        }
        else
        {
            sp.Height = SplitDividerWidth;
            sp.ResizeDirection = GridResizeDirection.Rows;
            sp.Cursor = Cursors.SizeNS;
        }
        sp.DragCompleted += (_, _) => SplitSaveRatio();
        return sp;
    }

    private bool _splitAnimateNext;
    private System.Diagnostics.Stopwatch? _splitAnimClock;
    private double[] _splitAnimFromCols = Array.Empty<double>();
    private double[] _splitAnimFromRows = Array.Empty<double>();
    private double[] _splitAnimToCols = Array.Empty<double>();
    private double[] _splitAnimToRows = Array.Empty<double>();
    private bool _splitAnimHooked;

    private static double[] SplitNormalize(double[] v)
    {
        double sum = v.Sum();
        if (sum <= 0) return v.Select(_ => 1.0 / Math.Max(1, v.Length)).ToArray();
        return v.Select(x => x / sum).ToArray();
    }

    private double[]? SplitCaptureWeights(bool columns)
    {
        var list = new List<double>();
        if (columns)
        {
            var cd = ViewContainer.ColumnDefinitions;
            for (int i = 0; i < cd.Count; i += 2) list.Add(cd[i].ActualWidth);
        }
        else
        {
            var rd = ViewContainer.RowDefinitions;
            for (int i = 1; i < rd.Count; i += 2) list.Add(rd[i].ActualHeight);
        }
        if (list.Count == 0 || list.Sum() <= 0) return null;
        return SplitNormalize(list.ToArray());
    }

    private void SplitStopAnimation()
    {
        if (_splitAnimHooked)
        {
            CompositionTarget.Rendering -= SplitAnimTick;
            _splitAnimHooked = false;
        }
        _splitAnimClock = null;
    }

    private void SplitApplyWeights(double[] cols, double[] rows)
    {
        var cd = ViewContainer.ColumnDefinitions;
        var rd = ViewContainer.RowDefinitions;
        for (int i = 0; i < cols.Length && i * 2 < cd.Count; i++)
            cd[i * 2].Width = new GridLength(Math.Max(cols[i], 0.0001), GridUnitType.Star);
        for (int i = 0; i < rows.Length && 1 + i * 2 < rd.Count; i++)
            rd[1 + i * 2].Height = new GridLength(Math.Max(rows[i], 0.0001), GridUnitType.Star);
    }

    private void SplitSetTrackMins(bool restore)
    {
        var cd = ViewContainer.ColumnDefinitions;
        var rd = ViewContainer.RowDefinitions;
        for (int i = 0; i < cd.Count; i += 2) cd[i].MinWidth = restore ? SplitPaneMinWidth : 0;
        for (int i = 1; i < rd.Count; i += 2) rd[i].MinHeight = restore ? SplitPaneMinHeight : 0;
    }

    private void SplitStartAnimation(double[]? prevCols, double[]? prevRows, double[] toCols, double[] toRows)
    {
        SplitStopAnimation();
        var fromCols = prevCols != null && prevCols.Length == toCols.Length ? prevCols : SplitConcentrate(toCols.Length);
        var fromRows = prevRows != null && prevRows.Length == toRows.Length ? prevRows : SplitConcentrate(toRows.Length);
        bool same = fromCols.Zip(toCols, (a, b) => Math.Abs(a - b) < 0.002).All(x => x) &&
                    fromRows.Zip(toRows, (a, b) => Math.Abs(a - b) < 0.002).All(x => x);
        if (same) return;

        _splitAnimFromCols = fromCols;
        _splitAnimFromRows = fromRows;
        _splitAnimToCols = toCols;
        _splitAnimToRows = toRows;
        SplitSetTrackMins(false);
        SplitApplyWeights(fromCols, fromRows);
        _splitAnimClock = System.Diagnostics.Stopwatch.StartNew();
        CompositionTarget.Rendering += SplitAnimTick;
        _splitAnimHooked = true;
    }

    private static double[] SplitConcentrate(int n)
    {
        var v = new double[n];
        if (n == 0) return v;
        if (n == 1)
        {
            v[0] = 1;
            return v;
        }
        double rest = 0.04 / (n - 1);
        for (int i = 0; i < n; i++) v[i] = i == 0 ? 0.96 : rest;
        return v;
    }

    private void SplitAnimTick(object? sender, EventArgs e)
    {
        if (_splitAnimClock == null || !_splitActive || _splitLayout == null)
        {
            SplitStopAnimation();
            return;
        }
        double t = Math.Min(1.0, _splitAnimClock.Elapsed.TotalMilliseconds / 170.0);
        double eased = 1 - Math.Pow(1 - t, 3);
        var cols = new double[_splitAnimToCols.Length];
        var rows = new double[_splitAnimToRows.Length];
        for (int i = 0; i < cols.Length; i++) cols[i] = _splitAnimFromCols[i] + (_splitAnimToCols[i] - _splitAnimFromCols[i]) * eased;
        for (int i = 0; i < rows.Length; i++) rows[i] = _splitAnimFromRows[i] + (_splitAnimToRows[i] - _splitAnimFromRows[i]) * eased;
        SplitApplyWeights(cols, rows);
        if (t >= 1.0)
        {
            SplitStopAnimation();
            SplitApplyWeights(_splitAnimToCols, _splitAnimToRows);
            SplitSetTrackMins(true);
        }
    }

    private void SplitBuildLayout(SplitLayoutDef def)
    {
        bool animate = _splitAnimateNext;
        _splitAnimateNext = false;
        var prevCols = animate ? SplitCaptureWeights(true) : null;
        var prevRows = animate ? SplitCaptureWeights(false) : null;
        SplitStopAnimation();
        ViewContainer.SizeChanged -= SplitContainerSizeChanged;
        SplitClearGridChildren();
        ViewContainer.ColumnDefinitions.Clear();
        ViewContainer.RowDefinitions.Clear();

        _splitLayout = def;
        _splitLayoutId = def.Id;

        var xs = SplitEdges(def.Slots.SelectMany(s => new[] { s.X, s.X + s.W }));
        var ys = SplitEdges(def.Slots.SelectMany(s => new[] { s.Y, s.Y + s.H }));
        int cols = xs.Count - 1;
        int rows = ys.Count - 1;
        bool ratioLayout = string.Equals(def.Id, SplitLayoutCatalog.DefaultId, StringComparison.OrdinalIgnoreCase) && cols == 2 && rows == 1;
        double ratio = Math.Clamp(SettingsService.Current.SplitViewRatio, 0.2, 0.8);
        SplitLoadRatios(def.Id, cols, rows, out var colW, out var rowW);
        SplitRememberLayout(def.Id);

        var targetRows = new double[rows];
        var targetCols = new double[cols];
        for (int r = 0; r < rows; r++) targetRows[r] = rowW != null ? rowW[r] : ys[r + 1] - ys[r];
        for (int c = 0; c < cols; c++) targetCols[c] = colW != null ? colW[c] : (ratioLayout ? (c == 0 ? ratio : 1 - ratio) : xs[c + 1] - xs[c]);
        targetRows = SplitNormalize(targetRows);
        targetCols = SplitNormalize(targetCols);

        ViewContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitIndicatorHeight) });
        for (int r = 0; r < rows; r++)
        {
            if (r > 0) ViewContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ViewContainer.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(targetRows[r], GridUnitType.Star),
                MinHeight = SplitPaneMinHeight
            });
        }
        for (int c = 0; c < cols; c++)
        {
            if (c > 0) ViewContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ViewContainer.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(targetCols[c], GridUnitType.Star),
                MinWidth = SplitPaneMinWidth
            });
        }

        _splitCells = new SplitCell[def.Slots.Count];
        for (int i = 0; i < def.Slots.Count; i++)
        {
            var s = def.Slots[i];
            var cell = new SplitCell
            {
                XA = SplitEdgeIndex(xs, s.X),
                XB = SplitEdgeIndex(xs, s.X + s.W),
                YA = SplitEdgeIndex(ys, s.Y),
                YB = SplitEdgeIndex(ys, s.Y + s.H)
            };
            cell.Col = 2 * cell.XA;
            cell.ColSpan = 2 * (cell.XB - cell.XA) - 1;
            cell.Row = 1 + 2 * cell.YA;
            cell.RowSpan = 2 * (cell.YB - cell.YA) - 1;
            _splitCells[i] = cell;
        }

        _splitIndicators = new Border?[def.Slots.Count];
        for (int i = 0; i < _splitCells.Length; i++)
        {
            var cell = _splitCells[i];
            if (cell.YA != 0) continue;
            var b = new Border { Background = Brushes.Transparent };
            Grid.SetRow(b, 0);
            Grid.SetColumn(b, cell.Col);
            Grid.SetColumnSpan(b, cell.ColSpan);
            ViewContainer.Children.Add(b);
            _splitIndicators[i] = b;
        }

        var chrome = SplitBrush("Brush_SurfaceChrome", Brushes.DimGray);

        for (int e = 1; e < cols; e++)
        {
            int runStart = -1;
            for (int r = 0; r <= rows; r++)
            {
                int rr = r;
                bool has = r < rows && _splitCells.Any(c => c.XB == e && c.YA <= rr && rr < c.YB);
                if (has && runStart < 0)
                {
                    runStart = r;
                }
                else if (!has && runStart >= 0)
                {
                    var sp = SplitCreateDivider(true, chrome);
                    Grid.SetColumn(sp, 2 * e - 1);
                    Grid.SetRow(sp, 1 + 2 * runStart);
                    Grid.SetRowSpan(sp, 2 * (r - runStart) - 1);
                    ViewContainer.Children.Add(sp);
                    _splitVDividers.Add(sp);
                    runStart = -1;
                }
            }
        }

        for (int e = 1; e < rows; e++)
        {
            for (int j = 0; j < cols; j++)
            {
                int jj = j;
                int ee = e;
                bool has = _splitCells.Any(c => c.YB == ee && c.XA <= jj && jj < c.XB);
                if (!has) continue;
                var sp = SplitCreateDivider(false, chrome);
                Grid.SetRow(sp, 2 * e);
                Grid.SetColumn(sp, 2 * j);
                Grid.SetColumnSpan(sp, 1);
                ViewContainer.Children.Add(sp);
                _splitHDividers.Add(new SplitHDivider { Splitter = sp, Edge = e, ColInterval = j });
            }
        }

        foreach (var kv in _tabViews)
        {
            if (SplitSlotOf(kv.Key) >= 0) continue;
            Grid.SetColumn(kv.Value, 0);
            Grid.SetColumnSpan(kv.Value, 1);
            Grid.SetRow(kv.Value, 1);
            Grid.SetRowSpan(kv.Value, 1);
        }

        for (int i = 0; i < def.Slots.Count && i < SplitMaxSlots; i++)
        {
            var tab = _splitSlots[i];
            if (tab != null && _tabViews.TryGetValue(tab, out var view)) SplitPlaceElement(view, i);
        }

        _splitMinWidth = cols * SplitPaneMinWidth + (cols - 1) * SplitDividerWidth;
        ViewContainer.SizeChanged += SplitContainerSizeChanged;
        ViewContainer.LayoutUpdated -= SplitOnLayoutUpdated;
        ViewContainer.LayoutUpdated += SplitOnLayoutUpdated;
        LocationChanged -= SplitOnWindowMoved;
        LocationChanged += SplitOnWindowMoved;
        StateChanged -= SplitOnWindowMoved;
        StateChanged += SplitOnWindowMoved;
        PreviewKeyDown -= SplitOnPreviewKey;
        PreviewKeyDown += SplitOnPreviewKey;
        SplitUpdateIndicator();
        if (animate && (cols > 1 || rows > 1)) SplitStartAnimation(prevCols, prevRows, targetCols, targetRows);
        LogService.Debug("split", () => $"layout {def.Id} cols={cols} rows={rows} slots={def.Slots.Count}");
    }

    private static string SplitFormatWeights(IEnumerable<double> values)
    {
        return string.Join(",", values.Select(v => v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static bool SplitParseWeights(string text, int expected, out double[] result)
    {
        result = Array.Empty<double>();
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != expected) return false;
        var list = new double[expected];
        for (int i = 0; i < expected; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return false;
            if (double.IsNaN(v) || v < 0.05 || v > 1.0) return false;
            list[i] = v;
        }
        result = list;
        return true;
    }

    private static void SplitLoadRatios(string layoutId, int cols, int rows, out double[]? colW, out double[]? rowW)
    {
        colW = null;
        rowW = null;
        var map = SettingsService.Current.SplitViewLayoutRatios;
        if (map == null || !map.TryGetValue(layoutId, out var text) || string.IsNullOrWhiteSpace(text)) return;
        var halves = text.Split('|');
        if (halves.Length != 2) return;
        if (SplitParseWeights(halves[0], cols, out var c) && SplitParseWeights(halves[1], rows, out var r))
        {
            colW = c;
            rowW = r;
        }
    }

    private static void SplitRememberLayout(string layoutId)
    {
        if (string.Equals(SettingsService.Current.SplitViewLastLayout, layoutId, StringComparison.Ordinal)) return;
        SettingsService.Current.SplitViewLastLayout = layoutId;
        SettingsService.Save();
    }

    private void SplitSaveRatio()
    {
        if (_splitLayout == null) return;
        var cd = ViewContainer.ColumnDefinitions;
        var rd = ViewContainer.RowDefinitions;
        var cw = new List<double>();
        for (int i = 0; i < cd.Count; i += 2) cw.Add(cd[i].ActualWidth);
        var rw = new List<double>();
        for (int i = 1; i < rd.Count; i += 2) rw.Add(rd[i].ActualHeight);
        if (cw.Count == 0 || rw.Count == 0) return;
        double ct = cw.Sum();
        double rt = rw.Sum();
        if (ct <= 0 || rt <= 0) return;

        var settings = SettingsService.Current;
        settings.SplitViewLayoutRatios ??= new Dictionary<string, string>();
        settings.SplitViewLayoutRatios[_splitLayoutId] =
            SplitFormatWeights(cw.Select(v => v / ct)) + "|" + SplitFormatWeights(rw.Select(v => v / rt));

        if (string.Equals(_splitLayoutId, SplitLayoutCatalog.DefaultId, StringComparison.OrdinalIgnoreCase) && cw.Count == 2 && rw.Count == 1)
        {
            settings.SplitViewRatio = Math.Clamp(cw[0] / ct, 0.2, 0.8);
        }
        SettingsService.Save();
    }

    private void SplitContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_splitActive) return;
        if (e.NewSize.Width <= 0) return;
        if (e.NewSize.Width < _splitMinWidth)
        {
            ExitSplitView();
        }
    }

    private void SplitUpdateIndicator()
    {
        if (!_splitActive || _splitLayout == null) return;
        int active = SplitSlotOf(CurrentTab);
        var accent = SplitBrush("Brush_Accent", Brushes.Gray);
        var chrome = SplitBrush("Brush_SurfaceChrome", Brushes.DimGray);
        for (int i = 0; i < _splitIndicators.Length; i++)
        {
            var b = _splitIndicators[i];
            if (b != null) b.Background = i == active ? accent : Brushes.Transparent;
        }
        SplitCell? ac = active >= 0 && active < _splitCells.Length ? _splitCells[active] : null;
        foreach (var h in _splitHDividers)
        {
            bool hit = ac != null && ac.YA == h.Edge && ac.XA <= h.ColInterval && h.ColInterval < ac.XB;
            h.Splitter.Background = hit ? accent : chrome;
        }
    }

    private void SplitScheduleIndicator()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(SplitUpdateIndicator));
    }

    private void SplitRemoveLayout()
    {
        SplitStopAnimation();
        ViewContainer.SizeChanged -= SplitContainerSizeChanged;
        ViewContainer.LayoutUpdated -= SplitOnLayoutUpdated;
        LocationChanged -= SplitOnWindowMoved;
        StateChanged -= SplitOnWindowMoved;
        SplitDestroyGrips();
        SplitClosePicker();
        SplitClearGridChildren();
        if (_splitPickerHost != null)
        {
            ViewContainer.Children.Remove(_splitPickerHost);
            _splitPickerHost = null;
        }
        foreach (var view in _tabViews.Values)
        {
            Grid.SetColumn(view, 0);
            Grid.SetColumnSpan(view, 1);
            Grid.SetRow(view, 0);
            Grid.SetRowSpan(view, 1);
        }
        ViewContainer.ColumnDefinitions.Clear();
        ViewContainer.RowDefinitions.Clear();
        _splitLayout = null;
        _splitCells = Array.Empty<SplitCell>();
    }

    private void SplitSetSlot(int slot, TabViewModel tab)
    {
        if (!_tabViews.TryGetValue(tab, out var view)) return;
        _splitSlots[slot] = tab;
        var ph = _splitPlaceholders[slot];
        if (ph != null)
        {
            ViewContainer.Children.Remove(ph);
            _splitPlaceholders[slot] = null;
        }
        SplitPlaceElement(view, slot);
        WakeTab(tab);
        view.Visibility = Visibility.Visible;
        SplitHookFocus(view);
    }

    private void SplitShowPlaceholder(int slot)
    {
        if (_splitPlaceholders[slot] != null) return;
        var ph = new Border
        {
            Background = SplitBrush("Brush_SurfaceChrome", Brushes.Black),
            Child = new TextBlock
            {
                Text = "Empty",
                FontSize = 14,
                Foreground = SplitBrush("Brush_MutedText", Brushes.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        SplitPlaceElement(ph, slot);
        ViewContainer.Children.Add(ph);
        _splitPlaceholders[slot] = ph;
    }

    private void SplitFillEmpty(int count, TabViewModel? exclude = null)
    {
        var used = new HashSet<TabViewModel>();
        for (int i = 0; i < SplitMaxSlots; i++)
        {
            var t = _splitSlots[i];
            if (t != null) used.Add(t);
        }
        var pool = SplitCandidates().Where(t => !used.Contains(t) && !ReferenceEquals(t, exclude)).ToList();
        int p = 0;
        for (int i = 0; i < count && i < SplitMaxSlots; i++)
        {
            if (_splitSlots[i] != null) continue;
            if (p < pool.Count) SplitSetSlot(i, pool[p++]);
            else SplitShowPlaceholder(i);
        }
    }

    private void SplitApplyLayout(SplitLayoutDef def)
    {
        var active = CurrentTab;
        int count = Math.Min(def.Slots.Count, SplitMaxSlots);

        if (active != null)
        {
            int ai = SplitSlotOf(active);
            if (ai >= count)
            {
                var displaced = _splitSlots[0];
                _splitSlots[0] = active;
                _splitSlots[ai] = displaced;
            }
        }

        for (int i = count; i < SplitMaxSlots; i++)
        {
            var t = _splitSlots[i];
            if (t != null && !ReferenceEquals(t, active) && _tabViews.TryGetValue(t, out var v))
            {
                v.Visibility = Visibility.Collapsed;
            }
            _splitSlots[i] = null;
        }

        if (_splitPickerHost != null)
        {
            ViewContainer.Children.Remove(_splitPickerHost);
            _splitPickerHost = null;
        }
        _splitPicking = false;

        _splitAnimateNext = true;
        SplitBuildLayout(def);
        SplitFillEmpty(count);
        SplitScheduleIndicator();
        SplitLayoutPop();
    }

    private sealed class SplitDropTarget
    {
        public bool IsPicker;
        public int Slot;
        public SplitLayoutDef? Def;
        public Rect DeviceRect;
        public FrameworkElement? Cell;
    }

    private sealed class SplitPickerTarget
    {
        public FrameworkElement Cell = null!;
        public SplitLayoutDef Def = null!;
        public int Slot;
        public bool Fits;
    }

    private readonly List<SplitPickerTarget> _splitPickerTargets = new();
    private Brush? _splitPickerFieldBrush;
    private Brush? _splitPickerAccentBrush;
    private FrameworkElement? _splitDragPickerCell;
    private Window? _splitDropOverlay;
    private Rect _splitOverlayRect;

    private static Rect SplitElementDeviceRect(FrameworkElement el)
    {
        try
        {
            if (!el.IsLoaded || PresentationSource.FromVisual(el) == null) return Rect.Empty;
            var p = el.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(el);
            return new Rect(p.X, p.Y, el.ActualWidth * dpi.DpiScaleX, el.ActualHeight * dpi.DpiScaleY);
        }
        catch (Exception ex)
        {
            LogService.Debug("split", () => $"rect failed: {ex.Message}");
            return Rect.Empty;
        }
    }

    private SplitDropTarget? SplitFindDropTarget()
    {
        var cpt = SplitCursorDevice();
        Point cursor = new Point(cpt.X, cpt.Y);

        if (_splitPickerWindow != null)
        {
            foreach (var t in _splitPickerTargets)
            {
                if (!t.Fits) continue;
                var r = SplitElementDeviceRect(t.Cell);
                if (r.IsEmpty || !r.Contains(cursor)) continue;
                return new SplitDropTarget { IsPicker = true, Slot = t.Slot, Def = t.Def, DeviceRect = r, Cell = t.Cell };
            }
        }

        if (_splitActive && _splitLayout != null && !_splitPicking && !_splitAwaitNewTab)
        {
            int count = Math.Min(_splitLayout.Slots.Count, SplitMaxSlots);
            for (int i = 0; i < count; i++)
            {
                FrameworkElement? el = _splitPlaceholders[i];
                var tab = _splitSlots[i];
                if (el == null && tab != null && _tabViews.TryGetValue(tab, out var view)) el = view;
                if (el == null || el.Visibility != Visibility.Visible) continue;
                var r = SplitElementDeviceRect(el);
                if (r.IsEmpty || !r.Contains(cursor)) continue;
                return new SplitDropTarget { IsPicker = false, Slot = i, DeviceRect = r };
            }
        }

        return null;
    }

    private void SplitDragClearHighlight()
    {
        if (_splitDragPickerCell != null)
        {
            if (_splitPickerFieldBrush != null) ((Border)_splitDragPickerCell).Background = _splitPickerFieldBrush;
            _splitDragPickerCell = null;
        }
        if (_splitDropOverlay != null)
        {
            var o = _splitDropOverlay;
            _splitDropOverlay = null;
            try
            {
                o.Close();
            }
            catch (Exception ex)
            {
                LogService.Debug("split", () => $"overlay close failed: {ex.Message}");
            }
        }
    }

    private void SplitShowDropOverlay(Rect deviceRect)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double l = deviceRect.X / dpi.DpiScaleX;
        double t = deviceRect.Y / dpi.DpiScaleY;
        double w = deviceRect.Width / dpi.DpiScaleX;
        double h = deviceRect.Height / dpi.DpiScaleY;

        if (_splitDropOverlay == null)
        {
            var accent = SplitBrush("Brush_Accent", Brushes.DodgerBlue);
            var fill = accent.CloneCurrentValue();
            fill.Opacity = 0.25;
            var border = new Border
            {
                Background = fill,
                BorderBrush = accent,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false
            };
            _splitDropOverlay = new Window
            {
                Style = null,
                Opacity = 0,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = l,
                Top = t,
                Width = w,
                Height = h,
                Content = border
            };
            _splitOverlayRect = new Rect(l, t, w, h);
            _splitDropOverlay.Show();
            _splitDropOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(100)));
            return;
        }

        var next = new Rect(l, t, w, h);
        if (Math.Abs(next.X - _splitOverlayRect.X) < 1 && Math.Abs(next.Y - _splitOverlayRect.Y) < 1 &&
            Math.Abs(next.Width - _splitOverlayRect.Width) < 1 && Math.Abs(next.Height - _splitOverlayRect.Height) < 1) return;
        _splitOverlayRect = next;
        var dur = TimeSpan.FromMilliseconds(110);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _splitDropOverlay.BeginAnimation(Window.LeftProperty, new DoubleAnimation(l, dur) { EasingFunction = ease });
        _splitDropOverlay.BeginAnimation(Window.TopProperty, new DoubleAnimation(t, dur) { EasingFunction = ease });
        _splitDropOverlay.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
        _splitDropOverlay.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
    }

    private void SplitFlashSlot(int slot)
    {
        if (!_splitActive || slot < 0 || slot >= SplitMaxSlots) return;
        var r = SplitSlotDeviceRect(slot);
        if (r.IsEmpty) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var accent = SplitBrush("Brush_Accent", Brushes.DodgerBlue);
        var fill = accent.CloneCurrentValue();
        fill.Opacity = 0.3;
        var flash = new Window
        {
            Style = null,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Focusable = false,
            IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = r.X / dpi.DpiScaleX,
            Top = r.Y / dpi.DpiScaleY,
            Width = r.Width / dpi.DpiScaleX,
            Height = r.Height / dpi.DpiScaleY,
            Content = new Border
            {
                Background = fill,
                BorderBrush = accent,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false
            }
        };
        flash.Show();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) =>
        {
            try
            {
                flash.Close();
            }
            catch (Exception ex)
            {
                LogService.Debug("split", () => $"flash close failed: {ex.Message}");
            }
        };
        flash.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private Rect SplitSlotDeviceRect(int slot)
    {
        if (slot < 0 || slot >= SplitMaxSlots) return Rect.Empty;
        FrameworkElement? el = _splitPlaceholders[slot];
        var tab = _splitSlots[slot];
        if (el == null && tab != null && _tabViews.TryGetValue(tab, out var view)) el = view;
        if (el == null || el.Visibility != Visibility.Visible) return Rect.Empty;
        return SplitElementDeviceRect(el);
    }

    private Rect SplitCursorCardRect()
    {
        var cur = SplitCursorDevice();
        var dpi = VisualTreeHelper.GetDpi(this);
        double w = 150 * dpi.DpiScaleX;
        double h = 38 * dpi.DpiScaleY;
        return new Rect(cur.X - w / 2, cur.Y - h / 2, w, h);
    }

    private void SplitFlyCard(Rect fromDevice, Rect toDevice, string title)
    {
        if (fromDevice.IsEmpty || toDevice.IsEmpty) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var f = new Rect(fromDevice.X / dpi.DpiScaleX, fromDevice.Y / dpi.DpiScaleY, fromDevice.Width / dpi.DpiScaleX, fromDevice.Height / dpi.DpiScaleY);
        var t = new Rect(toDevice.X / dpi.DpiScaleX, toDevice.Y / dpi.DpiScaleY, toDevice.Width / dpi.DpiScaleX, toDevice.Height / dpi.DpiScaleY);

        var accent = SplitBrush("Brush_Accent", Brushes.DodgerBlue);
        var fill = accent.CloneCurrentValue();
        fill.Opacity = 0.38;
        var label = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = SplitBrush("Brush_Text", Brushes.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        var card = new Window
        {
            Style = null,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Focusable = false,
            IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = f.X,
            Top = f.Y,
            Width = f.Width,
            Height = f.Height,
            Content = new Border
            {
                Background = fill,
                BorderBrush = accent,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(10),
                IsHitTestVisible = false,
                Child = label
            }
        };
        card.Show();

        var dur = TimeSpan.FromMilliseconds(210);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        card.BeginAnimation(Window.LeftProperty, new DoubleAnimation(f.X, t.X, dur) { EasingFunction = ease });
        card.BeginAnimation(Window.TopProperty, new DoubleAnimation(f.Y, t.Y, dur) { EasingFunction = ease });
        card.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(f.Width, t.Width, dur) { EasingFunction = ease });
        card.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(f.Height, t.Height, dur) { EasingFunction = ease });
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { BeginTime = TimeSpan.FromMilliseconds(150) };
        fade.Completed += (_, _) =>
        {
            try
            {
                card.Close();
            }
            catch (Exception ex)
            {
                LogService.Debug("split", () => $"card close failed: {ex.Message}");
            }
        };
        card.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private async void SplitLayoutPop()
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(190);
            int n = Math.Min(_splitLayout?.Slots.Count ?? 0, SplitMaxSlots);
            for (int i = 0; i < n; i++)
            {
                if (!_splitActive) return;
                SplitFlashSlot(i);
                await System.Threading.Tasks.Task.Delay(55);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug("split", () => $"layout pop failed: {ex.Message}");
        }
    }

    private void SplitScheduleFlash(params int[] slots)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            foreach (var i in slots) SplitFlashSlot(i);
        }));
    }

    public bool SplitDragUpdate()
    {
        var target = SplitFindDropTarget();
        if (target == null)
        {
            SplitDragClearHighlight();
            return false;
        }

        ClearTabDropGhost();
        _dragInsertIndex = -1;

        if (target.IsPicker)
        {
            if (_splitDropOverlay != null)
            {
                var o = _splitDropOverlay;
                _splitDropOverlay = null;
                o.Close();
            }
            if (!ReferenceEquals(_splitDragPickerCell, target.Cell))
            {
                if (_splitDragPickerCell is Border old && _splitPickerFieldBrush != null) old.Background = _splitPickerFieldBrush;
                _splitDragPickerCell = target.Cell;
                if (target.Cell is Border b && _splitPickerAccentBrush != null) b.Background = _splitPickerAccentBrush;
            }
        }
        else
        {
            if (_splitDragPickerCell is Border old && _splitPickerFieldBrush != null) old.Background = _splitPickerFieldBrush;
            _splitDragPickerCell = null;
            SplitShowDropOverlay(target.DeviceRect);
        }
        return true;
    }

    public bool SplitDragDrop(TabViewModel tab)
    {
        var target = SplitFindDropTarget();
        SplitDragClearHighlight();
        if (target == null) return false;

        try
        {
            if (target.IsPicker && target.Def != null)
            {
                SplitClosePicker();
                SplitDropOnPickerSlot(tab, target.Def, target.Slot);
            }
            else
            {
                SplitDropOnPane(tab, target.Slot);
            }
        }
        catch (Exception ex)
        {
            LogService.Write("SPLIT", $"drop failed: {ex}");
            return false;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_splitActive) SplitSelectTab(tab);
            SplitUpdateIndicator();
        }));
        return true;
    }

    public void SplitDragEnd()
    {
        SplitDragClearHighlight();
        SplitClosePicker();
    }

    private void SplitPrepareDropTab(TabViewModel tab)
    {
        if (!_tabViews.TryGetValue(tab, out var view)) return;
        WakeTab(tab);
        view.Visibility = Visibility.Visible;
        SplitHookFocus(view);
    }

    private void SplitRefreshSlots()
    {
        if (_splitLayout == null) return;
        int count = Math.Min(_splitLayout.Slots.Count, SplitMaxSlots);
        for (int i = 0; i < count; i++)
        {
            var tab = _splitSlots[i];
            if (tab != null && _tabViews.TryGetValue(tab, out var view))
            {
                var ph = _splitPlaceholders[i];
                if (ph != null)
                {
                    ViewContainer.Children.Remove(ph);
                    _splitPlaceholders[i] = null;
                }
                SplitPlaceElement(view, i);
                view.Visibility = Visibility.Visible;
            }
            else
            {
                _splitSlots[i] = null;
                SplitShowPlaceholder(i);
            }
        }
    }

    private void SplitDropOnPane(TabViewModel tab, int slot)
    {
        if (!_splitActive || _splitLayout == null) return;
        int from = SplitSlotOf(tab);
        if (from == slot) return;

        var rTo = SplitSlotDeviceRect(slot);
        var rFrom = from >= 0 ? SplitSlotDeviceRect(from) : SplitCursorCardRect();
        string title = tab.Title ?? "";

        var occupant = _splitSlots[slot];
        string occTitle = occupant?.Title ?? "";
        if (from >= 0)
        {
            _splitSlots[slot] = tab;
            _splitSlots[from] = occupant;
        }
        else
        {
            SplitPrepareDropTab(tab);
            _splitSlots[slot] = tab;
            if (occupant != null && _tabViews.TryGetValue(occupant, out var ov)) ov.Visibility = Visibility.Collapsed;
        }
        SplitRefreshSlots();
        SplitScheduleIndicator();
        SplitFlyCard(rFrom, rTo, title);
        if (from >= 0)
        {
            if (occupant != null) SplitFlyCard(rTo, rFrom, occTitle);
            SplitScheduleFlash(slot, from);
        }
        else
        {
            SplitScheduleFlash(slot);
        }
    }

    private void SplitDropOnPickerSlot(TabViewModel tab, SplitLayoutDef def, int slot)
    {
        if (slot < 0 || slot >= def.Slots.Count || slot >= SplitMaxSlots) slot = 0;
        int count = Math.Min(def.Slots.Count, SplitMaxSlots);

        if (_splitActive)
        {
            int from = SplitSlotOf(tab);
            var occupant = _splitSlots[slot];
            if (from >= 0)
            {
                _splitSlots[slot] = tab;
                _splitSlots[from] = occupant;
            }
            else
            {
                SplitPrepareDropTab(tab);
                _splitSlots[slot] = tab;
                if (occupant != null && _tabViews.TryGetValue(occupant, out var ov)) ov.Visibility = Visibility.Collapsed;
            }
            SplitApplyLayout(def);
            return;
        }

        var current = CurrentTab;
        if (current == null || _activeTabView == null) return;

        _splitActive = true;
        _splitPicking = false;
        _splitAwaitNewTab = false;
        _splitAnimateNext = true;
        Array.Clear(_splitSlots, 0, _splitSlots.Length);
        SplitPrepareDropTab(tab);
        _splitSlots[slot] = tab;
        if (!ReferenceEquals(current, tab))
        {
            for (int i = 0; i < count; i++)
            {
                if (i == slot) continue;
                _splitSlots[i] = current;
                break;
            }
        }
        SplitBuildLayout(def);
        SplitHookFocus(_activeTabView);
        SplitFillEmpty(count);
        SplitScheduleIndicator();
        SplitLayoutPop();
    }

    private readonly Window?[] _splitGrips = new Window?[SplitMaxSlots];
    private TabViewModel? _splitGripTab;
    private bool _splitGripDragging;
    private System.Drawing.Point _splitGripStart;
    private bool _splitSyncing;

    private UIElement? _splitGripPill;

    private void SplitOnPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (SplitCancelDrag()) e.Handled = true;
    }

    private bool SplitCancelDrag()
    {
        bool any = false;
        if (_splitGripDragging)
        {
            any = true;
            _splitGripPill?.ReleaseMouseCapture();
        }
        _splitGripTab = null;
        _splitGripDragging = false;
        if (_isDragging)
        {
            any = true;
            _isDragging = false;
            _draggedTab = null;
            _dragInsertIndex = -1;
            Mouse.Capture(null);
        }
        if (any)
        {
            DestroyDragGhost();
            SplitDragEnd();
        }
        return any;
    }

    private System.Drawing.Point SplitCursorDevice()
    {
        GetCursorPos(out var pt);
        return pt;
    }

    private void SplitMoveGhost()
    {
        if (_dragGhost == null) return;
        var pt = SplitCursorDevice();
        var dpi = VisualTreeHelper.GetDpi(this);
        _dragGhost.Left = pt.X / dpi.DpiScaleX + 10;
        _dragGhost.Top = pt.Y / dpi.DpiScaleY - 24;
    }

    private void SplitOnLayoutUpdated(object? sender, EventArgs e)
    {
        SplitSyncGrips();
    }

    private void SplitOnWindowMoved(object? sender, EventArgs e)
    {
        SplitSyncGrips();
    }

    private void SplitDestroyGrips()
    {
        for (int i = 0; i < _splitGrips.Length; i++)
        {
            var g = _splitGrips[i];
            if (g == null) continue;
            _splitGrips[i] = null;
            try
            {
                g.Close();
            }
            catch (Exception ex)
            {
                LogService.Debug("split", () => $"grip close failed: {ex.Message}");
            }
        }
    }

    private const double SplitGripWidth = 46;
    private const double SplitGripHeight = 12;

    private static class SplitWin
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    private static Color SplitBrushColor(Brush brush, Color fallback)
    {
        return brush is SolidColorBrush sb ? sb.Color : fallback;
    }

    private static Color SplitMix(Color a, Color b, double t)
    {
        return Color.FromRgb(
            (byte)Math.Round(a.R * t + b.R * (1 - t)),
            (byte)Math.Round(a.G * t + b.G * (1 - t)),
            (byte)Math.Round(a.B * t + b.B * (1 - t)));
    }

    private void SplitShapeGrip(Window win)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) return;
            int off = 1;
            SplitWin.DwmSetWindowAttribute(hwnd, 2, ref off, sizeof(int));
            int noRound = 1;
            SplitWin.DwmSetWindowAttribute(hwnd, 33, ref noRound, sizeof(int));
            var dpi = VisualTreeHelper.GetDpi(this);
            int wpx = (int)Math.Ceiling(SplitGripWidth * dpi.DpiScaleX);
            int hpx = (int)Math.Ceiling(SplitGripHeight * dpi.DpiScaleY);
            var rgn = SplitWin.CreateRoundRectRgn(0, 0, wpx + 1, hpx + 1, hpx, hpx);
            SplitWin.SetWindowRgn(hwnd, rgn, true);
        }
        catch (Exception ex)
        {
            LogService.Debug("split", () => $"grip shape failed: {ex.Message}");
        }
    }

    private Window SplitCreateGrip(int slot)
    {
        var accentColor = SplitBrushColor(SplitBrush("Brush_Accent", Brushes.DodgerBlue), Colors.DodgerBlue);
        var mutedColor = SplitBrushColor(SplitBrush("Brush_MutedText", Brushes.Gray), Colors.Gray);
        var idle = new SolidColorBrush(SplitMix(mutedColor, Color.FromRgb(32, 32, 32), 0.75));
        var hot = new SolidColorBrush(accentColor);
        idle.Freeze();
        hot.Freeze();

        var gripWindow = new Window
        {
            Owner = this,
            Style = null,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            Background = idle,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Manual,
            Width = SplitGripWidth,
            Height = SplitGripHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Cursor = Cursors.SizeAll,
            Content = new Border { Background = Brushes.Transparent }
        };
        gripWindow.SourceInitialized += (_, _) => SplitShapeGrip(gripWindow);

        gripWindow.MouseEnter += (_, _) => gripWindow.Background = hot;
        gripWindow.MouseLeave += (_, _) =>
        {
            if (!_splitGripDragging) gripWindow.Background = idle;
        };
        gripWindow.MouseLeftButtonDown += (_, e) =>
        {
            var tab = slot < SplitMaxSlots ? _splitSlots[slot] : null;
            if (tab == null) return;
            _splitGripTab = tab;
            _splitGripPill = gripWindow;
            _splitGripDragging = false;
            _splitGripStart = SplitCursorDevice();
            gripWindow.CaptureMouse();
            e.Handled = true;
        };
        gripWindow.MouseMove += (_, e) =>
        {
            if (_splitGripTab == null || e.LeftButton != MouseButtonState.Pressed) return;
            var cur = SplitCursorDevice();
            var dpi = VisualTreeHelper.GetDpi(this);
            if (!_splitGripDragging)
            {
                double dx = Math.Abs(cur.X - _splitGripStart.X);
                double dy = Math.Abs(cur.Y - _splitGripStart.Y);
                if (dx < SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX &&
                    dy < SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY) return;
                _splitGripDragging = true;
                CreateDragGhost(_splitGripTab, new Point(cur.X, cur.Y));
            }
            SplitMoveGhost();
            SplitDragUpdate();
        };
        gripWindow.MouseLeftButtonUp += (_, e) =>
        {
            gripWindow.ReleaseMouseCapture();
            var tab = _splitGripTab;
            bool dragging = _splitGripDragging;
            _splitGripTab = null;
            _splitGripDragging = false;
            gripWindow.Background = idle;
            e.Handled = true;
            try
            {
                if (dragging)
                {
                    if (tab != null) SplitDragDrop(tab);
                    DestroyDragGhost();
                    SplitDragEnd();
                }
                else if (tab != null)
                {
                    SplitSelectTab(tab);
                }
            }
            catch (Exception ex)
            {
                LogService.Write("SPLIT", $"grip release failed: {ex}");
            }
            Activate();
        };
        gripWindow.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && SplitCancelDrag()) e.Handled = true;
        };
        return gripWindow;
    }

    private void SplitSyncGrips()
    {
        if (_splitSyncing) return;
        _splitSyncing = true;
        try
        {
            if (!_splitActive || _splitLayout == null || _splitPicking || _splitAwaitNewTab ||
                _splitPickerWindow != null || WindowState == WindowState.Minimized)
            {
                if (_splitGripTab == null) SplitDestroyGrips();
                return;
            }

            int count = Math.Min(_splitLayout.Slots.Count, SplitMaxSlots);
            var dpi = VisualTreeHelper.GetDpi(this);
            for (int i = 0; i < SplitMaxSlots; i++)
            {
                var tab = i < count ? _splitSlots[i] : null;
                Rect r = Rect.Empty;
                if (tab != null && _tabViews.TryGetValue(tab, out var view) && view.Visibility == Visibility.Visible)
                {
                    r = SplitElementDeviceRect(view);
                }

                if (r.IsEmpty)
                {
                    var old = _splitGrips[i];
                    if (old != null && _splitGripTab == null)
                    {
                        _splitGrips[i] = null;
                        old.Close();
                    }
                    continue;
                }

                var grip = _splitGrips[i];
                if (grip == null)
                {
                    grip = SplitCreateGrip(i);
                    _splitGrips[i] = grip;
                }

                double left = (r.X + r.Width / 2) / dpi.DpiScaleX - SplitGripWidth / 2;
                double top = r.Y / dpi.DpiScaleY + 2;
                if (Math.Abs(grip.Left - left) > 0.5 || double.IsNaN(grip.Left)) grip.Left = left;
                if (Math.Abs(grip.Top - top) > 0.5 || double.IsNaN(grip.Top)) grip.Top = top;
                if (!grip.IsVisible) grip.Show();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug("split", () => $"grip sync failed: {ex.Message}");
        }
        finally
        {
            _splitSyncing = false;
        }
    }

    private void SplitClosePicker()
    {
        var w = _splitPickerWindow;
        if (w == null) return;
        _splitPickerWindow = null;
        _splitPickerTargets.Clear();
        try
        {
            w.Close();
        }
        catch (Exception ex)
        {
            LogService.Debug("split", () => $"picker close failed: {ex.Message}");
        }
        SplitSyncGrips();
    }

    private void SplitChooseLayout(SplitLayoutDef def, int slot)
    {
        if (CurrentTab == null || _activeTabView == null) return;
        if (slot < 0 || slot >= def.Slots.Count || slot >= SplitMaxSlots) slot = 0;

        if (_splitActive)
        {
            int ai = SplitSlotOf(CurrentTab);
            if (ai >= 0 && ai != slot)
            {
                var other = _splitSlots[slot];
                _splitSlots[slot] = _splitSlots[ai];
                _splitSlots[ai] = other;
            }
            SplitApplyLayout(def);
            return;
        }

        SplitEnterWithLayout(def, slot);
    }

    private void SplitEnterWithLayout(SplitLayoutDef def, int slot)
    {
        if (_splitActive || CurrentTab == null || _activeTabView == null) return;

        _splitActive = true;
        _splitPicking = false;
        _splitAwaitNewTab = false;
        Array.Clear(_splitSlots, 0, _splitSlots.Length);
        _splitSlots[slot] = CurrentTab;
        _splitAnimateNext = true;
        SplitBuildLayout(def);
        SplitHookFocus(_activeTabView);
        SplitFillEmpty(Math.Min(def.Slots.Count, SplitMaxSlots));
        SplitScheduleIndicator();
        SplitLayoutPop();
    }

    private FrameworkElement SplitBuildThumb(SplitLayoutDef def, double tw, double th, bool current, bool last, bool fits, Brush fieldBrush, Brush mutedBrush, Brush accentBrush)
    {
        var canvas = new Canvas { Width = tw, Height = th, Background = Brushes.Transparent };
        for (int i = 0; i < def.Slots.Count; i++)
        {
            var s = def.Slots[i];
            int slotIndex = i;
            var cell = new Border
            {
                Width = Math.Max(4, s.W * tw - 3),
                Height = Math.Max(4, s.H * th - 3),
                Background = fieldBrush,
                BorderBrush = mutedBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = fits ? Cursors.Hand : Cursors.Arrow
            };
            Canvas.SetLeft(cell, s.X * tw + 1.5);
            Canvas.SetTop(cell, s.Y * th + 1.5);
            if (fits)
            {
                cell.MouseEnter += (_, _) => cell.Background = accentBrush;
                cell.MouseLeave += (_, _) => cell.Background = fieldBrush;
                cell.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    SplitClosePicker();
                    SplitChooseLayout(def, slotIndex);
                };
            }
            canvas.Children.Add(cell);
            _splitPickerTargets.Add(new SplitPickerTarget { Cell = cell, Def = def, Slot = slotIndex, Fits = fits });
        }

        return new Border
        {
            Margin = new Thickness(6),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1.5),
            BorderBrush = current ? accentBrush : (last ? mutedBrush : Brushes.Transparent),
            Opacity = fits ? 1.0 : 0.35,
            ToolTip = last ? def.Name + " (last used)" : def.Name,
            Child = canvas
        };
    }

    private void SplitShowSnapPicker()
    {
        if (_splitPickerWindow != null) return;
        if (CurrentTab == null || _activeTabView == null) return;

        var shape = SplitCurrentMonitorShape();
        var layouts = SplitLayoutCatalog.For(shape);
        if (layouts.Count == 0) return;

        double tw;
        double th;
        int perRow;
        switch (shape)
        {
            case SplitMonitorShape.Ultrawide:
                tw = 120; th = 52; perRow = 5;
                break;
            case SplitMonitorShape.Portrait:
                tw = 52; th = 88; perRow = 3;
                break;
            default:
                tw = 88; th = 56; perRow = 6;
                break;
        }

        var textBrush = SplitBrush("Brush_Text", Brushes.White);
        var mutedBrush = SplitBrush("Brush_MutedText", Brushes.Gray);
        var fieldBrush = SplitBrush("Brush_SurfaceField", Brushes.DimGray);
        var chromeBrush = SplitBrush("Brush_SurfaceChrome", Brushes.Black);
        var accentBrush = SplitBrush("Brush_Accent", Brushes.Gray);

        _splitPickerTargets.Clear();
        _splitPickerFieldBrush = fieldBrush;
        _splitPickerAccentBrush = accentBrush;
        double avail = ViewContainer.ActualWidth;
        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Width = perRow * (tw + 23)
        };
        foreach (var def in layouts)
        {
            int cols = SplitColumnCount(def);
            double need = cols * SplitPaneMinWidth + (cols - 1) * SplitDividerWidth;
            bool fits = avail <= 0 || avail >= need;
            bool isCurrent = _splitActive && string.Equals(def.Id, _splitLayoutId, StringComparison.OrdinalIgnoreCase);
            bool isLast = !isCurrent && string.Equals(def.Id, SettingsService.Current.SplitViewLastLayout, StringComparison.OrdinalIgnoreCase);
            wrap.Children.Add(SplitBuildThumb(def, tw, th, isCurrent, isLast, fits, fieldBrush, mutedBrush, accentBrush));
        }

        var header = new DockPanel { Margin = new Thickness(10, 8, 10, 0), LastChildFill = true };
        if (_splitActive)
        {
            var close = new TextBlock
            {
                Text = "Close split",
                FontSize = 12,
                Foreground = accentBrush,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            close.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SplitClosePicker();
                ExitSplitView();
            };
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
        }
        header.Children.Add(new TextBlock
        {
            Text = "Split layout",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = mutedBrush
        });

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(wrap);

        var root = new Border
        {
            Background = chromeBrush,
            BorderBrush = fieldBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Child = stack
        };

        var win = new Window
        {
            Owner = this,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = root
        };

        Point origin = new Point(0, 0);
        try
        {
            var px = ViewContainer.PointToScreen(new Point(0, 0));
            var src = PresentationSource.FromVisual(this);
            origin = src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(px) : px;
        }
        catch (Exception ex)
        {
            LogService.Debug("split", () => $"picker origin failed: {ex.Message}");
        }

        double centerX = origin.X + ViewContainer.ActualWidth / 2;
        win.Left = centerX - wrap.Width / 2 - 20;
        win.Top = origin.Y + 12;
        win.Opacity = 0;
        win.Loaded += (_, _) =>
        {
            win.Left = centerX - win.ActualWidth / 2;
            win.Top = origin.Y + 12;
            var dur = TimeSpan.FromMilliseconds(130);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            win.BeginAnimation(Window.TopProperty, new DoubleAnimation(origin.Y - 2, origin.Y + 12, dur) { EasingFunction = ease });
            win.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        };
        win.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SplitClosePicker();
            }
        };
        bool armed = false;
        win.Activated += (_, _) =>
        {
            win.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => armed = true));
        };
        win.Deactivated += (_, _) =>
        {
            if (!armed) return;
            win.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                if (!ReferenceEquals(_splitPickerWindow, win)) return;
                if (_draggedTab != null) return;
                SplitClosePicker();
            }));
        };
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_splitPickerWindow, win)) _splitPickerWindow = null;
        };

        PreviewKeyDown -= SplitOnPreviewKey;
        PreviewKeyDown += SplitOnPreviewKey;
        _splitPickerWindow = win;
        win.Show();
        win.Activate();
    }

    private IEnumerable<TabViewModel> SplitCandidates()
    {
        var current = CurrentTab;
        var ordered = new List<TabViewModel>();
        foreach (var t in _mruTabs)
        {
            if (!ReferenceEquals(t, current) && _allTabs.Contains(t) && _tabViews.ContainsKey(t)) ordered.Add(t);
        }
        foreach (var t in _allTabs)
        {
            if (!ReferenceEquals(t, current) && !ordered.Contains(t) && _tabViews.ContainsKey(t)) ordered.Add(t);
        }
        return ordered;
    }

    public void ToggleSplitView()
    {
        if (_splitPickerWindow != null)
        {
            SplitClosePicker();
            return;
        }
        if (_splitActive && (_splitPicking || _splitAwaitNewTab))
        {
            ExitSplitView();
            return;
        }
        SplitShowSnapPicker();
    }

    public void EnterSplitView()
    {
        if (_splitActive) return;
        if (CurrentTab == null || _activeTabView == null) return;

        _splitActive = true;
        _splitPicking = true;
        _splitAwaitNewTab = false;
        Array.Clear(_splitSlots, 0, _splitSlots.Length);
        _splitSlots[0] = CurrentTab;
        SplitBuildLayout(SplitLayoutCatalog.Find(SplitLayoutCatalog.DefaultId));
        SplitHookFocus(_activeTabView);

        if (SettingsService.Current.SplitViewPickMode == 2)
        {
            var recent = SplitCandidates().FirstOrDefault();
            if (recent != null)
            {
                SplitAssignSecondary(recent);
            }
            else
            {
                SplitCreateNewSecondary();
            }
            return;
        }

        SplitShowPicker();
    }

    public void ExitSplitView(TabViewModel? keep = null)
    {
        if (!_splitActive) return;

        var active = CurrentTab;
        var keepTab = keep ?? active;

        _splitActive = false;
        _splitPicking = false;
        _splitAwaitNewTab = false;

        var members = new List<TabViewModel>();
        for (int i = 0; i < SplitMaxSlots; i++)
        {
            var t = _splitSlots[i];
            if (t != null) members.Add(t);
            _splitSlots[i] = null;
        }

        foreach (var t in members)
        {
            if (!ReferenceEquals(t, keepTab) && _tabViews.TryGetValue(t, out var mv))
            {
                mv.Visibility = Visibility.Collapsed;
            }
        }

        if (keepTab != null && !ReferenceEquals(keepTab, active) && active != null && _tabViews.TryGetValue(active, out var activeView))
        {
            activeView.Visibility = Visibility.Collapsed;
        }

        SplitRemoveLayout();

        if (keepTab != null && _tabViews.TryGetValue(keepTab, out var keepView))
        {
            keepView.Visibility = Visibility.Visible;
            if (!ReferenceEquals(keepTab, active)) SplitSelectTab(keepTab);
        }
    }

    private void SplitAssignSecondary(TabViewModel tab)
    {
        int slot = SplitFirstEmptySlot();
        if (slot < 0 || !_tabViews.ContainsKey(tab))
        {
            ExitSplitView();
            return;
        }

        if (_splitPickerHost != null)
        {
            ViewContainer.Children.Remove(_splitPickerHost);
            _splitPickerHost = null;
        }

        _splitPicking = false;
        SplitSetSlot(slot, tab);
        SplitScheduleIndicator();
    }

    private void SplitCreateNewSecondary()
    {
        if (_splitPickerHost != null)
        {
            ViewContainer.Children.Remove(_splitPickerHost);
            _splitPickerHost = null;
        }
        _splitAwaitNewTab = true;
        CreateNewTab();
    }

    private bool SplitViewPrepareSwitch(TabViewModel selected)
    {
        if (!_splitActive) return false;

        if (_splitAwaitNewTab)
        {
            _splitAwaitNewTab = false;
            int slot = SplitFirstEmptySlot();
            if (slot >= 0 && _tabViews.TryGetValue(selected, out var newView) && _activeTabView != null)
            {
                _splitPicking = false;
                _splitSlots[slot] = selected;
                SplitPlaceElement(newView, slot);
                SplitHookFocus(newView);
                SplitScheduleIndicator();
                return true;
            }
            ExitSplitView();
            return false;
        }

        if (_splitPicking)
        {
            ExitSplitView();
            return false;
        }

        if (SplitSlotOf(selected) >= 0)
        {
            SplitScheduleIndicator();
            return true;
        }

        var outgoing = SplitTabOf(_activeTabView);
        int outSlot = SplitSlotOf(outgoing);
        if (outSlot >= 0 && _tabViews.TryGetValue(selected, out var incoming))
        {
            _splitSlots[outSlot] = selected;
            SplitPlaceElement(incoming, outSlot);
            SplitHookFocus(incoming);
        }
        SplitScheduleIndicator();
        return false;
    }

    private void SplitViewOnTabClosing(TabViewModel tab)
    {
        if (!_splitActive) return;

        var current = CurrentTab;
        bool isCurrent = ReferenceEquals(tab, current);
        int slot = SplitSlotOf(tab);
        if (slot < 0 && !isCurrent) return;

        int count = Math.Min(_splitLayout?.Slots.Count ?? 0, SplitMaxSlots);
        if (count > 2 && slot >= 0 && !_splitPicking && !_splitAwaitNewTab)
        {
            _splitSlots[slot] = null;
            if (_tabViews.TryGetValue(tab, out var closingView)) closingView.Visibility = Visibility.Collapsed;

            TabViewModel? firstRemaining = null;
            int filled = 0;
            for (int i = 0; i < SplitMaxSlots; i++)
            {
                var t = _splitSlots[i];
                if (t == null) continue;
                filled++;
                if (firstRemaining == null) firstRemaining = t;
            }

            if (filled < 2)
            {
                if (firstRemaining != null) ExitSplitView(firstRemaining);
                else ExitSplitView();
                return;
            }

            if (isCurrent && firstRemaining != null) SplitSelectTab(firstRemaining);
            SplitFillEmpty(count, tab);
            SplitScheduleIndicator();
            return;
        }

        if (isCurrent)
        {
            TabViewModel? other = null;
            for (int i = 0; i < SplitMaxSlots; i++)
            {
                var t = _splitSlots[i];
                if (t != null && !ReferenceEquals(t, tab))
                {
                    other = t;
                    break;
                }
            }
            if (other != null) ExitSplitView(other);
            else ExitSplitView();
            return;
        }

        ExitSplitView();
    }

    private bool SplitViewOwnsTab(TabViewModel tab)
    {
        if (!_splitActive) return false;
        return SplitSlotOf(tab) >= 0 || ReferenceEquals(tab, CurrentTab);
    }

    private void SplitShowPicker()
    {
        var textBrush = SplitBrush("Brush_Text", Brushes.White);
        var mutedBrush = SplitBrush("Brush_MutedText", Brushes.Gray);
        var fieldBrush = SplitBrush("Brush_SurfaceField", Brushes.DimGray);
        var chromeBrush = SplitBrush("Brush_SurfaceChrome", Brushes.Black);
        var accentBrush = SplitBrush("Brush_Accent", Brushes.Gray);

        var header = new TextBlock
        {
            Text = "Choose a tab for this side",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var hint = new TextBlock
        {
            Text = "Press Esc to cancel",
            FontSize = 12,
            Foreground = mutedBrush,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        wrap.Children.Add(SplitBuildCard(null, "New Tab", "Start fresh", fieldBrush, textBrush, mutedBrush, accentBrush));
        foreach (var tab in SplitCandidates())
        {
            string host = "";
            if (Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri)) host = uri.Host;
            wrap.Children.Add(SplitBuildCard(tab, string.IsNullOrWhiteSpace(tab.Title) ? "Untitled" : tab.Title, host, fieldBrush, textBrush, mutedBrush, accentBrush));
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = wrap
        };

        var stack = new DockPanel { LastChildFill = true, Margin = new Thickness(28) };
        var top = new StackPanel();
        top.Children.Add(header);
        top.Children.Add(hint);
        DockPanel.SetDock(top, Dock.Top);
        stack.Children.Add(top);
        stack.Children.Add(scroll);

        _splitPickerHost = new Border
        {
            Background = chromeBrush,
            Focusable = true,
            Child = stack
        };
        SplitPlaceElement(_splitPickerHost, SplitFirstEmptySlot());
        Panel.SetZIndex(_splitPickerHost, 5);
        _splitPickerHost.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ExitSplitView();
            }
        };
        ViewContainer.Children.Add(_splitPickerHost);
        _splitPickerHost.Focus();
        Keyboard.Focus(_splitPickerHost);
    }

    private FrameworkElement SplitBuildCard(TabViewModel? tab, string title, string subtitle, Brush fieldBrush, Brush textBrush, Brush mutedBrush, Brush accentBrush)
    {
        FrameworkElement visual;
        if (tab?.Thumbnail != null)
        {
            visual = new Image
            {
                Source = tab.Thumbnail,
                Stretch = Stretch.UniformToFill,
                Height = 96
            };
        }
        else
        {
            string glyph = tab == null ? "+" : (string.IsNullOrWhiteSpace(title) ? "?" : title.Substring(0, 1).ToUpperInvariant());
            visual = new Border
            {
                Height = 96,
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontSize = 34,
                    Foreground = mutedBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = textBrush,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(10, 8, 10, 0)
        };
        var subBlock = new TextBlock
        {
            Text = subtitle,
            Foreground = mutedBrush,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(10, 2, 10, 10)
        };

        var content = new StackPanel();
        var clip = new Border { Child = visual, ClipToBounds = true, CornerRadius = new CornerRadius(6, 6, 0, 0) };
        content.Children.Add(clip);
        content.Children.Add(titleBlock);
        content.Children.Add(subBlock);

        var card = new Border
        {
            Width = 190,
            Margin = new Thickness(0, 0, 14, 14),
            CornerRadius = new CornerRadius(8),
            Background = fieldBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Child = content
        };
        card.MouseEnter += (_, _) => card.BorderBrush = accentBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = Brushes.Transparent;
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!_splitActive || !_splitPicking) return;
            if (tab == null) SplitCreateNewSecondary();
            else SplitAssignSecondary(tab);
        };
        return card;
    }
}
