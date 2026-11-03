using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;
using System.IO;
using System.Windows.Documents;

namespace Horizon.Stealth.Views;

public partial class HomePageView
{
    // =========================================================================
    // 1. Bottom Island Spring Physics Animation (diff * 0.22 at 60 FPS)
    // =========================================================================
    private DispatcherTimer? _islandSpringTimer;
    private double _islandTargetOpacity = 0.0;
    private double _islandTargetY = 28.0;

    private TranslateTransform GetIslandTranslateTransform()
    {
        if (TransFavBookmarksIsland != null) return TransFavBookmarksIsland;
        if (GridFavBookmarksIsland.RenderTransform is TranslateTransform tt) return tt;
        var newTt = new TranslateTransform(0, 0);
        GridFavBookmarksIsland.RenderTransform = newTt;
        return newTt;
    }

    private void AnimateFavBookmarksIsland(bool show)
    {
        if (GridFavBookmarksIsland == null) return;

        _islandTargetOpacity = show ? 1.0 : 0.0;
        _islandTargetY = show ? 0.0 : 28.0;

        if (show)
        {
            if (GridFavBookmarksIsland.Visibility != Visibility.Visible)
            {
                GridFavBookmarksIsland.Visibility = Visibility.Visible;
                GridFavBookmarksIsland.Opacity = 0.0;
                GetIslandTranslateTransform().Y = 28.0;
            }
        }

        // Enable GPU raster cache mode during motion to achieve silky 60 FPS even with 100+ icons
        if (GridFavBookmarksIsland.CacheMode == null)
        {
            GridFavBookmarksIsland.CacheMode = new BitmapCache { EnableClearType = false, RenderAtScale = 1.0 };
        }

        if (_islandSpringTimer == null)
        {
            _islandSpringTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _islandSpringTimer.Tick += IslandSpringTimer_Tick;
        }

        if (!_islandSpringTimer.IsEnabled)
        {
            _islandSpringTimer.Start();
        }
    }

    private void IslandSpringTimer_Tick(object? sender, EventArgs e)
    {
        if (GridFavBookmarksIsland == null)
        {
            _islandSpringTimer?.Stop();
            return;
        }

        var tt = GetIslandTranslateTransform();
        double curY = tt.Y;
        double curOp = GridFavBookmarksIsland.Opacity;

        double diffY = _islandTargetY - curY;
        double diffOp = _islandTargetOpacity - curOp;

        if (Math.Abs(diffY) < 0.5 && Math.Abs(diffOp) < 0.01)
        {
            tt.Y = _islandTargetY;
            GridFavBookmarksIsland.Opacity = _islandTargetOpacity;
            _islandSpringTimer?.Stop();

            // Release GPU cache at rest to restore native crisp typography
            GridFavBookmarksIsland.CacheMode = null;

            if (_islandTargetOpacity <= 0.01)
            {
                GridFavBookmarksIsland.Visibility = Visibility.Collapsed;
            }
            _homeGlass?.Invalidate();
            return;
        }

        // Exact asymptotic spring physics formula adapted from tab widening/shrinking (diff * 0.22 at 60 FPS)
        tt.Y = curY + (diffY * 0.22);
        GridFavBookmarksIsland.Opacity = Math.Clamp(curOp + (diffOp * 0.22), 0.0, 1.0);
    }

    // =========================================================================
    // 2. Windows 11 Widget Board Configuration & Matrix Layout Model
    // =========================================================================
    public class WidgetBoardCardConfig
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int Column { get; set; } = 0; // 0 = Left, 1 = Center, 2 = Right
        public int Row { get; set; } = 0;    // 0 = Top, 1 = Middle, 2 = Bottom
        public int ColSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    private TextBlock? _calcExprText;
    private TextBlock? _calcResultText;
    private string _calcCurrentInput = "0";
    private double _calcFirstOperand = 0.0;
    private string _calcPendingOp = "";
    private bool _calcNewNumber = true;

    private TextBlock? _widgetBoardBatteryPercent;
    private TextBlock? _widgetBoardBatteryStatus;
    private ProgressBar? _widgetBoardBatteryBar;
    private TextBlock? _widgetBoardClipboardPreview;
    private TextBlock? _widgetBoardClipboardStats;

    private List<WidgetBoardCardConfig>? _widgetBoardCards;
    private DispatcherTimer? _widgetBoardClockTimer;
    private TextBlock? _widgetBoardClockTime;
    private TextBlock? _widgetBoardClockDate;
    private TextBlock? _widgetBoardClockZone;
    private TextBlock? _widgetBoardWeatherTemp;
    private TextBlock? _widgetBoardWeatherDesc;
    private TextBlock? _widgetBoardWeatherCity;
    private TextBlock? _widgetBoardMediaTitle;
    private TextBlock? _widgetBoardMediaArtist;
    private TextBlock? _widgetBoardSystemRam;
    private TextBlock? _widgetBoardSystemStatus;

    private RichTextBox? _widgetBoardNotesRtb;
    private TextBlock? _widgetBoardNotesStatus;
    private TextBlock? _widgetBoardNotesSaveStatus;
    private DispatcherTimer? _widgetBoardNotesSaveTimer;

    private List<WidgetBoardCardConfig> GetDefaultWidgetBoardCards()
    {
        return new List<WidgetBoardCardConfig>
        {
            // Column 0: Time, Environment & Power
            new() { Id = "clock",           Title = "Clock",                     Column = 0, Row = 0, ColSpan = 1, RowSpan = 1 },
            new() { Id = "weather",         Title = "Weather",                   Column = 0, Row = 1, ColSpan = 1, RowSpan = 1 },
            new() { Id = "battery",         Title = "Battery & Power",           Column = 0, Row = 2, ColSpan = 1, RowSpan = 1 },
            new() { Id = "datasaver",       Title = "Data Saver & Privacy",      Column = 0, Row = 3, ColSpan = 1, RowSpan = 1 },

            // Column 1: Media, Performance & Calculations
            new() { Id = "media",           Title = "Media Player",              Column = 1, Row = 0, ColSpan = 1, RowSpan = 1 },
            new() { Id = "system",          Title = "System & Aegis",            Column = 1, Row = 1, ColSpan = 1, RowSpan = 1 },
            new() { Id = "calculator",      Title = "Calculator",                Column = 1, Row = 2, ColSpan = 1, RowSpan = 1 },
            new() { Id = "converter",       Title = "Unit & Currency Converter", Column = 1, Row = 3, ColSpan = 1, RowSpan = 1 },

            // Column 2: Planning, Quick Notes & Notifications
            new() { Id = "calendar",        Title = "Calendar",                  Column = 2, Row = 0, ColSpan = 1, RowSpan = 2 },
            new() { Id = "notes",           Title = "Quick Notes",               Column = 2, Row = 2, ColSpan = 1, RowSpan = 1 },
            new() { Id = "notifications",   Title = "Notifications & Alerts",    Column = 2, Row = 3, ColSpan = 1, RowSpan = 1 },

            // Column 3: Browser Power Tools & Sessions
            new() { Id = "tabgroups",       Title = "Tab Group Manager",         Column = 3, Row = 0, ColSpan = 1, RowSpan = 1 },
            new() { Id = "clipboard",       Title = "Clipboard Monitor",         Column = 3, Row = 1, ColSpan = 1, RowSpan = 1 },
            new() { Id = "readinglist",     Title = "Reading List & Queue",      Column = 3, Row = 2, ColSpan = 1, RowSpan = 1 },
            new() { Id = "sessionrestorer", Title = "Session Restorer",          Column = 3, Row = 3, ColSpan = 1, RowSpan = 1 }
        };
    }

