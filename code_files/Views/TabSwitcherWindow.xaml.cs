using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth.Views;

public partial class TabSwitcherWindow : Window
{
    // ── Win32 ──────────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern short GetKeyState(int vKey);
    private const int VK_CONTROL = 0x11;
    private static bool IsCtrlDown() => (GetKeyState(VK_CONTROL) & 0x8000) != 0;

    // ── State ──────────────────────────────────────────────────────────────────
    private List<TabViewModel>                       _tabs;
    private List<TabViewModel>                       _visibleTabs = new();
    private readonly Func<TabViewModel, Task<ImageSource?>> _captureFunc;
    private DispatcherTimer?                         _pollTimer;
    private int    _selectedIndex;
    private bool   _committed;
    private bool   _isPersistent;
    private string _filterText  = "";
    private readonly HashSet<int> _multiSelected = new(); // indices into _visibleTabs
    private int _anchorIndex = -1;

    // ── Events ─────────────────────────────────────────────────────────────────
    public event Action<int>?          TabCommitted;
    public event Action<TabViewModel>? TabCloseRequested;
    public event Action?               Cancelled;

    public event Action<TabViewModel>?                TabDuplicateRequested;
    public event Action<TabViewModel>?                TabRenameRequested;
    public event Action<TabViewModel>?                TabChangePositionRequested;
    public event Action<TabViewModel>?                TabBringToTopRequested;
    public event Action<TabViewModel>?                TabMuteToggleRequested;
    public event Action<TabViewModel>?                TabSleepToggleRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiDuplicateRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiRenameRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiBringToTopRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiSendToEndRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiGroupRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiSleepAllRequested;
    public event Action<IReadOnlyList<TabViewModel>>? MultiWakeAllRequested;

