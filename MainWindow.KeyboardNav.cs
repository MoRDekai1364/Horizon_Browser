using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Horizon.Stealth.Services;

namespace Horizon.Stealth;

public partial class MainWindow
{
    private class NativeHintItem
    {
        public string Code { get; set; } = "";
        public FrameworkElement Element { get; set; } = null!;
        public Action Action { get; set; } = null!;
        public Border? Badge { get; set; }
        public TextBlock? TextPrefix { get; set; }
        public TextBlock? TextSuffix { get; set; }
    }

    public static bool IsStickyKeyboardNavActive { get; set; } = false;

    private bool _isNativeHintsActive = false;
    private bool _isNativeStickyMode = false;
    private string _nativeCurrentQuery = "";
    private readonly List<NativeHintItem> _nativeHints = new();

    private static readonly string[] HeaderCodeSequence = new[]
    {
        "HS", "HA", "HD", "HF", "HJ", "HK", "HL", "HE", "HW", "HC", "HM", "HP", "HG", "HH",
        "JS", "JA", "JD", "JF", "JJ", "JK", "JL", "JE", "JW", "JC", "JM", "JP", "JG", "JH"
    };

    private static readonly string[] SidebarCodeSequence = new[]
    {
        "WS", "WA", "WD", "WF", "WJ", "WK", "WL", "WE", "WW", "WC", "WM", "WP", "WG", "WH",
        "ES", "EA", "ED", "EF", "EJ", "EK", "EL", "EE", "EW", "EC", "EM", "EP", "EG", "EH"
    };

    /// <summary>
    /// Attaches keyboard navigation messaging hooks to the given browser instance.
    /// </summary>
    public void WireKeyboardNavForBrowser(Controls.BrowserView? browser)
    {
        if (browser == null) return;
        browser.KeyboardNavToggleRequested -= OnBrowserKeyboardNavToggleRequested;
        browser.KeyboardNavToggleRequested += OnBrowserKeyboardNavToggleRequested;
        browser.KeyboardNavKeyReceived -= OnBrowserKeyboardNavKeyReceived;
        browser.KeyboardNavKeyReceived += OnBrowserKeyboardNavKeyReceived;
        browser.KeyboardNavExited -= OnBrowserKeyboardNavExited;
        browser.KeyboardNavExited += OnBrowserKeyboardNavExited;
    }

    private void OnBrowserKeyboardNavToggleRequested(object? sender, bool sticky)
    {
        Dispatcher.Invoke(() =>
        {
            if (_isNativeHintsActive && !_isNativeStickyMode && sticky)
            {
                _isNativeStickyMode = true;
                IsStickyKeyboardNavActive = true;
                return;
            }

            if (_isNativeHintsActive && (!sticky || _isNativeStickyMode))
            {
                HideNativeHints();
            }
            else
            {
                ShowNativeHints(sticky);
            }
        });
    }

    private void OnBrowserKeyboardNavKeyReceived(object? sender, string key)
    {
        Dispatcher.Invoke(() =>
        {
            ProcessKeyboardNavKey(key);
        });
    }