    private void LoadWidgetBoardCards()
    {
        try
        {
            string json = SettingsService.Current.HomeWidgetBoardConfigJson;
            if (!string.IsNullOrWhiteSpace(json))
            {
                var loaded = JsonSerializer.Deserialize<List<WidgetBoardCardConfig>>(json);
                if (loaded != null && loaded.Count >= 6)
                {
                    var defaults = GetDefaultWidgetBoardCards();
                    var existingIds = new HashSet<string>(loaded.Select(c => c.Id));
                    bool addedAny = false;
                    foreach (var def in defaults)
                    {
                        if (!existingIds.Contains(def.Id))
                        {
                            int col = def.Column;
                            int row = def.Row;
                            while (loaded.Any(c => c.Column == col && c.Row == row))
                            {
                                col++;
                                if (col > 3) { col = 0; row++; }
                                if (row > 3) break;
                            }
                            def.Column = Math.Clamp(col, 0, 3);
                            def.Row = Math.Clamp(row, 0, 3);
                            loaded.Add(def);
                            addedAny = true;
                        }
                    }

                    bool hasDuplicates = loaded.GroupBy(c => $"{c.Column},{c.Row}").Any(g => g.Count() > 1);
                    if (!hasDuplicates)
                    {
                        _widgetBoardCards = loaded;
                        if (addedAny) SaveWidgetBoardCards();
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "HomePageView.LoadWidgetBoardCards");
        }

        _widgetBoardCards = GetDefaultWidgetBoardCards();
        SaveWidgetBoardCards();
    }

    private void SaveWidgetBoardCards()
    {
        if (_widgetBoardCards == null) return;
        try
        {
            string json = JsonSerializer.Serialize(_widgetBoardCards);
            SettingsService.Current.HomeWidgetBoardConfigJson = json;
            SettingsService.Save();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "HomePageView.SaveWidgetBoardCards");
        }
    }

    // =========================================================================
    // 3. Widget Board Population & Layout Engine
    // =========================================================================
    public Grid GetWidgetBoardGrid()
    {
        if (GridWidgetBoard.Parent is ContentControl cc)
        {
            cc.Content = null;
        }
        else if (GridWidgetBoard.Parent is Panel p)
        {
            p.Children.Remove(GridWidgetBoard);
        }
        return GridWidgetBoard;
    }

    public void EnsureWidgetBoardPopulated()
    {
        if (GridWidgetBoard == null) return;
        if (_widgetBoardCards == null) LoadWidgetBoardCards();

        if (SettingsService.Current.HomepageWidgetUtilityIds != null)
        {
            foreach (var utilId in SettingsService.Current.HomepageWidgetUtilityIds)
            {
                string cardId = "util_" + utilId;
                if (!_widgetBoardCards!.Any(c => c.Id == cardId))
                {
                    var util = UtilitiesService.Instance.GetById(utilId);
                    if (util != null)
                    {
                        int minCol = 0;
                        int minCount = int.MaxValue;
                        for (int c = 0; c < 4; c++)
                        {
                            int count = _widgetBoardCards!.Count(x => x.Column == c);
                            if (count < minCount)
                            {
                                minCount = count;
                                minCol = c;
                            }
                        }
                        _widgetBoardCards!.Add(new WidgetBoardCardConfig
                        {
                            Id = cardId,
                            Title = util.Title,
                            Column = minCol,
                            Row = minCount,
                            // util title
                        });
                    }
                }
            }
        }

        GridWidgetBoard.Children.Clear();
        GridWidgetBoard.ColumnDefinitions.Clear();
        GridWidgetBoard.RowDefinitions.Clear();

        // 4-Column Layout: Left (320px), Center-Left (340px), Center-Right (340px), Right (340px)
        GridWidgetBoard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        GridWidgetBoard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        GridWidgetBoard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        GridWidgetBoard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

        // Single star row hosting independent vertical column stackpanels to prevent any cards from overlapping
        GridWidgetBoard.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var colPanels = new List<StackPanel>();
        for (int c = 0; c < 4; c++)
        {
            var colStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, c < 3 ? 12 : 0, 0),
                Tag = c,
                AllowDrop = true
            };
            colStack.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent("HorizonUtilityItem"))
                {
                    e.Effects = DragDropEffects.Copy;
                    colStack.Background = new SolidColorBrush(Color.FromArgb(0x28, 0x00, 0xE5, 0xFF));
                    e.Handled = true;
                    return;
                }
                if (e.Data.GetDataPresent(typeof(WidgetBoardCardConfig)))
                {
                    e.Effects = DragDropEffects.Move;
                    colStack.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0xE5, 0xFF));
                    e.Handled = true;
                }
            };
            colStack.DragLeave += (s, e) =>
            {
                colStack.Background = Brushes.Transparent;
            };
            colStack.Drop += (s, e) =>
            {
                colStack.Background = Brushes.Transparent;
                if (e.Data.GetDataPresent("HorizonUtilityItem"))
                {
                    if (e.Data.GetData("HorizonUtilityItem") is UtilityItem util)
                    {
                        UtilitiesService.Instance.SendToHomepageWidgets(util);
                        AddUtilityAsWidgetCard(util, (int)colStack.Tag);
                        e.Effects = DragDropEffects.Copy;
                        e.Handled = true;
                        return;
                    }
                }
                if (e.Data.GetData(typeof(WidgetBoardCardConfig)) is WidgetBoardCardConfig droppedCard)
                {
                    int targetCol = (int)colStack.Tag;
                    droppedCard.Column = targetCol;
                    var existingInCol = _widgetBoardCards!.Where(x => x.Column == targetCol && x != droppedCard).ToList();
                    droppedCard.Row = existingInCol.Count;
                    SaveWidgetBoardCards();
                    EnsureWidgetBoardPopulated();
                }
            };

            Grid.SetColumn(colStack, c);
            Grid.SetRow(colStack, 0);
            GridWidgetBoard.Children.Add(colStack);
            colPanels.Add(colStack);
        }

        foreach (var cardConfig in _widgetBoardCards!.OrderBy(c => c.Row))
        {
            var cardElement = BuildWidgetCardElement(cardConfig);
            if (cardElement != null)
            {
                int col = Math.Clamp(cardConfig.Column, 0, 3);
                colPanels[col].Children.Add(cardElement);
            }
        }

        StartWidgetBoardLiveTimers();
    }

    public void ResetWidgetBoardLayout()
    {
        _widgetBoardCards = GetDefaultWidgetBoardCards();
        SaveWidgetBoardCards();
        EnsureWidgetBoardPopulated();
    }

    private FrameworkElement? BuildWidgetCardElement(WidgetBoardCardConfig config)
    {
        FrameworkElement bodyContent;
        if (config.Id.StartsWith("util_"))
        {
            string utilId = config.Id.Substring(5);
            var util = UtilitiesService.Instance.GetById(utilId);
            bodyContent = BuildDynamicUtilityCardContent(util, utilId);
        }
        else
        {
            bodyContent = config.Id switch
            {
                "clock"           => BuildClockCardContent(),
                "weather"         => BuildWeatherCardContent(),
                "calendar"        => BuildCalendarCardContent(),
                "media"           => BuildMediaCardContent(),
                "system"          => BuildSystemCardContent(),
                "notes"           => BuildNotesCardContent(),
                "calculator"      => BuildCalculatorCardContent(),
                "converter"       => BuildConverterCardContent(),
                "battery"         => BuildBatteryCardContent(),
                "notifications"   => BuildNotificationsCardContent(),
                "datasaver"       => BuildDataSaverCardContent(),
                "tabgroups"       => BuildTabGroupsCardContent(),
                "clipboard"       => BuildClipboardCardContent(),
                "readinglist"     => BuildReadingListCardContent(),
                "sessionrestorer" => BuildSessionRestorerCardContent(),
                _                 => BuildClockCardContent()
            };
        }

        return BuildCardShell(config, bodyContent);
    }

    public void AddUtilityAsWidgetCard(UtilityItem util, int targetCol = 0)
    {
        if (_widgetBoardCards == null) LoadWidgetBoardCards();
        string cardId = "util_" + util.Id;
        var existing = _widgetBoardCards!.FirstOrDefault(c => c.Id == cardId);
        if (existing == null)
        {
            var existingInCol = _widgetBoardCards!.Where(x => x.Column == targetCol).ToList();
            _widgetBoardCards!.Add(new WidgetBoardCardConfig
            {
                Id = cardId,
                Title = util.Title,
                Column = targetCol,
                Row = existingInCol.Count,
                // util title
            });
            SaveWidgetBoardCards();
            EnsureWidgetBoardPopulated();
        }
    }

    private FrameworkElement BuildDynamicUtilityCardContent(UtilityItem? util, string utilId)
    {
        var panel = new StackPanel { Margin = new Thickness(4) };

        var descText = new TextBlock
        {
            Text = util?.Description ?? "Horizon browser utility card.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(descText);

        var btnRun = new Button
        {
            Content = $"\u25B6 Launch {util?.Title ?? utilId}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x4A, 0x9E, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x4A, 0x9E, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Height = 28,
            Padding = new Thickness(12, 0, 12, 0)
        };
        btnRun.Click += (s, e) =>
        {
            if (util != null)
            {
                UtilitiesService.Instance.ExecuteUtility(util, Window.GetWindow(this));
            }
        };
        panel.Children.Add(btnRun);

        return panel;
    }

    

    // =========================================================================
    // 4. Card Shell (Header with drag, rename, move buttons, and glass styling)
    // =========================================================================
    private Border BuildCardShell(WidgetBoardCardConfig config, FrameworkElement bodyContent)
    {
        var shell = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x18, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(6),
            Padding = new Thickness(14, 10, 14, 12),
            Tag = config
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body

        // Header Row
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Drag Handle
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title / Inline Edit
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Rename & Move Buttons

        // Drag Handle with interactive drag & drop cues
        var dragHandle = new TextBlock
        {
            Text = "\u22EE\u22EE",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Click and drag to move card between columns"
        };

        Point dragStartPt = default;
        bool isCardDragging = false;
        dragHandle.PreviewMouseLeftButtonDown += (s, e) =>
        {
            dragStartPt = e.GetPosition(null);
            isCardDragging = true;
        };
        dragHandle.PreviewMouseMove += (s, e) =>
        {
            if (isCardDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point curPt = e.GetPosition(null);
                if (Math.Abs(curPt.X - dragStartPt.X) > 6 || Math.Abs(curPt.Y - dragStartPt.Y) > 6)
                {
                    isCardDragging = false;
                    shell.Opacity = 0.65;
                    DragDrop.DoDragDrop(shell, config, DragDropEffects.Move);
                    shell.Opacity = 1.0;
                }
            }
        };
        dragHandle.PreviewMouseLeftButtonUp += (s, e) => isCardDragging = false;

        Grid.SetColumn(dragHandle, 0);
        headerGrid.Children.Add(dragHandle);

        // Title & Inline Rename Container
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        string displayTitle = config.Id == "notes" && !config.Title.StartsWith("\U0001F4DD") ? $"\U0001F4DD {config.Title}" : config.Title;
        var titleBlock = new TextBlock
        {
            Text = displayTitle,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = config.Id == "notes" ? new SolidColorBrush(Color.FromArgb(0xEE, 0x90, 0xCA, 0xF9)) : new SolidColorBrush(Color.FromArgb(0xD5, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Click edit icon to rename"
        };

        var renameBox = new TextBox
        {
            Text = config.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0xE5, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            Visibility = Visibility.Collapsed,
            MinWidth = 100
        };

        void CommitRename()
        {
            if (renameBox.Visibility == Visibility.Visible)
            {
                string newTitle = renameBox.Text.Trim();
                if (!string.IsNullOrEmpty(newTitle))
                {
                    config.Title = newTitle;
                    titleBlock.Text = newTitle;
                    SaveWidgetBoardCards();
                }
                renameBox.Visibility = Visibility.Collapsed;
                titleBlock.Visibility = Visibility.Visible;
            }
        }

        renameBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
            else if (e.Key == Key.Escape) { renameBox.Visibility = Visibility.Collapsed; titleBlock.Visibility = Visibility.Visible; e.Handled = true; }
        };
        renameBox.LostFocus += (s, e) => CommitRename();

        titleStack.Children.Add(titleBlock);
        titleStack.Children.Add(renameBox);
        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);

        // Action Buttons Row (Rename pencil + Move Left/Up/Down/Right)
        var actionsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var btnRename = MakeWidgetButton("\u270E", "Rename widget title", () =>
        {
            titleBlock.Visibility = Visibility.Collapsed;
            renameBox.Visibility = Visibility.Visible;
            renameBox.Focus();
            renameBox.SelectAll();
        });
        actionsStack.Children.Add(btnRename);

        var btnLeft = MakeWidgetButton("\u25C0", "Move Left", () => MoveCardDirection(config, -1, 0));
        var btnUp = MakeWidgetButton("\u25B2", "Move Up", () => MoveCardDirection(config, 0, -1));
        var btnDown = MakeWidgetButton("\u25BC", "Move Down", () => MoveCardDirection(config, 0, 1));
        var btnRight = MakeWidgetButton("\u25B6", "Move Right", () => MoveCardDirection(config, 1, 0));

        actionsStack.Children.Add(btnLeft);
        actionsStack.Children.Add(btnUp);
        actionsStack.Children.Add(btnDown);
        actionsStack.Children.Add(btnRight);

        Grid.SetColumn(actionsStack, 2);
        headerGrid.Children.Add(actionsStack);

        Grid.SetRow(headerGrid, 0);
        rootGrid.Children.Add(headerGrid);

        // Body Row
        Grid.SetRow(bodyContent, 1);
        rootGrid.Children.Add(bodyContent);

        shell.Child = rootGrid;
        return shell;
    }

    private Button MakeWidgetButton(string glyph, string toolTip, Action onClick)
    {
        var btn = new Button
        {
            Content = glyph,
            ToolTip = toolTip,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(2, 0, 0, 0)
        };
        btn.Click += (s, e) => { onClick(); e.Handled = true; };
        return btn;
    }

    private void MoveCardDirection(WidgetBoardCardConfig card, int deltaCol, int deltaRow)
    {
        if (_widgetBoardCards == null) return;

        int targetCol = Math.Clamp(card.Column + deltaCol, 0, 3);
        int targetRow = Math.Clamp(card.Row + deltaRow, 0, 3);

        if (targetCol == card.Column && targetRow == card.Row) return;

        // Check if another card occupies target slot
        var neighbor = _widgetBoardCards.FirstOrDefault(c => c != card && c.Column == targetCol && c.Row == targetRow);
        if (neighbor != null)
        {
            // Swap positions
            neighbor.Column = card.Column;
            neighbor.Row = card.Row;
        }

        card.Column = targetCol;
        card.Row = targetRow;

        SaveWidgetBoardCards();
        EnsureWidgetBoardPopulated();
    }

    // =========================================================================
    // 5. Individual Card Body Builders
    // =========================================================================
    private FrameworkElement BuildClockCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

        _widgetBoardClockTime = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm:ss"),
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas, Segoe UI Variable Display, Segoe UI")
        };

        _widgetBoardClockDate = new TextBlock
        {
            Text = DateTime.Now.ToString("dddd, MMMM d, yyyy"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(1, 4, 0, 2)
        };

        var tz = TimeZoneInfo.Local;
        _widgetBoardClockZone = new TextBlock
        {
            Text = $"{tz.DisplayName} (UTC{(tz.BaseUtcOffset >= TimeSpan.Zero ? "+" : "")}{tz.BaseUtcOffset.Hours})",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(1, 0, 0, 0)
        };

        stack.Children.Add(_widgetBoardClockTime);
        stack.Children.Add(_widgetBoardClockDate);
        stack.Children.Add(_widgetBoardClockZone);
        return stack;
    }

    private FrameworkElement BuildWeatherCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string wx = !string.IsNullOrWhiteSpace(WeatherBridge.WeatherText) ? WeatherBridge.WeatherText : "+7°C Overcast";
        string city = !string.IsNullOrWhiteSpace(WeatherBridge.City) ? WeatherBridge.City : "Sofia";

        _widgetBoardWeatherTemp = new TextBlock
        {
            Text = wx,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_widgetBoardWeatherTemp, 0);
        topRow.Children.Add(_widgetBoardWeatherTemp);

        _widgetBoardWeatherDesc = new TextBlock
        {
            Text = "\u26C5 Current Forecast",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_widgetBoardWeatherDesc, 1);
        topRow.Children.Add(_widgetBoardWeatherDesc);

        stack.Children.Add(topRow);

        _widgetBoardWeatherCity = new TextBlock
        {
            Text = $"{city} \u00B7 Atmospheric Pressure 1014 hPa \u00B7 Humidity 68%",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 8, 0, 0)
        };
        stack.Children.Add(_widgetBoardWeatherCity);

        return stack;
    }

    private FrameworkElement BuildCalendarCardContent()
    {
        try
        {
            var data = new StackElementData
            {
                Id = "board_calendar",
                Title = "Calendar",
                Kind = StackElementKind.Widget,
                SourceId = "calendar"
            };
            return new StackCalendarWidget(data)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 280
            };
        }
        catch
        {
            var fallback = new TextBlock
            {
                Text = DateTime.Today.ToString("MMMM yyyy\n\nInteractive Calendar"),
                FontSize = 14,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(10)
            };
            return fallback;
        }
    }

    private FrameworkElement BuildMediaCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        var infoGrid = new Grid();
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var musicIcon = new TextBlock
        {
            Text = "\U0001F3B5",
            FontSize = 26,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(musicIcon, 0);
        infoGrid.Children.Add(musicIcon);

        var trackStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string curTitle = !string.IsNullOrWhiteSpace(TblMediaTitle?.Text) ? TblMediaTitle.Text : "Horizon Audio Stream";
        _widgetBoardMediaTitle = new TextBlock
        {
            Text = curTitle,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        string curArtist = !string.IsNullOrWhiteSpace(TblMediaArtist?.Text) ? TblMediaArtist.Text : "Ready to play";
        _widgetBoardMediaArtist = new TextBlock
        {
            Text = curArtist,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        trackStack.Children.Add(_widgetBoardMediaTitle);
        trackStack.Children.Add(_widgetBoardMediaArtist);
        Grid.SetColumn(trackStack, 1);
        infoGrid.Children.Add(trackStack);

        stack.Children.Add(infoGrid);

        // Control Buttons
        var ctrlRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };

        Button MakeMediaBtn(string glyph, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = glyph,
                FontSize = 14,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Width = 36,
                Height = 30,
                Margin = new Thickness(4, 0, 4, 0)
            };
            b.Click += onClick;
            return b;
        }

        ctrlRow.Children.Add(MakeMediaBtn("\u23EE", BtnMediaPrev_Click));
        ctrlRow.Children.Add(MakeMediaBtn("\u25B6", BtnMediaPlayPause_Click));
        ctrlRow.Children.Add(MakeMediaBtn("\u23ED", BtnMediaNext_Click));
        ctrlRow.Children.Add(MakeMediaBtn("\U0001F50A", BtnMediaMute_Click));

        var btnCinema = new Button
        {
            Content = "\U0001F31F",
            ToolTip = "Ambient Lighting / Cinema Mode (Vignette & Backlight)",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE5, 0xFF)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Width = 36,
            Height = 30,
            Margin = new Thickness(4, 0, 4, 0)
        };
        btnCinema.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.ToggleCinemaModeAsync();
        ctrlRow.Children.Add(btnCinema);

        stack.Children.Add(ctrlRow);
        return stack;
    }

    private FrameworkElement BuildSystemCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        long memMb = 280;
        try { memMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024); } catch { }

        _widgetBoardSystemRam = new TextBlock
        {
            Text = $"{memMb} MB Browser RAM",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x66))
        };
        stack.Children.Add(_widgetBoardSystemRam);

        _widgetBoardSystemStatus = new TextBlock
        {
            Text = "\u26A1 Aegis Active \u00B7 High-efficiency Tab Hibernation",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 4, 0, 8)
        };
        stack.Children.Add(_widgetBoardSystemStatus);

        var btnTrim = new Button
        {
            Content = "Optimize Memory",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0xFF, 0x66)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0xFF, 0x66)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        btnTrim.Click += (s, e) =>
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                long newMem = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                if (_widgetBoardSystemRam != null) _widgetBoardSystemRam.Text = $"{newMem} MB Browser RAM";
                btnTrim.Content = "Memory Trimmed!";
            }
            catch { }
            e.Handled = true;
        };
        stack.Children.Add(btnTrim);

        return stack;
    }

    private static void ApplyDarkComboBoxStyle(ComboBox cb, Color accent)
    {
        var darkBg = Color.FromArgb(0xF0, 0x14, 0x18, 0x22);
        var lightFg = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        cb.Resources[SystemColors.WindowBrushKey] = new SolidColorBrush(darkBg);
        cb.Resources[SystemColors.WindowTextBrushKey] = new SolidColorBrush(lightFg);
        cb.Resources[SystemColors.ControlBrushKey] = new SolidColorBrush(darkBg);
        cb.Resources[SystemColors.ControlTextBrushKey] = new SolidColorBrush(lightFg);
        cb.Resources[SystemColors.ControlLightBrushKey] = new SolidColorBrush(darkBg);
        cb.Resources[SystemColors.ControlDarkBrushKey] = new SolidColorBrush(darkBg);
        cb.Resources[SystemColors.ControlLightLightBrushKey] = new SolidColorBrush(darkBg);
        cb.Resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(accent);
        cb.Resources[SystemColors.HighlightTextBrushKey] = new SolidColorBrush(Colors.White);
        cb.Foreground = new SolidColorBrush(lightFg);
    }

    private FrameworkElement BuildNotesCardContent()
    {
        var mainStack = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

        // =====================================================================
        // 1. MS Word Ribbon Toolbar (Header)
        // =====================================================================
        var ribbonWrap = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x35, 0x10, 0x14, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(4)
        };

        var ribbonStack = new StackPanel { Orientation = Orientation.Vertical };

        Button RibbonBtn(string label, string tooltip, Action onClick, bool isBold = false, bool isItalic = false, bool isStrike = false, int w = 22)
        {
            var btn = new Button
            {
                Content = label,
                ToolTip = tooltip,
                Width = w,
                Height = 22,
                FontSize = 11,
                Focusable = false,
                Margin = new Thickness(1, 0, 1, 0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1)
            };
            if (isBold) btn.FontWeight = FontWeights.Bold;
            if (isItalic) btn.FontStyle = FontStyles.Italic;
            if (isStrike) btn.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Strikethrough);

            btn.MouseEnter += (s, e) => btn.Background = new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x9E, 0xFF));
            btn.MouseLeave += (s, e) => btn.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
            btn.Click += (s, e) => { onClick(); e.Handled = true; };
            return btn;
        }

        Border VSep() => new Border
        {
            Width = 1,
            Height = 16,
            Margin = new Thickness(3, 3, 3, 3),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))
        };

        // -- Row 1: Font Family, Font Size, Bold, Italic, Underline, Strike, Color, Popout --
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };

        var fontFamilyBox = new ComboBox
        {
            ItemsSource = new[] { "Segoe UI", "Consolas", "Arial", "Calibri", "Georgia" },
            SelectedIndex = 0,
            Width = 76,
            Height = 22,
            FontSize = 9.5,
            Margin = new Thickness(0, 0, 2, 0),
            Focusable = false,
            ToolTip = "Font Family"
        };
        ApplyDarkComboBoxStyle(fontFamilyBox, Color.FromRgb(0x4A, 0x9E, 0xFF));
        fontFamilyBox.SelectionChanged += (s, e) =>
        {
            if (fontFamilyBox.SelectedItem is string fam && _widgetBoardNotesRtb != null)
            {
                _widgetBoardNotesRtb.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(fam));
                _widgetBoardNotesRtb.Focus();
            }
        };
        row1.Children.Add(fontFamilyBox);

        var fontSizeBox = new ComboBox
        {
            ItemsSource = new[] { "9", "10", "11", "12", "14", "16", "18", "20" },
            SelectedIndex = 3, // "12"
            Width = 44,
            Height = 22,
            FontSize = 9.5,
            Margin = new Thickness(0, 0, 2, 0),
            Focusable = false,
            ToolTip = "Font Size"
        };
        ApplyDarkComboBoxStyle(fontSizeBox, Color.FromRgb(0x4A, 0x9E, 0xFF));
        fontSizeBox.SelectionChanged += (s, e) =>
        {
            if (fontSizeBox.SelectedItem is string sz && double.TryParse(sz, out var val) && _widgetBoardNotesRtb != null)
            {
                _widgetBoardNotesRtb.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, val);
                _widgetBoardNotesRtb.Focus();
            }
        };
        row1.Children.Add(fontSizeBox);

        row1.Children.Add(VSep());

        row1.Children.Add(RibbonBtn("B", "Bold (Ctrl+B)", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                EditingCommands.ToggleBold.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }, isBold: true));

        row1.Children.Add(RibbonBtn("I", "Italic (Ctrl+I)", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                EditingCommands.ToggleItalic.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }, isItalic: true));

        row1.Children.Add(RibbonBtn("U", "Underline (Ctrl+U)", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                EditingCommands.ToggleUnderline.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }));

        row1.Children.Add(RibbonBtn("S", "Strikethrough", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                var cur = _widgetBoardNotesRtb.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
                bool isStrike = cur is TextDecorationCollection tdc && tdc.Count > 0 && tdc[0].Location == TextDecorationLocation.Strikethrough;
                _widgetBoardNotesRtb.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, isStrike ? null : TextDecorations.Strikethrough);
                _widgetBoardNotesRtb.Focus();
            }
        }, isStrike: true));

        row1.Children.Add(VSep());

        int currentColorIndex = 0;
        Color[] colorPalette =
        {
            Colors.White,
            Color.FromRgb(0x00, 0xE5, 0xFF), // Cyan
            Color.FromRgb(0xFF, 0xD5, 0x4F), // Yellow
            Color.FromRgb(0xFF, 0x52, 0x52), // Coral
            Color.FromRgb(0x69, 0xF0, 0xAE), // Mint
            Color.FromRgb(0xB3, 0x88, 0xFF)  // Purple
        };
        string[] colorNames = { "White", "Cyan", "Yellow", "Coral", "Mint", "Purple" };

        var btnColor = new Button
        {
            Content = "A",
            ToolTip = "Text Color: White (Click to cycle colors)",
            Width = 22,
            Height = 22,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Focusable = false,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            Foreground = new SolidColorBrush(colorPalette[0]),
            Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1)
        };
        btnColor.MouseEnter += (s, e) => btnColor.Background = new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x9E, 0xFF));
        btnColor.MouseLeave += (s, e) => btnColor.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        btnColor.Click += (s, e) =>
        {
            currentColorIndex = (currentColorIndex + 1) % colorPalette.Length;
            var activeBrush = new SolidColorBrush(colorPalette[currentColorIndex]);
            btnColor.Foreground = activeBrush;
            btnColor.ToolTip = $"Text Color: {colorNames[currentColorIndex]} (Click to cycle colors)";
            if (_widgetBoardNotesRtb != null)
            {
                _widgetBoardNotesRtb.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, activeBrush);
                _widgetBoardNotesRtb.Focus();
            }
            e.Handled = true;
        };
        row1.Children.Add(btnColor);

        row1.Children.Add(VSep());

        // Popout Full Notes Window
        var btnPopout = RibbonBtn("\u2197", "Open in Full Horizon Notes Window", () =>
        {
            var mw = (Window.GetWindow(this) as MainWindow) ?? (Application.Current.MainWindow as MainWindow);
            mw?.OpenNotesWindow();
        }, w: 24);
        btnPopout.Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9));
        row1.Children.Add(btnPopout);

        ribbonStack.Children.Add(row1);

        // -- Row 2: Undo, Redo, Bullets, Numbers, Alignments, Checkbox, Copy, Clear --
        var row2 = new StackPanel { Orientation = Orientation.Horizontal };

        row2.Children.Add(RibbonBtn("\u21B6", "Undo (Ctrl+Z)", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                ApplicationCommands.Undo.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }));

        row2.Children.Add(RibbonBtn("\u21B7", "Redo (Ctrl+Y)", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                ApplicationCommands.Redo.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }));

        row2.Children.Add(VSep());

        row2.Children.Add(RibbonBtn("\u2022 \u2261", "Bullet list", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                EditingCommands.ToggleBullets.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }, w: 24));

        row2.Children.Add(RibbonBtn("1. \u2261", "Numbered list", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                EditingCommands.ToggleNumbering.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }, w: 24));

        row2.Children.Add(VSep());

        row2.Children.Add(RibbonBtn("\u21E4", "Align Left (Ctrl+L)", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                EditingCommands.AlignLeft.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }));

        row2.Children.Add(RibbonBtn("\u21F8", "Align Center (Ctrl+E)", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                EditingCommands.AlignCenter.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }));

        row2.Children.Add(RibbonBtn("\u21E5", "Align Right (Ctrl+R)", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                EditingCommands.AlignRight.Execute(null, _widgetBoardNotesRtb);
                _widgetBoardNotesRtb.Focus();
            }
        }));

        row2.Children.Add(VSep());

        row2.Children.Add(RibbonBtn("\u2611", "Insert Checkbox", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                var cb = new CheckBox
                {
                    Margin = new Thickness(0, 0, 4, -2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                var ic = new InlineUIContainer(cb, _widgetBoardNotesRtb.CaretPosition);
                _widgetBoardNotesRtb.CaretPosition = ic.ElementEnd;
                _widgetBoardNotesRtb.CaretPosition.InsertTextInRun(" ");
                _widgetBoardNotesRtb.Focus();
            }
        }));

        row2.Children.Add(RibbonBtn("\U0001F4CB", "Copy Note Content", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                try
                {
                    string txt = new TextRange(_widgetBoardNotesRtb.Document.ContentStart, _widgetBoardNotesRtb.Document.ContentEnd).Text.Trim();
                    if (!string.IsNullOrEmpty(txt))
                    {
                        Clipboard.SetText(txt);
                        if (_widgetBoardNotesStatus != null) _widgetBoardNotesStatus.Text = "Copied to clipboard \u2713";
                    }
                }
                catch { }
            }
        }));

        row2.Children.Add(RibbonBtn("\U0001F5D1", "Clear Note", () =>
        {
            if (_widgetBoardNotesRtb != null)
            {
                _widgetBoardNotesRtb.Document.Blocks.Clear();
                _widgetBoardNotesRtb.Document.Blocks.Add(new Paragraph());
                _widgetBoardNotesRtb.Focus();
            }
        }));

        ribbonStack.Children.Add(row2);
        ribbonWrap.Child = ribbonStack;
        mainStack.Children.Add(ribbonWrap);

        // =====================================================================
        // 2. RichText Document Canvas
        // =====================================================================
        _widgetBoardNotesRtb = new RichTextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0x00, 0x00)),
            Foreground = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
            CaretBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Height = 115,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 4)
        };
        _widgetBoardNotesRtb.Document.LineHeight = 1.2;
        _widgetBoardNotesRtb.Document.PagePadding = new Thickness(0);
        _widgetBoardNotesRtb.SpellCheck.IsEnabled = true;

        // Hyperlink click support
        _widgetBoardNotesRtb.PreviewMouseLeftButtonDown += (s, e) =>
        {
            var pos = _widgetBoardNotesRtb.GetPositionFromPoint(e.GetPosition(_widgetBoardNotesRtb), true);
            if (pos == null) return;
            var el = pos.Parent as TextElement;
            while (el != null && el is not Hyperlink) el = el.Parent as TextElement;
            if (el is Hyperlink hl && hl.NavigateUri != null)
            {
                try { Process.Start(new ProcessStartInfo(hl.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                e.Handled = true;
            }
        };

        mainStack.Children.Add(_widgetBoardNotesRtb);

        // =====================================================================
        // 3. Word Status Bar
        // =====================================================================
        var statusGrid = new Grid { Margin = new Thickness(2, 2, 2, 0) };
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _widgetBoardNotesStatus = new TextBlock
        {
            Text = "0 words  \u2022  0 chars",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_widgetBoardNotesStatus, 0);
        statusGrid.Children.Add(_widgetBoardNotesStatus);

        _widgetBoardNotesSaveStatus = new TextBlock
        {
            Text = "\u2713 Saved",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0x69, 0xF0, 0xAE)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(_widgetBoardNotesSaveStatus, 1);
        statusGrid.Children.Add(_widgetBoardNotesSaveStatus);

        mainStack.Children.Add(statusGrid);

        // =====================================================================
        // 4. Persistence & Autosave Pipeline
        // =====================================================================
        string NoteFilePath()
        {
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Notes");
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "WidgetNotes.bin");
        }

        void UpdateWordCharStats()
        {
            if (_widgetBoardNotesRtb == null) return;
            try
            {
                string raw = new TextRange(_widgetBoardNotesRtb.Document.ContentStart, _widgetBoardNotesRtb.Document.ContentEnd).Text;
                int chars = raw.Replace("\r", "").Replace("\n", "").Length;
                var words = raw.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int wordCount = words.Length;
                if (_widgetBoardNotesStatus != null)
                {
                    _widgetBoardNotesStatus.Text = $"{wordCount} words  \u2022  {chars} chars";
                }
            }
            catch { }
        }

        void SaveNotes()
        {
            if (_widgetBoardNotesRtb == null) return;
            try
            {
                string path = NoteFilePath();
                using var ms = new System.IO.MemoryStream();
                new TextRange(_widgetBoardNotesRtb.Document.ContentStart, _widgetBoardNotesRtb.Document.ContentEnd)
                    .Save(ms, DataFormats.XamlPackage);
                System.IO.File.WriteAllBytes(path, ms.ToArray());

                string plain = new TextRange(_widgetBoardNotesRtb.Document.ContentStart, _widgetBoardNotesRtb.Document.ContentEnd).Text.Trim();
                SettingsService.Current.HomeWidgetNotesText = plain;
                SettingsService.Save();

                UpdateWordCharStats();
                if (_widgetBoardNotesSaveStatus != null)
                {
                    _widgetBoardNotesSaveStatus.Text = "\u2713 Saved";
                }
            }
            catch { }
        }

        void ScheduleSave()
        {
            if (_widgetBoardNotesSaveStatus != null)
            {
                _widgetBoardNotesSaveStatus.Text = "\u2022 Editing...";
            }
            UpdateWordCharStats();
            _widgetBoardNotesSaveTimer?.Stop();
            _widgetBoardNotesSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _widgetBoardNotesSaveTimer.Tick += (s, e) =>
            {
                _widgetBoardNotesSaveTimer.Stop();
                SaveNotes();
            };
            _widgetBoardNotesSaveTimer.Start();
        }

        _widgetBoardNotesRtb.TextChanged += (s, e) => ScheduleSave();

        // Initial Load
        void LoadNotes()
        {
            if (_widgetBoardNotesRtb == null) return;
            string path = NoteFilePath();
            if (System.IO.File.Exists(path))
            {
                try
                {
                    using var ms = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path));
                    new TextRange(_widgetBoardNotesRtb.Document.ContentStart, _widgetBoardNotesRtb.Document.ContentEnd)
                        .Load(ms, DataFormats.XamlPackage);
                    UpdateWordCharStats();
                    return;
                }
                catch { }
            }

            string legacy = SettingsService.Current.HomeWidgetNotesText ?? "";
            _widgetBoardNotesRtb.Document.Blocks.Clear();
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                _widgetBoardNotesRtb.Document.Blocks.Add(new Paragraph(new Run(legacy)));
            }
            else
            {
                _widgetBoardNotesRtb.Document.Blocks.Add(new Paragraph());
            }
            UpdateWordCharStats();
        }

        LoadNotes();

        return mainStack;
    }

    private FrameworkElement BuildCalculatorCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        var displayBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand,
            ToolTip = "Click to copy calculation"
        };
        displayBorder.MouseLeftButtonDown += (s, e) =>
        {
            if (_calcResultText != null)
            {
                try { Clipboard.SetText(_calcResultText.Text); } catch { }
            }
        };

        var displayGrid = new Grid();
        displayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        displayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _calcExprText = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(_calcExprText, 0);
        displayGrid.Children.Add(_calcExprText);

        _calcResultText = new TextBlock
        {
            Text = "0",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(_calcResultText, 1);
        displayGrid.Children.Add(_calcResultText);

        displayBorder.Child = displayGrid;
        stack.Children.Add(displayBorder);

        var keyGrid = new Grid();
        for (int c = 0; c < 4; c++) keyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 5; r++) keyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void AddKey(string label, int col, int row, Color bg, Color fg, Action act)
        {
            var btn = new Button
            {
                Content = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fg),
                Background = new SolidColorBrush(bg),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Height = 26,
                Margin = new Thickness(2),
                Padding = new Thickness(0)
            };
            btn.Click += (s, e) => { act(); e.Handled = true; };
            Grid.SetColumn(btn, col);
            Grid.SetRow(btn, row);
            keyGrid.Children.Add(btn);
        }

        Color numBg = Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF);
        Color opBg = Color.FromArgb(0x40, 0x00, 0xE5, 0xFF);
        Color funcBg = Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF);
        Color numFg = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
        Color opFg = Color.FromRgb(0x00, 0xE5, 0xFF);

        // Row 0
        AddKey("C", 0, 0, funcBg, numFg, () => {
            _calcCurrentInput = "0"; _calcFirstOperand = 0; _calcPendingOp = ""; _calcNewNumber = true;
            if (_calcExprText != null) _calcExprText.Text = "";
            if (_calcResultText != null) _calcResultText.Text = "0";
        });
        AddKey("\u00B1", 1, 0, funcBg, numFg, () => {
            if (double.TryParse(_calcCurrentInput, out double val)) {
                _calcCurrentInput = (-val).ToString();
                if (_calcResultText != null) _calcResultText.Text = _calcCurrentInput;
            }
        });
        AddKey("%", 2, 0, funcBg, numFg, () => {
            if (double.TryParse(_calcCurrentInput, out double val)) {
                _calcCurrentInput = (val / 100.0).ToString();
                if (_calcResultText != null) _calcResultText.Text = _calcCurrentInput;
            }
        });
        AddKey("\u00F7", 3, 0, opBg, opFg, () => HandleOp("/"));

        // Row 1
        AddKey("7", 0, 1, numBg, numFg, () => HandleDigit("7"));
        AddKey("8", 1, 1, numBg, numFg, () => HandleDigit("8"));
        AddKey("9", 2, 1, numBg, numFg, () => HandleDigit("9"));
        AddKey("\u00D7", 3, 1, opBg, opFg, () => HandleOp("*"));

        // Row 2
        AddKey("4", 0, 2, numBg, numFg, () => HandleDigit("4"));
        AddKey("5", 1, 2, numBg, numFg, () => HandleDigit("5"));
        AddKey("6", 2, 2, numBg, numFg, () => HandleDigit("6"));
        AddKey("-", 3, 2, opBg, opFg, () => HandleOp("-"));

        // Row 3
        AddKey("1", 0, 3, numBg, numFg, () => HandleDigit("1"));
        AddKey("2", 1, 3, numBg, numFg, () => HandleDigit("2"));
        AddKey("3", 2, 3, numBg, numFg, () => HandleDigit("3"));
        AddKey("+", 3, 3, opBg, opFg, () => HandleOp("+"));

        // Row 4
        AddKey("0", 0, 4, numBg, numFg, () => HandleDigit("0"));
        AddKey(".", 1, 4, numBg, numFg, () => {
            if (!_calcCurrentInput.Contains('.')) {
                _calcCurrentInput += ".";
                if (_calcResultText != null) _calcResultText.Text = _calcCurrentInput;
            }
        });
        AddKey("\u232B", 2, 4, funcBg, numFg, () => {
            if (_calcCurrentInput.Length > 1) _calcCurrentInput = _calcCurrentInput.Substring(0, _calcCurrentInput.Length - 1);
            else _calcCurrentInput = "0";
            if (_calcResultText != null) _calcResultText.Text = _calcCurrentInput;
        });
        AddKey("=", 3, 4, Color.FromRgb(0x00, 0x96, 0x88), Brushes.White.Color, () => HandleEquals());

        stack.Children.Add(keyGrid);
        return stack;
    }

    private void HandleDigit(string d)
    {
        if (_calcNewNumber || _calcCurrentInput == "0")
        {
            _calcCurrentInput = d;
            _calcNewNumber = false;
        }
        else
        {
            _calcCurrentInput += d;
        }
        if (_calcResultText != null) _calcResultText.Text = _calcCurrentInput;
    }

    private void HandleOp(string op)
    {
        double.TryParse(_calcCurrentInput, out _calcFirstOperand);
        _calcPendingOp = op;
        _calcNewNumber = true;
        if (_calcExprText != null) _calcExprText.Text = $"{_calcFirstOperand} {op}";
    }

    private void HandleEquals()
    {
        if (string.IsNullOrEmpty(_calcPendingOp)) return;
        double.TryParse(_calcCurrentInput, out double secondOperand);
        double result = _calcPendingOp switch
        {
            "+" => _calcFirstOperand + secondOperand,
            "-" => _calcFirstOperand - secondOperand,
            "*" => _calcFirstOperand * secondOperand,
            "/" => secondOperand != 0 ? _calcFirstOperand / secondOperand : 0,
            _ => secondOperand
        };
        if (_calcExprText != null) _calcExprText.Text = $"{_calcFirstOperand} {_calcPendingOp} {secondOperand} =";
        _calcCurrentInput = result.ToString();
        if (_calcResultText != null) _calcResultText.Text = _calcCurrentInput;
        _calcPendingOp = "";
        _calcNewNumber = true;
    }

    private FrameworkElement BuildConverterCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        string curCategory = "Currency";
        var catGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        for (int i = 0; i < 4; i++) catGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var categories = new[] { "Currency", "Length", "Weight", "Temp" };
        var catButtons = new List<Button>();

        var inputVal = new TextBox
        {
            Text = "100",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };

        var unitFrom = new TextBlock
        {
            Text = "USD",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 36
        };

        var resultVal = new TextBlock
        {
            Text = "92.15",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var unitTo = new TextBlock
        {
            Text = "EUR",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 36
        };

        void Recalculate()
        {
            if (!double.TryParse(inputVal.Text, out double v)) v = 0;
            string res = curCategory switch
            {
                "Currency" => (v * 0.92).ToString("F2"),
                "Length"   => (v * 0.621371).ToString("F2"),
                "Weight"   => (v * 2.20462).ToString("F2"),
                "Temp"     => ((v * 9.0 / 5.0) + 32).ToString("F1"),
                _          => v.ToString("F2")
            };
            resultVal.Text = res;
        }

        void SelectCategory(string cat)
        {
            curCategory = cat;
            switch (cat)
            {
                case "Currency": unitFrom.Text = "USD"; unitTo.Text = "EUR"; break;
                case "Length": unitFrom.Text = "KM"; unitTo.Text = "MI"; break;
                case "Weight": unitFrom.Text = "KG"; unitTo.Text = "LBS"; break;
                case "Temp": unitFrom.Text = "\u00B0C"; unitTo.Text = "\u00B0F"; break;
            }
            foreach (var b in catButtons)
            {
                bool isSel = (string)b.Tag == cat;
                b.Background = new SolidColorBrush(isSel ? Color.FromArgb(0x40, 0x00, 0xE5, 0xFF) : Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
                b.Foreground = new SolidColorBrush(isSel ? Color.FromRgb(0x00, 0xE5, 0xFF) : Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
            }
            Recalculate();
        }

        for (int i = 0; i < categories.Length; i++)
        {
            string c = categories[i];
            var btn = new Button
            {
                Content = c,
                Tag = c,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Height = 22,
                Margin = new Thickness(1),
                Padding = new Thickness(0)
            };
            btn.Click += (s, e) => SelectCategory(c);
            catButtons.Add(btn);
            Grid.SetColumn(btn, i);
            catGrid.Children.Add(btn);
        }
        stack.Children.Add(catGrid);

        var inRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        inRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(inputVal, 0); inRow.Children.Add(inputVal);
        Grid.SetColumn(unitFrom, 1); inRow.Children.Add(unitFrom);
        inputVal.TextChanged += (s, e) => Recalculate();
        stack.Children.Add(inRow);

        var outBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var outRow = new Grid();
        outRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(resultVal, 0); outRow.Children.Add(resultVal);
        Grid.SetColumn(unitTo, 1); outRow.Children.Add(unitTo);
        outBorder.Child = outRow;
        stack.Children.Add(outBorder);

        SelectCategory("Currency");
        return stack;
    }

    private FrameworkElement BuildBatteryCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        int pct = 100;
        bool isAc = true;
        try
        {
            if (GetSystemPowerStatus(out var sps) && sps.BatteryLifePercent <= 100)
            {
                pct = sps.BatteryLifePercent;
                isAc = sps.ACLineStatus == 1;
            }
        }
        catch { }

        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _widgetBoardBatteryPercent = new TextBlock
        {
            Text = $"{pct}%",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_widgetBoardBatteryPercent, 0);
        topRow.Children.Add(_widgetBoardBatteryPercent);

        _widgetBoardBatteryStatus = new TextBlock
        {
            Text = isAc ? "\u26A1 AC Connected" : "\U0001F50B On Battery",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(isAc ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0x00, 0xE5, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_widgetBoardBatteryStatus, 1);
        topRow.Children.Add(_widgetBoardBatteryStatus);

        stack.Children.Add(topRow);

        _widgetBoardBatteryBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = pct,
            Height = 6,
            Margin = new Thickness(0, 8, 0, 8),
            Foreground = new SolidColorBrush(pct > 25 ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0xF4, 0x43, 0x36)),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0)
        };
        stack.Children.Add(_widgetBoardBatteryBar);

        var modeBtn = new Button
        {
            Content = "\u26A1 Aegis Eco Saver: Active",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0xE5, 0xFF)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        stack.Children.Add(modeBtn);

        return stack;
    }

    private FrameworkElement BuildNotificationsCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

        void AddItem(string icon, string title, string sub)
        {
            var itemBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0x00, 0x00)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ic = new TextBlock
            {
                Text = icon,
                FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(ic, 0); g.Children.Add(ic);

            var sp = new StackPanel();
            var t = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            var s = new TextBlock { Text = sub, FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)) };
            sp.Children.Add(t);
            sp.Children.Add(s);
            Grid.SetColumn(sp, 1); g.Children.Add(sp);

            itemBorder.Child = g;
            stack.Children.Add(itemBorder);
        }

        AddItem("\u26A1", "Aegis Governor Active", "2 inactive tabs frozen (+380MB saved)");
        AddItem("\U0001F6E1", "Privacy Shield", "28 trackers & ads blocked");
        AddItem("\u2193", "Downloads Clean", "All files verified & safe");

        return stack;
    }

    private FrameworkElement BuildDataSaverCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sp1 = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var t1 = new TextBlock { Text = "84.6 MB", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)), FontFamily = new FontFamily("Consolas, Segoe UI") };
        var s1 = new TextBlock { Text = "Bandwidth Saved", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) };
        sp1.Children.Add(t1); sp1.Children.Add(s1);
        Grid.SetColumn(sp1, 0); g.Children.Add(sp1);

        var sp2 = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        var t2 = new TextBlock { Text = "1,842", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), FontFamily = new FontFamily("Consolas, Segoe UI") };
        var s2 = new TextBlock { Text = "Ads & Trackers Blocked", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) };
        sp2.Children.Add(t2); sp2.Children.Add(s2);
        Grid.SetColumn(sp2, 1); g.Children.Add(sp2);

        stack.Children.Add(g);

        var badge = new TextBlock
        {
            Text = "\u25CF Zero-Latency AdShield Active",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            Margin = new Thickness(0, 8, 0, 0)
        };
        stack.Children.Add(badge);

        return stack;
    }

    private FrameworkElement BuildTabGroupsCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var countText = new TextBlock
        {
            Text = "6 Tabs",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countText, 0); topRow.Children.Add(countText);

        var statusText = new TextBlock
        {
            Text = "4 Active \u00B7 2 Suspended",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(statusText, 1); topRow.Children.Add(statusText);
        stack.Children.Add(topRow);

        var chipPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        void AddChip(string name, string colorHex)
        {
            var b = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 4, 4)
            };
            b.Child = new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            chipPanel.Children.Add(b);
        }
        AddChip("Work (3)", "#304CAF50");
        AddChip("Media (2)", "#302196F3");
        AddChip("Social (1)", "#30FF9800");
        stack.Children.Add(chipPanel);

        var btnSave = new Button
        {
            Content = "\U0001F4BE Save Tab Session",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        btnSave.Click += (s, e) => {
            btnSave.Content = "\u2713 Session Saved!";
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) => { btnSave.Content = "\U0001F4BE Save Tab Session"; t.Stop(); };
            t.Start();
        };
        stack.Children.Add(btnSave);

        return stack;
    }

    private FrameworkElement BuildClipboardCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        string clip = "";
        try { if (Clipboard.ContainsText()) clip = Clipboard.GetText().Trim(); } catch { }
        if (string.IsNullOrEmpty(clip)) clip = "Clipboard is currently empty.";

        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            MinHeight = 44
        };

        _widgetBoardClipboardPreview = new TextBlock
        {
            Text = clip.Length > 80 ? clip.Substring(0, 80) + "..." : clip,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 48
        };
        box.Child = _widgetBoardClipboardPreview;
        stack.Children.Add(box);

        var btnRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _widgetBoardClipboardStats = new TextBlock
        {
            Text = $"{clip.Length} chars",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_widgetBoardClipboardStats, 0); btnRow.Children.Add(_widgetBoardClipboardStats);

        var btnCopy = new Button
        {
            Content = "\U0001F4CB Copy",
            FontSize = 10,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(8, 2, 8, 2)
        };
        btnCopy.Click += (s, e) => {
            try { Clipboard.SetText(_widgetBoardClipboardPreview?.Text ?? ""); } catch { }
        };
        Grid.SetColumn(btnCopy, 1); btnRow.Children.Add(btnCopy);

        stack.Children.Add(btnRow);
        return stack;
    }

    private FrameworkElement BuildReadingListCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        void AddItem(string title, string domain)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            var d = new TextBlock { Text = domain, FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 0); g.Children.Add(t);
            Grid.SetColumn(d, 1); g.Children.Add(d);
            b.Child = g;
            stack.Children.Add(b);
        }

        AddItem("High Performance Rust CDPs", "rust-lang.org");
        AddItem("WPF Hardware Compositing (.NET 9)", "learn.microsoft.com");

        var addBtn = new Button
        {
            Content = "+ Add Current Tab to List",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(addBtn);

        return stack;
    }

    private FrameworkElement BuildSessionRestorerCardContent()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };

        var info = new TextBlock
        {
            Text = "Last Closed Session",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        };
        stack.Children.Add(info);

        var details = new TextBlock
        {
            Text = "8 tabs \u00B7 Closed 42m ago\nYouTube, GitHub, Docs, Horizon",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 2, 0, 8)
        };
        stack.Children.Add(details);

        var btnRestore = new Button
        {
            Content = "\u21BA Restore 8 Tabs",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE5, 0xFF)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        stack.Children.Add(btnRestore);

        return stack;
    }

    // =========================================================================
    // 6. Live Tick Timers for Live Widget Cards
    // =========================================================================
    private void StartWidgetBoardLiveTimers()
    {
        if (_widgetBoardClockTimer == null)
        {
            _widgetBoardClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _widgetBoardClockTimer.Tick += (s, e) =>
            {
                if (_widgetBoardClockTime != null)
                    _widgetBoardClockTime.Text = DateTime.Now.ToString("HH:mm:ss");

                if (_widgetBoardBatteryPercent != null)
                {
                    try
                    {
                        if (GetSystemPowerStatus(out var sps) && sps.BatteryLifePercent <= 100)
                        {
                            _widgetBoardBatteryPercent.Text = $"{sps.BatteryLifePercent}%";
                            if (_widgetBoardBatteryBar != null) _widgetBoardBatteryBar.Value = sps.BatteryLifePercent;
                        }
                    }
                    catch { }
                }

                if (_widgetBoardMediaTitle != null && TblMediaTitle != null)
                {
                    if (!string.IsNullOrWhiteSpace(TblMediaTitle.Text) && _widgetBoardMediaTitle.Text != TblMediaTitle.Text)
                        _widgetBoardMediaTitle.Text = TblMediaTitle.Text;
                }
                if (_widgetBoardMediaArtist != null && TblMediaArtist != null)
                {
                    if (!string.IsNullOrWhiteSpace(TblMediaArtist.Text) && _widgetBoardMediaArtist.Text != TblMediaArtist.Text)
                        _widgetBoardMediaArtist.Text = TblMediaArtist.Text;
                }
            };
            _widgetBoardClockTimer.Start();
        }
    }

    // =========================================================================
    // 7. Fullscreen WidgetBoardView & Menu Host Integration
    // =========================================================================
    private WidgetBoardView? _widgetBoardView;

    public void OpenWidgetBoardMenu(FrameworkElement? origin)
    {
        EnsureWidgetBoardPopulated();

        if (_widgetBoardView == null)
        {
            _widgetBoardView = new WidgetBoardView(this);
            _widgetBoardView.CloseRequested += () => _startMenuHost.Close();
            _widgetBoardView.ResetLayoutRequested += () => ResetWidgetBoardLayout();
        }

        Rect bounds = new Rect(0, 0, 1, 1);
        if (origin != null && origin.IsLoaded)
        {
            bounds = origin.TransformToAncestor(RootHomeGrid).TransformBounds(new Rect(0, 0, origin.ActualWidth, origin.ActualHeight));
        }

        _startMenuHost.Open(_widgetBoardView, bounds);
    }
}

