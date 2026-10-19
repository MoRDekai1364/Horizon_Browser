using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;

internal static partial class Program
{
    private static int _checks;
    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(AppContext.BaseDirectory, "HorizonData"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            if (args.Contains("--weather-only"))
            {
                CheckWeather();
                Console.WriteLine($"PASS: {_checks} weather checks");
                return 0;
            }
            if (!args.Contains("--layout-only"))
            {
                CheckMoon();
                CheckSun();
                CheckStorm();
                CheckSettings();
                CheckAnimations();
            }
            CheckHomepage();
            Console.WriteLine($"PASS: {_checks} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Shutdown(); }
    }

    private static void CheckMoon()
    {
        var full = AstronomyService.MoonAt(Utc("2024-03-25T07:00:00Z"));
        Check(full.Illumination > 0.98 && Math.Abs(full.Phase - 0.5) < 0.02, "Known full moon");
        var eclipse = AstronomyService.MoonAt(Utc("2024-04-08T18:21:00Z"));
        Check(eclipse.Illumination < 0.02 && (eclipse.Phase < 0.03 || eclipse.Phase > 0.97), "Known new moon/eclipse");
        var first = AstronomyService.MoonAt(Utc("2024-04-15T19:13:00Z"));
        Check(first.Phase > 0.22 && first.Phase < 0.28 && first.Illumination > 0.4 && first.Illumination < 0.6, "First quarter");
        var last = AstronomyService.MoonAt(Utc("2024-04-02T03:15:00Z"));
        Check(last.Phase > 0.72 && last.Phase < 0.78, "Last quarter");
        for (var date = Utc("1900-01-01T00:00:00Z"); date.Year < 2100; date = date.AddDays(7))
        {
            var moon = AstronomyService.MoonAt(date);
            Check(double.IsFinite(moon.Phase) && moon.Phase >= 0 && moon.Phase <= 1
                && double.IsFinite(moon.Illumination) && moon.Illumination >= 0 && moon.Illumination <= 1, "Moon range " + date);
        }
        Console.WriteLine("PASS: moon reference dates and 200-year range");
    }

    private static void CheckSun()
    {
        var zone = AstronomyService.ResolveZone("Europe/Sofia");
        var date = new DateOnly(2025, 3, 30);
        var rise = Utc("2025-03-30T03:50:00Z");
        var set = Utc("2025-03-30T16:47:00Z");
        var snapshot = new SolarSnapshot("Sofia", zone, new[] {
            new SolarDay(date, rise, set, (set - rise).TotalSeconds),
            new SolarDay(date.AddDays(1), rise.AddDays(1).AddMinutes(-2), set.AddDays(1), (set-rise).TotalSeconds)
        }, rise);
        var midday = AstronomyService.SunAt(snapshot, rise + (set - rise) / 2);
        Check(midday.IsDay && Math.Abs(midday.Progress!.Value - 0.5) < 0.0001, "Daylight midpoint");
        Check(TimeZoneInfo.ConvertTime(rise, zone).Hour == 6, "City DST conversion");
        Check(!AstronomyService.SunAt(snapshot, rise.AddMinutes(-1)).IsDay, "Before sunrise");
        var night = AstronomyService.SunAt(snapshot, set.AddHours(1));
        Check(!night.IsDay && night.Progress == 1 && night.Detail.Contains("Sunrise"), "Next day's sunrise after sunset");
        Check(AstronomyService.SunAt(snapshot, rise.AddDays(6)).Progress == null, "Expired dates do not reuse old events");
        Check(AstronomyService.SunAt(null, rise).Progress == null, "Missing sun data");
        var polar = new SolarSnapshot("Tromso", zone, new[] { new SolarDay(date, null, null, 86400) }, rise);
        Check(AstronomyService.SunAt(polar, rise).IsDay, "Polar day");
        Check(!AstronomyService.SunAt(polar with { Days = new[] { new SolarDay(date, null, null, 0) } }, rise).IsDay, "Polar night");
        var parsed = AstronomyService.ParseSolar("""
            {"timezone":"Europe/Sofia","daily":{"time":[1743285600],"sunrise":[1743306600],"sunset":[1743353220],"daylight_duration":[46620]}}
            """, "Sofia", rise);
        Check(parsed.Days[0].Date == date && parsed.Days[0].Sunrise == rise, "Open-Meteo Unix timestamp contract");
        Console.WriteLine("PASS: sun progress, DST, midnight, polar events and provider parser");
    }

    private static void CheckStorm()
    {
        var now = Utc("2026-10-17T12:00:00Z");
        var storm = AstronomyService.ParseStorm("""
            [{"time_tag":"2026-10-17T09:00:00","Kp":5.33},
             {"time_tag":"2026-10-17T06:00:00","Kp":2.0},
             {"time_tag":"2026-10-17T15:00:00","Kp":9.0},
             {"time_tag":"2026-10-17T11:00:00","Kp":-1},
             {"time_tag":"2026-10-17T11:00:00","Kp":"bad"}, null]
            """, now);
        Check(storm.Kp == 5.33 && storm.ObservedUtc.Offset == TimeSpan.Zero, "Latest valid observed Kp, UTC, future filtering");
        Check(AstronomyService.StormLevel(4.99).Contains("No geomagnetic storm"), "Below storm threshold");
        foreach (int grade in Enumerable.Range(1, 5)) Check(AstronomyService.StormLevel(grade + 4).StartsWith("G" + grade), "NOAA G" + grade);
        Console.WriteLine("PASS: NOAA parsing and storm thresholds");
    }

    private static void CheckSettings()
    {
        var defaults = new SettingsData();
        Check(!defaults.HomeShowMoonPhase && !defaults.HomeShowSunProgress, "New homepage widgets are optional by default");
        defaults.HomeShowMoonPhase = defaults.HomeShowSunProgress = true;
        defaults.WidgetModes = new() { "MoonPhase", "Clock", "SunProgress" };
        var restored = JsonSerializer.Deserialize<SettingsData>(JsonSerializer.Serialize(defaults))!;
        Check(restored.HomeShowMoonPhase && restored.HomeShowSunProgress
            && restored.WidgetModes.SequenceEqual(defaults.WidgetModes), "Widget visibility and header order persist");
        foreach (string id in new[] { "moon_phase", "sun_progress" })
            Check(StackElementStore.Validate(new StackElementData { Kind = StackElementKind.Widget, SourceId = id }, out _), "Catalog validates " + id);
        Check(RowsPanel.ColumnsFor(100) == 1, "Narrow shortcut panel allows one column");
        Console.WriteLine("PASS: widget defaults, persistence and catalog entries");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner)!;

    private static void CheckAnimations()
    {
        var root = new Grid();
        var underlay = new Border { Opacity = 0.65, IsHitTestVisible = false };
        var sheet = new FolderSheetHost();
        sheet.Underlays.Add(underlay);
        root.Children.Add(underlay);
        root.Children.Add(sheet);
        var stack = new StackHost { Width = 300, Height = 350 };
        stack.AddElement("First", new Border());
        stack.AddElement("Second", new Border());
        root.Children.Add(stack);
        var win = new Window { Content = root, Width = 700, Height = 500, Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            win.Show();
            Pump(80);
            for (int i = 0; i < 30; i++)
            {
                sheet.Open(new Border(), new Rect(10, 10, 60, 60));
                Pump(12);
                sheet.Close();
                Pump(12);
                var replacement = new Border();
                sheet.Open(replacement, new Rect(20, 20, 60, 60));
                Pump(330);
                Check(sheet.IsOpen && ReferenceEquals(sheet.Content, replacement) && Field<Border>(sheet, "_sheet").Opacity > 0.99, "Interrupted sheet reopen " + i);
                sheet.Close();
                Pump(330);
                Check(!sheet.IsOpen && sheet.Content == null && Math.Abs(underlay.Opacity - 0.65) < 0.001 && !underlay.IsHitTestVisible, "Sheet cleanup preserves underlay " + i);
            }
            foreach (bool inPlace in new[] { true, false, true, false })
            {
                void Open() { if (inPlace) stack.ShowInPlace(new Border(), new Rect(12, 12, 60, 60)); else stack.ShowOverlay(new Border()); }
                Open(); Pump(30); stack.CloseOverlay(); Pump(30); Open(); Pump(350);
                Check(stack.IsOverlayOpen && Field<Grid>(stack, "Overlay").Opacity > 0.99, "Stack reopen");
                stack.CloseOverlay(); Pump(350);
                Check(!stack.IsOverlayOpen && Field<ContentPresenter>(stack, "OverlayContent").Content == null
                    && Field<ContentPresenter>(stack, "Incoming").Opacity == 1, "Stack overlay cleanup");
            }
            stack.SetRestSize(300, 350);
            stack.ExpandTo(350, 400); Pump(25); stack.Collapse(); Pump(25); stack.ExpandTo(340, 390); Pump(350);
            Check(stack.IsExpanded && Math.Abs(stack.Width - 340) < 1, "Stale collapse cannot overwrite new expansion");
            stack.SetRestSize(260, 200); stack.Collapse(); Pump(350);
            Check(Math.Abs(stack.Width - 260) < 1 && Math.Abs(stack.Height - 200) < 1, "Collapse uses resized rest bounds");
            Console.WriteLine("PASS: 30 interrupted folder cycles, both stack modes and resize interruptions");
        }
        finally { win.Close(); }
    }

    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);

