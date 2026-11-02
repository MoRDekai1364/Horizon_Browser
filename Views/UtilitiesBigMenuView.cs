using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public sealed class UtilitiesBigMenuView : UserControl
{
    public event Action? CloseRequested;

    private readonly HomePageView _homePage;
    private readonly TextBox _txtSearch;
    private readonly StackPanel _cardsWrapPanel;
    private readonly ComboBox _cmbSort;
    private string _activeCategory = "All";
    private string _activeSort = "Category";
    private Point _dragStartPoint;

    public UtilitiesBigMenuView(HomePageView homePage)
    {
        _homePage = homePage;
        HomeGlass.SetExclude(this, true);

        var rootGrid = new Grid
        {
            Margin = new Thickness(36, 24, 36, 28),
            MaxWidth = 1380
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar (Search & Category Chips)
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scrollable Cards

        // 1. Header
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var iconText = new TextBlock
        {
            Text = "\u26A1",
            FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        var titleText = new TextBlock
        {
            Text = "Horizon Utilities & Browser Tools",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        var subtitleText = new TextBlock
        {
            Text = " \u2022 Master suite of 34+ utilities \u2022 Drag to Favorites, Bookmarks, Homepage, Header, or Widgets",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 3, 0, 0)
        };
        titleStack.Children.Add(iconText);
        titleStack.Children.Add(titleText);
        titleStack.Children.Add(subtitleText);
        Grid.SetColumn(titleStack, 0);
        headerGrid.Children.Add(titleStack);

        var btnClose = new Button
        {
            Content = "\u2715",
            FontSize = 14,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0)
        };
        btnClose.Click += (s, e) => { CloseRequested?.Invoke(); e.Handled = true; };
        Grid.SetColumn(btnClose, 1);
        headerGrid.Children.Add(btnClose);

        Grid.SetRow(headerGrid, 0);
        rootGrid.Children.Add(headerGrid);

        // 2. Toolbar (Search, Categories, Sort)
        var toolbarGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) }); // Search
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Categories
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Sort

        // Search Box with hint
        var searchBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x14, 0x18, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0)
        };
        var searchGrid = new Grid();
        var searchPlaceholder = new TextBlock
        {
            Text = "\uD83D\uDD0D Search utilities\u2026",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        _txtSearch = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        _txtSearch.TextChanged += (s, e) =>
        {
            searchPlaceholder.Visibility = string.IsNullOrEmpty(_txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            RefreshCards();
        };
        searchGrid.Children.Add(searchPlaceholder);
        searchGrid.Children.Add(_txtSearch);
        searchBorder.Child = searchGrid;
        Grid.SetColumn(searchBorder, 0);
        toolbarGrid.Children.Add(searchBorder);

        // Category Pills
        var catScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var catPanel = new StackPanel { Orientation = Orientation.Horizontal };
        AddCategoryChip(catPanel, "All", $"All ({UtilitiesService.Instance.GetAllUtilities().Count})", true);
        AddCategoryChip(catPanel, "Academic & Research", "\U0001F4DA Academic & Research (8)", false);
        AddCategoryChip(catPanel, "System & Network", "\U0001F5A5\uFE0F System & Network (10)", false);
        AddCategoryChip(catPanel, "Security & Privacy", "\uD83D\uDEE1\uFE0F Security & Privacy (13)", false);
        AddCategoryChip(catPanel, "Web Tools & Automation", "\u26A1 Web & Automation (15)", false);
        AddCategoryChip(catPanel, "Media & Accessibility", "\uD83C\uDFAC Media & Audio (6)", false);
        AddCategoryChip(catPanel, "Developer & Power Tools", "\uD83D\uDE80 Developer Tools (9)", false);
        catScroll.Content = catPanel;
        Grid.SetColumn(catScroll, 1);
        toolbarGrid.Children.Add(catScroll);

        // Sort ComboBox
        _cmbSort = new ComboBox
        {
            Width = 110,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x14, 0x18, 0x22)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF))
        };
        _cmbSort.Items.Add(new ComboBoxItem { Content = "Category", IsSelected = true });
        _cmbSort.Items.Add(new ComboBoxItem { Content = "Name" });
        _cmbSort.Items.Add(new ComboBoxItem { Content = "Pinned" });
        _cmbSort.SelectionChanged += (s, e) =>
        {
            if (_cmbSort.SelectedItem is ComboBoxItem cbi && cbi.Content is string sortName)
            {
                _activeSort = sortName;
                RefreshCards();
            }
        };
        Grid.SetColumn(_cmbSort, 2);
        toolbarGrid.Children.Add(_cmbSort);

        Grid.SetRow(toolbarGrid, 1);
        rootGrid.Children.Add(toolbarGrid);

        // 3. Scrollable Cards Container (Masonry / 4-Column Wrap)
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _cardsWrapPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 16)
        };

        scrollViewer.Content = _cardsWrapPanel;
        Grid.SetRow(scrollViewer, 2);
        rootGrid.Children.Add(scrollViewer);

        Content = rootGrid;
        RefreshCards();
    }

    private void AddCategoryChip(StackPanel container, string catKey, string label, bool isSelected)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = isSelected ? new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Tag = catKey
        };

        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
            Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF))
        };
        border.Child = text;

        border.MouseLeftButtonUp += (s, e) =>
        {
            _activeCategory = catKey;
            foreach (UIElement child in container.Children)
            {
                if (child is Border b && b.Child is TextBlock tb)
                {
                    bool active = b.Tag as string == catKey;
                    b.Background = active ? new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                    b.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                    tb.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
                    tb.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
                }
            }
            RefreshCards();
            e.Handled = true;
        };

        container.Children.Add(border);
    }

    public void RefreshCatalog()
    {
        RefreshCards();
    }

    private void RefreshCards()
    {
        _cardsWrapPanel.Children.Clear();

        UtilitiesService.Instance.RefreshItems(_txtSearch.Text, _activeCategory, _activeSort);
        var items = UtilitiesService.Instance.Items.ToList();

        // 4 Columns Grid
        var columnsGrid = new Grid();
        for (int i = 0; i < 4; i++)
        {
            columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var colPanels = new List<StackPanel>();
        for (int i = 0; i < 4; i++)
        {
            var pnl = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 8, 0, i == 3 ? 0 : 8, 0) };
            Grid.SetColumn(pnl, i);
            columnsGrid.Children.Add(pnl);
            colPanels.Add(pnl);
        }

        int targetCol = 0;
        foreach (var item in items)
        {
            var card = CreateUtilityCard(item);
            colPanels[targetCol].Children.Add(card);
            targetCol = (targetCol + 1) % 4;
        }

        _cardsWrapPanel.Children.Add(columnsGrid);
    }

    private FrameworkElement CreateUtilityCard(UtilityItem item)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x14, 0x18, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(12, 10, 12, 10),
            Cursor = Cursors.Hand,
            Tag = item
        };

        // Hover Effect
        card.MouseEnter += (s, e) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1C, 0x22, 0x30));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF));
        };
        card.MouseLeave += (s, e) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x14, 0x18, 0x22));
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        };

        var cardGrid = new Grid();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header (Icon, Title, Badge, Pin)
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Description
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Actions / Routing

        // 1. Header
        var headGrid = new Grid();
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title & Badge
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Pin

        // Icon Box
        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderBrush = ParseColorBrush(item.AccentColor),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var iconText = new TextBlock
        {
            Text = item.Icon,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = iconText;
        Grid.SetColumn(iconBorder, 0);
        headGrid.Children.Add(iconBorder);

        // Title and Badge
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock
        {
            Text = item.Title,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var badgeBorder = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 2, 0, 0)
        };
        var badgeText = new TextBlock
        {
            Text = item.BadgeText,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = ParseColorBrush(item.AccentColor)
        };
        badgeBorder.Child = badgeText;
        titleStack.Children.Add(titleText);
        titleStack.Children.Add(badgeBorder);
        Grid.SetColumn(titleStack, 1);
        headGrid.Children.Add(titleStack);

        // Pin Button
        var btnPin = new Button
        {
            Content = item.IsPinned ? "\uD83D\uDCCC" : "\uD83D\uDCCD",
            FontSize = 12,
            Foreground = item.IsPinned ? new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = item.IsPinned ? "Unpin from Top" : "Pin to Top",
            Padding = new Thickness(4)
        };
        btnPin.Click += (s, e) =>
        {
            UtilitiesService.Instance.TogglePin(item.Id);
            RefreshCards();
            e.Handled = true;
        };
        Grid.SetColumn(btnPin, 2);
        headGrid.Children.Add(btnPin);

        Grid.SetRow(headGrid, 0);
        cardGrid.Children.Add(headGrid);

        // 2. Description
        var descText = new TextBlock
        {
            Text = item.Description,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8),
            MaxHeight = 36,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(descText, 1);
        cardGrid.Children.Add(descText);

        // 3. Actions Row (Launch & Routing Menu)
        var actionGrid = new Grid();
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var btnLaunch = new Button
        {
            Content = "\u25B6 Launch",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x4A, 0x9E, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x4A, 0x9E, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0)
        };
        btnLaunch.Click += (s, e) =>
        {
            UtilitiesService.Instance.ExecuteUtility(item, Window.GetWindow(this));
            e.Handled = true;
        };
        Grid.SetColumn(btnLaunch, 0);
        actionGrid.Children.Add(btnLaunch);

        // Route Options Button
        var btnRoute = new Button
        {
            Content = "\u22EE Send to\u2026",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Height = 26,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(6, 0, 6, 0)
        };
        btnRoute.Click += (s, e) =>
        {
            OpenCardContextMenu(btnRoute, item);
            e.Handled = true;
        };
        Grid.SetColumn(btnRoute, 1);
        actionGrid.Children.Add(btnRoute);

        Grid.SetRow(actionGrid, 2);
        cardGrid.Children.Add(actionGrid);

        card.Child = cardGrid;

        // Context Menu on Right Click
        card.MouseRightButtonUp += (s, e) =>
        {
            OpenCardContextMenu(card, item);
            e.Handled = true;
        };

        // Double click launches
        card.MouseLeftButtonUp += (s, e) =>
        {
            if (e.ClickCount >= 2)
            {
                UtilitiesService.Instance.ExecuteUtility(item, Window.GetWindow(this));
                e.Handled = true;
            }
        };

        // Drag and Drop Pipeline
        card.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _dragStartPoint = e.GetPosition(null);
        };
        card.PreviewMouseMove += (s, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point pos = e.GetPosition(null);
                Vector diff = _dragStartPoint - pos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var data = new DataObject("HorizonUtilityItem", item);
                    DragDrop.DoDragDrop(card, data, DragDropEffects.Copy | DragDropEffects.Move);
                }
            }
        };

        return card;
    }

    private void OpenCardContextMenu(FrameworkElement anchor, UtilityItem item)
    {
        var cm = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x12, 0x15, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White
        };

        // Pin / Unpin
        var miPin = new MenuItem
        {
            Header = item.IsPinned ? "\uD83D\uDCCC Unpin from Top" : "\uD83D\uDCCD Pin to Top",
            Foreground = Brushes.White
        };
        miPin.Click += (s, e) =>
        {
            UtilitiesService.Instance.TogglePin(item.Id);
            RefreshCards();
        };
        cm.Items.Add(miPin);
        cm.Items.Add(new Separator());

        // Send to Favorites
        var miFav = new MenuItem { Header = "\u2B50 Send to Favorites", Foreground = Brushes.White };
        miFav.Click += (s, e) =>
        {
            if (UtilitiesService.Instance.SendToFavorites(item))
            {
                _homePage.RefreshFavorites();
                MessageBox.Show(Window.GetWindow(this), $"Added '{item.Title}' to Favorites.", "Horizon Utilities", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        cm.Items.Add(miFav);

        // Send to Bookmarks
        var miBm = new MenuItem { Header = "\uD83D\uDD16 Send to Bookmarks", Foreground = Brushes.White };
        miBm.Click += (s, e) =>
        {
            if (UtilitiesService.Instance.SendToBookmarks(item))
            {
                _homePage.RefreshBookmarks();
                MessageBox.Show(Window.GetWindow(this), $"Added '{item.Title}' to Bookmarks.", "Horizon Utilities", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        cm.Items.Add(miBm);

        // Send to Homepage Quick Launch
        var miHome = new MenuItem { Header = "\uD83C\uDFE0 Pin to Homepage", Foreground = Brushes.White };
        miHome.Click += (s, e) =>
        {
            UtilitiesService.Instance.SendToFavorites(item);
            _homePage.RefreshFavorites();
            MessageBox.Show(Window.GetWindow(this), $"Pinned '{item.Title}' to Homepage quick launch.", "Horizon Utilities", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        cm.Items.Add(miHome);

        // Send to Header Right-Click Context Menu
        var miHeader = new MenuItem { Header = "\uD83D\uDDB1\uFE0F Send to Header Context Menu", Foreground = Brushes.White };
        miHeader.Click += (s, e) =>
        {
            if (UtilitiesService.Instance.SendToHeaderContextMenu(item))
            {
                MessageBox.Show(Window.GetWindow(this), $"Added '{item.Title}' to Header Right-Click Menu.", "Horizon Utilities", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        cm.Items.Add(miHeader);

        // Send to Homepage as a Widget
        var miWidget = new MenuItem { Header = "\uD83E\uDDE9 Add as Homepage Widget", Foreground = Brushes.White };
        miWidget.Click += (s, e) =>
        {
            if (UtilitiesService.Instance.SendToHomepageWidgets(item))
            {
                MessageBox.Show(Window.GetWindow(this), $"Added '{item.Title}' to Homepage Widget Board.", "Horizon Utilities", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        cm.Items.Add(miWidget);

        cm.Items.Add(new Separator());

        // Launch
        var miLaunch = new MenuItem { Header = "\u25B6 Launch Now", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)) };
        miLaunch.Click += (s, e) =>
        {
            UtilitiesService.Instance.ExecuteUtility(item, Window.GetWindow(this));
        };
        cm.Items.Add(miLaunch);

        cm.PlacementTarget = anchor;
        cm.IsOpen = true;
    }

    private Brush ParseColorBrush(string hex)
    {
        try
        {
            return (Brush?)new BrushConverter().ConvertFromString(hex) ?? new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF));
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF));
        }
    }
}
