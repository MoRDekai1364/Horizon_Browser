using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Navigation;
using System.Text.RegularExpressions;
using Horizon.Stealth.Services;

namespace Horizon.Stealth;

public partial class MainWindow
{
    // ── Calendar event storage (local + synced) ───────────────────────────────
    private readonly List<CalendarEvent> _calendarEvents = new();
    private string _currentNoteTab = "";

    // ── Navigation live state ─────────────────────────────────────────────────
    private bool      _navActive            = false;
    private DateTime  _navStartedAt         = DateTime.MinValue;
    private string    _navDestinationLabel  = "";
    private TimeSpan  _navEstimatedDuration = TimeSpan.Zero;
    private double    _navBearingDeg        = 0.0;
    private double    _navUserLat           = 0.0;
    private double    _navUserLon           = 0.0;
    private double    _navDestLat           = 0.0;
    private double    _navDestLon           = 0.0;
    private double    _navTotalDistanceKm   = 0.0;
    private Window?   _navHudWindow         = null;

    // ═══════════════════════════════════════════════════════════════════════════
    //  CLOCK  —  5 display modes + Stopwatch + Timer
    // ═══════════════════════════════════════════════════════════════════════════

    private int _clockMode = 0;

    private readonly Stopwatch _swClock = new();
    private TimeSpan _timerDuration = TimeSpan.FromMinutes(5);
    private DateTime _timerStartedAt;
    private bool     _timerIsRunning = false;

    private string GetClockText() => _clockMode switch
    {
        0 => DateTime.Now.ToString("HH:mm:ss"),
        1 => DateTime.Now.ToString("HH:mm"),
        2 => DateTime.Now.ToString("hh:mm tt"),
        3 => DateTime.Now.ToString("HH:mm"),
        4 => FormatElapsed(_swClock.Elapsed),
        5 => FormatTimer(),
        _ => DateTime.Now.ToString("HH:mm:ss")
    };

    private string GetClockSubText() =>
        CurrentWidgetMode() == "Clock" && _clockMode == 3
            ? ":" + DateTime.Now.ToString("ss")
            : "";