    private static void CheckHomepage()
    {
        typeof(StackElementStore).GetField("_loaded", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        var settings = SettingsService.Current;
        settings.WidgetWeatherCity = "Sofia";
        settings.HomeShowMoonPhase = true;
        settings.HomeShowSunProgress = true;
        settings.HomeShowRightColumn = true;
        settings.HomeInactivityTimeoutSeconds = 60;
        settings.PinnedUrls = Enumerable.Range(1, 36).Select(i => new PinItem {
            Name = "Shortcut " + i, Url = "about:blank#" + i, Category = "Folder " + (i / 6), IconEmoji = "★"
        }).ToList();
        settings.WallpaperFolderPath = System.IO.Path.Combine(AppContext.BaseDirectory, "empty-wallpaper");
        System.IO.Directory.CreateDirectory(settings.WallpaperFolderPath);
        typeof(AstronomyService).GetField("_stormAttempt", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, DateTimeOffset.UtcNow);
        typeof(AstronomyService).GetField("_requestedCity", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, "Sofia");
        typeof(AstronomyService).GetField("_solarAttempt", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var zone = AstronomyService.ResolveZone("Europe/Sofia");
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var sampleDays = Enumerable.Range(0, 3).Select(i =>
        {
            var day = local.Date.AddDays(i);
            var rise = new DateTimeOffset(day.AddHours(7), zone.GetUtcOffset(day.AddHours(7)));
            var set = new DateTimeOffset(day.AddHours(18), zone.GetUtcOffset(day.AddHours(18)));
            return new SolarDay(DateOnly.FromDateTime(day), rise, set, (set-rise).TotalSeconds);
        }).ToArray();
        typeof(AstronomyService).GetProperty("Solar")!.SetValue(null, new SolarSnapshot("Sofia", zone, sampleDays, now));
        var home = new Horizon.Stealth.Views.HomePageView();
        var win = new Window { Content = home, Width = 1280, Height = 720, Left = -12000, Top = -12000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        var artifacts = System.IO.Path.Combine(AppContext.BaseDirectory, "layout-artifacts");
        System.IO.Directory.CreateDirectory(artifacts);
        try
        {
            win.Show();
            Pump(700);
            var art = new DrawingVisual();
            using (var dc = art.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(35, 67, 100), Color.FromRgb(78, 38, 64), 40), null, new Rect(0, 0, 1280, 720));
                for (int i = 0; i < 12; i++) dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(100, 150, 200, 230)), null, new Point(i * 115, i % 3 * 220), 60, 60);
            }
            var wallpaper = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
            wallpaper.Render(art);
            Field<ImageBrush>(home, "BgImageBrush").ImageSource = wallpaper;
            foreach (var size in new[] { (1280, 720), (1600, 420), (600, 850), (420, 360), (900, 450), (350, 300) })
            {
                win.Width = size.Item1;
                win.Height = size.Item2;
                Invoke(home, "RegisterUserActivity");
                Pump(700);
                var root = Field<Grid>(home, "RootHomeGrid");
                var layout = Field<Grid>(home, "GridHomeLayout");
                double boundary = (double)Invoke(home, "HomeBottomBoundary")!;
                if (size.Item1 >= 1200) Check(Field<StackHost>(home, "HomeStack").IsVisible, "Right stack should fit at " + size);
                foreach (string name in new[] { "GridSearchRow", "SearchBoxBorder", "CenterContentScroll", "ClockWeatherScroll", "HomeStack" })
                {
                    var element = Field<FrameworkElement>(home, name);
                    if (!element.IsVisible) continue;
                    Rect bounds = element.TransformToAncestor(layout).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                    Check(bounds.Left >= -1 && bounds.Right <= root.ActualWidth + 1, $"{size}: horizontal bounds for {name}: {bounds}");
                    Check(bounds.Bottom <= boundary + 1, $"{size}: bottom bounds for {name}: {bounds.Bottom} > {boundary}");
                    if (name == "GridSearchRow" && Grid.GetColumnSpan(Field<Grid>(home, "PnlSearchArea")) == 1)
                    {
                        double left = Field<ColumnDefinition>(home, "ColClockWeather").ActualWidth;
                        double right = left + Field<ColumnDefinition>(home, "ColSearchArea").ActualWidth;
                        Check(bounds.Left >= left - 1 && bounds.Right <= right + 1, $"Search row fits its center column at {size}: row={bounds}, column={left}..{right}, switch={Field<Grid>(home, "SwitchSlot").ActualWidth}, search={Field<Border>(home, "SearchBoxBorder").ActualWidth}, icons={Field<bool?>(home, "_switchIconMode")}");
                    }
                }
                foreach (string name in new[] { "BtnCompactInfo", "BtnCompactWidgets", "PnlVpnAdBlock", "BtnChangeWallpaper", "BtnHomeSettings" })
                {
                    var button = Field<FrameworkElement>(home, name);
                    if (!button.IsVisible) continue;
                    var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    Check(bounds.Left >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1, "Footer/header action bounds: " + name + " at " + size);
                }
                var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(artifacts, $"home-{size.Item1}x{size.Item2}.png"));
                encoder.Save(stream);
                Console.WriteLine($"PASS: homepage bounds at {size.Item1}x{size.Item2}");
            }
            Field<Button>(home, "BtnToggleFavorites").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(450);
            var startMenu = Field<FolderSheetHost>(home, "_startMenuHost");
            var glass = Field<HomeGlassInlineLayer>(home, "_homeGlass");
            Check(startMenu.IsOpen && Field<Dictionary<FrameworkElement, Border>>(glass, "_shapes").ContainsKey(startMenu.GlassSurface), "Show more sheet has registered glass");
            var shapes = Field<Dictionary<FrameworkElement, Border>>(glass, "_shapes");
            Check(shapes[startMenu.GlassSurface].Visibility == Visibility.Visible, "Expanded glass contributes to the mask");
            settings.DisableBlurEffects = true;
            HomeGlassMotion.RaiseBlurSettingChanged(); Pump(80);
            Check(Field<bool>(glass, "_flat"), "Expanded sheet respects disabled blur");
            settings.DisableBlurEffects = false;
            HomeGlassMotion.RaiseBlurSettingChanged(); Pump(80);
            Check(!Field<bool>(glass, "_flat"), "Expanded sheet restores wallpaper blur");
            startMenu.Close(); Pump(350);
            Field<Button>(home, "BtnCompactInfo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(450);
            Check(startMenu.IsOpen && Field<Grid>(home, "ClockWeatherContent").IsVisible, "Compact info keeps left widgets accessible");
            startMenu.Close(); Pump(350);
            Check(ReferenceEquals(Field<ScrollViewer>(home, "ClockWeatherScroll").Content, Field<Grid>(home, "ClockWeatherContent")), "Compact info restores its parent");
            Field<Button>(home, "BtnCompactWidgets").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(450);
            var compact = Field<FolderSheetHost>(home, "_compactStackHost");
            Check(compact.IsOpen && Field<StackHost>(home, "HomeStack").IsVisible, "Compact widget stack stays accessible");
            compact.Close(); Pump(450);
            Check(Field<Grid>(home, "GridHomeLayout").Children.Contains(Field<StackHost>(home, "HomeStack")), "Compact stack restores its parent");
            win.Width = 1280; win.Height = 720; Pump(500);
            var leftScroll = Field<ScrollViewer>(home, "ClockWeatherScroll");
            if (leftScroll.ScrollableHeight > 0)
            {
                leftScroll.ScrollToEnd(); Pump(100);
                Check(leftScroll.VerticalOffset > 0, "Left widgets can scroll");
                leftScroll.ScrollToTop(); Pump(100);
            }
            Invoke(home, "ApplyActiveLayoutMatrix", true, null, null); Pump(100);
            Check(ReferenceEquals(Field<Grid>(home, "ClockWeatherContent").Parent, Field<Grid>(home, "GridHomeLayout")), "Inactivity layout can move outside the left scroll viewport");
            Invoke(home, "ApplyActiveLayoutMatrix", false, null, null); Pump(100);
            Check(ReferenceEquals(Field<ScrollViewer>(home, "ClockWeatherScroll").Content, Field<Grid>(home, "ClockWeatherContent")), "Activity restores the scroll viewport");
            Console.WriteLine("PASS: Show more glass and compact stack restoration");
        }
        finally { win.Close(); }
    }
}
