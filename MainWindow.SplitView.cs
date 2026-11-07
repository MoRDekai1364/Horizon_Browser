using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth;

public partial class MainWindow : Window
{
    private const double SplitPaneMinWidth = 260;
    private const double SplitDividerWidth = 6;

    private bool _splitActive;
    private bool _splitPicking;
    private bool _splitAwaitNewTab;
    private TabViewModel? _splitOtherTab;
    private Border? _splitPickerHost;
    private GridSplitter? _splitDivider;
    private Border? _splitIndicatorLeft;
    private Border? _splitIndicatorRight;
    private readonly HashSet<Controls.BrowserView> _splitFocusHooked = new();

    public bool IsSplitViewActive => _splitActive;

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

    private void SplitBuildLayout()
    {
        double ratio = Math.Clamp(SettingsService.Current.SplitViewRatio, 0.2, 0.8);
        ViewContainer.ColumnDefinitions.Clear();
        ViewContainer.RowDefinitions.Clear();
        ViewContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        ViewContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ViewContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star), MinWidth = SplitPaneMinWidth });
        ViewContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ViewContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star), MinWidth = SplitPaneMinWidth });

        _splitDivider = new GridSplitter
        {
            Width = SplitDividerWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = false,
            Cursor = Cursors.SizeWE,
            Background = SplitBrush("Brush_SurfaceChrome", Brushes.DimGray)
        };
        Grid.SetColumn(_splitDivider, 1);
        Grid.SetRow(_splitDivider, 1);
        _splitDivider.DragCompleted += (_, _) => SplitSaveRatio();
        ViewContainer.Children.Add(_splitDivider);

        _splitIndicatorLeft = new Border { Background = Brushes.Transparent };
        _splitIndicatorRight = new Border { Background = Brushes.Transparent };
        Grid.SetColumn(_splitIndicatorLeft, 0);
        Grid.SetColumn(_splitIndicatorRight, 2);
        Grid.SetRow(_splitIndicatorLeft, 0);
        Grid.SetRow(_splitIndicatorRight, 0);
        ViewContainer.Children.Add(_splitIndicatorLeft);
        ViewContainer.Children.Add(_splitIndicatorRight);

        foreach (var view in _tabViews.Values)
        {
            Grid.SetRow(view, 1);
        }

        ViewContainer.SizeChanged += SplitContainerSizeChanged;
        SplitUpdateIndicator();
    }

    private void SplitSaveRatio()
    {
        if (ViewContainer.ColumnDefinitions.Count != 3) return;
        double left = ViewContainer.ColumnDefinitions[0].ActualWidth;
        double right = ViewContainer.ColumnDefinitions[2].ActualWidth;
        double total = left + right;
        if (total <= 0) return;
        SettingsService.Current.SplitViewRatio = Math.Clamp(left / total, 0.2, 0.8);
        SettingsService.Save();
    }

    private void SplitContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_splitActive) return;
        if (e.NewSize.Width <= 0) return;
        if (e.NewSize.Width < SplitPaneMinWidth * 2 + SplitDividerWidth)
        {
            ExitSplitView();
        }
    }

    private void SplitUpdateIndicator()
    {
        if (!_splitActive || _splitIndicatorLeft == null || _splitIndicatorRight == null) return;
        var active = CurrentTab;
        int col = 0;
        if (active != null && _tabViews.TryGetValue(active, out var view)) col = Grid.GetColumn(view);
        var accent = SplitBrush("Brush_Accent", Brushes.Gray);
        _splitIndicatorLeft.Background = col == 0 ? accent : Brushes.Transparent;
        _splitIndicatorRight.Background = col == 2 ? accent : Brushes.Transparent;
    }

    private void SplitScheduleIndicator()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(SplitUpdateIndicator));
    }

    private void SplitRemoveLayout()
    {
        ViewContainer.SizeChanged -= SplitContainerSizeChanged;
        if (_splitDivider != null)
        {
            ViewContainer.Children.Remove(_splitDivider);
            _splitDivider = null;
        }
        if (_splitIndicatorLeft != null)
        {
            ViewContainer.Children.Remove(_splitIndicatorLeft);
            _splitIndicatorLeft = null;
        }
        if (_splitIndicatorRight != null)
        {
            ViewContainer.Children.Remove(_splitIndicatorRight);
            _splitIndicatorRight = null;
        }
        if (_splitPickerHost != null)
        {
            ViewContainer.Children.Remove(_splitPickerHost);
            _splitPickerHost = null;
        }
        foreach (var view in _tabViews.Values)
        {
            Grid.SetColumn(view, 0);
            Grid.SetRow(view, 0);
        }
        ViewContainer.ColumnDefinitions.Clear();
        ViewContainer.RowDefinitions.Clear();
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
        if (_splitActive)
        {
            ExitSplitView();
            return;
        }
        EnterSplitView();
    }

    public void EnterSplitView()
    {
        if (_splitActive) return;
        if (CurrentTab == null || _activeTabView == null) return;

        _splitActive = true;
        _splitPicking = true;
        _splitOtherTab = null;
        _splitAwaitNewTab = false;
        Grid.SetColumn(_activeTabView, 0);
        SplitBuildLayout();
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
        var other = _splitOtherTab;
        var keepTab = keep ?? active;

        _splitActive = false;
        _splitPicking = false;
        _splitAwaitNewTab = false;
        _splitOtherTab = null;

        if (other != null && !ReferenceEquals(other, keepTab) && _tabViews.TryGetValue(other, out var otherView))
        {
            otherView.Visibility = Visibility.Collapsed;
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
        if (!_tabViews.TryGetValue(tab, out var view))
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
        _splitOtherTab = tab;
        Grid.SetColumn(view, 2);
        Grid.SetRow(view, 1);
        WakeTab(tab);
        view.Visibility = Visibility.Visible;
        SplitHookFocus(view);
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
            if (_tabViews.TryGetValue(selected, out var newView) && _activeTabView != null)
            {
                _splitPicking = false;
                _splitOtherTab = SplitTabOf(_activeTabView);
                Grid.SetColumn(newView, 2);
                Grid.SetRow(newView, 1);
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

        if (ReferenceEquals(selected, _splitOtherTab))
        {
            _splitOtherTab = SplitTabOf(_activeTabView);
            SplitScheduleIndicator();
            return true;
        }

        if (_activeTabView != null && _tabViews.TryGetValue(selected, out var incoming))
        {
            Grid.SetColumn(incoming, Grid.GetColumn(_activeTabView));
            Grid.SetRow(incoming, 1);
            SplitHookFocus(incoming);
        }
        SplitScheduleIndicator();
        return false;
    }

    private void SplitViewOnTabClosing(TabViewModel tab)
    {
        if (!_splitActive) return;

        if (ReferenceEquals(tab, _splitOtherTab))
        {
            ExitSplitView();
            return;
        }

        if (ReferenceEquals(tab, CurrentTab))
        {
            if (_splitOtherTab != null) ExitSplitView(_splitOtherTab);
            else ExitSplitView();
        }
    }

    private bool SplitViewOwnsTab(TabViewModel tab)
    {
        if (!_splitActive) return false;
        return ReferenceEquals(tab, _splitOtherTab) || ReferenceEquals(tab, CurrentTab);
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
        Grid.SetColumn(_splitPickerHost, 2);
        Grid.SetRow(_splitPickerHost, 1);
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