    private static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100}";

    private string FormatTimeSpan(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";

    private string FormatTimer()
    {
        if (!_timerIsRunning) return FormatTimeSpan(_timerDuration);
        var remaining = _timerDuration - (DateTime.Now - _timerStartedAt);
        if (remaining <= TimeSpan.Zero)
        {
            _timerIsRunning = false;
            Dispatcher.BeginInvoke(() =>
                MessageBox.Show("⏰  Timer finished!", "Horizon", MessageBoxButton.OK, MessageBoxImage.Information));
            return "00:00";
        }
        return FormatTimeSpan(remaining);
    }

    private void OpenClockModeMenu()
    {
        var win = MakeToolWindow("Clock Mode", 272);
        var root = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        root.Children.Add(SectionLabel("CLOCK DISPLAY"));

        (string Label, int Mode)[] modes =
        {
            ("🕐  24 h  with seconds  —  14:32:05",  0),
            ("🕑  24 h  no seconds    —  14:32",      1),
            ("🕒  12 h  AM/PM         —  2:32 PM",    2),
            ("🕓  24 h  + :ss corner  —  14:32 ˢˢ",  3),
        };

        foreach (var (lbl, idx) in modes)
        {
            var btn = MenuButton(lbl, _clockMode == idx);
            var cap = idx;
            btn.Click += (s, e) =>
            {
                _clockMode = cap;
                SettingsService.Current.ClockMode = cap;
                SettingsService.Save();
                RefreshWidgetDisplay();
                win.Close();
            };
            root.Children.Add(btn);
        }

        root.Children.Add(new Separator { Background = new SolidColorBrush(C(0x2a2a2a)), Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(SectionLabel("STOPWATCH & TIMER"));

        var swBtn = MenuButton("⏱  Stopwatch", _clockMode == 4);
        swBtn.Click += (s, e) =>
        {
            _clockMode = 4; SettingsService.Current.ClockMode = 4; SettingsService.Save();
            win.Close(); OpenStopwatchWindow();
        };

        var tmBtn = MenuButton("⏲  Timer", _clockMode == 5);
        tmBtn.Click += (s, e) =>
        {
            _clockMode = 5; SettingsService.Current.ClockMode = 5; SettingsService.Save();
            win.Close(); OpenTimerWindow();
        };

        root.Children.Add(swBtn);
        root.Children.Add(tmBtn);
        win.Content = root;
        win.Show();
    }

    private void OpenStopwatchWindow()
    {
        var win = MakeToolWindow("Stopwatch", 300, true);
        var root = new StackPanel { Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center };

        var display = new TextBlock
        {
            Text = "00:00.0", FontSize = 48, FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Consolas"), Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6)
        };

        var lapList = new ListBox
        {
            Background = new SolidColorBrush(C(0x1a1a1a)), Foreground = new SolidColorBrush(C(0xaaaaaa)),
            BorderThickness = new Thickness(0), MaxHeight = 110, MinWidth = 210,
            FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 0, 0, 10)
        };

        int lapCount = 0;
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var btnSS    = AccentButton(_swClock.IsRunning ? "⏸  Pause" : "▶  Start", C(0x1a4a1a), C(0x2a7a2a), 88);
        var btnLap   = AccentButton("⌚  Lap",   C(0x1a3454), C(0x2e6aa0), 76);
        var btnReset = AccentButton("↺  Reset", C(0x3a2222), C(0x663333), 76);

        btnSS.Click    += (s, e) => { if (_swClock.IsRunning) { _swClock.Stop(); btnSS.Content = "▶  Resume"; } else { _swClock.Start(); btnSS.Content = "⏸  Pause"; } };
        btnLap.Click   += (s, e) => { lapCount++; lapList.Items.Insert(0, $"Lap {lapCount,2}   {FormatElapsed(_swClock.Elapsed)}"); };
        btnReset.Click += (s, e) => { _swClock.Reset(); lapList.Items.Clear(); lapCount = 0; display.Text = "00:00.0"; btnSS.Content = "▶  Start"; };

        foreach (var b in new[] { btnSS, btnLap, btnReset }) { b.Margin = new Thickness(3, 0, 3, 0); btnRow.Children.Add(b); }
        root.Children.Add(display); root.Children.Add(lapList); root.Children.Add(btnRow);

        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        tick.Tick += (s, e) => display.Text = FormatElapsed(_swClock.Elapsed);
        tick.Start();
        win.Closed += (s, e) => tick.Stop();
        win.Content = root;
        win.Show();
    }

    private void OpenTimerWindow()
    {
        var win = MakeToolWindow("Timer", 300, true);
        var root = new StackPanel { Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Center };

        var spinRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        var (hSp, hBox) = Spinner("HRS", (int)_timerDuration.TotalHours);
        var (mSp, mBox) = Spinner("MIN", _timerDuration.Minutes);
        var (sSp, sBox) = Spinner("SEC", _timerDuration.Seconds);

        TextBlock Colon() => new TextBlock { Text = ":", Foreground = Brushes.White, FontSize = 26, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, 6) };
        spinRow.Children.Add(hSp); spinRow.Children.Add(Colon());
        spinRow.Children.Add(mSp); spinRow.Children.Add(Colon());
        spinRow.Children.Add(sSp);

        var presets = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (lbl, m) in new[] { ("1m",1), ("3m",3), ("5m",5), ("10m",10), ("15m",15), ("30m",30) })
        {
            var pb = new Button
            {
                Content = lbl, Width = 36, Height = 22, FontSize = 10, Margin = new Thickness(2,0,2,0), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(C(0x222222)), Foreground = new SolidColorBrush(C(0x888888)),
                BorderBrush = new SolidColorBrush(C(0x333333)), BorderThickness = new Thickness(1)
            };
            var cm = m;
            pb.Click += (s, e) => { hBox.Text = "00"; mBox.Text = cm.ToString("D2"); sBox.Text = "00"; };
            presets.Children.Add(pb);
        }

        var display = new TextBlock
        {
            FontSize = 48, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"),
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12)
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var btnSS  = AccentButton(_timerIsRunning ? "⏸  Pause" : "▶  Start", C(0x1a4a1a), C(0x2a7a2a), 88);
        var btnRst = AccentButton("↺  Reset", C(0x3a2222), C(0x663333), 76);

        TimeSpan ReadSet()
        {
            int.TryParse(hBox.Text, out int h); int.TryParse(mBox.Text, out int m2); int.TryParse(sBox.Text, out int s2);
            return new TimeSpan(h, m2, s2);
        }

        btnSS.Click += (s, e) =>
        {
            if (_timerIsRunning) { _timerIsRunning = false; btnSS.Content = "▶  Resume"; }
            else { _timerDuration = ReadSet(); _timerStartedAt = DateTime.Now; _timerIsRunning = true; btnSS.Content = "⏸  Pause"; }
        };
        btnRst.Click += (s, e) =>
        {
            _timerIsRunning = false; _timerDuration = ReadSet();
            display.Text = FormatTimeSpan(_timerDuration); display.Foreground = Brushes.White;
            btnSS.Content = "▶  Start";
        };
        btnRow.Children.Add(btnSS); btnRst.Margin = new Thickness(6, 0, 0, 0); btnRow.Children.Add(btnRst);
        root.Children.Add(spinRow); root.Children.Add(presets); root.Children.Add(display); root.Children.Add(btnRow);

        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        tick.Tick += (s, e) =>
        {
            var txt = FormatTimer();
            display.Text = txt;
            display.Foreground = (!_timerIsRunning && txt == "00:00")
                ? new SolidColorBrush(C(0xff6644)) : Brushes.White;
        };
        tick.Start();
        win.Closed += (s, e) => tick.Stop();
        win.Content = root;
        win.Show();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CONVERTER WIDGET
    // ═══════════════════════════════════════════════════════════════════════════

    private void OpenConverterWindow()
    {
        var win = new Window
        {
            Title = "Converter", Width = 330, Height = 320,
            Background = new SolidColorBrush(C(0x161616)),
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.CanResize,
            Owner = this, ShowInTaskbar = false, Topmost = true
        };
        win.Closing += (s, e) => { win.Owner = null; };
        win.Closed  += (s, e) => Dispatcher.BeginInvoke(new Action(() => { try { if (WindowState != WindowState.Minimized) Activate(); } catch { } }));

        var root = new DockPanel { Margin = new Thickness(8) };

        var _dci = DarkComboItemStyle();
        var catBox = new ComboBox
        {
            ItemsSource = new[] { "📏 Length", "⚖ Weight", "🌡 Temperature", "💨 Speed", "📦 Volume", "📐 Area", "💾 Data", "💱 Currency", "⏱ Time" },
            SelectedIndex = 0,
            Background = new SolidColorBrush(C(0x222222)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x333333)), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4),
            ItemContainerStyle = _dci
        };
        DockPanel.SetDock(catBox, Dock.Top);
        root.Children.Add(catBox);

        var cg = new Grid();
        cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        ComboBox MakeCatCombo() => new ComboBox { Background = new SolidColorBrush(C(0x222222)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(C(0x333333)), Margin = new Thickness(0,0,0,4), Padding = new Thickness(4,2,4,2), ItemContainerStyle = _dci };
        var fromUnitBox = MakeCatCombo(); var toUnitBox = MakeCatCombo();

        var swapBtn = new Button { Content = "⇄", Width = 30, Height = 24, Background = new SolidColorBrush(C(0x2a2a2a)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(C(0x444444)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontSize = 14, Margin = new Thickness(2, 0, 2, 4) };

        TextBox MakeNumBox(bool readOnly) => new TextBox
        {
            Background = new SolidColorBrush(readOnly ? C(0x0a0a0a) : C(0x0e0e0e)),
            Foreground = readOnly ? new SolidColorBrush(C(0x88ff88)) : Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x333333)), BorderThickness = new Thickness(1),
            FontSize = 22, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold,
            Padding = new Thickness(8, 4, 8, 4), TextAlignment = TextAlignment.Right,
            IsReadOnly = readOnly, VerticalContentAlignment = VerticalAlignment.Center
        };
        var fromIn = MakeNumBox(false); var toOut = MakeNumBox(true);
        var formula = new TextBlock { Foreground = new SolidColorBrush(C(0x888888)), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };

        Grid.SetRow(fromUnitBox, 0); Grid.SetColumn(fromUnitBox, 0);
        Grid.SetRow(swapBtn,     0); Grid.SetColumn(swapBtn, 1);
        Grid.SetRow(toUnitBox,   0); Grid.SetColumn(toUnitBox, 2);
        Grid.SetRow(fromIn,      1); Grid.SetColumn(fromIn, 0);
        Grid.SetRow(toOut,       1); Grid.SetColumn(toOut, 2);
        Grid.SetRow(formula,     2); Grid.SetColumn(formula, 0); Grid.SetColumnSpan(formula, 3);
        foreach (var el in new UIElement[] { fromUnitBox, swapBtn, toUnitBox, fromIn, toOut, formula }) cg.Children.Add(el);
        DockPanel.SetDock(cg, Dock.Top);
        root.Children.Add(cg);

        var data = new Dictionary<string, (string[] units, Func<double, int, int, double> conv)>
        {
            ["📏 Length"]      = (new[]{"mm","cm","m","km","in","ft","yd","mi","nmi"}, (v,f,t) => { double[] m={0.001,0.01,1,1000,0.0254,0.3048,0.9144,1609.344,1852}; return v*m[f]/m[t]; }),
            ["⚖ Weight"]      = (new[]{"mg","g","kg","t","oz","lb","st"},             (v,f,t) => { double[] g={0.001,1,1000,1e6,28.3495,453.592,6350.29}; return v*g[f]/g[t]; }),
            ["🌡 Temperature"] = (new[]{"°C","°F","K"},
                (v,f,t) => { double c = f switch{0=>v,1=>(v-32)*5/9.0,2=>v-273.15,_=>v}; return t switch{0=>c,1=>c*9/5.0+32,2=>c+273.15,_=>c}; }),
            ["💨 Speed"]       = (new[]{"m/s","km/h","mph","knots","ft/s","Mach"},    (v,f,t) => { double[] ms={1,1/3.6,0.44704,0.514444,0.3048,340.29}; return v*ms[f]/ms[t]; }),
            ["📦 Volume"]      = (new[]{"ml","cl","dl","L","fl oz","cup","pt","qt","gal"}, (v,f,t) => { double[] ml={1,10,100,1000,29.5735,236.588,473.176,946.353,3785.41}; return v*ml[f]/ml[t]; }),
            ["📐 Area"]        = (new[]{"mm²","cm²","m²","km²","in²","ft²","acre","ha","mi²"}, (v,f,t) => { double[] m2={1e-6,1e-4,1,1e6,6.4516e-4,0.092903,4046.86,10000,2589988}; return v*m2[f]/m2[t]; }),
            ["💾 Data"]        = (new[]{"bit","B","KB","MB","GB","TB","KiB","MiB","GiB","TiB"}, (v,f,t) => { double[] b={1,8,8e3,8e6,8e9,8e12,8192,8388608,8589934592,8796093022208}; return v*b[f]/b[t]; }),
            ["💱 Currency"]    = (new[]{"USD","EUR","GBP","JPY","CNY","CAD","AUD","CHF","INR","MXN"},
                (v,f,t) => { double[] u={1,0.92,0.79,149.5,7.24,1.36,1.52,0.89,83.1,17.2}; return v/u[f]*u[t]; }),
            ["⏱ Time"]        = (new[]{"ms","sec","min","hr","day","wk","mo","yr"},
                (v,f,t) => { double[] s={0.001,1,60,3600,86400,604800,2629800,31557600}; return v*s[f]/s[t]; }),
        };

        void DoConvert()
        {
            if (catBox.SelectedItem is not string cat || !data.TryGetValue(cat, out var d)) return;
            if (fromUnitBox.SelectedIndex < 0 || toUnitBox.SelectedIndex < 0) return;
            if (!double.TryParse(fromIn.Text.Replace(",","."), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) { toOut.Text = "—"; return; }
            double r = d.conv(v, fromUnitBox.SelectedIndex, toUnitBox.SelectedIndex);
            toOut.Text = r.ToString(Math.Abs(r) > 1e6 || (Math.Abs(r) < 1e-3 && r != 0) ? "G5" : "G8", CultureInfo.InvariantCulture);
            double unit = d.conv(1, fromUnitBox.SelectedIndex, toUnitBox.SelectedIndex);
            formula.Text = $"1 {fromUnitBox.SelectedItem} = {unit.ToString("G5", CultureInfo.InvariantCulture)} {toUnitBox.SelectedItem}";
            if (cat == "💱 Currency") formula.Text += "  (approx, offline rates)";
        }
        void UpdateUnits()
        {
            if (catBox.SelectedItem is not string cat || !data.TryGetValue(cat, out var d)) return;
            fromUnitBox.ItemsSource = d.units; toUnitBox.ItemsSource = d.units;
            fromUnitBox.SelectedIndex = 0; toUnitBox.SelectedIndex = Math.Min(1, d.units.Length - 1);
            fromIn.Text = "1"; DoConvert();
        }

        catBox.SelectionChanged += (s, e) => UpdateUnits();
        fromUnitBox.SelectionChanged += (s, e) => DoConvert();
        toUnitBox.SelectionChanged += (s, e) => DoConvert();
        fromIn.TextChanged += (s, e) => DoConvert();
        swapBtn.Click += (s, e) => { (fromUnitBox.SelectedIndex, toUnitBox.SelectedIndex) = (toUnitBox.SelectedIndex, fromUnitBox.SelectedIndex); DoConvert(); };
        UpdateUnits();
        win.Content = root;
        win.Show();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CALENDAR WIDGET
    // ═══════════════════════════════════════════════════════════════════════════

    private void OpenCalendarWindow()
    {
        var win = new Window
        {
            Title = "Calendar", Width = 400, Height = 500,
            Background = new SolidColorBrush(C(0x131313)),
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.CanResize,
            Owner = this, ShowInTaskbar = false, Topmost = true
        };

        var outer = new DockPanel();
        var tabBar = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(C(0x0e0e0e)) };
        DockPanel.SetDock(tabBar, Dock.Top);
        outer.Children.Add(tabBar);

        var calPanel      = BuildMonthCalendarPanel();
        var datePanel     = BuildDateCalcPanel();
        var agePanel      = BuildAgeCalcPanel();
        var accountsPanel = BuildCalendarAccountsPanel();

        Panel[] panels = { calPanel, datePanel, agePanel, accountsPanel };
        string[] tabs  = { "📅 Calendar", "📊 Date Calc", "🎂 Age Calc", "🔗 Accounts" };
        var tabBtns    = new Button[4];

        for (int i = 0; i < 4; i++)
        {
            var btn = new Button
            {
                Content = tabs[i], FontSize = 11, Height = 30, Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.Transparent, Foreground = new SolidColorBrush(C(0x666666)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand
            };
            tabBtns[i] = btn;
            var ci = i;
            btn.Click += (s, e) =>
            {
                for (int j = 0; j < 4; j++)
                {
                    panels[j].Visibility = j == ci ? Visibility.Visible : Visibility.Collapsed;
                    tabBtns[j].Foreground = new SolidColorBrush(j == ci ? C(0x88ccff) : C(0x666666));
                    tabBtns[j].BorderThickness = j == ci ? new Thickness(0, 0, 0, 2) : new Thickness(0);
                    tabBtns[j].BorderBrush     = new SolidColorBrush(j == ci ? C(0x2e6aa0) : C(0x000000));
                }
            };
            tabBar.Children.Add(btn);
        }

        var holder = new Grid();
        foreach (var p in panels) holder.Children.Add(p);
        outer.Children.Add(holder);

        tabBtns[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        win.Closing += (s, e) => { win.Owner = null; };
        win.Closed  += (s, e) => Dispatcher.BeginInvoke(new Action(() => { try { if (WindowState != WindowState.Minimized) Activate(); } catch { } }));
        win.Content = outer;
        win.Show();
    }

    private Grid BuildMonthCalendarPanel()
    {
        var g = new Grid { Margin = new Thickness(8) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var cur   = DateTime.Today;
        var today = DateTime.Today;

        // Nav row with + Event button
        var nav = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var prev   = NavBtn("‹");
        var next   = NavBtn("›");
        var addBtn = new Button
        {
            Content = "+ Event", FontSize = 10, Height = 24, Padding = new Thickness(8, 0, 8, 0),
            Background = new SolidColorBrush(C(0x1a3454)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x2e6aa0)), BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0)
        };
        var monthLbl = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White
        };
        Grid.SetColumn(prev, 0); Grid.SetColumn(monthLbl, 1); Grid.SetColumn(next, 2); Grid.SetColumn(addBtn, 3);
        nav.Children.Add(prev); nav.Children.Add(monthLbl); nav.Children.Add(next); nav.Children.Add(addBtn);

        // Day-of-week headers
        var dowHeader = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (d, wk) in new[]{("Mo",false),("Tu",false),("We",false),("Th",false),("Fr",false),("Sa",true),("Su",true)})
            dowHeader.Children.Add(new TextBlock
            {
                Text = d, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(wk ? C(0x5588ff) : C(0x555555)),
                FontSize = 11, FontWeight = FontWeights.Bold
            });

        var dayGrid = new UniformGrid { Rows = 6, Columns = 7 };

        addBtn.Click += (s, e) => OpenAddEventDialog(today, () => RenderCalendar(dayGrid, monthLbl, cur, today));

        void RenderCalendar(UniformGrid grid, TextBlock lbl, DateTime month, DateTime todayRef)
        {
            lbl.Text = month.ToString("MMMM  yyyy");
            grid.Children.Clear();
            var first = new DateTime(month.Year, month.Month, 1);
            int start = ((int)first.DayOfWeek + 6) % 7;
            int dim   = DateTime.DaysInMonth(month.Year, month.Month);

            for (int i = 0; i < 42; i++)
            {
                int dn   = i - start + 1;
                var cell = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(5) };

                if (dn >= 1 && dn <= dim)
                {
                    var date     = new DateTime(month.Year, month.Month, dn);
                    bool isToday = date == todayRef;
                    bool isWknd  = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    var dayEvents = _calendarEvents.Where(ev => ev.Start.Date == date.Date).ToList();
                    bool hasEvents = dayEvents.Count > 0;

                    cell.Background      = new SolidColorBrush(isToday ? C(0x1a4472) : C(0x1e1e1e));
                    cell.BorderBrush     = new SolidColorBrush(isToday ? C(0x2e6aa0) : hasEvents ? C(0x336633) : C(0x2a2a2a));
                    cell.BorderThickness = new Thickness(isToday || hasEvents ? 1 : 0);
                    cell.Cursor          = Cursors.Hand;

                    var cellContent = new Grid();
                    cellContent.Children.Add(new TextBlock
                    {
                        Text = dn.ToString(),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                        FontSize = 12, FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = new SolidColorBrush(isToday ? C(0xaaddff) : isWknd ? C(0x5588ff) : C(0xcccccc))
                    });

                    if (hasEvents)
                    {
                        cellContent.Children.Add(new System.Windows.Shapes.Ellipse
                        {
                            Width = 4, Height = 4,
                            Fill = new SolidColorBrush(C(0x44bb44)),
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment   = VerticalAlignment.Top,
                            Margin = new Thickness(0, 2, 2, 0)
                        });
                    }
                    cell.Child = cellContent;

                    var captureDate   = date;
                    var captureEvents = dayEvents;
                    cell.MouseLeftButtonUp += (s, e) =>
                        ShowDayEventsPopup(captureDate, captureEvents, () => RenderCalendar(grid, lbl, month, todayRef));
                }
                else
                {
                    cell.Background = Brushes.Transparent;
                }
                grid.Children.Add(cell);
            }
        }

        prev.Click += (s, e) => { cur = cur.AddMonths(-1); RenderCalendar(dayGrid, monthLbl, cur, today); };
        next.Click += (s, e) => { cur = cur.AddMonths(1);  RenderCalendar(dayGrid, monthLbl, cur, today); };

        Grid.SetRow(nav, 0); Grid.SetRow(dowHeader, 1); Grid.SetRow(dayGrid, 2);
        g.Children.Add(nav); g.Children.Add(dowHeader); g.Children.Add(dayGrid);
        RenderCalendar(dayGrid, monthLbl, cur, today);
        return g;
    }

    private void ShowDayEventsPopup(DateTime date, List<CalendarEvent> events, Action refreshCalendar)
    {
        var popup = MakeToolWindow($"📅  {date:dddd, d MMMM yyyy}", 340);
        var root  = new StackPanel { Margin = new Thickness(12) };

        if (events.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "No events on this day.",
                Foreground = new SolidColorBrush(C(0x999999)),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 10)
            });
        }
        else
        {
            foreach (var ev in events.OrderBy(e => e.Start))
            {
                var evBorder = new Border
                {
                    Background = new SolidColorBrush(C(0x1a2a1a)),
                    BorderBrush = new SolidColorBrush(C(0x2a5a2a)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6)
                };
                string timeStr = ev.Start.Date == ev.End.Date
                    ? $"{ev.Start:HH:mm} – {ev.End:HH:mm}"
                    : $"{ev.Start:d MMM HH:mm} – {ev.End:d MMM HH:mm}";

                var evStack = new StackPanel();
                evStack.Children.Add(new TextBlock { Text = ev.Title, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                evStack.Children.Add(new TextBlock { Text = timeStr, Foreground = new SolidColorBrush(C(0x88ccff)), FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
                if (!string.IsNullOrEmpty(ev.Location))
                    evStack.Children.Add(new TextBlock { Text = "📍 " + ev.Location, Foreground = new SolidColorBrush(C(0x666666)), FontSize = 10, TextWrapping = TextWrapping.Wrap });
                if (!string.IsNullOrEmpty(ev.Source))
                    evStack.Children.Add(new TextBlock { Text = ev.Source, Foreground = new SolidColorBrush(C(0x444444)), FontSize = 9, FontStyle = FontStyles.Italic });

                // Remove button (only for local events)
                if (ev.Source == "Local")
                {
                    var removeBtn = new Button
                    {
                        Content = "🗑 Remove", FontSize = 9, Padding = new Thickness(6, 2, 6, 2),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Background = new SolidColorBrush(C(0x3a1a1a)), Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(C(0x662222)), BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand, Margin = new Thickness(0, 4, 0, 0)
                    };
                    var captureEv = ev;
                    removeBtn.Click += (s, e) =>
                    {
                        _calendarEvents.Remove(captureEv);
                        popup.Close();
                        refreshCalendar();
                    };
                    evStack.Children.Add(removeBtn);
                }

                evBorder.Child = evStack;
                root.Children.Add(evBorder);
            }
        }

        var addBtn = SyncButton("+ Add Event", C(0x1a3454), C(0x2e6aa0));
        addBtn.Click += (s, e) => { popup.Close(); OpenAddEventDialog(date, refreshCalendar); };
        root.Children.Add(addBtn);

        popup.Content = root;
        popup.Show();
    }

    private void OpenAddEventDialog(DateTime defaultDate, Action? refreshCalendar = null)
    {
        var dlg = MakeToolWindow("Add Event", 320);
        var root = new StackPanel { Margin = new Thickness(14) };

        root.Children.Add(SectionLabel("NEW EVENT"));

        root.Children.Add(FieldLabel("Title"));
        var titleBox = new TextBox
        {
            Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x333333)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 8), FontSize = 12
        };
        root.Children.Add(titleBox);

        root.Children.Add(FieldLabel("Date"));
        var datePicker = new DatePicker
        {
            SelectedDate = defaultDate,
            Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x333333)), Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(datePicker);

        root.Children.Add(FieldLabel("Time  (HH:mm – HH:mm)"));
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var startBox = new TextBox { Text = "09:00", Width = 60, Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(C(0x333333)), BorderThickness = new Thickness(1), Padding = new Thickness(4), FontFamily = new FontFamily("Consolas") };
        var sepLbl   = new TextBlock { Text = " – ", Foreground = new SolidColorBrush(C(0x666666)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        var endBox   = new TextBox { Text = "10:00", Width = 60, Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(C(0x333333)), BorderThickness = new Thickness(1), Padding = new Thickness(4), FontFamily = new FontFamily("Consolas") };
        timeRow.Children.Add(startBox); timeRow.Children.Add(sepLbl); timeRow.Children.Add(endBox);
        root.Children.Add(timeRow);

        root.Children.Add(FieldLabel("Location (optional)"));
        var locBox = new TextBox
        {
            Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x333333)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 12), FontSize = 12
        };
        root.Children.Add(locBox);

        var statusBlock = new TextBlock { Foreground = new SolidColorBrush(C(0xff5555)), FontSize = 10, Margin = new Thickness(0, 0, 0, 6) };
        root.Children.Add(statusBlock);

        var saveBtn = SyncButton("💾  Save Event", C(0x1a3a1a), C(0x2a6a2a));
        saveBtn.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text)) { statusBlock.Text = "Title is required."; return; }
            if (datePicker.SelectedDate == null) { statusBlock.Text = "Please pick a date."; return; }

            DateTime baseDate = datePicker.SelectedDate.Value.Date;
            DateTime start    = baseDate;
            DateTime end      = baseDate.AddHours(1);
            if (TimeSpan.TryParse(startBox.Text, out var st)) start = baseDate + st;
            if (TimeSpan.TryParse(endBox.Text,   out var et)) end   = baseDate + et;

            _calendarEvents.Add(new CalendarEvent
            {
                Id       = Guid.NewGuid().ToString(),
                Title    = titleBox.Text.Trim(),
                Start    = start,
                End      = end,
                Location = locBox.Text.Trim(),
                Source   = "Local"
            });

            dlg.Close();
            refreshCalendar?.Invoke();
        };
        root.Children.Add(saveBtn);
        dlg.Content = root;
        dlg.Show();
        titleBox.Focus();
    }

    // ── Calendar Accounts panel ───────────────────────────────────────────────
    private StackPanel BuildCalendarAccountsPanel()
    {
        var root = new StackPanel { Margin = new Thickness(12), Visibility = Visibility.Collapsed };
        root.Children.Add(SectionLabel("CONNECTED ACCOUNTS"));

        var accountsList = new StackPanel();
        root.Children.Add(accountsList);

        var statusBlock = new TextBlock { Foreground = new SolidColorBrush(C(0x888888)), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };

        void RefreshAccountsList()
        {
            accountsList.Children.Clear();
            var accounts = SettingsService.Current.SyncAccounts;
            if (accounts.Count == 0)
            {
                accountsList.Children.Add(new TextBlock
                {
                    Text = "No accounts connected yet.",
                    Foreground = new SolidColorBrush(C(0x888888)),
                    FontSize = 11, Margin = new Thickness(0, 0, 0, 8)
                });
                return;
            }

            foreach (var acc in accounts)
            {
                var row = new Border
                {
                    Background = new SolidColorBrush(C(0x1a1a1a)), BorderBrush = new SolidColorBrush(C(0x2a2a2a)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 4)
                };
                var rowInner = new Grid();
                rowInner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowInner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                string icon = acc.Provider == "Google" ? "🟢" : "🔵";
                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = $"{icon} {acc.DisplayName}", Foreground = Brushes.White, FontSize = 12 });
                info.Children.Add(new TextBlock { Text = acc.Email, Foreground = new SolidColorBrush(C(0x666666)), FontSize = 10 });
                Grid.SetColumn(info, 0);
                rowInner.Children.Add(info);

                var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var syncBtn = new Button
                {
                    Content = "↻", Width = 26, Height = 24, FontSize = 13,
                    Background = new SolidColorBrush(C(0x1a3a1a)), Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(C(0x2a5a2a)), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Sync Calendar"
                };
                var removeBtn = new Button
                {
                    Content = "✕", Width = 24, Height = 24, FontSize = 11,
                    Background = new SolidColorBrush(C(0x3a1a1a)), Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(C(0x662222)), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, ToolTip = "Remove account"
                };

                var captureAcc = acc;
                syncBtn.Click += async (s, e) =>
                {
                    statusBlock.Text = $"Syncing {captureAcc.Email}…";
                    try
                    {
                        if (captureAcc.Provider == "Google")
                            await SyncNotesGoogleAsync("", captureAcc);
                        else
                            await SyncNotesMicrosoftAsync("", captureAcc);

                        _calendarEvents.RemoveAll(ev => ev.Source == captureAcc.Email);
                        SettingsService.Save();
                        statusBlock.Text = $"✓ Synced {captureAcc.Email}";
                    }
                    catch (Exception ex) { statusBlock.Text = $"Sync failed: {ex.Message}"; }
                };
                removeBtn.Click += (s, e) =>
                {
                    SettingsService.Current.SyncAccounts.Remove(captureAcc);
                    _calendarEvents.RemoveAll(ev => ev.Source == captureAcc.Email);
                    SettingsService.Save();
                    RefreshAccountsList();
                };

                btnStack.Children.Add(syncBtn); btnStack.Children.Add(removeBtn);
                Grid.SetColumn(btnStack, 1);
                rowInner.Children.Add(btnStack);
                row.Child = rowInner;
                accountsList.Children.Add(row);
            }
        }

        RefreshAccountsList();

        root.Children.Add(new Separator { Background = new SolidColorBrush(C(0x2a2a2a)), Margin = new Thickness(0, 10, 0, 10) });
        root.Children.Add(SectionLabel("ADD ACCOUNT"));

        var btnGoogle = SyncButton("🟢  Add Google Account", C(0x1e3a1e), C(0x2e6a2e));
        var btnMS     = SyncButton("🔵  Add Microsoft Account", C(0x1a2e54), C(0x2e5aa0));

        btnGoogle.Click += async (s, e) =>
        {
            statusBlock.Text = "Opening Google sign-in…";
            try
            {
                var account = await AddAccountViaWebViewAsync("Google");
                if (account != null)
                {
                    SettingsService.Current.SyncAccounts.RemoveAll(a => a.Email == account.Email);
                    SettingsService.Current.SyncAccounts.Add(account);
                    SettingsService.Save();
                    statusBlock.Text = $"✓ Signed in as {account.Email}";
                    RefreshAccountsList();
                }
                else statusBlock.Text = "Sign-in cancelled.";
            }
            catch (Exception ex) { statusBlock.Text = $"Error: {ex.Message}"; }
        };

        btnMS.Click += async (s, e) =>
        {
            if (string.IsNullOrEmpty(SettingsService.Current.MicrosoftClientId))
            {
                statusBlock.Text = "Set Microsoft Client ID in Settings → Integrations first."; return;
            }
            statusBlock.Text = "Opening Microsoft sign-in…";
            try
            {
                var account = await AddAccountViaWebViewAsync("Microsoft");
                if (account != null)
                {
                    SettingsService.Current.SyncAccounts.RemoveAll(a => a.Email == account.Email);
                    SettingsService.Current.SyncAccounts.Add(account);
                    SettingsService.Save();
                    statusBlock.Text = $"✓ Signed in as {account.Email}";
                    RefreshAccountsList();
                }
                else statusBlock.Text = "Sign-in cancelled.";
            }
            catch (Exception ex) { statusBlock.Text = $"Error: {ex.Message}"; }
        };

        // Sync-all
        var syncAllBtn = BarButton("↻  Sync All Accounts", C(0x1a2a3a), C(0x2e4a6a));
        syncAllBtn.Margin = new Thickness(0, 10, 0, 0);
        syncAllBtn.Click += async (s, e) =>
        {
            var accounts = SettingsService.Current.SyncAccounts;
            if (accounts.Count == 0) { statusBlock.Text = "No accounts to sync."; return; }
            statusBlock.Text = "Syncing all accounts…";
            int total = 0;
            foreach (var acc in accounts.ToList())
            {
                try
                {
                    List<CalendarEvent> events = acc.Provider == "Google"
                        ? await AccountSyncService.FetchGoogleCalendarAsync(acc, AccountSyncService.AppGoogleClientId, AccountSyncService.AppGoogleClientSecret)
                        : await AccountSyncService.FetchMicrosoftCalendarAsync(acc, SettingsService.Current.MicrosoftClientId);
                    _calendarEvents.RemoveAll(ev => ev.Source == acc.Email);
                    _calendarEvents.AddRange(events);
                    total += events.Count;
                }
                catch { }
            }
            SettingsService.Save();
            statusBlock.Text = $"✓ Synced {total} events across {accounts.Count} account(s)";
        };

        root.Children.Add(btnGoogle);
        root.Children.Add(btnMS);
        root.Children.Add(syncAllBtn);
        root.Children.Add(statusBlock);
        return root;
    }

    private StackPanel BuildDateCalcPanel()
    {
        var root = new StackPanel { Margin = new Thickness(12), Visibility = Visibility.Collapsed };
        root.Children.Add(SectionLabel("DATE DIFFERENCE"));

        DatePicker MkPicker(DateTime def)
        {
            var dp = new DatePicker
            {
                SelectedDate = def, Background = new SolidColorBrush(C(0x1e1e1e)),
                Foreground = Brushes.White, BorderBrush = new SolidColorBrush(C(0x333333)), Margin = new Thickness(0,0,0,6)
            };
            ApplyDarkDatePickerStyle(dp);
            return dp;
        }

        root.Children.Add(FieldLabel("Start date")); var p1 = MkPicker(DateTime.Today.AddMonths(-1)); root.Children.Add(p1);
        root.Children.Add(FieldLabel("End date"));   var p2 = MkPicker(DateTime.Today);               root.Children.Add(p2);

        var diffBlock = new TextBlock { Foreground = new SolidColorBrush(C(0x88ccff)), FontSize = 13, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,0) };

        void Calc()
        {
            if (p1.SelectedDate == null || p2.SelectedDate == null) return;
            var a = p1.SelectedDate.Value; var b = p2.SelectedDate.Value;
            if (a > b) (a, b) = (b, a);
            var span = b - a;
            int yrs = 0, mos = 0; var t = a;
            while (t.AddYears(1)  <= b) { yrs++; t = t.AddYears(1); }
            while (t.AddMonths(1) <= b) { mos++; t = t.AddMonths(1); }
            int days = (b - t).Days;
            diffBlock.Text = $"{span.Days} days total\n= {yrs}y {mos}m {days}d\n= {span.Days / 7} weeks {span.Days % 7} days\n= {(long)span.TotalHours:N0} hours";
        }

        p1.SelectedDateChanged += (s, e) => Calc();
        p2.SelectedDateChanged += (s, e) => Calc();
        root.Children.Add(diffBlock);

        root.Children.Add(new Separator { Background = new SolidColorBrush(C(0x2a2a2a)), Margin = new Thickness(0, 14, 0, 14) });
        root.Children.Add(SectionLabel("DAYS FROM TODAY"));
        root.Children.Add(FieldLabel("Target date"));
        var p3 = MkPicker(DateTime.Today.AddMonths(3)); root.Children.Add(p3);
        var daysBlock = new TextBlock { Foreground = new SolidColorBrush(C(0x88ccff)), FontSize = 13, FontFamily = new FontFamily("Consolas") };
        p3.SelectedDateChanged += (s, e) =>
        {
            if (p3.SelectedDate == null) return;
            int d = (p3.SelectedDate.Value - DateTime.Today).Days;
            daysBlock.Text = d >= 0 ? $"In {d} day{(d != 1 ? "s" : "")}" : $"{-d} day{(-d != 1 ? "s" : "")} ago";
        };
        root.Children.Add(daysBlock);
        Calc();
        return root;
    }

    private StackPanel BuildAgeCalcPanel()
    {
        var root = new StackPanel { Margin = new Thickness(12), Visibility = Visibility.Collapsed };
        root.Children.Add(SectionLabel("AGE CALCULATOR"));
        root.Children.Add(FieldLabel("Date of birth"));
        var dob = new DatePicker { SelectedDate = new DateTime(1990,1,1), Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(C(0x333333)), Margin = new Thickness(0,0,0,6) };
        ApplyDarkDatePickerStyle(dob);
        root.Children.Add(dob);
        root.Children.Add(FieldLabel("Calculate age on"));
        var on = new DatePicker { SelectedDate = DateTime.Today, Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(C(0x333333)), Margin = new Thickness(0,0,0,10) };
        ApplyDarkDatePickerStyle(on);
        root.Children.Add(on);

        var ageBlock = new TextBlock { Foreground = new SolidColorBrush(C(0x88ccff)), FontSize = 13, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap };
        root.Children.Add(ageBlock);

        void Calc()
        {
            if (dob.SelectedDate == null || on.SelectedDate == null) return;
            var d = dob.SelectedDate.Value; var o = on.SelectedDate.Value;
            if (d > o) { ageBlock.Text = "Birth date must be before target date."; return; }
            int yrs = o.Year - d.Year; if (o < d.AddYears(yrs)) yrs--;
            var af = d.AddYears(yrs); int mos = 0;
            while (af.AddMonths(1) <= o) { mos++; af = af.AddMonths(1); }
            int days  = (o - af).Days;
            int total = (int)(o - d).TotalDays;
            var nextBd = new DateTime(o.Year, d.Month, d.Day);
            if (nextBd <= o) nextBd = nextBd.AddYears(1);
            int toBd = (nextBd - o).Days;
            ageBlock.Text = $"{yrs} years, {mos} months, {days} days\n\n= {total:N0} days old\n= {(long)total * 24:N0} hours\n\nNext birthday in {toBd} day{(toBd != 1 ? "s" : "")}\n({nextBd:dd MMM yyyy})";
        }

        dob.SelectedDateChanged += (s, e) => Calc();
        on.SelectedDateChanged  += (s, e) => Calc();
        Calc();
        return root;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  NOTES WIDGET
    // ═══════════════════════════════════════════════════════════════════════════

    private void OpenNotesWindow()
    {
        var tabs = SettingsService.Current.WidgetNoteTabs;

        // Migrate legacy single note on first use
        if (tabs.Count == 0)
        {
            tabs["Main"] = SettingsService.Current.WidgetNotes;
            SettingsService.Current.WidgetNotes = "";
            SettingsService.Save();
        }

        if (string.IsNullOrEmpty(_currentNoteTab) || !tabs.ContainsKey(_currentNoteTab))
            _currentNoteTab = tabs.Keys.First();

        var win = new Window
        {
            Title = "Notes", Width = 560, Height = 480,
            Background = new SolidColorBrush(C(0x141414)),
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.CanResizeWithGrip,
            Owner = this, ShowInTaskbar = false, Topmost = true
        };
        win.Closing  += (s, e) => { win.Owner = null; };
        win.Closed   += (s, e) => Dispatcher.BeginInvoke(new Action(() => { try { if (WindowState != WindowState.Minimized) Activate(); } catch { } }));
        win.AllowDrop = true;   // required for OLE drop from Explorer into a ToolWindow
        // Forward window-level drag events to the RTB so dropping on the tab
        // bar or formatting bar still works.
        win.DragOver += (s, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ||
                        e.Data.GetDataPresent(DataFormats.UnicodeText) ||
                        e.Data.GetDataPresent("UniformResourceLocatorW")
                        ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };

        var outer = new DockPanel();

        // ── Tab bar ──────────────────────────────────────────────────────────
        var tabScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(C(0x0d0d0d))
        };
        var tabBar = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(C(0x0d0d0d)) };
        tabScroll.Content = tabBar;
        DockPanel.SetDock(tabScroll, Dock.Top);
        outer.Children.Add(tabScroll);

        // ── Formatting toolbar ───────────────────────────────────────────────
        var fmtBar = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(C(0x111111)) };
        DockPanel.SetDock(fmtBar, Dock.Top);
        outer.Children.Add(fmtBar);

        // ── Status bar ───────────────────────────────────────────────────────
        var statusTx = new TextBlock
        {
            Foreground = new SolidColorBrush(C(0x444444)), VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10, Margin = new Thickness(8, 2, 8, 2)
        };
        var statusBar = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(C(0x0d0d0d)) };
        statusBar.Children.Add(statusTx);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        outer.Children.Add(statusBar);

        // ── Rich text editor ─────────────────────────────────────────────────
        var rtb = new RichTextBox
        {
            Background  = new SolidColorBrush(C(0x141414)),
            Foreground  = new SolidColorBrush(C(0xdddddd)),
            CaretBrush  = Brushes.White,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true, AcceptsTab = true,
            FontFamily  = new FontFamily("Segoe UI"), FontSize = 13,
            Margin      = new Thickness(4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowDrop   = true,
        };
        rtb.SpellCheck.IsEnabled = true;
        outer.Children.Add(rtb);

        // ── One-click hyperlink open (RichTextBox normally eats the click
        //    to position the caret, requiring Ctrl+Click to navigate) ─────────
        rtb.PreviewMouseLeftButtonDown += (s, e) =>
        {
            var pos = rtb.GetPositionFromPoint(e.GetPosition(rtb), true);
            if (pos == null) return;
            var el = pos.Parent as TextElement;
            while (el != null && el is not Hyperlink) el = el.Parent as TextElement;
            if (el is Hyperlink hl && hl.NavigateUri != null)
            {
                try { Process.Start(new ProcessStartInfo(hl.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                e.Handled = true;
            }
        };

        // ── Paste image support ───────────────────────────────────────────────
        DataObject.AddPastingHandler(rtb, (s, e) =>
        {
            if (e.DataObject.GetDataPresent(DataFormats.Bitmap))
            {
                e.CancelCommand();
                var bmpSrc = e.DataObject.GetData(DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource;
                bmpSrc ??= System.Windows.Clipboard.GetImage();
                if (bmpSrc == null) return;
                
                var ms = new System.IO.MemoryStream();
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmpSrc));
                encoder.Save(ms);
                ms.Seek(0, System.IO.SeekOrigin.Begin);
                var wpfBmp = new System.Windows.Media.Imaging.BitmapImage();
                wpfBmp.BeginInit();
                wpfBmp.StreamSource = ms;
                wpfBmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                wpfBmp.EndInit();
                var img = new Image { Source = wpfBmp, MaxWidth = 420, Margin = new Thickness(0, 4, 0, 4), Stretch = Stretch.Uniform };
                var ic = new InlineUIContainer(img, rtb.CaretPosition);
                rtb.CaretPosition = ic.ElementEnd;
            }
        });

        win.Content = outer;

        // ── Save / load helpers ───────────────────────────────────────────────
        // Notes are stored as raw XamlPackage bytes on disk (Notes\<name>.bin)
        // rather than as base64 blobs in settings — this removes the practical
        // size limit and keeps settings.json small.
        static string NoteFilePath(string name)
        {
            string dir  = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Notes");
            System.IO.Directory.CreateDirectory(dir);
            string safe = string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars()));
            return System.IO.Path.Combine(dir, safe + ".bin");
        }

        void SaveCurrentTab()
        {
            if (string.IsNullOrEmpty(_currentNoteTab)) return;
            try
            {
                using var ms = new System.IO.MemoryStream();
                new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd)
                    .Save(ms, DataFormats.XamlPackage);
                System.IO.File.WriteAllBytes(NoteFilePath(_currentNoteTab), ms.ToArray());

                // Clear any legacy settings blob so the JSON doesn't grow
                if (tabs.ContainsKey(_currentNoteTab)) tabs[_currentNoteTab] = "";

                string plain = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text.Trim();
                SettingsService.Current.WidgetNotes = "Notes";
                SettingsService.Save();
            }
            catch { }
        }

        void LoadTab(string name)
        {
            if (!string.IsNullOrEmpty(_currentNoteTab) && _currentNoteTab != name)
                SaveCurrentTab();
            _currentNoteTab = name;
            rtb.Document = new FlowDocument();
            rtb.Document.Foreground = new SolidColorBrush(C(0xdddddd));

            string filePath = NoteFilePath(name);

            if (System.IO.File.Exists(filePath))
            {
                // Fast path — file-backed note
                try
                {
                    using var ms = new System.IO.MemoryStream(System.IO.File.ReadAllBytes(filePath));
                    new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd)
                        .Load(ms, DataFormats.XamlPackage);
                }
                catch { }
            }
            else if (tabs.TryGetValue(name, out var legacy) && !string.IsNullOrEmpty(legacy))
            {
                // One-time migration: settings blob → file on disk
                try
                {
                    using var ms = new System.IO.MemoryStream(Convert.FromBase64String(legacy));
                    new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd)
                        .Load(ms, DataFormats.XamlPackage);
                }
                catch
                {
                    rtb.Document.Blocks.Clear();
                    rtb.Document.Blocks.Add(new Paragraph(new Run(legacy)));
                }
                // Persist to file immediately so we don't re-read the blob next time
                SaveCurrentTab();
            }
            AutoLinkifyNotes(rtb);
            rtb.Focus();
        }

        // ── Auto-detect URLs and make them clickable ────────────────────────────
        static void CollectPlainRuns(InlineCollection inlines, List<(Run run, InlineCollection parent)> runs)
        {
            foreach (var inline in inlines.ToList())
            {
                if (inline is Run r) runs.Add((r, inlines));
                else if (inline is Span span) CollectPlainRuns(span.Inlines, runs);
            }
        }

        void AutoLinkifyNotes(RichTextBox targetRtb)
        {
            try
            {
                var urlRegex = new Regex(@"(https?://[^\s]+|www\.[^\s]+)", RegexOptions.Compiled);
                var runs = new List<(Run run, InlineCollection parent)>();
                foreach (var block in targetRtb.Document.Blocks.ToList())
                    if (block is Paragraph p) CollectPlainRuns(p.Inlines, runs);

                foreach (var (run, parent) in runs)
                {
                    if (run.Parent is Hyperlink) continue;
                    string text = run.Text;
                    if (string.IsNullOrEmpty(text)) continue;
                    var matches = urlRegex.Matches(text);
                    if (matches.Count == 0) continue;

                    int last = 0;
                    var newInlines = new List<Inline>();
                    foreach (Match m in matches)
                    {
                        if (m.Index > last)
                            newInlines.Add(new Run(text.Substring(last, m.Index - last)));
                        string url = m.Value.TrimEnd('.', ',', ')', ']');
                        string href = url.StartsWith("www.") ? "http://" + url : url;
                        Hyperlink link;
                        try { link = new Hyperlink(new Run(url)) { NavigateUri = new Uri(href) }; }
                        catch { newInlines.Add(new Run(m.Value)); last = m.Index + m.Length; continue; }
                        link.Foreground = new SolidColorBrush(C(0x66aaff));
                        link.Cursor = Cursors.Hand;
                        link.RequestNavigate += (s, e) =>
                        {
                            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                            e.Handled = true;
                        };
                        newInlines.Add(link);
                        last = m.Index + m.Length;
                    }
                    if (last < text.Length)
                        newInlines.Add(new Run(text.Substring(last)));

                    foreach (var ni in newInlines) parent.InsertBefore(run, ni);
                    parent.Remove(run);
                }
            }
            catch { }
        }
        // (end AutoLinkifyNotes)

        // ── Tab bar builder ───────────────────────────────────────────────────
        void RebuildTabBar()
        {
            tabBar.Children.Clear();
            foreach (var name in tabs.Keys.ToList())
            {
                var capName  = name;
                bool isCur   = capName == _currentNoteTab;
                var tabPanel = new StackPanel { Orientation = Orientation.Horizontal };

                var tabBtn = new Button
                {
                    Content = capName, FontSize = 11, Height = 28,
                    Padding = new Thickness(10, 0, 5, 0),
                    Background  = new SolidColorBrush(isCur ? C(0x1a1a2e) : Brushes.Transparent.Color),
                    Foreground  = new SolidColorBrush(isCur ? C(0x88ccff) : C(0x666666)),
                    BorderBrush = new SolidColorBrush(isCur ? C(0x2e6aa0) : C(0x000000)),
                    BorderThickness = isCur ? new Thickness(0, 0, 0, 2) : new Thickness(0),
                    Cursor = Cursors.Hand
                };
                tabBtn.Click += (s, e) => { LoadTab(capName); RebuildTabBar(); };

                var closeBtn = new Button
                {
                    Content = "✕", FontSize = 9, Width = 18, Height = 18, Padding = new Thickness(0),
                    Background  = Brushes.Transparent, Foreground  = new SolidColorBrush(C(0x555555)),
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                    Margin  = new Thickness(0, 5, 3, 5), ToolTip = "Close tab"
                };
                closeBtn.Click += (s, e) =>
                {
                    if (tabs.Count <= 1) return;
                    tabs.Remove(capName);
                    SettingsService.Save();
                    if (_currentNoteTab == capName) _currentNoteTab = tabs.Keys.First();
                    LoadTab(_currentNoteTab);
                    RebuildTabBar();
                };

                tabPanel.Children.Add(tabBtn);
                tabPanel.Children.Add(closeBtn);
                tabBar.Children.Add(tabPanel);
            }

            var addBtn = new Button
            {
                Content = "+", FontSize = 15, Width = 28, Height = 28,
                Background = Brushes.Transparent, Foreground = new SolidColorBrush(C(0x444444)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 0, 0), ToolTip = "New note"
            };
            addBtn.Click += (s, e) =>
            {
                SaveCurrentTab();
                string n = "Note"; int i = 2;
                while (tabs.ContainsKey(n)) n = $"Note {i++}";
                tabs[n] = "";
                _currentNoteTab = n;
                SettingsService.Save();
                LoadTab(n);
                RebuildTabBar();
            };
            tabBar.Children.Add(addBtn);
        }

        // ── Formatting toolbar ────────────────────────────────────────────────
        Button FmtBtn(string lbl, string tip, int w = 28) => new Button
        {
            Content = lbl, Width = w, Height = 24, FontSize = 11, ToolTip = tip,
            Margin = new Thickness(2, 2, 0, 2),
            Background  = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x333333)), BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        var btnBold   = FmtBtn("B",  "Bold (Ctrl+B)");   btnBold.FontWeight   = FontWeights.Bold;
        var btnItalic = FmtBtn("I",  "Italic (Ctrl+I)");  btnItalic.FontStyle  = FontStyles.Italic;
        var btnULine  = FmtBtn("U",  "Underline (Ctrl+U)");
        var btnH1     = FmtBtn("H1", "Heading 1", 30);
        var btnH2     = FmtBtn("H2", "Heading 2", 30);
        var btnBullet = FmtBtn("• ≡", "Bullet list", 34);
        var btnNum    = FmtBtn("1 ≡", "Numbered list", 34);
        var btnCheck  = FmtBtn("☑", "Insert checkbox", 26);
        var btnTable  = FmtBtn("⊞", "Insert table", 26);
        var btnColor  = FmtBtn("A", "Cycle text colour", 26);
        btnColor.Foreground = new SolidColorBrush(C(0x88ccff));

        var fmtSep   = new Border { Width = 1, Margin = new Thickness(4, 3, 4, 3), Background = new SolidColorBrush(C(0x2a2a2a)) };
        var btnSave  = BarButton("💾 Save", C(0x1a3a1a), C(0x2a5a2a));
        var btnSync  = BarButton("☁ Sync", C(0x1a3454), C(0x2e6aa0));

        btnBold.Click   += (s, e) => { EditingCommands.ToggleBold.Execute(null, rtb);      rtb.Focus(); };
        btnItalic.Click += (s, e) => { EditingCommands.ToggleItalic.Execute(null, rtb);    rtb.Focus(); };
        btnULine.Click  += (s, e) => { EditingCommands.ToggleUnderline.Execute(null, rtb); rtb.Focus(); };
        btnBullet.Click += (s, e) => { EditingCommands.ToggleBullets.Execute(null, rtb);   rtb.Focus(); };
        btnNum.Click    += (s, e) => { EditingCommands.ToggleNumbering.Execute(null, rtb); rtb.Focus(); };
        btnCheck.Click  += (s, e) =>
        {
            var cb = new CheckBox { Margin = new Thickness(0, 0, 4, -2), VerticalAlignment = VerticalAlignment.Center };
            var ic = new InlineUIContainer(cb, rtb.CaretPosition);
            rtb.CaretPosition = ic.ElementEnd;
            rtb.CaretPosition.InsertTextInRun(" ");
            rtb.Focus();
        };
        btnTable.Click  += (s, e) => OpenInsertTableDialog(rtb);

        btnH1.Click += (s, e) =>
        {
            rtb.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,   20.0);
            rtb.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            rtb.Focus();
        };
        btnH2.Click += (s, e) =>
        {
            rtb.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,   15.0);
            rtb.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
            rtb.Focus();
        };

        int    accentIdx = 0;
        Color[] accents  = { C(0xffffff), C(0x88ccff), C(0x88ff88), C(0xffdd66), C(0xff8888), C(0xcc88ff) };
        btnColor.Click += (s, e) =>
        {
            accentIdx = (accentIdx + 1) % accents.Length;
            var brush = new SolidColorBrush(accents[accentIdx]);
            rtb.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            btnColor.Foreground = brush;
            rtb.Focus();
        };

        btnSave.Click += (s, e) => { SaveCurrentTab(); statusTx.Text = "Saved ✓"; };
        btnSync.Click += (s, e) => OpenNotesSyncDialog(rtb, statusTx);

        foreach (UIElement el in new UIElement[] { btnBold, btnItalic, btnULine, fmtSep, btnH1, btnH2, btnBullet, btnNum, btnCheck, btnTable, btnColor, btnSave, btnSync })
            fmtBar.Children.Add(el);

        // ── Drag & drop ───────────────────────────────────────────────────────
        rtb.DragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        rtb.Drop += (s, e) =>
        {
            var pos = rtb.GetPositionFromPoint(e.GetPosition(rtb), true);
            if (pos != null) rtb.CaretPosition = pos;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var f in files)
                {
                    string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    bool isImg = ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
                    if (isImg)
                    {
                        try
                        {
                            var bmp = new System.Windows.Media.Imaging.BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(f); bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bmp.DecodePixelWidth = 420; bmp.EndInit();
                            var img = new Image { Source = bmp, MaxWidth = 420, Margin = new Thickness(0, 4, 0, 4), Stretch = Stretch.Uniform };
                            var ic  = new InlineUIContainer(img, rtb.CaretPosition);
                            rtb.CaretPosition = ic.ElementEnd;
                        }
                        catch { rtb.CaretPosition.InsertTextInRun($"🖼 {f}\n"); }
                    }
                    else
                    {
                        string icon = ext is ".pdf" ? "📄" : ext is ".mp3" or ".wav" or ".flac" ? "🎵" :
                                      ext is ".mp4" or ".mkv" or ".avi" ? "🎬" : "📎";
                        rtb.CaretPosition.InsertTextInRun($"{icon} {System.IO.Path.GetFileName(f)}: {f}\n");
                    }
                }
                statusTx.Text = $"Dropped {files.Length} file(s) ✓";
            }
            else if (e.Data.GetDataPresent("UniformResourceLocatorW"))
            {
                string url = ((string)e.Data.GetData("UniformResourceLocatorW")).Trim('\0');
                rtb.CaretPosition.InsertTextInRun($"🔗 {url}\n");
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                rtb.CaretPosition.InsertTextInRun((string)e.Data.GetData(DataFormats.UnicodeText));
            }
            SaveCurrentTab();
            e.Handled = true;
        };

        // ── Auto-save (2 s debounce) ──────────────────────────────────────────
        var autoSave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        autoSave.Tick     += (s, e) => { autoSave.Stop(); SaveCurrentTab(); AutoLinkifyNotes(rtb); statusTx.Text = "Auto-saved ✓"; };
        rtb.TextChanged   += (s, e) => { autoSave.Stop(); autoSave.Start(); statusTx.Text = ""; };
        win.Closed        += (s, e) => { autoSave.Stop(); SaveCurrentTab(); };

        // ── Init ──────────────────────────────────────────────────────────────
        RebuildTabBar();
        LoadTab(_currentNoteTab);
        win.Show();
    }

    private void OpenInsertTableDialog(RichTextBox rtb)
    {
        var win = MakeToolWindow("Insert Table", 220);
        var root = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        root.Children.Add(SectionLabel("TABLE SIZE"));

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 10) };
        var (rowsPanel, rowsBox) = Spinner("Rows", 3);
        var (colsPanel, colsBox) = Spinner("Cols", 3);
        row.Children.Add(rowsPanel);
        row.Children.Add(colsPanel);
        root.Children.Add(row);

        var insertBtn = AccentButton("⊞  Insert", C(0x1a3454), C(0x2e6aa0), 180);
        insertBtn.HorizontalAlignment = HorizontalAlignment.Center;
        insertBtn.Click += (s, e) =>
        {
            if (!int.TryParse(rowsBox.Text, out int rows) || rows < 1) rows = 1;
            if (!int.TryParse(colsBox.Text, out int cols) || cols < 1) cols = 1;
            rows = Math.Min(rows, 20);
            cols = Math.Min(cols, 10);

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 4) };
            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());
            var rg = new TableRowGroup();
            for (int r = 0; r < rows; r++)
            {
                var tr = new TableRow();
                for (int c = 0; c < cols; c++)
                {
                    tr.Cells.Add(new TableCell(new Paragraph(new Run("")))
                    {
                        BorderBrush = new SolidColorBrush(C(0x333333)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(4, 2, 4, 2)
                    });
                }
                rg.Rows.Add(tr);
            }
            table.RowGroups.Add(rg);

            var caretPara = rtb.CaretPosition.Paragraph;
            if (caretPara != null)
                rtb.Document.Blocks.InsertAfter(caretPara, table);
            else
                rtb.Document.Blocks.Add(table);

            var afterPara = new Paragraph(new Run(""));
            rtb.Document.Blocks.InsertAfter(table, afterPara);
            rtb.CaretPosition = afterPara.ContentStart;
            rtb.Focus();
            win.Close();
        };
        root.Children.Add(insertBtn);

        win.Content = root;
        win.Show();
    }

    private static string NotesSummaryTitle(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return "";

        // ── Step 1: prefer a heading-style first line (short, no sentence end) ──
        string firstLine = plain.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        if (firstLine.Length <= 32 && !firstLine.EndsWith('.') && !firstLine.EndsWith(','))
            return firstLine.Length > 18 ? firstLine[..15] + "…" : firstLine;

        // ── Step 2: keyword extraction — pick most-frequent non-stopword ────────
        var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a","an","the","and","or","but","is","are","was","were","be","been","being",
            "have","has","had","do","does","did","will","would","could","should","may",
            "might","shall","can","to","of","in","on","at","for","with","by","from",
            "up","about","into","through","that","this","these","those","it","its",
            "i","you","he","she","we","they","my","your","his","her","our","their",
            "not","no","so","if","as","than","then","when","what","how","all","more"
        };

        var words = System.Text.RegularExpressions.Regex
            .Matches(plain.ToLowerInvariant(), @"[a-zA-ZÀ-ž]{4,}")
            .Select(m => m.Value)
            .Where(w => !stopwords.Contains(w));

        var topWord = words
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Select(g => char.ToUpper(g.Key[0]) + g.Key[1..])
            .FirstOrDefault();

        if (topWord != null) return topWord;

        // ── Step 3: fallback — first words of first line ─────────────────────
        return firstLine.Length > 18 ? firstLine[..15] + "…" : firstLine;
    }

    private void OpenNotesSyncDialog(RichTextBox notesTb, TextBlock statusLabel)
    {
        string GetNoteText() => new TextRange(notesTb.Document.ContentStart, notesTb.Document.ContentEnd).Text;
        void   SetNoteText(string t) { notesTb.Document.Blocks.Clear(); notesTb.Document.Blocks.Add(new Paragraph(new Run(t))); }
        var dlg  = MakeToolWindow("Sync Notes", 340);
        var root = new StackPanel { Margin = new Thickness(14) };

        var accounts = SettingsService.Current.SyncAccounts;

        root.Children.Add(SectionLabel("PUSH NOTES TO ACCOUNT"));

        if (accounts.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "No accounts connected.\nGo to Calendar → Accounts to add Google or Microsoft.",
                Foreground = new SolidColorBrush(C(0x555555)),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });
        }
        else
        {
            foreach (var acc in accounts)
            {
                string icon    = acc.Provider == "Google" ? "🟢" : "🔵";
                string service = acc.Provider == "Google" ? "Google Keep" : "OneNote";
                var pushBtn    = SyncButton($"{icon}  {acc.DisplayName}  →  {service}", C(0x1a2a1a), C(0x2a4a2a));
                var captureAcc = acc;
                pushBtn.Click += async (s, e) =>
                {
                    statusLabel.Text = $"Pushing to {service}…"; dlg.Close();
                    try
                    {
                        if (captureAcc.Provider == "Google")
                            await SyncNotesGoogleAsync(new TextRange(notesTb.Document.ContentStart, notesTb.Document.ContentEnd).Text, captureAcc);
                        else
                            await SyncNotesMicrosoftAsync(new TextRange(notesTb.Document.ContentStart, notesTb.Document.ContentEnd).Text, captureAcc);
                        statusLabel.Text = $"{service} sync done ✓";
                    }
                    catch (Exception ex) { statusLabel.Text = $"Sync error: {ex.Message}"; }
                };
                root.Children.Add(pushBtn);
            }
        }

        root.Children.Add(new Separator { Background = new SolidColorBrush(C(0x2a2a2a)), Margin = new Thickness(0, 10, 0, 10) });
        root.Children.Add(SectionLabel("IMPORT NOTES FROM ACCOUNT"));

        foreach (var acc in accounts)
        {
            string icon    = acc.Provider == "Google" ? "🟢" : "🔵";
            var fetchBtn   = SyncButton($"{icon}  Import from {acc.Email}", C(0x1a2a3a), C(0x2a4a6a));
            var captureAcc = acc;
            fetchBtn.Click += async (s, e) =>
            {
                statusLabel.Text = "Fetching notes…"; dlg.Close();
                try
                {
                    var sb = new StringBuilder(GetNoteText());
                    if (sb.Length > 0) sb.AppendLine("\n─────────────────");

                    if (captureAcc.Provider == "Google")
                    {
                        var notes = await AccountSyncService.FetchGoogleTasksAsync(
                            captureAcc,
                            AccountSyncService.AppGoogleClientId,
                            AccountSyncService.AppGoogleClientSecret);
                        foreach (var n in notes) { sb.AppendLine($"## {n.Title}"); if (!string.IsNullOrEmpty(n.Body)) sb.AppendLine(n.Body); sb.AppendLine(); }
                        statusLabel.Text = $"Imported {notes.Count} notes ✓";
                    }
                    else
                    {
                        var notes = await AccountSyncService.FetchMicrosoftNotesAsync(
                            captureAcc, SettingsService.Current.MicrosoftClientId);
                        foreach (var n in notes) { sb.AppendLine($"## {n.Title}"); sb.AppendLine($"Updated: {n.Updated:dd MMM yyyy HH:mm}"); sb.AppendLine(); }
                        statusLabel.Text = $"Imported {notes.Count} notes ✓";
                    }

                    SetNoteText(sb.ToString());
                    SettingsService.Current.WidgetNotes = GetNoteText();
                    SettingsService.Save();
                }
                catch (Exception ex) { statusLabel.Text = $"Fetch error: {ex.Message}"; }
            };
            root.Children.Add(fetchBtn);
        }

        root.Children.Add(new TextBlock
        {
            Text = "Apple Notes has no public API and cannot be synced.",
            Foreground = new SolidColorBrush(C(0x777777)), FontSize = 10,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        });

        dlg.Content = root;
        dlg.Show();
    }

    private async Task SyncNotesGoogleAsync(string content, SyncAccount account)
    {
        // Google never published a public Keep API — push to Google Tasks instead,
        // which is real, documented, and supports a title + notes body.
        await AccountSyncService.PushGoogleTaskAsync(
            account, AccountSyncService.AppGoogleClientId, AccountSyncService.AppGoogleClientSecret,
            $"Horizon Notes – {DateTime.Now:dd MMM yyyy HH:mm}", content);
    }

    private async Task SyncNotesMicrosoftAsync(string content, SyncAccount account)
    {
        if (account.IsExpired)
            await AccountSyncService.RefreshMicrosoftTokenAsync(
                account, SettingsService.Current.MicrosoftClientId);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);

        string htmlBody = System.Web.HttpUtility.HtmlEncode(content).Replace("\n", "<br/>");
        string html = $"<!DOCTYPE html><html><head><title>Horizon Notes – {DateTime.Now:dd MMM yyyy HH:mm}</title></head><body><p>{htmlBody}</p></body></html>";
        var resp = await http.PostAsync("https://graph.microsoft.com/v1.0/me/onenote/pages",
            new StringContent(html, Encoding.UTF8, "text/html"));

        if (!resp.IsSuccessStatusCode)
        {
            string err = await resp.Content.ReadAsStringAsync();
            throw new Exception($"OneNote API {resp.StatusCode}: {err[..Math.Min(err.Length, 120)]}");
        }
    }



	// ── In-app account login (uses browser's existing sessions) ──────────────
    private async Task<SyncAccount?> AddAccountViaWebViewAsync(string provider)
    {
        bool isGoogle     = provider == "Google";
        string clientId   = isGoogle ? AccountSyncService.AppGoogleClientId : SettingsService.Current.MicrosoftClientId;
        string clientSecret = isGoogle ? AccountSyncService.AppGoogleClientSecret : "";

        if (string.IsNullOrEmpty(clientId))
            return null;

        int port;
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();

        string redirectUri = $"http://localhost:{port}/";
        var (verifier, challenge) = AccountSyncService.GeneratePkce();
        string state = AccountSyncService.GenerateState();

        string scope = isGoogle
            ? Uri.EscapeDataString("openid email profile https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/tasks")
            : Uri.EscapeDataString("openid email profile offline_access https://graph.microsoft.com/Calendars.ReadWrite https://graph.microsoft.com/Notes.ReadWrite");

        string authUrl = isGoogle
            ? $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={scope}&code_challenge={challenge}&code_challenge_method=S256&state={state}&access_type=offline&prompt=select_account"
            : $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={scope}&code_challenge={challenge}&code_challenge_method=S256&state={state}&prompt=select_account";

        var tcs    = new TaskCompletionSource<string?>();
        var popup  = new Window
        {
            Title = $"Sign in with {provider}", Width = 480, Height = 640,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.CanResize,
            Owner = this, ShowInTaskbar = false
        };
        popup.Closed += (_, _) => tcs.TrySetResult(null);

        var wv = new Microsoft.Web.WebView2.Wpf.WebView2();
        popup.Content = wv;
        popup.Show();

        try
        {
            // Dedicated WebView2 profile for OAuth logins — deliberately NOT the
            // main browsing environment. Horizon's stealth/anti-detect fingerprint
            // spoofing must never be applied to a real Google/Microsoft sign-in
            // page (it can get the login flagged as suspicious), and this keeps
            // your signed-in account session separate from browsing state.
            string oauthProfileDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon", "OAuthProfile");
            System.IO.Directory.CreateDirectory(oauthProfileDir);
            var oauthEnv = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                userDataFolder: oauthProfileDir);
            await wv.EnsureCoreWebView2Async(oauthEnv);
        }
        catch (Exception ex)
        {
            popup.Close();
            throw new Exception($"Could not start sign-in browser: {ex.Message}", ex);
        }

        wv.CoreWebView2.NavigationStarting += (s, e) =>
        {
            if (!e.Uri.StartsWith($"http://localhost:{port}/")) return;
            e.Cancel = true;
            var parts = e.Uri.Contains("?") ? e.Uri.Split('?')[1] : "";
            var qs = parts.Split('&')
                .Select(p => p.Split('='))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
            qs.TryGetValue("code",  out var code2);
            qs.TryGetValue("state", out var retState);
            tcs.TrySetResult(retState == state ? code2 : null);
            Dispatcher.Invoke(() => popup.Close());
        };

        wv.CoreWebView2.Navigate(authUrl);
        string? code = await tcs.Task;
        if (string.IsNullOrEmpty(code)) return null;

        return isGoogle
            ? await AccountSyncService.ExchangeGoogleCodeAsync(code, verifier, redirectUri, clientId, clientSecret)
            : await AccountSyncService.ExchangeMsCodeAsync(code, verifier, redirectUri, clientId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  NAVIGATION WIDGET
    // ═══════════════════════════════════════════════════════════════════════════

    private void OpenNavigationWindow()
    {
        var customDomains = SettingsService.Current.NavigationCustomDomains;

        bool IsNavUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string l = url.ToLowerInvariant();
            string[] builtIn =
            {
                "google.com/maps", "maps.google", "bing.com/maps", "waze.com",
                "openstreetmap.org", "maps.apple.com", "maps.me", "here.com/maps",
                "yandex.com/maps", "2gis.com"
            };
            if (builtIn.Any(d => l.Contains(d))) return true;
            return customDomains.Any(d => !string.IsNullOrWhiteSpace(d) && l.Contains(d.ToLowerInvariant()));
        }

        var win = MakeToolWindow("Navigation", 370);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 540
        };
        var root = new StackPanel { Margin = new Thickness(12) };
        scroll.Content = root;

        var navTabs = _allTabs.Where(t => IsNavUrl(t.Url)).ToList();
        if (navTabs.Count > 0)
        {
            root.Children.Add(SectionLabel("ACTIVE NAV TABS"));
            foreach (var tab in navTabs)
            {
                string raw = tab.Title ?? tab.Url ?? "";
                string lbl = raw.Length > 38 ? raw[..35] + "…" : raw;
                var btn = MenuButton("🗺  " + lbl, false);
                var capture = tab;
                btn.Click += (s, e) =>
                {
                    if (Tabs.Contains(capture))              ListTabs.SelectedItem         = capture;
                    else if (OverflowTabs.Contains(capture)) ListOverflowTabs.SelectedItem = capture;
                    win.Close();
                };
                root.Children.Add(btn);
            }
            root.Children.Add(new Separator { Background = new SolidColorBrush(C(0x2a2a2a)), Margin = new Thickness(0, 10, 0, 10) });
        }

        root.Children.Add(SectionLabel("OPEN NAVIGATION"));
        root.Children.Add(FieldLabel("Address / coordinates"));

        var addrBox = new TextBox
        {
            Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x444444)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4), FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(addrBox);
        root.Children.Add(FieldLabel("Provider"));

        string[] providers = { "Google Maps", "Bing Maps", "OpenStreetMap", "Waze", "Apple Maps" };
        var providerBox = new ComboBox
        {
            ItemsSource = providers,
            SelectedItem = providers.Contains(SettingsService.Current.NavigationProvider)
                           ? SettingsService.Current.NavigationProvider : providers[0],
            Background = new SolidColorBrush(C(0x222222)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x333333)), Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 10),
            ItemContainerStyle = DarkComboItemStyle()
        };
        root.Children.Add(providerBox);

        providerBox.SelectionChanged += (s, e) =>
        {
            if (providerBox.SelectedItem is string p)
            {
                SettingsService.Current.NavigationProvider = p;
                SettingsService.Save();
            }
        };

        var openBtn = AccentButton("🧭  Open", C(0x1a3454), C(0x2e6aa0), 110);
        openBtn.HorizontalAlignment = HorizontalAlignment.Left;
        openBtn.Click += (s, e) =>
        {
            string addr = addrBox.Text.Trim();
            if (string.IsNullOrEmpty(addr)) return;
            string enc = Uri.EscapeDataString(addr);
            string url = (providerBox.SelectedItem as string ?? "Google Maps") switch
            {
                "Google Maps"   => $"https://www.google.com/maps/search/{enc}",
                "Bing Maps"     => $"https://www.bing.com/maps?q={enc}",
                "OpenStreetMap" => $"https://www.openstreetmap.org/search?query={enc}",
                "Waze"          => $"https://www.waze.com/live-map/directions?to={enc}",
                "Apple Maps"    => $"https://maps.apple.com/?q={enc}",
                _               => $"https://www.google.com/maps/search/{enc}"
            };
            CreateNewTab(url);
            _ = StartNavigation(addr);
            win.Close();
        };

        addrBox.KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Return)
                openBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };

        root.Children.Add(openBtn);

        root.Children.Add(new Separator { Background = new SolidColorBrush(C(0x2a2a2a)), Margin = new Thickness(0, 14, 0, 10) });
        root.Children.Add(SectionLabel("CUSTOM NAV DOMAINS"));
        root.Children.Add(FieldLabel("Domains scanned when detecting active nav tabs"));

        var domainsPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        void RebuildDomainsList()
        {
            domainsPanel.Children.Clear();
            foreach (var dom in customDomains.ToList())
            {
                var capDom = dom;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var domLbl = new TextBlock
                {
                    Text = capDom, Foreground = new SolidColorBrush(C(0xcccccc)),
                    FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                var removeBtn = new Button
                {
                    Content = "✕", Width = 22, Height = 22, FontSize = 9, Padding = new Thickness(0),
                    Background = new SolidColorBrush(C(0x2a1a1a)), Foreground = new SolidColorBrush(C(0x996666)),
                    BorderBrush = new SolidColorBrush(C(0x442222)), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                removeBtn.Click += (s, e) =>
                {
                    customDomains.Remove(capDom);
                    SettingsService.Save();
                    RebuildDomainsList();
                };

                Grid.SetColumn(domLbl, 0);
                Grid.SetColumn(removeBtn, 1);
                row.Children.Add(domLbl);
                row.Children.Add(removeBtn);
                domainsPanel.Children.Add(row);
            }
        }

        RebuildDomainsList();
        root.Children.Add(domainsPanel);

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var newDomainBox = new TextBox
        {
            Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x444444)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4), FontSize = 12,
            Width = 224, Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Enter a domain fragment, e.g. maps.mysite.com"
        };
        var addDomainBtn = AccentButton("+ Add", C(0x1a2a1a), C(0x2a5a2a), 62);

        void DoAdd()
        {
            string d = newDomainBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(d) || customDomains.Contains(d)) return;
            customDomains.Add(d);
            SettingsService.Save();
            newDomainBox.Text = "";
            RebuildDomainsList();
        }

        addDomainBtn.Click  += (s, e) => DoAdd();
        newDomainBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Return) DoAdd(); };

        addRow.Children.Add(newDomainBox);
        addRow.Children.Add(addDomainBtn);
        root.Children.Add(addRow);

        win.Content = scroll;
        win.Show();
        addrBox.Focus();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  NAVIGATION LIVE ENGINE
    // ═══════════════════════════════════════════════════════════════════════════

    internal string GetNavWidgetText()
    {
        if (!_navActive) return "🧭  Navigation";
        string arrow = BearingToArrow(_navBearingDeg);
        if (_navEstimatedDuration > TimeSpan.Zero)
        {
            var elapsed   = DateTime.Now - _navStartedAt;
            var remaining = _navEstimatedDuration - elapsed;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            return $"{arrow}  {(int)remaining.TotalMinutes} min";
        }
        var el = DateTime.Now - _navStartedAt;
        return $"{arrow}  {(int)el.TotalMinutes:D2}:{el.Seconds:D2}";
    }

    private static string BearingToArrow(double deg)
    {
        double d = ((deg % 360) + 360) % 360;
        if (d < 22.5)  return "↑";
        if (d < 67.5)  return "↗";
        if (d < 112.5) return "→";
        if (d < 157.5) return "↘";
        if (d < 202.5) return "↓";
        if (d < 247.5) return "↙";
        if (d < 292.5) return "←";
        if (d < 337.5) return "↖";
        return "↑";
    }

    private async Task<(double lat, double lon, bool ok)> GetLocationAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("HorizonBrowser/1.0");
            string raw = await http.GetStringAsync("http://ip-api.com/json");
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var st) && st.GetString() == "success")
                return (root.GetProperty("lat").GetDouble(), root.GetProperty("lon").GetDouble(), true);
        }
        catch { }
        return (0, 0, false);
    }

    private async Task<(double lat, double lon, bool ok)> GeocodeAddressAsync(string address)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("HorizonBrowser/1.0");
            string enc  = Uri.EscapeDataString(address);
            string raw  = await http.GetStringAsync(
                $"https://nominatim.openstreetmap.org/search?q={enc}&format=json&limit=1");
            using var doc = JsonDocument.Parse(raw);
            var arr = doc.RootElement;
            if (arr.GetArrayLength() > 0)
            {
                var first = arr[0];
                double lat = double.Parse(first.GetProperty("lat").GetString() ?? "0", CultureInfo.InvariantCulture);
                double lon = double.Parse(first.GetProperty("lon").GetString() ?? "0", CultureInfo.InvariantCulture);
                return (lat, lon, true);
            }
        }
        catch { }
        return (0, 0, false);
    }

    private async Task<(TimeSpan duration, double distKm, bool ok)> GetRouteAsync(
        double srcLat, double srcLon, double dstLat, double dstLon)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("HorizonBrowser/1.0");
            string url = "https://router.project-osrm.org/route/v1/driving/" +
                         $"{srcLon.ToString(CultureInfo.InvariantCulture)},{srcLat.ToString(CultureInfo.InvariantCulture)};" +
                         $"{dstLon.ToString(CultureInfo.InvariantCulture)},{dstLat.ToString(CultureInfo.InvariantCulture)}?overview=false";
            string raw = await http.GetStringAsync(url);
            using var doc  = JsonDocument.Parse(raw);
            var route = doc.RootElement.GetProperty("routes")[0];
            double sec = route.GetProperty("duration").GetDouble();
            double m   = route.GetProperty("distance").GetDouble();
            return (TimeSpan.FromSeconds(sec), m / 1000.0, true);
        }
        catch { }
        return (TimeSpan.Zero, 0, false);
    }

    private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double dLon  = (lon2 - lon1) * Math.PI / 180.0;
        double rLat1 = lat1 * Math.PI / 180.0;
        double rLat2 = lat2 * Math.PI / 180.0;
        double x = Math.Cos(rLat2) * Math.Sin(dLon);
        double y = Math.Cos(rLat1) * Math.Sin(rLat2) - Math.Sin(rLat1) * Math.Cos(rLat2) * Math.Cos(dLon);
        return ((Math.Atan2(x, y) * 180.0 / Math.PI) + 360) % 360;
    }

    private async Task StartNavigation(string destination)
    {
        _navDestinationLabel  = destination;
        _navStartedAt         = DateTime.Now;
        _navActive            = true;
        _navEstimatedDuration = TimeSpan.Zero;
        _navBearingDeg        = 0;
        _navTotalDistanceKm   = 0;
        Dispatcher.Invoke(RefreshWidgetDisplay);

        var (uLat, uLon, uOk) = await GetLocationAsync();
        if (uOk) { _navUserLat = uLat; _navUserLon = uLon; }

        var (dLat, dLon, dOk) = await GeocodeAddressAsync(destination);
        if (dOk)
        {
            _navDestLat    = dLat;
            _navDestLon    = dLon;
            _navBearingDeg = CalculateBearing(_navUserLat, _navUserLon, _navDestLat, _navDestLon);
        }

        if (uOk && dOk)
        {
            var (dur, dist, rOk) = await GetRouteAsync(_navUserLat, _navUserLon, _navDestLat, _navDestLon);
            if (rOk) { _navEstimatedDuration = dur; _navTotalDistanceKm = dist; }
        }

        Dispatcher.Invoke(RefreshWidgetDisplay);
    }

    internal void StopNavigation()
    {
        _navActive = false;
        _navHudWindow?.Close();
        _navHudWindow = null;
        Dispatcher.Invoke(RefreshWidgetDisplay);
    }

    internal void OpenNavHudWindow()
    {
        if (_navHudWindow != null) { _navHudWindow.Activate(); return; }

        var win = new Window
        {
            Title = "Navigation HUD", Width = 400, SizeToContent = SizeToContent.Height,
            Background      = new SolidColorBrush(C(0x0d1117)),
            WindowStyle     = WindowStyle.ToolWindow,
            ResizeMode      = ResizeMode.NoResize,
            Owner           = this, ShowInTaskbar = false, Topmost = true
        };
        win.Closing += (_, _) => { win.Owner = null; _navHudWindow = null; };
        win.Closed  += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            try { if (WindowState != WindowState.Minimized) Activate(); } catch { }
        }));

        var root = new StackPanel();

        var header = new Border
        {
            Background      = new SolidColorBrush(C(0x161b22)),
            BorderBrush     = new SolidColorBrush(C(0x2e6aa0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(12, 10, 12, 10)
        };
        var hGrid = new Grid();
        hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var arrowTx = new TextBlock
        {
            FontSize = 38, Foreground = new SolidColorBrush(C(0x58a6ff)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
        };
        var destStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var destLbl   = new TextBlock
        {
            Text = _navDestinationLabel.Length > 30 ? _navDestinationLabel[..27] + "…" : _navDestinationLabel,
            Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap
        };
        var etaLbl = new TextBlock { Foreground = new SolidColorBrush(C(0x8b949e)), FontSize = 11 };
        destStack.Children.Add(destLbl);
        destStack.Children.Add(etaLbl);

        var distTx = new TextBlock
        {
            FontSize = 11, Foreground = new SolidColorBrush(C(0x8b949e)),
            VerticalAlignment = VerticalAlignment.Bottom, TextAlignment = TextAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0)
        };
        if (_navTotalDistanceKm > 0) distTx.Text = $"{_navTotalDistanceKm:F1} km";

        Grid.SetColumn(arrowTx,   0);
        Grid.SetColumn(destStack, 1);
        Grid.SetColumn(distTx,    2);
        hGrid.Children.Add(arrowTx);
        hGrid.Children.Add(destStack);
        hGrid.Children.Add(distTx);
        header.Child = hGrid;
        root.Children.Add(header);

        const double mapW = 376.0, mapH = 260.0;
        var mapCanvas = new Canvas
        {
            Width = mapW, Height = mapH,
            Background = new SolidColorBrush(C(0x0d1117))
        };
        DrawNavMap(mapCanvas, mapW, mapH);
        root.Children.Add(mapCanvas);

        var footer = new Border
        {
            Background      = new SolidColorBrush(C(0x161b22)),
            BorderBrush     = new SolidColorBrush(C(0x21262d)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding         = new Thickness(12, 8, 12, 8)
        };
        var fGrid = new Grid();
        fGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var elapsedTx = new TextBlock
        {
            Foreground = new SolidColorBrush(C(0x8b949e)), FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas")
        };
        var stopBtn = AccentButton("⏹  Stop", C(0x3a1a1a), C(0x8b1a1a), 90);
        stopBtn.Click += (_, _) => { StopNavigation(); win.Close(); };

        Grid.SetColumn(elapsedTx, 0);
        Grid.SetColumn(stopBtn,   1);
        fGrid.Children.Add(elapsedTx);
        fGrid.Children.Add(stopBtn);
        footer.Child = fGrid;
        root.Children.Add(footer);

        win.Content = root;

        void UpdateHud()
        {
            arrowTx.Text = BearingToArrow(_navBearingDeg);
            var elapsed = DateTime.Now - _navStartedAt;
            elapsedTx.Text = $"⏱  {(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            if (_navEstimatedDuration > TimeSpan.Zero)
            {
                var rem = _navEstimatedDuration - elapsed;
                if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;
                etaLbl.Text = $"ETA  {(int)rem.TotalMinutes} min {rem.Seconds:D2} s";
            }
            else
            {
                etaLbl.Text = _navBearingDeg == 0 ? "Locating…" : "Calculating route…";
            }
        }
        UpdateHud();

        var tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => UpdateHud();
        tick.Start();
        win.Closed += (_, _) => tick.Stop();

        _navHudWindow = win;
        win.Show();
    }

    private void DrawNavMap(Canvas canvas, double w, double h)
    {
        double cx   = w / 2.0;
        double cy   = h / 2.0;
        int    seed = (int)(Math.Abs(_navUserLat * 1000) + Math.Abs(_navUserLon * 1000));
        var    rng  = new Random(seed == 0 ? 42 : seed);

        var parkFill  = new SolidColorBrush(Color.FromArgb(70, 18, 52, 18));
        var waterFill = new SolidColorBrush(Color.FromArgb(70, 10, 36, 70));
        var roadMajor = new SolidColorBrush(C(0x232e3c));
        var roadMinor = new SolidColorBrush(C(0x181f28));
        Color[] blockColors =
        {
            Color.FromArgb(85, 28, 33, 43), Color.FromArgb(75, 24, 29, 40),
            Color.FromArgb(70, 33, 28, 42), Color.FromArgb(80, 20, 26, 36)
        };

        for (int i = 0; i < 2; i++)
        {
            double pw = 50 + rng.NextDouble() * 70;
            double ph = 30 + rng.NextDouble() * 45;
            double px = rng.NextDouble() * (w - pw);
            double py = rng.NextDouble() * (h - ph);
            var pr = new System.Windows.Shapes.Rectangle { Width = pw, Height = ph, Fill = parkFill, RadiusX = 6, RadiusY = 6 };
            Canvas.SetLeft(pr, px); Canvas.SetTop(pr, py); canvas.Children.Add(pr);
        }
        {
            double pw = 35 + rng.NextDouble() * 90;
            double ph = 14 + rng.NextDouble() * 22;
            double px = rng.NextDouble() * (w - pw);
            double py = rng.NextDouble() * (h - ph);
            var wr = new System.Windows.Shapes.Rectangle { Width = pw, Height = ph, Fill = waterFill, RadiusX = 10, RadiusY = 10 };
            Canvas.SetLeft(wr, px); Canvas.SetTop(wr, py); canvas.Children.Add(wr);
        }

        double[] hy = { 0.20, 0.36, 0.50, 0.64, 0.80 };
        double[] vx = { 0.15, 0.30, 0.50, 0.70, 0.85 };
        string[] roadNames = { "Main St", "Broadway", "Oak Ave", "River Rd", "Park Ln", "High St", "Mill Rd" };
        int rni = seed % roadNames.Length;

        foreach (double fy in hy)
        {
            double y     = fy * h;
            bool   major = Math.Abs(fy - 0.50) < 0.01;
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = major ? roadMajor : roadMinor,
                StrokeThickness = major ? 5 : 2.5
            });
            if (major)
            {
                var lbl = new TextBlock
                {
                    Text = roadNames[rni % roadNames.Length],
                    Foreground = new SolidColorBrush(C(0x2e3c50)), FontSize = 7.5
                };
                Canvas.SetLeft(lbl, 4); Canvas.SetTop(lbl, y - 10); canvas.Children.Add(lbl);
                rni++;
            }
        }
        foreach (double fx in vx)
        {
            double x     = fx * w;
            bool   major = Math.Abs(fx - 0.50) < 0.01;
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                Stroke = major ? roadMajor : roadMinor,
                StrokeThickness = major ? 5 : 2.5
            });
            if (major)
            {
                var lbl = new TextBlock
                {
                    Text = roadNames[(rni + 1) % roadNames.Length],
                    Foreground = new SolidColorBrush(C(0x2e3c50)), FontSize = 7.5
                };
                Canvas.SetLeft(lbl, x + 3); Canvas.SetTop(lbl, 3); canvas.Children.Add(lbl);
            }
        }

        for (int ri = 0; ri < hy.Length - 1; ri++)
        {
            for (int ci = 0; ci < vx.Length - 1; ci++)
            {
                double bx = vx[ci] * w + 3;
                double by = hy[ri] * h + 3;
                double bw = (vx[ci + 1] - vx[ci]) * w - 6;
                double bh = (hy[ri + 1] - hy[ri]) * h - 6;
                if (bw < 8 || bh < 8) continue;
                int nb = 1 + rng.Next(3);
                for (int bi = 0; bi < nb; bi++)
                {
                    double mg  = 5;
                    double bbx = bx + mg + rng.NextDouble() * Math.Max(0, bw - 2 * mg - 10);
                    double bby = by + mg + rng.NextDouble() * Math.Max(0, bh - 2 * mg - 6);
                    double bbw = 8 + rng.NextDouble() * bw * 0.35;
                    double bbh = 5 + rng.NextDouble() * bh * 0.35;
                    if (bbx + bbw > bx + bw - mg || bby + bbh > by + bh - mg) continue;
                    var blk = new System.Windows.Shapes.Rectangle
                    {
                        Width = bbw, Height = bbh,
                        Fill = new SolidColorBrush(blockColors[rng.Next(blockColors.Length)]),
                        RadiusX = 1, RadiusY = 1
                    };
                    Canvas.SetLeft(blk, bbx); Canvas.SetTop(blk, bby); canvas.Children.Add(blk);
                }
            }
        }

        string[] pois = { "🏪", "⛽", "🏦", "🌳", "🍕", "🏥", "🚦", "☕", "🏬", "🌳", "🏫", "🏨" };
        for (int i = 0; i < 10; i++)
        {
            double px = 8 + rng.NextDouble() * (w - 16);
            double py = 8 + rng.NextDouble() * (h - 16);
            if (Math.Abs(px - cx) < 22 && Math.Abs(py - cy) < 22) continue;
            var poi = new TextBlock { Text = pois[i % pois.Length], FontSize = 12 };
            Canvas.SetLeft(poi, px); Canvas.SetTop(poi, py); canvas.Children.Add(poi);
        }

        if (_navActive && (_navDestLat != 0 || _navDestLon != 0))
        {
            double bearRad = _navBearingDeg * Math.PI / 180.0;
            double ddx = Math.Sin(bearRad), ddy = -Math.Cos(bearRad);
            double sc  = 1e9;
            if (ddx > 0) sc = Math.Min(sc, (w - cx) / ddx);
            if (ddx < 0) sc = Math.Min(sc, -cx / ddx);
            if (ddy > 0) sc = Math.Min(sc, (h - cy) / ddy);
            if (ddy < 0) sc = Math.Min(sc, -cy / ddy);
            sc *= 0.92;
            double ex = cx + ddx * sc, ey = cy + ddy * sc;
            canvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = cx, Y1 = cy, X2 = ex, Y2 = ey,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 46, 140, 220)),
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 8, 4 }
            });
            var destPin = new TextBlock { Text = "🏁", FontSize = 20 };
            Canvas.SetLeft(destPin, ex - 10); Canvas.SetTop(destPin, ey - 18); canvas.Children.Add(destPin);
        }

        double compX = w - 22, compY = 18;
        foreach (var (ddx2, ddy2, lbl) in new[]
        {
            (0.0, -9.0, "N"), (9.0, 0.0, "E"), (0.0, 9.0, "S"), (-9.0, 0.0, "W")
        })
        {
            var clbl = new TextBlock
            {
                Text = lbl, FontSize = lbl == "N" ? 9 : 7.5,
                Foreground = new SolidColorBrush(lbl == "N" ? C(0x58a6ff) : C(0x3a4a5a)),
                FontWeight = lbl == "N" ? FontWeights.Bold : FontWeights.Normal
            };
            Canvas.SetLeft(clbl, compX + ddx2 - 4); Canvas.SetTop(clbl, compY + ddy2 - 7); canvas.Children.Add(clbl);
        }

        var glow = new System.Windows.Shapes.Ellipse
        {
            Width = 22, Height = 22,
            Fill = new SolidColorBrush(Color.FromArgb(45, 46, 140, 220))
        };
        Canvas.SetLeft(glow, cx - 11); Canvas.SetTop(glow, cy - 11); canvas.Children.Add(glow);
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10,
            Fill = new SolidColorBrush(C(0x2ea0d6)),
            Stroke = Brushes.White, StrokeThickness = 1.5
        };
        Canvas.SetLeft(dot, cx - 5); Canvas.SetTop(dot, cy - 5); canvas.Children.Add(dot);

        if (_navBearingDeg != 0)
        {
            double brRad = _navBearingDeg * Math.PI / 180.0;
            double arLen = 20;
            double tx = cx + Math.Sin(brRad) * arLen,  ty = cy - Math.Cos(brRad) * arLen;
            double lx = cx + Math.Sin(brRad - 0.55) * 7, ly = cy - Math.Cos(brRad - 0.55) * 7;
            double rx = cx + Math.Sin(brRad + 0.55) * 7, ry = cy - Math.Cos(brRad + 0.55) * 7;
            canvas.Children.Add(new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection { new Point(tx, ty), new Point(lx, ly), new Point(rx, ry) },
                Fill = new SolidColorBrush(C(0x2ea0d6)), Opacity = 0.9
            });
        }

        canvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 8, Y1 = h - 10, X2 = 52, Y2 = h - 10,
            Stroke = new SolidColorBrush(C(0x2e3c50)), StrokeThickness = 2
        });
        string scaleT = _navTotalDistanceKm > 0 ? $"{(_navTotalDistanceKm / 4.0):F1}km" : "~1km";
        var sclLbl = new TextBlock { Text = scaleT, Foreground = new SolidColorBrush(C(0x2e3c50)), FontSize = 7 };
        Canvas.SetLeft(sclLbl, 8); Canvas.SetTop(sclLbl, h - 21); canvas.Children.Add(sclLbl);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SHARED UI HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private Window MakeToolWindow(string title, int width, bool resizable = false)
    {
        var w = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height,
            Background = new SolidColorBrush(C(0x161616)),
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = resizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize,
            Owner = this, ShowInTaskbar = false, Topmost = true
        };
        w.Closing += (s, e) => { w.Owner = null; };
        w.Closed  += (s, e) => Dispatcher.BeginInvoke(new Action(() => { try { if (WindowState != WindowState.Minimized) Activate(); } catch { } }));
        return w;
    }

    private static Button MenuButton(string label, bool active) => new Button
    {
        Content = label, HorizontalContentAlignment = HorizontalAlignment.Left,
        Background  = new SolidColorBrush(active ? C(0x1a3a5a) : C(0x222222)),
        Foreground  = new SolidColorBrush(active ? C(0xaaddff) : C(0xcccccc)),
        BorderBrush = new SolidColorBrush(active ? C(0x2e6aa0) : C(0x333333)),
        BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7),
        Margin = new Thickness(0, 2, 0, 2), Cursor = Cursors.Hand, FontSize = 12
    };

    private static Button AccentButton(string label, Color bg, Color border, int width = 80) => new Button
    {
        Content = label, Width = width, Height = 32, FontSize = 12,
        Background = new SolidColorBrush(bg), Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(border), BorderThickness = new Thickness(1), Cursor = Cursors.Hand
    };

    private static Button NavBtn(string content) => new Button
    {
        Content = content, Width = 28, Height = 24,
        Background = new SolidColorBrush(C(0x222222)), Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(C(0x333333)), BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand, FontSize = 16
    };

    private static Button BarButton(string label, Color bg, Color border) => new Button
    {
        Content = label, Padding = new Thickness(9,4,9,4), Margin = new Thickness(4,4,2,4),
        Background = new SolidColorBrush(bg), Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(border), BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand, FontSize = 11
    };

    private static Button SyncButton(string label, Color bg, Color border) => new Button
    {
        Content = label, HorizontalContentAlignment = HorizontalAlignment.Left,
        Background = new SolidColorBrush(bg), Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(border), BorderThickness = new Thickness(1),
        Padding = new Thickness(10,8,10,8), Margin = new Thickness(0,3,0,3), Cursor = Cursors.Hand, FontSize = 12
    };

    private static Button TabButton(string label, bool active) => new Button
    {
        Content = label, FontSize = 12, Height = 32, Background = Brushes.Transparent,
        Foreground  = new SolidColorBrush(active ? C(0x88ccff) : C(0x888888)),
        BorderBrush = new SolidColorBrush(active ? C(0x2e6aa0) : C(0x000000)),
        BorderThickness = active ? new Thickness(0,0,0,2) : new Thickness(0), Cursor = Cursors.Hand
    };

    private static void TabButtonActive(Button btn, bool active)
    {
        btn.Foreground      = new SolidColorBrush(active ? C(0x88ccff) : C(0x888888));
        btn.BorderBrush     = new SolidColorBrush(active ? C(0x2e6aa0) : C(0x000000));
        btn.BorderThickness = active ? new Thickness(0,0,0,2) : new Thickness(0);
    }

    private static TextBlock SectionLabel(string text) => new TextBlock
    {
        Text = text, Foreground = new SolidColorBrush(C(0xaaaaaa)),
		FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8)
    };

    private static TextBlock FieldLabel(string text) => new TextBlock
    {
        Text = text, Foreground = new SolidColorBrush(C(0xaaaaaa)),
		FontSize = 10, Margin = new Thickness(0, 0, 0, 2)
    };

    private static (StackPanel panel, TextBox box) Spinner(string label, int val)
    {
        var sp  = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(3,0,3,0) };
        var lbl = new TextBlock { Text = label, Foreground = new SolidColorBrush(C(0x999999)), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10 };
        var box = new TextBox  { Text = val.ToString("D2"), Width = 44, TextAlignment = TextAlignment.Center,
            Background = new SolidColorBrush(C(0x1e1e1e)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(C(0x404040)), BorderThickness = new Thickness(1),
            FontSize = 24, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold };
        sp.Children.Add(lbl); sp.Children.Add(box);
        return (sp, box);
    }

    private static Color C(int hex) =>
        Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8 & 0xff), (byte)(hex & 0xff));

    /// <summary>
    /// Forces the internal DatePickerTextBox to use dark theme colors.
    /// WPF's DatePicker does not forward Foreground/Background to its inner
    /// textbox — it uses system colors instead, which causes invisible text
    /// in Windows dark mode (white text on white, or vice versa).
    /// </summary>
    private static void ApplyDarkDatePickerStyle(DatePicker dp)
    {
        var tbStyle = new Style(typeof(System.Windows.Controls.Primitives.DatePickerTextBox));
        tbStyle.Setters.Add(new Setter(Control.BackgroundProperty,   new SolidColorBrush(C(0x1e1e1e))));
        tbStyle.Setters.Add(new Setter(Control.ForegroundProperty,   new SolidColorBrush(C(0xdddddd))));
        tbStyle.Setters.Add(new Setter(Control.BorderBrushProperty,  new SolidColorBrush(C(0x333333))));
        tbStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        tbStyle.Setters.Add(new Setter(Control.PaddingProperty,      new Thickness(4, 2, 4, 2)));
        dp.Resources[typeof(System.Windows.Controls.Primitives.DatePickerTextBox)] = tbStyle;
    }

    // Forces white text on dark background inside every ComboBox dropdown popup,
    // regardless of the Windows system theme (fixes dark-on-dark on dark mode).
    private static Style DarkComboItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, new SolidColorBrush(C(0x1e1e1e))));
        style.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, new SolidColorBrush(C(0xdddddd))));
        style.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(6, 3, 6, 3)));
        var trigger = new Trigger { Property = ComboBoxItem.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, new SolidColorBrush(C(0x2e6aa0))));
        trigger.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, Brushes.White));
        style.Triggers.Add(trigger);
        var selTrigger = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        selTrigger.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, new SolidColorBrush(C(0x1a3a5a))));
        selTrigger.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, new SolidColorBrush(C(0xaaddff))));
        style.Triggers.Add(selTrigger);
        return style;
    }
}