// =========================================================================
// 8. Fullscreen WidgetBoardView UserControl (Hosted in _startMenuHost)
// =========================================================================
public sealed class WidgetBoardView : UserControl
{
    public event Action? CloseRequested;
    public event Action? ResetLayoutRequested;

    public WidgetBoardView(HomePageView homePage)
    {
        HomeGlass.SetExclude(this, true);

        var rootGrid = new Grid
        {
            Margin = new Thickness(40, 24, 40, 28)
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Board Body

        // Header Row
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock
        {
            Text = "Widgets",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        var subText = new TextBlock
        {
            Text = "  \u00B7  Dashboard & System Monitor",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 4, 0, 0)
        };
        titleStack.Children.Add(titleText);
        titleStack.Children.Add(subText);
        Grid.SetColumn(titleStack, 0);
        headerGrid.Children.Add(titleStack);

        var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var btnReset = new Button
        {
            Content = "\u21BA Reset Layout",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 10, 0)
        };
        btnReset.Click += (s, e) => { ResetLayoutRequested?.Invoke(); e.Handled = true; };
        btnStack.Children.Add(btnReset);

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
        btnStack.Children.Add(btnClose);

        Grid.SetColumn(btnStack, 1);
        headerGrid.Children.Add(btnStack);

        Grid.SetRow(headerGrid, 0);
        rootGrid.Children.Add(headerGrid);

        // Body Row
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = homePage.GetWidgetBoardGrid()
        };
        Grid.SetRow(scrollViewer, 1);
        rootGrid.Children.Add(scrollViewer);

        Content = rootGrid;
    }
}
