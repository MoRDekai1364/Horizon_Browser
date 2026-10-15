using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Controls;

public sealed class StackCalendarWidget : UserControl
{
    private DateTime _viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _selectedDate = DateTime.Today;

    private readonly TextBlock _monthLabel;
    private readonly UniformGrid _dowGrid;
    private readonly UniformGrid _dayGrid;
    private readonly TextBlock _eventPreview;
    private readonly Border _previewBorder;

    public StackElementData Data { get; }

    public StackCalendarWidget(StackElementData data)
    {
        Data = data;
        HomeGlass.SetExclude(this, true);
        MinHeight = 240;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // DOW
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer / Event

        // --- Header ---
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var prevBtn = MakeNavBtn("‹", "Previous month");
        prevBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _viewMonth = _viewMonth.AddMonths(-1);
            Render();
        };

        var nextBtn = MakeNavBtn("›", "Next month");
        nextBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _viewMonth = _viewMonth.AddMonths(1);
            Render();
        };

        var openBtn = MakeNavBtn("↗", "Open full Calendar window");
        openBtn.Margin = new Thickness(4, 0, 0, 0);
        openBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            LaunchFullCalendar();
        };

        _monthLabel = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Click to jump to current month"
        };
        _monthLabel.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
        _monthLabel.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _selectedDate = DateTime.Today;
            Render();
        };

        Grid.SetColumn(prevBtn, 0);
        Grid.SetColumn(_monthLabel, 1);
        Grid.SetColumn(nextBtn, 2);
        Grid.SetColumn(openBtn, 3);
        header.Children.Add(prevBtn);
        header.Children.Add(_monthLabel);
        header.Children.Add(nextBtn);
        header.Children.Add(openBtn);

        // --- Day of week header ---
        _dowGrid = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 0, 0, 6) };

        // --- Month days grid ---
        _dayGrid = new UniformGrid { Rows = 6, Columns = 7, Margin = new Thickness(0, 0, 0, 6) };

        // --- Footer preview ---
        _previewBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(8, 6, 8, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Click to open full Calendar"
        };
        _previewBorder.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            LaunchFullCalendar();
        };

        _eventPreview = new TextBlock
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.85
        };
        _eventPreview.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
        _previewBorder.Child = _eventPreview;

        Grid.SetRow(header, 0);
        Grid.SetRow(_dowGrid, 1);
        Grid.SetRow(_dayGrid, 2);
        Grid.SetRow(_previewBorder, 3);

        root.Children.Add(header);
        root.Children.Add(_dowGrid);
        root.Children.Add(_dayGrid);
        root.Children.Add(_previewBorder);

        Content = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 12),
            Child = root
        };

        Loaded += (_, _) =>
        {
            CalendarBridge.Updated += OnCalendarUpdated;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            Render();
        };

        Unloaded += (_, _) =>
        {
            CalendarBridge.Updated -= OnCalendarUpdated;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        };

        Render();
    }

    private void OnCalendarUpdated()
    {
        Dispatcher.InvokeAsync(Render);
    }

    private void OnLanguageChanged()
    {
        Dispatcher.InvokeAsync(Render);
    }

    private static Border MakeNavBtn(string text, string tooltip)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");

        var b = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = tb
        };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        return b;
    }

    private void LaunchFullCalendar()
    {
        if (Application.Current?.MainWindow is MainWindow main)
        {
            try
            {
                main.OpenCalendarWindow();
            }
            catch { }
        }
    }

    private void Render()
    {
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            string lang = LocalizationService.Instance.CurrentLanguage;
            culture = new CultureInfo(lang);
        }
        catch { }

        _monthLabel.Text = _viewMonth.ToString("MMMM yyyy", culture);

        // Day of week headers
        _dowGrid.Children.Clear();
        // Monday-based week
        string[] dows;
        if (culture.TwoLetterISOLanguageName.Equals("bg", StringComparison.OrdinalIgnoreCase))
            dows = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Нд" };
        else if (culture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase))
            dows = new[] { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };
        else if (culture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase))
            dows = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        else if (culture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase))
            dows = new[] { "Lu", "Ma", "Me", "Je", "Ve", "Sa", "Di" };
        else if (culture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase))
            dows = new[] { "Lu", "Ma", "Me", "Gi", "Ve", "Sa", "Do" };
        else if (culture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase))
            dows = new[] { "Lu", "Ma", "Mi", "Ju", "Vi", "Sá", "Do" };
        else
            dows = new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };

        for (int i = 0; i < 7; i++)
        {
            bool isWeekend = i >= 5;
            var dowText = new TextBlock
            {
                Text = dows[i],
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = isWeekend ? 0.9 : 0.6,
                Foreground = isWeekend ? new SolidColorBrush(Color.FromRgb(0x7A, 0xAB, 0xF8)) : null
            };
            if (!isWeekend) dowText.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
            _dowGrid.Children.Add(dowText);
        }

        // Days Grid
        _dayGrid.Children.Clear();
        var firstDay = new DateTime(_viewMonth.Year, _viewMonth.Month, 1);
        int startOffset = ((int)firstDay.DayOfWeek + 6) % 7;
        int daysInMonth = DateTime.DaysInMonth(_viewMonth.Year, _viewMonth.Month);
        var prevMonth = _viewMonth.AddMonths(-1);
        int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        var today = DateTime.Today;

        Color accentColor = WeatherBridge.ThemeAccent;
        if (accentColor.A == 0) accentColor = Color.FromRgb(0x00, 0x78, 0xD7);

        for (int cell = 0; cell < 42; cell++)
        {
            DateTime cellDate;
            bool isCurrentMonth;

            if (cell < startOffset)
            {
                int day = daysInPrevMonth - (startOffset - 1 - cell);
                cellDate = new DateTime(prevMonth.Year, prevMonth.Month, day);
                isCurrentMonth = false;
            }
            else if (cell - startOffset < daysInMonth)
            {
                int day = cell - startOffset + 1;
                cellDate = new DateTime(_viewMonth.Year, _viewMonth.Month, day);
                isCurrentMonth = true;
            }
            else
            {
                int day = cell - startOffset - daysInMonth + 1;
                var nextM = _viewMonth.AddMonths(1);
                cellDate = new DateTime(nextM.Year, nextM.Month, day);
                isCurrentMonth = false;
            }

            bool isToday = cellDate.Date == today;
            bool isSelected = _selectedDate.HasValue && cellDate.Date == _selectedDate.Value.Date;
            bool hasEvents = CalendarBridge.Events.Any(e => e.Start.Date == cellDate.Date);

            var cellBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(1),
                Height = 22,
                Cursor = Cursors.Hand,
                ClipToBounds = true
            };

            if (isToday)
            {
                cellBorder.Background = new SolidColorBrush(Color.FromArgb(0x40, accentColor.R, accentColor.G, accentColor.B));
                cellBorder.BorderBrush = new SolidColorBrush(accentColor);
                cellBorder.BorderThickness = new Thickness(1);
            }
            else if (isSelected)
            {
                cellBorder.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
                cellBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
                cellBorder.BorderThickness = new Thickness(1);
            }

            var cellContent = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var dayNum = new TextBlock
            {
                Text = cellDate.Day.ToString(),
                FontSize = 10,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = isCurrentMonth ? 1.0 : 0.3
            };

            if (isToday)
            {
                dayNum.Foreground = new SolidColorBrush(accentColor);
            }
            else
            {
                dayNum.SetResourceReference(TextBlock.ForegroundProperty, "HomeTextBrush");
            }

            cellContent.Children.Add(dayNum);

            if (hasEvents)
            {
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Fill = isToday ? new SolidColorBrush(accentColor) : new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64)),
                    Margin = new Thickness(0, 1, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                cellContent.Children.Add(dot);
            }

            cellBorder.Child = cellContent;

            var capturedDate = cellDate;
            cellBorder.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _selectedDate = capturedDate;
                if (!isCurrentMonth)
                {
                    _viewMonth = new DateTime(capturedDate.Year, capturedDate.Month, 1);
                }
                Render();
            };

            cellBorder.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    LaunchFullCalendar();
                }
            };

            _dayGrid.Children.Add(cellBorder);
        }

        // Update footer preview
        UpdateFooterPreview();
    }

    private void UpdateFooterPreview()
    {
        if (_selectedDate.HasValue)
        {
            var date = _selectedDate.Value.Date;
            var dayEvents = CalendarBridge.Events.Where(e => e.Start.Date == date).OrderBy(e => e.Start).ToList();
            if (dayEvents.Count > 0)
            {
                var ev = dayEvents[0];
                string time = ev.Start.ToString("HH:mm");
                _eventPreview.Text = $"📅 {date:d MMM}: {time} {ev.Title}";
                return;
            }
        }

        var next = CalendarBridge.GetNextEvent();
        if (next != null)
        {
            _eventPreview.Text = $"📅 Next: {next.Start:ddd HH:mm} {next.Title}";
            return;
        }

        var loc = LocalizationService.Instance;
        string noEvents = loc.GetString("Calendar.NoEvents", "No upcoming events");
        _eventPreview.Text = $"📅 {noEvents}";
    }
}
