using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth.Views;

public partial class ArcCommandBarWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly ObservableCollection<CommandBarItem> _items = new();
    private static ArcCommandBarWindow? _instance;

    private const double OuterMargin = 0;
    private const double BleedPad = 48;
    private BitmapSource? _snapshot;
    private Border? _snapshotWall;
    private Rect _screenBoundsPx = new Rect(0, 0, 0, 0);
    private double _dpiScale = 1.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct IslandAccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IslandCompositionData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref IslandCompositionData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public class CommandBarItem
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string DisplayIcon { get; set; } = "\u26A1";
        public string ActionHint { get; set; } = "Execute";
        public string KeyGesture { get; set; } = "";
        public Visibility KeyGestureVisibility => string.IsNullOrWhiteSpace(KeyGesture) ? Visibility.Collapsed : Visibility.Visible;
        public Func<Task>? Action { get; set; }
        public CustomShortcut? Shortcut { get; set; }
        public string Group { get; set; } = "";
        public int Score { get; set; }
    }

    public class ManageRow
    {
        public CustomShortcut Shortcut { get; }

        public ManageRow(CustomShortcut shortcut)
        {
            Shortcut = shortcut;
        }

        public string Icon => Shortcut.Icon;
        public string Title => Shortcut.Title;
        public string Subtitle => string.IsNullOrWhiteSpace(Shortcut.KeyGesture)
            ? Shortcut.Category
            : Shortcut.Category + " \u00B7 " + Shortcut.KeyGesture;
        public Visibility ManageVisibility => Shortcut.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;

        public bool IsEnabled
        {
            get => Shortcut.IsEnabled;
            set
            {
                Shortcut.IsEnabled = value;
                CustomShortcutService.UpdateShortcut(Shortcut);
            }
        }
    }

    private const string DefaultScriptPayload = "console.log('Running Horizon script on:', location.href);";
    private const string DefaultUrlPayload = "https://";
    private string? _editingShortcutId;
    private bool _returnToManage;
    private string? _pendingDeleteId;
    private string? _urlWarnedPayload;

    public ArcCommandBarWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        Owner = mainWindow;
        ListResults.ItemsSource = _items;
        CollectionViewSource.GetDefaultView(_items).GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandBarItem.Group)));

        foreach (var (command, label) in CustomShortcutService.KnownBrowserCommands)
        {
            CmbNewCommand.Items.Add(new ComboBoxItem { Content = label, Tag = command });
        }
        CmbNewCommand.SelectedIndex = 0;

        CardClipHost.Loaded      += (_, _) => UpdateCardClip();
        CardClipHost.SizeChanged += (_, _) => UpdateCardClip();
        AttachNativeBlur();

        Loaded += ArcCommandBarWindow_Loaded;
    }

    public static void ShowCommandBar(MainWindow mainWindow)
    {
        if (_instance != null && _instance.IsVisible)
        {
            _instance.Activate();
            _instance.TxtSearch.Focus();
            _instance.TxtSearch.SelectAll();
            return;
        }

        _instance = new ArcCommandBarWindow(mainWindow);

        double left;
        double top;
        try
        {
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(mainWindow).Handle);
            var work = screen.WorkingArea;
            double sc = VisualTreeHelper.GetDpi(mainWindow).DpiScaleX;
            if (sc <= 0) sc = 1.0;
            left = work.X / sc + (work.Width / sc - _instance.Width) / 2.0;
            top = work.Y / sc + (work.Height / sc - _instance.Height) / 2.0;
        }
        catch
        {
            left = mainWindow.Left + (mainWindow.ActualWidth - _instance.Width) / 2.0;
            top = mainWindow.Top + (mainWindow.ActualHeight - _instance.Height) / 2.0;
        }

        _instance.Left = Math.Max(0.0, left);
        _instance.Top = Math.Max(0.0, top);

        _instance.Show();
        _instance.Activate();
        _instance.TxtSearch.Focus();
    }

    private void ArcCommandBarWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSnapshotCrop();
        PopulateItems("");
        TxtSearch.Focus();
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? d = e.OriginalSource as DependencyObject;
        while (d != null && !ReferenceEquals(d, sender))
        {
            if (d is TextBox || ReferenceEquals(d, BtnClear) || ReferenceEquals(d, BtnNewShortcut) || ReferenceEquals(d, BtnManage)) return;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void UpdateCardClip()
    {
        if (CardClipHost.ActualWidth > 0 && CardClipHost.ActualHeight > 0)
        {
            CardClipHost.Clip = new RectangleGeometry(
                new Rect(0, 0, CardClipHost.ActualWidth, CardClipHost.ActualHeight), 8, 8);
        }
        UpdateSnapshotCrop();
    }

    private void AttachSnapshotBackdrop()
    {
        try
        {
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(_mainWindow).Handle);
            var bounds = screen.Bounds;
            _screenBoundsPx = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            _dpiScale = VisualTreeHelper.GetDpi(_mainWindow).DpiScaleX;
        }
        catch
        {
            _screenBoundsPx = new Rect(0, 0, 0, 0);
            _dpiScale = 1.0;
        }

        if (SettingsService.Current.DisableBlurEffects)
        {
            CardBaseBg.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x12, 0x12));
            return;
        }

        if (_screenBoundsPx.Width <= 0 || _screenBoundsPx.Height <= 0) return;

        _snapshot = TabSwitcherWindow.CaptureScreenArea(
            (int)_screenBoundsPx.X, (int)_screenBoundsPx.Y,
            (int)_screenBoundsPx.Width, (int)_screenBoundsPx.Height);
        if (_snapshot == null) return;

        CardBaseBg.Background = Brushes.Transparent;

        _snapshotWall = new Border
        {
            Margin = new Thickness(-BleedPad),
            Background = new ImageBrush(_snapshot)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox
            },
            Effect = new BlurEffect
            {
                Radius = 36,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            }
        };

        var tint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xA6, 0x0E, 0x0E, 0x0E))
        };

        var host = new Grid();
        host.Children.Add(_snapshotWall);
        host.Children.Add(tint);
        GlassBackdropHost.Children.Add(host);

        LocationChanged += (_, _) => UpdateSnapshotCrop();
    }

    private void UpdateSnapshotCrop()
    {
        if (_snapshotWall?.Background is not ImageBrush brush) return;
        if (_screenBoundsPx.Width <= 0 || _screenBoundsPx.Height <= 0) return;
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;

        double sc = _dpiScale <= 0 ? 1.0 : _dpiScale;
        double cardW = CardClipHost.ActualWidth  > 0 ? CardClipHost.ActualWidth  : Math.Max(1.0, Width  - 2.0 * OuterMargin);
        double cardH = CardClipHost.ActualHeight > 0 ? CardClipHost.ActualHeight : Math.Max(1.0, Height - 2.0 * OuterMargin);

        double leftPx = (Left + OuterMargin - BleedPad) * sc - _screenBoundsPx.X;
        double topPx  = (Top  + OuterMargin - BleedPad) * sc - _screenBoundsPx.Y;
        double wPx    = (cardW + 2.0 * BleedPad) * sc;
        double hPx    = (cardH + 2.0 * BleedPad) * sc;

        double x0 = Math.Clamp(leftPx / _screenBoundsPx.Width, 0.0, 1.0);
        double x1 = Math.Clamp((leftPx + wPx) / _screenBoundsPx.Width, 0.0, 1.0);
        double y0 = Math.Clamp(topPx / _screenBoundsPx.Height, 0.0, 1.0);
        double y1 = Math.Clamp((topPx + hPx) / _screenBoundsPx.Height, 0.0, 1.0);

        if (x1 - x0 < 0.001 || y1 - y0 < 0.001) return;
        brush.Viewbox = new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    private void ApplyBlur()
    {
        try
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null && source.Handle != IntPtr.Zero)
            {
                IntPtr hwnd = source.Handle;
                // Windows 11 transient acrylic backdrop
                try
                {
                    int backdrop = 4;
                    DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
                }
                catch { }

                // Windows 10/11 blur behind
                try
                {
                    var accent = new IslandAccentPolicy
                    {
                        AccentState = 3,
                        AccentFlags = 2,
                        GradientColor = unchecked((int)0x9010131A)
                    };
                    int size = Marshal.SizeOf(accent);
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    Marshal.StructureToPtr(accent, ptr, false);
                    var data = new IslandCompositionData
                    {
                        Attribute = 19,
                        Data = ptr,
                        SizeOfData = size
                    };
                    SetWindowCompositionAttribute(hwnd, ref data);
                    Marshal.FreeHGlobal(ptr);
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool LooksLikeUrl(string q)
    {
        return q.Contains('.') && !q.Contains(' ') && !q.StartsWith("?") ||
               q.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               q.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               q.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUrl(string q)
    {
        return q.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               q.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               ? q : "https://" + q;
    }

    private static string BuildSearchUrl(string q)
    {
        string template = SettingsService.Current.SearchEngineUrl;
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{query}"))
        {
            template = "https://www.google.com/search?q={query}";
        }
        return template.Replace("{query}", Uri.EscapeDataString(q));
    }

    private static string SearchEngineName()
    {
        string name = SettingsService.Current.SearchEngine;
        return string.IsNullOrWhiteSpace(name) ? "the web" : name;
    }

    private static string Acronym(string t)
    {
        var sb = new StringBuilder();
        bool wordStart = true;
        foreach (char c in t)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (wordStart) sb.Append(c);
                wordStart = false;
            }
            else
            {
                wordStart = true;
            }
        }
        return sb.ToString();
    }

    private static bool IsSubsequence(string q, string t)
    {
        int i = 0;
        foreach (char c in t)
        {
            if (i < q.Length && c == q[i]) i++;
        }
        return i == q.Length;
    }

    private static int ScoreText(string query, string? text, bool isTitle)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        string q = query.Trim().ToLowerInvariant();
        if (q.Length == 0) return 0;
        string t = text.ToLowerInvariant();

        if (t == q) return 1000;
        if (t.StartsWith(q, StringComparison.Ordinal)) return 800;

        int idx = t.IndexOf(q, StringComparison.Ordinal);
        if (idx > 0) return char.IsLetterOrDigit(t[idx - 1]) ? 400 : 600;

        string[] tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1 && tokens.All(tk => t.Contains(tk, StringComparison.Ordinal))) return 300;

        if (isTitle)
        {
            string compact = q.Replace(" ", "");
            if (compact.Length >= 2 && Acronym(t).StartsWith(compact, StringComparison.Ordinal)) return 500;
            if (compact.Length >= 3 && IsSubsequence(compact, t)) return 200;
        }

        return 0;
    }

    private static int ScoreShortcut(string q, CustomShortcut s)
    {
        int best = ScoreText(q, s.Title, true);
        best = Math.Max(best, ScoreText(q, s.Keywords, false) * 7 / 10);
        best = Math.Max(best, ScoreText(q, s.Description, false) / 2);
        best = Math.Max(best, ScoreText(q, s.Category, false) / 2);
        return best;
    }

    private static int GroupRank(string group)
    {
        return group switch
        {
            "Web" => 0,
            "Commands" => 1,
            "Tabs" => 2,
            "Links" => 3,
            "Scripts" => 4,
            _ => 5
        };
    }

    private void PopulateItems(string query)
    {
        _items.Clear();
        string q = (query ?? "").Trim();
        bool hasQuery = !string.IsNullOrEmpty(q);
        var list = new List<CommandBarItem>();

        if (hasQuery)
        {
            if (LooksLikeUrl(q))
            {
                string navUrl = NormalizeUrl(q);
                list.Add(new CommandBarItem
                {
                    Title = navUrl,
                    Subtitle = "Navigate directly to website",
                    DisplayIcon = "\uD83C\uDF10",
                    ActionHint = "Go to URL",
                    Group = "Web",
                    Score = 900,
                    Action = () =>
                    {
                        _mainWindow.CreateNewTab(navUrl, activate: true);
                        Close();
                        return Task.CompletedTask;
                    }
                });
            }

            string engine = SearchEngineName();
            list.Add(new CommandBarItem
            {
                Title = $"Search {engine} for \"{q}\"",
                Subtitle = "Search the web with your default search engine",
                DisplayIcon = "\uD83D\uDD0D",
                ActionHint = "Search",
                Group = "Web",
                Score = 300,
                Action = () =>
                {
                    _mainWindow.CreateNewTab(BuildSearchUrl(q), activate: true);
                    Close();
                    return Task.CompletedTask;
                }
            });
        }

        var openTabs = _mainWindow.Tabs;
        if (openTabs != null)
        {
            var tabItems = new List<CommandBarItem>();
            foreach (var tab in openTabs)
            {
                int score = 100;
                if (hasQuery)
                {
                    score = ScoreText(q, tab.Title, true);
                    score = Math.Max(score, ScoreText(q, tab.DomainTitle, false) * 7 / 10);
                    score = Math.Max(score, ScoreText(q, tab.Url, false) / 2);
                    if (score == 0) continue;
                }

                string tabTitle = string.IsNullOrWhiteSpace(tab.Title) ? "Empty Tab" : tab.Title;
                var targetTab = tab;

                tabItems.Add(new CommandBarItem
                {
                    Title = tabTitle,
                    Subtitle = $"Open Tab \u00B7 {tab.DomainTitle}",
                    DisplayIcon = "\uD83D\uDDD2",
                    ActionHint = "Switch to Tab",
                    Group = "Tabs",
                    Score = score,
                    Action = () =>
                    {
                        _mainWindow.ListTabs.SelectedItem = targetTab;
                        Close();
                        return Task.CompletedTask;
                    }
                });
            }

            int tabLimit = hasQuery ? 30 : 20;
            list.AddRange(tabItems.OrderByDescending(i => i.Score).Take(tabLimit));
        }

        var shortcuts = CustomShortcutService.GetAllShortcuts().Where(s => s.IsEnabled).ToList();
        foreach (var s in shortcuts)
        {
            int score = 100;
            if (hasQuery)
            {
                score = ScoreShortcut(q, s);
                if (score == 0) continue;
            }

            string hint = s.ActionType switch
            {
                ShortcutActionType.Script => "Run Script",
                ShortcutActionType.BrowserCommand => "Action",
                _ => "Open"
            };

            string group = s.ActionType switch
            {
                ShortcutActionType.Script => "Scripts",
                ShortcutActionType.BrowserCommand => "Commands",
                _ => "Links"
            };

            string subtitle = s.Description;
            if (s.ActionType == ShortcutActionType.BrowserCommand &&
                string.Equals(s.Payload, "mute_tab", StringComparison.OrdinalIgnoreCase) &&
                _mainWindow.CurrentTab?.IsMuted == true)
            {
                subtitle = "Current tab is muted \u00B7 select to unmute";
            }

            list.Add(new CommandBarItem
            {
                Title = s.Title,
                Subtitle = subtitle,
                DisplayIcon = s.Icon,
                ActionHint = hint,
                KeyGesture = s.KeyGesture,
                Shortcut = s,
                Group = group,
                Score = score,
                Action = async () =>
                {
                    await CustomShortcutService.ExecuteShortcutAsync(s, _mainWindow, q);
                    Close();
                }
            });
        }

        IEnumerable<CommandBarItem> ordered;
        if (hasQuery)
        {
            ordered = list.GroupBy(i => i.Group)
                .OrderByDescending(g => g.Max(i => i.Score))
                .ThenBy(g => GroupRank(g.Key))
                .SelectMany(g => g.OrderByDescending(i => i.Score));
        }
        else
        {
            ordered = list.GroupBy(i => i.Group)
                .OrderBy(g => GroupRank(g.Key))
                .SelectMany(g => g);
        }

        foreach (var item in ordered)
        {
            _items.Add(item);
        }

        if (_items.Count > 0)
        {
            ListResults.SelectedIndex = 0;
        }
    }

    private async void RunItem(CommandBarItem item)
    {
        if (item.Action != null)
        {
            await item.Action();
        }
    }

    private async void ExecuteSelectedItem()
    {
        if (ListResults.SelectedItem is CommandBarItem item && item.Action != null)
        {
            await item.Action();
            return;
        }

        string q = TxtSearch.Text.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            string url = LooksLikeUrl(q) ? NormalizeUrl(q) : BuildSearchUrl(q);

            _mainWindow.CreateNewTab(url, activate: true);
            Close();
        }
        else
        {
            _mainWindow.CreateNewTab();
            Close();
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = TxtSearch.Text;
        TxtPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
        BtnClear.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
        PopulateItems(q);
    }

    private void TxtSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (_items.Count > 0)
            {
                int next = (ListResults.SelectedIndex + 1) % _items.Count;
                ListResults.SelectedIndex = next;
                ListResults.ScrollIntoView(ListResults.SelectedItem);
            }
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            if (_items.Count > 0)
            {
                int prev = (ListResults.SelectedIndex - 1 + _items.Count) % _items.Count;
                ListResults.SelectedIndex = prev;
                ListResults.ScrollIntoView(ListResults.SelectedItem);
            }
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ExecuteSelectedItem();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void ListResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void ListResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? d = e.OriginalSource as DependencyObject;
        while (d != null && d is not ListBoxItem)
        {
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        if (d is ListBoxItem container && container.DataContext is CommandBarItem item)
        {
            e.Handled = true;
            RunItem(item);
        }
    }

    private void BtnClear_Click(object sender, MouseButtonEventArgs e)
    {
        TxtSearch.Text = "";
        TxtSearch.Focus();
    }

    private void ShowNewShortcutError(string message)
    {
        TxtNewError.Text = message;
        TxtNewError.Visibility = Visibility.Visible;
    }

    private void ResetNewShortcutForm()
    {
        _editingShortcutId = null;
        _urlWarnedPayload = null;
        TxtNewHeader.Text = "Create Custom Shortcut & Script";
        TxtSaveLabel.Text = "Save Shortcut";
        TxtNewIcon.Text = "\u26A1";
        TxtNewTitle.Text = "";
        TxtNewHotkey.Text = "";
        TxtNewPayload.Text = "";
        CmbNewActionType.SelectedIndex = 0;
        CmbNewCommand.SelectedIndex = 0;
        TxtNewError.Visibility = Visibility.Collapsed;
        CmbNewActionType_SelectionChanged(CmbNewActionType, null!);
    }

    private void CloseShortcutForm()
    {
        PnlNewShortcut.Visibility = Visibility.Collapsed;
        ResetNewShortcutForm();
        if (_returnToManage)
        {
            _returnToManage = false;
            RefreshManageList();
            PnlManage.Visibility = Visibility.Visible;
        }
        else
        {
            PopulateItems(TxtSearch.Text);
            TxtSearch.Focus();
        }
    }

    private void RefreshManageList()
    {
        _pendingDeleteId = null;
        ListManage.ItemsSource = CustomShortcutService.GetAllShortcuts()
            .OrderBy(s => s.IsBuiltIn)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .Select(s => new ManageRow(s))
            .ToList();
    }

    private void BtnNewShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        PnlManage.Visibility = Visibility.Collapsed;
        _returnToManage = false;
        ResetNewShortcutForm();
        PnlNewShortcut.Visibility = Visibility.Visible;
        TxtNewTitle.Focus();
    }

    private void BtnCloseNewShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        CloseShortcutForm();
    }

    private void BtnManage_Click(object sender, MouseButtonEventArgs e)
    {
        PnlNewShortcut.Visibility = Visibility.Collapsed;
        RefreshManageList();
        PnlManage.Visibility = Visibility.Visible;
    }

    private void BtnCloseManage_Click(object sender, MouseButtonEventArgs e)
    {
        PnlManage.Visibility = Visibility.Collapsed;
        PopulateItems(TxtSearch.Text);
        TxtSearch.Focus();
    }

    private void BtnEditShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ManageRow row) return;
        var s = row.Shortcut;

        ResetNewShortcutForm();
        _editingShortcutId = s.Id;
        _returnToManage = true;
        TxtNewHeader.Text = "Edit Shortcut";
        TxtSaveLabel.Text = "Update Shortcut";
        TxtNewIcon.Text = s.Icon;
        TxtNewTitle.Text = s.Title;
        TxtNewHotkey.Text = s.KeyGesture;

        CmbNewActionType.SelectedIndex = s.ActionType switch
        {
            ShortcutActionType.Script => 0,
            ShortcutActionType.Url => 1,
            _ => 2
        };

        if (s.ActionType == ShortcutActionType.BrowserCommand)
        {
            for (int i = 0; i < CmbNewCommand.Items.Count; i++)
            {
                if (CmbNewCommand.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, s.Payload, StringComparison.OrdinalIgnoreCase))
                {
                    CmbNewCommand.SelectedIndex = i;
                    break;
                }
            }
        }
        else
        {
            TxtNewPayload.Text = s.Payload;
        }

        PnlManage.Visibility = Visibility.Collapsed;
        PnlNewShortcut.Visibility = Visibility.Visible;
        TxtNewTitle.Focus();
    }

    private void BtnDeleteShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ManageRow row) return;

        if (_pendingDeleteId == row.Shortcut.Id)
        {
            CustomShortcutService.DeleteShortcut(row.Shortcut.Id);
            RefreshManageList();
            return;
        }

        _pendingDeleteId = row.Shortcut.Id;
        if (sender is Border border && border.Child is TextBlock label)
        {
            label.Text = "Confirm?";
        }
    }

    private void CmbNewActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LblNewPayload == null || PnlNewPayload == null || PnlNewCommand == null) return;

        int index = CmbNewActionType.SelectedIndex;
        bool isCommand = index == 2;
        PnlNewPayload.Visibility = isCommand ? Visibility.Collapsed : Visibility.Visible;
        PnlNewCommand.Visibility = isCommand ? Visibility.Visible : Visibility.Collapsed;
        if (isCommand) return;

        bool isScript = index == 0;
        LblNewPayload.Text = isScript
            ? "JavaScript Code (Executes on current active tab):"
            : "Web URL Template (Use {query}, {url}, or {title} placeholders):";

        string current = TxtNewPayload.Text.Trim();
        if (current.Length == 0 || current == DefaultScriptPayload || current == DefaultUrlPayload)
        {
            TxtNewPayload.Text = isScript ? DefaultScriptPayload : DefaultUrlPayload;
        }
    }

    private static bool IsHttpUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private void BtnSaveNewShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        string title = TxtNewTitle.Text.Trim();
        int typeIndex = CmbNewActionType.SelectedIndex;
        string payload = typeIndex == 2
            ? ((CmbNewCommand.SelectedItem as ComboBoxItem)?.Tag as string ?? "")
            : TxtNewPayload.Text.Trim();

        if (string.IsNullOrEmpty(title))
        {
            ShowNewShortcutError("Enter a title.");
            return;
        }

        if (typeIndex == 0 && (payload.Length == 0 || payload == DefaultScriptPayload))
        {
            ShowNewShortcutError("Enter the JavaScript to run.");
            return;
        }

        if (typeIndex == 1 && (!IsHttpUrl(payload) || payload.Length <= "https://".Length))
        {
            ShowNewShortcutError("The URL template must start with http:// or https:// and contain an address.");
            return;
        }

        if (typeIndex == 1 && !payload.Contains("{query}") && _urlWarnedPayload != payload)
        {
            _urlWarnedPayload = payload;
            ShowNewShortcutError("This URL has no {query}, so text typed in the command bar will be ignored. Click save again to keep it as is.");
            return;
        }

        if (typeIndex == 2 && payload.Length == 0)
        {
            ShowNewShortcutError("Choose a browser command.");
            return;
        }

        string? gestureError = CustomShortcutService.ValidateGesture(TxtNewHotkey.Text, _editingShortcutId, out string gesture);
        if (gestureError != null)
        {
            ShowNewShortcutError(gestureError);
            return;
        }

        ShortcutActionType type = typeIndex switch
        {
            0 => ShortcutActionType.Script,
            1 => ShortcutActionType.Url,
            _ => ShortcutActionType.BrowserCommand
        };

        string defaultIcon = type switch
        {
            ShortcutActionType.Script => "\u26A1",
            ShortcutActionType.Url => "\uD83C\uDF10",
            _ => "\u2699"
        };

        string description = type switch
        {
            ShortcutActionType.Script => "Custom user JavaScript action",
            ShortcutActionType.Url => "Custom user URL shortcut",
            _ => "Custom browser command"
        };

        string category = type switch
        {
            ShortcutActionType.Script => "Script",
            ShortcutActionType.Url => "Navigation",
            _ => "Action"
        };

        string keywordSuffix = type switch
        {
            ShortcutActionType.Script => " custom script js",
            ShortcutActionType.Url => " custom url link",
            _ => " custom command action"
        };

        string icon = string.IsNullOrWhiteSpace(TxtNewIcon.Text) ? defaultIcon : TxtNewIcon.Text.Trim();

        if (_editingShortcutId != null)
        {
            var existing = CustomShortcutService.GetAllShortcuts().FirstOrDefault(x => x.Id == _editingShortcutId);
            if (existing != null)
            {
                existing.Title = title;
                existing.Description = description;
                existing.Icon = icon;
                existing.Category = category;
                existing.ActionType = type;
                existing.Payload = payload;
                existing.KeyGesture = gesture;
                existing.Keywords = title.ToLowerInvariant() + keywordSuffix;
                CustomShortcutService.UpdateShortcut(existing);
            }
        }
        else
        {
            CustomShortcutService.AddShortcut(new CustomShortcut
            {
                Title = title,
                Description = description,
                Icon = icon,
                Category = category,
                ActionType = type,
                Payload = payload,
                KeyGesture = gesture,
                Keywords = title.ToLowerInvariant() + keywordSuffix
            });
        }

        CloseShortcutForm();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private bool _isClosing;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _snapshot = null;
        _snapshotWall = null;
        if (_instance == this)
        {
            _instance = null;
        }
    }
}