    // ── Construction ──────────────────────────────────────────────────────────
    public TabSwitcherWindow(List<TabViewModel> tabs, int initialIndex,
                             Func<TabViewModel, Task<ImageSource?>> captureFunc,
                             bool persistent = false)
    {
        InitializeComponent();

        _tabs          = tabs;
        _visibleTabs   = new List<TabViewModel>(tabs);
        _captureFunc   = captureFunc;
        _isPersistent  = persistent;
        _selectedIndex = Math.Clamp(initialIndex, 0, Math.Max(0, tabs.Count - 1));
        _anchorIndex   = _selectedIndex;

        TxtTabSwitcherHdr.Text = $"{tabs.Count} tab{(tabs.Count == 1 ? "" : "s")}";

        BuildTabList();
        HighlightRow(_selectedIndex, null);
        _ = LoadPreviewAsync(_selectedIndex);

        if (_isPersistent)
        {
            ResizeMode                  = ResizeMode.CanResize;
            TxtCtrlTabLabel.Text        = "TABS";
            TxtHintBar.Text             = "↑↓/click: switch  ·  Ctrl+click: select  ·  Shift+click: range  ·  Ctrl+A: all  ·  Del: close  ·  Esc: dismiss";
            BtnSwitcherClose.Visibility = Visibility.Visible;
            Loaded += (_, _) => TxtSearch.Focus();
        }
        else
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _pollTimer.Tick += (_, _) => { if (!IsCtrlDown()) Commit(); };
            _pollTimer.Start();
        }
    }

    // ── Public navigation (called from MainWindow) ─────────────────────────────
    public void Step(int direction)
    {
        if (_visibleTabs.Count == 0) return;
        int next = (_selectedIndex + direction + _visibleTabs.Count) % _visibleTabs.Count;
        SelectIndex(next);
    }

    public void CommitCurrent() => Commit();
    public void CancelSelf()    => Cancel();

    // ── Build list ────────────────────────────────────────────────────────────
    private Border? _lastAccent;

    private void CloseTabFromSwitcher(TabViewModel tab)
    {
        TabCloseRequested?.Invoke(tab);
        _tabs.Remove(tab);
        _visibleTabs.Remove(tab);
        _multiSelected.Clear();
        _anchorIndex = -1;
        TxtTabSwitcherHdr.Text = $"{_tabs.Count} tab{(_tabs.Count == 1 ? "" : "s")}";
        if (_tabs.Count == 0) { _committed = true; Close(); return; }
        _selectedIndex = Math.Clamp(_selectedIndex, 0, _visibleTabs.Count - 1);
        BuildTabList();
        HighlightRow(_selectedIndex, null);
        _ = LoadPreviewAsync(_selectedIndex);
        UpdateActionBar();
    }

    // Closes all currently multi-selected tabs, or (if invert=true) all EXCEPT them.
    private void CloseSelectedTabs(bool invert = false)
    {
        var toClose = _visibleTabs
            .Where((_, i) => invert ? !_multiSelected.Contains(i) : _multiSelected.Contains(i))
            .ToList();

        foreach (var tab in toClose)
        {
            TabCloseRequested?.Invoke(tab);
            _tabs.Remove(tab);
        }

        _multiSelected.Clear();
        _anchorIndex = -1;

        _visibleTabs = string.IsNullOrEmpty(_filterText)
            ? new List<TabViewModel>(_tabs)
            : _tabs.FindAll(t =>
                  t.DisplayTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                  t.Url.Contains(_filterText, StringComparison.OrdinalIgnoreCase)          ||
                  t.DomainTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

        TxtTabSwitcherHdr.Text = $"{_tabs.Count} tab{(_tabs.Count == 1 ? "" : "s")}";

        if (_tabs.Count == 0) { _committed = true; Close(); return; }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, _visibleTabs.Count - 1);
        BuildTabList();
        HighlightRow(_selectedIndex, null);
        _ = LoadPreviewAsync(_selectedIndex);
        UpdateActionBar();
    }

    private void BuildTabList()
    {
        TabListPanel.Children.Clear();

        for (int i = 0; i < _visibleTabs.Count; i++)
        {
            var tab      = _visibleTabs[i];
            int rowIndex = i;

            // Outer row — holds the left accent bar + content
            var row = new Border
            {
                Tag        = rowIndex,
                Cursor     = Cursors.Hand,
                Background = Brushes.Transparent,
                Padding    = new Thickness(0)
            };

            // Close button — declared early so hover handlers can reference it
            var closeBtn = new Button
            {
                Content         = "✕",
                FontSize        = 10,
                Width           = 18, Height = 18,
                Padding         = new Thickness(0),
                Margin          = new Thickness(6, 0, 0, 0),
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground      = new SolidColorBrush(Color.FromRgb(0xCC, 0x55, 0x55)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor          = Cursors.Hand,
                Opacity         = 0.0,
                ToolTip         = "Close tab (Del)"
            };
            closeBtn.Click += (_, e) => { e.Handled = true; CloseTabFromSwitcher(tab); };

            row.MouseEnter += (_, _) =>
            {
                bool isCursor   = (int)row.Tag == _selectedIndex;
                bool isSelected = _multiSelected.Contains((int)row.Tag);
                if (!isCursor && !isSelected)
                    row.Background = new SolidColorBrush(Color.FromArgb(16, 0, 204, 68));
                closeBtn.Opacity = 1.0;
            };
            row.MouseLeave += (_, _) =>
            {
                bool isCursor   = (int)row.Tag == _selectedIndex;
                bool isSelected = _multiSelected.Contains((int)row.Tag);
                if (!isCursor && !isSelected)
                    row.Background = Brushes.Transparent;
                closeBtn.Opacity = 0.0;
            };
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.Handled) return; // close button already handled
                bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

                if (ctrl && shift)
                {
                    // Ctrl+Shift: extend existing selection with range from anchor
                    int anchor = _anchorIndex >= 0 ? _anchorIndex : _selectedIndex;
                    int lo = Math.Min(anchor, rowIndex);
                    int hi = Math.Max(anchor, rowIndex);
                    for (int j = lo; j <= hi; j++) _multiSelected.Add(j);
                    _selectedIndex = rowIndex;
                    HighlightRow(_selectedIndex, null);
                    UpdateActionBar();
                    _ = LoadPreviewAsync(_selectedIndex);
                }
                else if (ctrl)
                {
                    // Ctrl+click: toggle this item; anchor only moves when adding
                    if (_multiSelected.Contains(rowIndex))
                        _multiSelected.Remove(rowIndex);
                    else
                    {
                        _multiSelected.Add(rowIndex);
                        _anchorIndex = rowIndex;
                    }
                    _selectedIndex = rowIndex;
                    HighlightRow(_selectedIndex, null);
                    UpdateActionBar();
                    _ = LoadPreviewAsync(_selectedIndex);
                }
                else if (shift)
                {
                    // Shift+click: REPLACE selection with range from anchor to here
                    _multiSelected.Clear();
                    int anchor = _anchorIndex >= 0 ? _anchorIndex : _selectedIndex;
                    int lo = Math.Min(anchor, rowIndex);
                    int hi = Math.Max(anchor, rowIndex);
                    for (int j = lo; j <= hi; j++) _multiSelected.Add(j);
                    _selectedIndex = rowIndex;
                    HighlightRow(_selectedIndex, null);
                    UpdateActionBar();
                    _ = LoadPreviewAsync(_selectedIndex);
                }
                else
                {
                    // Plain click: clear multi-selection, set anchor, navigate
                    _multiSelected.Clear();
                    _anchorIndex   = rowIndex;
                    _selectedIndex = rowIndex;
                    UpdateActionBar();
                    SelectIndex(rowIndex);
                    Commit();
                }
            };

            row.MouseRightButtonUp += (_, e2) =>
            {
                if (e2.Handled) return;
                e2.Handled = true;
                if (_multiSelected.Count >= 2 && _multiSelected.Contains(rowIndex))
                    ShowSwitcherMultiContextMenu(row);
                else
                    ShowSwitcherTabContextMenu(row, tab);
            };

            // 3-column grid: accent | content
            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accent = new Border { Background = Brushes.Transparent, Tag = "accent" };
            Grid.SetColumn(accent, 0);
            rowGrid.Children.Add(accent);

            // Content area
            var inner = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var faviconImg = new Image
            {
                Width             = 18, Height = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch           = Stretch.Uniform
            };
            var faviconUri = GetFaviconUri(tab.Url);
            if (faviconUri != null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource       = faviconUri;
                bmp.DecodePixelWidth = 18;
                bmp.EndInit();
                faviconImg.Source = bmp;
            }
            Grid.SetColumn(faviconImg, 0);
            inner.Children.Add(faviconImg);

            // Title + domain stack
            var textStack = new StackPanel
            {
                Margin            = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            textStack.Children.Add(new TextBlock
            {
                Text         = tab.DisplayTitle,
                Foreground   = Brushes.White,
                FontSize     = 12,
                FontWeight   = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth     = 160
            });
            textStack.Children.Add(new TextBlock
            {
                Text         = tab.DomainTitle,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                FontSize     = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth     = 160
            });
            Grid.SetColumn(textStack, 1);
            inner.Children.Add(textStack);

            // Status badges
            var badges = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (tab.IsPlayingAudio)
                badges.Children.Add(Badge(tab.HasVideo ? "\U0001F3AC" : "\U0001F50A"));
            if (tab.IsMuted)
                badges.Children.Add(Badge("\U0001F507"));
            if (tab.IsSleeping)
                badges.Children.Add(Badge("\U0001F4A4"));
            if (tab.IsLoading)
                badges.Children.Add(Badge("\u27F3", Color.FromRgb(0x88, 0xCC, 0xFF)));
            if (tab.IsActiveDownload)
                badges.Children.Add(Badge("\u2B07"));
            Grid.SetColumn(badges, 2);
            inner.Children.Add(badges);

            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(closeBtn, 3);
            inner.Children.Add(closeBtn);

            Grid.SetColumn(inner, 1);
            rowGrid.Children.Add(inner);

            row.Child = rowGrid;
            TabListPanel.Children.Add(row);

            // Thin separator between rows
            if (i < _visibleTabs.Count - 1)
            {
                TabListPanel.Children.Add(new Border
                {
                    Height     = 1,
                    Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)),
                    Margin     = new Thickness(12, 0, 12, 0)
                });
            }
        }
    }

    private void UpdateActionBar()
    {
        // Action bar suppressed — all multi-select actions live in the right-click context menu.
        MultiActionBar.Visibility = Visibility.Collapsed;
    }

    private void BtnCloseSelected_Click(object sender, RoutedEventArgs e) => CloseSelectedTabs(invert: false);
    private void BtnCloseOthers_Click(object sender, RoutedEventArgs e)   => CloseSelectedTabs(invert: true);

    private static TextBlock Badge(string text, Color? color = null) => new TextBlock
    {
        Text       = text,
        FontSize   = 10,
        Margin     = new Thickness(2, 0, 0, 0),
        Foreground = new SolidColorBrush(color ?? Colors.White)
    };

    // ── Selection ─────────────────────────────────────────────────────────────
    private void SelectIndex(int index)
    {
        _selectedIndex = Math.Clamp(index, 0, _visibleTabs.Count - 1);
        HighlightRow(_selectedIndex, null);
        ScrollRowIntoView(_selectedIndex);
        _ = LoadPreviewAsync(_selectedIndex);
    }

    private void HighlightRow(int selectedIndex, Border? previousRow)
    {
        foreach (var child in TabListPanel.Children)
        {
            if (child is not Border row || row.Tag is not int rowIdx) continue;

            bool isCursor   = rowIdx == selectedIndex;
            bool isSelected = _multiSelected.Contains(rowIdx);

            if (isCursor)
                row.Background = new SolidColorBrush(Color.FromArgb(32, 0, 204, 68));
            else if (isSelected)
                row.Background = new SolidColorBrush(Color.FromArgb(22, 60, 160, 255));
            else
                row.Background = Brushes.Transparent;

            // Update left accent bar
            if (row.Child is Grid rg && rg.Children.Count > 0 &&
                rg.Children[0] is Border accentBar)
            {
                if (isCursor)
                    accentBar.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0x44));
                else if (isSelected)
                    accentBar.Background = new SolidColorBrush(Color.FromRgb(0x3C, 0xA0, 0xFF));
                else
                    accentBar.Background = Brushes.Transparent;
            }
        }
    }

    private void ScrollRowIntoView(int index)
    {
        // Each row is at index*2 in TabListPanel (separators occupy odd slots)
        int childIdx = index * 2;
        if (childIdx < TabListPanel.Children.Count &&
            TabListPanel.Children[childIdx] is FrameworkElement el)
        {
            el.BringIntoView();
        }
    }

    // ── Preview capture ───────────────────────────────────────────────────────
    private int _previewToken;

    private async Task LoadPreviewAsync(int index)
    {
        if (index < 0 || index >= _visibleTabs.Count) return;
        var tab = _visibleTabs[index];

        TxtPreviewTitle.Text = tab.DisplayTitle;

        int token = ++_previewToken;
        PreviewImage.Source             = null;
        PnlPreviewStatus.Visibility     = Visibility.Visible;
        TxtPreviewStatus.Text           = "Capturing preview...";

        var img = await _captureFunc(tab);

        if (token != _previewToken) return; // stale

        if (img != null)
        {
            PreviewImage.Source         = img;
            PnlPreviewStatus.Visibility = Visibility.Collapsed;
        }
        else
        {
            TxtPreviewStatus.Text = "No preview available";
        }
    }

    // ── Commit / Cancel ───────────────────────────────────────────────────────
    private void Commit()
    {
        if (_committed) return;
        _committed = true;
        _pollTimer?.Stop();
        int origIdx = (_selectedIndex >= 0 && _selectedIndex < _visibleTabs.Count)
            ? _tabs.IndexOf(_visibleTabs[_selectedIndex])
            : _selectedIndex;
        TabCommitted?.Invoke(origIdx);
        Close();
    }

    private void Cancel()
    {
        if (_committed) return;
        _committed = true;
        _pollTimer?.Stop();
        Cancelled?.Invoke();
        Close();
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

        switch (e.Key)
        {
            case Key.Escape:
                // First press clears multi-selection; second press cancels the window
                if (_multiSelected.Count > 0)
                {
                    _multiSelected.Clear(); _anchorIndex = -1;
                    HighlightRow(_selectedIndex, null);
                    UpdateActionBar();
                    e.Handled = true; return;
                }
                Cancel(); e.Handled = true; return;

            case Key.Return:
            case Key.Space:
                Commit(); e.Handled = true; return;

            case Key.Tab when ctrl:
                Step(shift ? -1 : +1); e.Handled = true; return;

            case Key.Down:
            case Key.Right:
                Step(+1); e.Handled = true; return;

            case Key.Up:
            case Key.Left:
                Step(-1); e.Handled = true; return;

            case Key.Delete:
                if (_multiSelected.Count > 1)
                    CloseSelectedTabs();
                else if (_visibleTabs.Count > 0)
                    CloseTabFromSwitcher(_visibleTabs[_selectedIndex]);
                e.Handled = true; return;

            case Key.LeftCtrl:
            case Key.RightCtrl:
                return; // still held — ignore

            case Key.A when ctrl:
                for (int i = 0; i < _visibleTabs.Count; i++) _multiSelected.Add(i);
                if (_visibleTabs.Count > 0) _anchorIndex = 0;
                HighlightRow(_selectedIndex, null);
                UpdateActionBar();
                e.Handled = true; return;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_committed) return;
        Cancel();
    }

    private void BtnSwitcherClose_Click(object sender, RoutedEventArgs e)
    {
        Cancel();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filterText  = TxtSearch.Text.Trim();
        _visibleTabs = string.IsNullOrEmpty(_filterText)
            ? new List<TabViewModel>(_tabs)
            : _tabs.FindAll(t =>
                  t.DisplayTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                  t.Url.Contains(_filterText, StringComparison.OrdinalIgnoreCase)          ||
                  t.DomainTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

        _selectedIndex = 0;
        _multiSelected.Clear();
        _anchorIndex = -1;
        BuildTabList();
        UpdateActionBar();

        if (_visibleTabs.Count > 0)
        {
            HighlightRow(0, null);
            _ = LoadPreviewAsync(0);
        }
        else
        {
            PreviewImage.Source         = null;
            PnlPreviewStatus.Visibility = Visibility.Visible;
            TxtPreviewStatus.Text       = "No matching tabs";
        }
    }

    private static Uri? GetFaviconUri(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "https" || uri.Scheme == "http"))
                return new Uri($"https://www.google.com/s2/favicons?domain={uri.Host}&sz=32");
        }
        catch { }
        return null;
    }

    // ── Dark context-menu helpers ──────────────────────────────────────────────
    private ContextMenu MakeDarkContextMenu(UIElement target) => new ContextMenu
    {
        PlacementTarget = target,
        Placement       = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        Background      = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
        BorderBrush     = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
        BorderThickness = new Thickness(1),
        Padding         = new Thickness(0, 3, 0, 3)
    };

    private static MenuItem MakeDarkMenuItem(string header, bool isEnabled = true)
    {
        var mi = new MenuItem
        {
            Header          = header,
            IsEnabled       = isEnabled,
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(14, 6, 14, 6),
            FontFamily      = new FontFamily("Consolas"),
            FontSize        = 12
        };
        // Override system highlight brushes so hover stays dark regardless of Windows theme
        mi.Resources[SystemColors.HighlightBrushKey]     = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        mi.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
        mi.Resources[SystemColors.MenuHighlightBrushKey] = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        return mi;
    }

    private static Separator MakeDarkSeparator() => new Separator
    {
        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
        Margin     = new Thickness(8, 2, 8, 2)
    };

    private void ShowSwitcherTabContextMenu(UIElement target, TabViewModel tab)
    {
        var cm = MakeDarkContextMenu(target);

        var miDup = MakeDarkMenuItem("📋  Duplicate Tab");
        miDup.Click += (_, _) => TabDuplicateRequested?.Invoke(tab);
        cm.Items.Add(miDup);

        var miRen = MakeDarkMenuItem("🏷  Rename Tab…");
        miRen.Click += (_, _) => TabRenameRequested?.Invoke(tab);
        cm.Items.Add(miRen);

        var miPos = MakeDarkMenuItem("↕  Change Tab Position");
        miPos.Click += (_, _) => TabChangePositionRequested?.Invoke(tab);
        cm.Items.Add(miPos);

        var miTop = MakeDarkMenuItem("⬆  Bring Tab to Top");
        miTop.Click += (_, _) => TabBringToTopRequested?.Invoke(tab);
        cm.Items.Add(miTop);

        cm.Items.Add(MakeDarkSeparator());

        var miMute = MakeDarkMenuItem(tab.IsMuted ? "🔇  Unmute Tab" : "🔊  Mute Tab");
        miMute.Click += (_, _) => TabMuteToggleRequested?.Invoke(tab);
        cm.Items.Add(miMute);

        var miSleep = MakeDarkMenuItem(
            tab.IsSleeping ? "☀  Wake Tab" : "💤  Sleep Tab",
            isEnabled: tab.IsSleeping || (!tab.IsPlayingAudio && !tab.NeverSleep));
        miSleep.Click += (_, _) => TabSleepToggleRequested?.Invoke(tab);
        cm.Items.Add(miSleep);

        cm.Items.Add(MakeDarkSeparator());

        var miClose = MakeDarkMenuItem("✕  Close Tab");
        miClose.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0x88, 0x88));
        miClose.Click += (_, _) => CloseTabFromSwitcher(tab);
        cm.Items.Add(miClose);

        int othersCount = _visibleTabs.Count - 1;
        var miOthers = MakeDarkMenuItem($"⊠  Close Others ({othersCount})", isEnabled: othersCount > 0);
        miOthers.Foreground = othersCount > 0
            ? new SolidColorBrush(Color.FromRgb(0xEE, 0x88, 0x88))
            : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        miOthers.Click += (_, _) =>
        {
            var toClose = _visibleTabs.Where(t => t != tab).ToList();
            foreach (var t in toClose)
            {
                TabCloseRequested?.Invoke(t);
                _tabs.Remove(t);
            }
            _multiSelected.Clear();
            _anchorIndex   = _selectedIndex;
            _visibleTabs = string.IsNullOrEmpty(_filterText)
                ? new List<TabViewModel>(_tabs)
                : _tabs.FindAll(tt =>
                    tt.DisplayTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                    tt.Url.Contains(_filterText, StringComparison.OrdinalIgnoreCase)          ||
                    tt.DomainTitle.Contains(_filterText, StringComparison.OrdinalIgnoreCase));
            TxtTabSwitcherHdr.Text = $"{_tabs.Count} tab{(_tabs.Count == 1 ? "" : "s")}";
            if (_tabs.Count == 0) { _committed = true; Close(); return; }
            _selectedIndex = Math.Clamp(_selectedIndex, 0, _visibleTabs.Count - 1);
            BuildTabList();
            HighlightRow(_selectedIndex, null);
            _ = LoadPreviewAsync(_selectedIndex);
            UpdateActionBar();
        };
        cm.Items.Add(miOthers);

        cm.IsOpen = true;
    }

    private void ShowSwitcherMultiContextMenu(UIElement target)
    {
        var selected = _multiSelected
            .Where(i => i >= 0 && i < _visibleTabs.Count)
            .OrderBy(i => i)
            .Select(i => _visibleTabs[i])
            .ToList();

        if (selected.Count == 0) return;

        var cm = MakeDarkContextMenu(target);

        var miHeader = MakeDarkMenuItem($"{selected.Count} TABS SELECTED", isEnabled: false);
        miHeader.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x99, 0xCC));
        miHeader.FontWeight = FontWeights.Bold;
        cm.Items.Add(miHeader);
        cm.Items.Add(MakeDarkSeparator());

        var miDup = MakeDarkMenuItem("📋  Duplicate All");
        miDup.Click += (_, _) => MultiDuplicateRequested?.Invoke(selected);
        cm.Items.Add(miDup);

        var miRen = MakeDarkMenuItem("🏷  Rename All…");
        miRen.Click += (_, _) => MultiRenameRequested?.Invoke(selected);
        cm.Items.Add(miRen);

        cm.Items.Add(MakeDarkSeparator());

        var miTop = MakeDarkMenuItem("⬆  Bring All to Top");
        miTop.Click += (_, _) => MultiBringToTopRequested?.Invoke(selected);
        cm.Items.Add(miTop);

        var miEnd = MakeDarkMenuItem("⬇  Send All to End");
        miEnd.Click += (_, _) => MultiSendToEndRequested?.Invoke(selected);
        cm.Items.Add(miEnd);

        var miGrp = MakeDarkMenuItem("📦  Group Selected Tabs");
        miGrp.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xEE, 0xBB));
        miGrp.Click += (_, _) => MultiGroupRequested?.Invoke(selected);
        cm.Items.Add(miGrp);

        cm.Items.Add(MakeDarkSeparator());

        var miSleep = MakeDarkMenuItem("💤  Sleep All Selected");
        miSleep.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xFF));
        miSleep.Click += (_, _) => MultiSleepAllRequested?.Invoke(selected);
        cm.Items.Add(miSleep);

        var miWake = MakeDarkMenuItem("☀  Wake All Selected");
        miWake.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xFF, 0xAA));
        miWake.Click += (_, _) => MultiWakeAllRequested?.Invoke(selected);
        cm.Items.Add(miWake);

        cm.Items.Add(MakeDarkSeparator());

        var miClose = MakeDarkMenuItem($"✕  Close {selected.Count} Selected");
        miClose.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0x88, 0x88));
        miClose.Click += (_, _) => CloseSelectedTabs(invert: false);
        cm.Items.Add(miClose);

        int othersCount = _visibleTabs.Count - selected.Count;
        var miOthers = MakeDarkMenuItem($"⊠  Close Others ({othersCount})", isEnabled: othersCount > 0);
        miOthers.Foreground = othersCount > 0
            ? new SolidColorBrush(Color.FromRgb(0xEE, 0x88, 0x88))
            : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        miOthers.Click += (_, _) => CloseSelectedTabs(invert: true);
        cm.Items.Add(miOthers);

        cm.IsOpen = true;
    }
}