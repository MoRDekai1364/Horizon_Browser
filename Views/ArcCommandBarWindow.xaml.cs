using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth.Views;

public partial class ArcCommandBarWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly ObservableCollection<CommandBarItem> _items = new();
    private static ArcCommandBarWindow? _instance;

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
    }

    public ArcCommandBarWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        Owner = mainWindow;
        ListResults.ItemsSource = _items;

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

        // Position at upper center of main window (Arc style)
        double left = mainWindow.Left + (mainWindow.ActualWidth - _instance.Width) / 2.0;
        double top = mainWindow.Top + Math.Max(70.0, mainWindow.ActualHeight * 0.16);

        _instance.Left = Math.Max(0.0, left);
        _instance.Top = Math.Max(0.0, top);

        _instance.Show();
        _instance.Activate();
        _instance.TxtSearch.Focus();
    }

    private void ArcCommandBarWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyBlur();
        PopulateItems("");
        TxtSearch.Focus();
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

    private void PopulateItems(string query)
    {
        _items.Clear();
        string q = (query ?? "").Trim();
        bool hasQuery = !string.IsNullOrEmpty(q);

        // 1. Direct URL navigation or Web Search if query present
        if (hasQuery)
        {
            bool looksLikeUrl = q.Contains('.') && !q.Contains(' ') && !q.StartsWith("?") ||
                                q.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                q.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                q.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);

            if (looksLikeUrl)
            {
                string navUrl = q.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                q.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                                ? q : "https://" + q;

                _items.Add(new CommandBarItem
                {
                    Title = navUrl,
                    Subtitle = "Navigate directly to website",
                    DisplayIcon = "\uD83C\uDF10",
                    ActionHint = "Go to URL",
                    Action = () =>
                    {
                        _mainWindow.CreateNewTab(navUrl, activate: true);
                        Close();
                        return Task.CompletedTask;
                    }
                });
            }

            _items.Add(new CommandBarItem
            {
                Title = $"Search Google for \"{q}\"",
                Subtitle = "Search the web with default engine",
                DisplayIcon = "\uD83D\uDD0D",
                ActionHint = "Search",
                Action = () =>
                {
                    string searchUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString(q);
                    _mainWindow.CreateNewTab(searchUrl, activate: true);
                    Close();
                    return Task.CompletedTask;
                }
            });
        }

        // 2. Open Tabs fuzzy match (Switch to Tab)
        var openTabs = _mainWindow.Tabs;
        if (openTabs != null)
        {
            foreach (var tab in openTabs)
            {
                if (!hasQuery || (tab.Title != null && tab.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                 (tab.Url != null && tab.Url.Contains(q, StringComparison.OrdinalIgnoreCase)))
                {
                    string tabTitle = string.IsNullOrWhiteSpace(tab.Title) ? "Empty Tab" : tab.Title;
                    string tabUrl = tab.Url ?? "";
                    var targetTab = tab;

                    _items.Add(new CommandBarItem
                    {
                        Title = tabTitle,
                        Subtitle = $"Open Tab \u00B7 {tab.DomainTitle}",
                        DisplayIcon = "\uD83D\uDDD2",
                        ActionHint = "Switch to Tab",
                        Action = () =>
                        {
                            _mainWindow.ListTabs.SelectedItem = targetTab;
                            Close();
                            return Task.CompletedTask;
                        }
                    });

                    if (_items.Count > 15) break;
                }
            }
        }

        // 3. Custom Shortcuts & User Scripts
        var shortcuts = CustomShortcutService.GetAllShortcuts().Where(s => s.IsEnabled).ToList();
        foreach (var s in shortcuts)
        {
            bool match = !hasQuery ||
                         s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         s.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         s.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         s.Category.Contains(q, StringComparison.OrdinalIgnoreCase);

            if (match)
            {
                string hint = s.ActionType switch
                {
                    ShortcutActionType.Script => "Run Script",
                    ShortcutActionType.BrowserCommand => "Action",
                    _ => "Open"
                };

                _items.Add(new CommandBarItem
                {
                    Title = s.Title,
                    Subtitle = s.Description,
                    DisplayIcon = s.Icon,
                    ActionHint = hint,
                    KeyGesture = s.KeyGesture,
                    Shortcut = s,
                    Action = async () =>
                    {
                        await CustomShortcutService.ExecuteShortcutAsync(s, _mainWindow, q);
                        Close();
                    }
                });
            }
        }

        if (_items.Count > 0)
        {
            ListResults.SelectedIndex = 0;
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
            string url = q.Contains('.') && !q.Contains(' ')
                ? (q.StartsWith("http") ? q : "https://" + q)
                : "https://www.google.com/search?q=" + Uri.EscapeDataString(q);

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

    private void ListResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelectedItem();
    }

    private void BtnClear_Click(object sender, MouseButtonEventArgs e)
    {
        TxtSearch.Text = "";
        TxtSearch.Focus();
    }

    private void BtnNewShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        PnlNewShortcut.Visibility = Visibility.Visible;
        TxtNewTitle.Focus();
    }

    private void BtnCloseNewShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        PnlNewShortcut.Visibility = Visibility.Collapsed;
        TxtSearch.Focus();
    }

    private void CmbNewActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LblNewPayload == null) return;
        bool isScript = CmbNewActionType.SelectedIndex == 0;
        LblNewPayload.Text = isScript
            ? "JavaScript Code (Executes on current active tab):"
            : "Web URL Template (Use {query}, {url}, or {title} placeholders):";

        if (string.IsNullOrWhiteSpace(TxtNewPayload.Text))
        {
            TxtNewPayload.Text = isScript
                ? "console.log('Running Horizon script on:', location.href);"
                : "https://";
        }
    }

    private void BtnSaveNewShortcut_Click(object sender, MouseButtonEventArgs e)
    {
        string title = TxtNewTitle.Text.Trim();
        string payload = TxtNewPayload.Text.Trim();
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(payload))
        {
            MessageBox.Show(this, "Please enter both a Title and a valid Script / URL payload.", "New Shortcut", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isScript = CmbNewActionType.SelectedIndex == 0;
        var shortcut = new CustomShortcut
        {
            Title = title,
            Description = isScript ? "Custom user JavaScript action" : "Custom user URL shortcut",
            Icon = string.IsNullOrWhiteSpace(TxtNewIcon.Text) ? (isScript ? "\u26A1" : "\uD83C\uDF10") : TxtNewIcon.Text.Trim(),
            Category = isScript ? "Script" : "Navigation",
            ActionType = isScript ? ShortcutActionType.Script : ShortcutActionType.Url,
            Payload = payload,
            KeyGesture = TxtNewHotkey.Text.Trim(),
            Keywords = title.ToLowerInvariant() + (isScript ? " custom script js" : " custom url link")
        };

        CustomShortcutService.AddShortcut(shortcut);
        PnlNewShortcut.Visibility = Visibility.Collapsed;
        PopulateItems(TxtSearch.Text);
        TxtSearch.Focus();
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
        if (_instance == this)
        {
            _instance = null;
        }
    }
}