    private void OnBrowserKeyboardNavExited(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isNativeStickyMode)
            {
                HideNativeHints();
            }
        });
    }

    /// <summary>
    /// Toggles hint navigation across the entire browser (Header, Sidebar, and active webpage).
    /// </summary>
    public void ToggleAllKeyboardNavigation(bool sticky = false)
    {
        if (_isNativeHintsActive)
        {
            if (!_isNativeStickyMode && sticky)
            {
                _isNativeStickyMode = true;
                IsStickyKeyboardNavActive = true;
                CurrentBrowser?.ToggleKeyboardNavigation(true);
                return;
            }

            HideNativeHints();
            CurrentBrowser?.DismissKeyboardNavigation();
        }
        else
        {
            ShowNativeHints(sticky);
            CurrentBrowser?.ToggleKeyboardNavigation(sticky);
        }
    }

    /// <summary>
    /// Renders native hint badges over all visible Header and Sidebar interactive elements.
    /// </summary>
    public void ShowNativeHints(bool sticky)
    {
        _isNativeHintsActive = true;
        _isNativeStickyMode = sticky;
        IsStickyKeyboardNavActive = sticky;
        _nativeCurrentQuery = "";

        if (PnlNativeHintOverlay == null) return;
        PnlNativeHintOverlay.Children.Clear();
        _nativeHints.Clear();
        PnlNativeHintOverlay.Visibility = Visibility.Visible;

        RenderNativeHints();
    }

    /// <summary>
    /// Re-renders native hint badges based on current UI layout.
    /// </summary>
    private void RenderNativeHints()
    {
        if (PnlNativeHintOverlay == null) return;
        PnlNativeHintOverlay.Children.Clear();
        _nativeHints.Clear();

        // 1. Collect Header Elements
        var headerElements = new List<(FrameworkElement Element, Action Action)>();

        if (BtnBack != null && BtnBack.IsVisible)
            headerElements.Add((BtnBack, () => BtnBack_Click(BtnBack, new RoutedEventArgs())));

        if (BtnForward != null && BtnForward.IsVisible)
            headerElements.Add((BtnForward, () => BtnForward_Click(BtnForward, new RoutedEventArgs())));

        if (BtnReload != null && BtnReload.IsVisible)
            headerElements.Add((BtnReload, () => BtnReload_Click(BtnReload, new RoutedEventArgs())));

        if (OmniboxControl != null && OmniboxControl.IsVisible)
            headerElements.Add((OmniboxControl, () =>
            {
                OmniboxControl.UrlInput?.Focus();
                OmniboxControl.UrlInput?.SelectAll();
            }));

        if (BtnSplitView != null && BtnSplitView.IsVisible)
            headerElements.Add((BtnSplitView, () => BtnSplitView_Click(BtnSplitView, new RoutedEventArgs())));

        if (BtnTabSwitcher != null && BtnTabSwitcher.IsVisible)
            headerElements.Add((BtnTabSwitcher, () => BtnCompactTabsBadge_Click(BtnTabSwitcher, new RoutedEventArgs())));

        if (BtnCreateWebApp != null && BtnCreateWebApp.IsVisible)
            headerElements.Add((BtnCreateWebApp, () => BtnInstallWebApp_Click(BtnCreateWebApp, new RoutedEventArgs())));

        // Open Tabs
        if (ListTabs != null && ListTabs.IsVisible)
        {
            for (int i = 0; i < ListTabs.Items.Count; i++)
            {
                int tabIdx = i;
                var lbi = ListTabs.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (lbi != null && lbi.IsVisible)
                {
                    headerElements.Add((lbi, () => ListTabs.SelectedIndex = tabIdx));
                }
            }
        }

        if (BtnNewTab != null && BtnNewTab.IsVisible)
            headerElements.Add((BtnNewTab, () => BtnNewTab_Click(BtnNewTab, new RoutedEventArgs())));

        if (BtnSettings != null && BtnSettings.IsVisible)
            headerElements.Add((BtnSettings, () => BtnSettings_Click(BtnSettings, new RoutedEventArgs())));

        if (BtnToggleSidebarHeader != null && BtnToggleSidebarHeader.IsVisible)
            headerElements.Add((BtnToggleSidebarHeader, () => ToggleSidebar_Click(BtnToggleSidebarHeader, new RoutedEventArgs())));

        if (BtnMinimizeHeader != null && BtnMinimizeHeader.IsVisible)
            headerElements.Add((BtnMinimizeHeader, () => WindowState = WindowState.Minimized));

        if (BtnMaximizeHeader != null && BtnMaximizeHeader.IsVisible)
            headerElements.Add((BtnMaximizeHeader, () => BtnMaximize_Click(this, new RoutedEventArgs())));

        if (BtnCloseHeader != null && BtnCloseHeader.IsVisible)
            headerElements.Add((BtnCloseHeader, () => BtnClose_Click(this, new RoutedEventArgs())));

        // Assign codes to Header elements
        for (int i = 0; i < headerElements.Count && i < HeaderCodeSequence.Length; i++)
        {
            var (el, action) = headerElements[i];
            _nativeHints.Add(new NativeHintItem
            {
                Code = HeaderCodeSequence[i],
                Element = el,
                Action = action
            });
        }

        // 2. Collect Sidebar Elements (if open)
        if (SidebarContainer != null && SidebarContainer.ActualWidth > 10 && RightSidebar != null && RightSidebar.IsVisible)
        {
            var sidebarElements = new List<(FrameworkElement Element, Action Action)>();
            CollectSidebarInteractiveElements(RightSidebar, sidebarElements);

            for (int i = 0; i < sidebarElements.Count && i < SidebarCodeSequence.Length; i++)
            {
                var (el, action) = sidebarElements[i];
                _nativeHints.Add(new NativeHintItem
                {
                    Code = SidebarCodeSequence[i],
                    Element = el,
                    Action = action
                });
            }
        }

        // 3. Render Badges
        foreach (var hint in _nativeHints)
        {
            try
            {
                if (!hint.Element.IsVisible || hint.Element.ActualWidth <= 0 || hint.Element.ActualHeight <= 0)
                    continue;

                Point pt = hint.Element.TransformToAncestor(this).Transform(new Point(0, 0));
                if (pt.X < 0 || pt.X > ActualWidth - 10 || pt.Y < 0 || pt.Y > ActualHeight - 10)
                    continue;

                var badge = CreateNativeBadge(hint.Code, out var prefixText, out var suffixText);
                hint.Badge = badge;
                hint.TextPrefix = prefixText;
                hint.TextSuffix = suffixText;

                Canvas.SetLeft(badge, Math.Max(2, pt.X + 2));
                Canvas.SetTop(badge, Math.Max(2, pt.Y + 2));
                PnlNativeHintOverlay.Children.Add(badge);
            }
            catch { }
        }
    }

    private void CollectSidebarInteractiveElements(DependencyObject parent, List<(FrameworkElement Element, Action Action)> items)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.IsVisible && fe.ActualWidth > 0 && fe.ActualHeight > 0)
            {
                if (fe is ButtonBase btn && btn.IsEnabled)
                {
                    items.Add((btn, () => btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));
                }
                else if (fe is Border b && (b.Name.StartsWith("Btn") || b.Cursor == Cursors.Hand))
                {
                    items.Add((b, () => b.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent })));
                }
                else if (fe is ListBoxItem lbi)
                {
                    items.Add((lbi, () => lbi.IsSelected = true));
                }
                else
                {
                    CollectSidebarInteractiveElements(child, items);
                }
            }
            else if (child != null)
            {
                CollectSidebarInteractiveElements(child, items);
            }
        }
    }

    private Border CreateNativeBadge(string code, out TextBlock prefixText, out TextBlock suffixText)
    {
        var border = new Border
        {
            Background = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FFF785"),
                (Color)ColorConverter.ConvertFromString("#FFC542"),
                new Point(0, 0),
                new Point(0, 1)),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E3BE23")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 1, 3, 1),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.45,
                BlurRadius = 3,
                ShadowDepth = 1,
                Direction = 270
            },
            IsHitTestVisible = false
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        prefixText = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("-apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D35400")),
            Background = Brushes.White,
            Visibility = Visibility.Collapsed
        };

        suffixText = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("-apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black
        };

        panel.Children.Add(prefixText);
        panel.Children.Add(suffixText);
        border.Child = panel;
        return border;
    }

    /// <summary>
    /// Filters native badges according to typed prefix query.
    /// </summary>
    private void FilterNativeHints(string query)
    {
        string q = query.ToUpperInvariant();
        foreach (var hint in _nativeHints)
        {
            if (hint.Badge == null) continue;

            if (string.IsNullOrEmpty(q))
            {
                hint.Badge.Visibility = Visibility.Visible;
                if (hint.TextPrefix != null) hint.TextPrefix.Visibility = Visibility.Collapsed;
                if (hint.TextSuffix != null) hint.TextSuffix.Text = hint.Code;
            }
            else if (hint.Code.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            {
                hint.Badge.Visibility = Visibility.Visible;
                if (hint.TextPrefix != null)
                {
                    hint.TextPrefix.Text = hint.Code.Substring(0, q.Length);
                    hint.TextPrefix.Visibility = Visibility.Visible;
                }
                if (hint.TextSuffix != null)
                {
                    hint.TextSuffix.Text = hint.Code.Substring(q.Length);
                }
            }
            else
            {
                hint.Badge.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Executes the action corresponding to a matched native hint code.
    /// </summary>
    private bool ExecuteNativeHint(string code)
    {
        var exact = _nativeHints.FirstOrDefault(h => h.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            try
            {
                exact.Action.Invoke();
            }
            catch (Exception ex)
            {
                LogService.Debug("keyboard_nav", () => $"Action failed for {code}: {ex.Message}");
            }

            if (_isNativeStickyMode)
            {
                _nativeCurrentQuery = "";
                FilterNativeHints("");
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(100);
                    if (_isNativeHintsActive && _isNativeStickyMode)
                    {
                        RenderNativeHints();
                    }
                });
            }
            else
            {
                HideNativeHints();
                CurrentBrowser?.DismissKeyboardNavigation();
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Processes a keystroke (from WPF or forwarded from WebView2) in the native navigation engine.
    /// </summary>
    public void ProcessKeyboardNavKey(string key)
    {
        if (!_isNativeHintsActive) return;

        if (key.Equals("ESCAPE", StringComparison.OrdinalIgnoreCase))
        {
            HideNativeHints();
            CurrentBrowser?.DismissKeyboardNavigation();
            return;
        }

        if (key.Equals("BACKSPACE", StringComparison.OrdinalIgnoreCase))
        {
            if (_nativeCurrentQuery.Length > 0)
            {
                _nativeCurrentQuery = _nativeCurrentQuery.Substring(0, _nativeCurrentQuery.Length - 1);
            }
            FilterNativeHints(_nativeCurrentQuery);
            return;
        }

        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if ("SADFJKLEWCMPGH".Contains(c))
            {
                _nativeCurrentQuery += c;
                FilterNativeHints(_nativeCurrentQuery);
                ExecuteNativeHint(_nativeCurrentQuery);
            }
        }
    }

    /// <summary>
    /// Dismisses all native hint badges and resets state.
    /// </summary>
    public void HideNativeHints()
    {
        _isNativeHintsActive = false;
        _isNativeStickyMode = false;
        IsStickyKeyboardNavActive = false;
        _nativeCurrentQuery = "";
        _nativeHints.Clear();
        if (PnlNativeHintOverlay != null)
        {
            PnlNativeHintOverlay.Children.Clear();
            PnlNativeHintOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
