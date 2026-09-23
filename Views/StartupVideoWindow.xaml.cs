using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class StartupVideoWindow : Window
{
    private bool _closing = false;
    private Action? _onFinished;
    private Dispatcher? _ownDispatcher;
    private bool _hostShown = false;
    private static readonly TimeSpan FadeIn  = TimeSpan.FromSeconds(0.4);
    private static readonly TimeSpan FadeOut = TimeSpan.FromSeconds(0.6);

    public StartupVideoWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Runs the splash on its own STA thread (own Dispatcher) so video playback
    /// is never affected by whatever the main thread is doing (e.g. MainWindow
    /// construction, WebView2 init). Calls onFinished on the main App dispatcher
    /// once the video has finished and starts fading out. If disabled/missing,
    /// calls onFinished immediately on the calling thread.
    /// </summary>
    public static void PlayIfEnabled(Action onFinished)
    {
        if (!SettingsService.Current.ShowStartupVideo)
        {
            LogService.Write("Startup", "Startup video skipped: ShowStartupVideo=false");
            onFinished();
            return;
        }

        string videoPath = ResolveVideoPath();
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            LogService.Write("Startup", $"Startup video skipped: file not found at {videoPath}");
            onFinished();
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                LogService.Write("Startup", $"Startup video playing from {videoPath}");

                var win = new StartupVideoWindow();
                win._onFinished = onFinished;
                win._ownDispatcher = Dispatcher.CurrentDispatcher;

                var s = SettingsService.Current;
                win.Width  = s.StartupVideoCachedWidth  > 0 ? s.StartupVideoCachedWidth  : 960;
                win.Height = s.StartupVideoCachedHeight > 0 ? s.StartupVideoCachedHeight : 540;
                win.Left = (SystemParameters.WorkArea.Width - win.Width) / 2;
                win.Top = (SystemParameters.WorkArea.Height - win.Height) / 2;
                win.Opacity = 0;

                try
                {
                    var bg = (Color)ColorConverter.ConvertFromString(s.StartupVideoBgColorHex);
                    win.RootGrid.Background = new SolidColorBrush(bg);
                }
                catch { }

                win.VideoPlayer.Source = new Uri(videoPath, UriKind.Absolute);
                win.VideoPlayer.Play();
                win.Show();

                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                LogService.RecordCrash(ex, "StartupVideo");
                Application.Current?.Dispatcher.BeginInvoke(onFinished);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "StartupVideoThread";
        thread.Start();
    }

    private static string GetFirstVideoInFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return "";
            var files = Directory.GetFiles(folder, "*.mp4");
            if (files.Length == 0) return "";
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files[0];
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveVideoPath()
    {
        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "start_videos");
        var s = SettingsService.Current;

        try
        {
            switch (s.StartupVideoMode)
            {
                case "Random":
                {
                    var all = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.mp4") : Array.Empty<string>();
                    if (all.Length == 0) return "";
                    var pick = all[new Random().Next(all.Length)];
                    return pick;
                }
                case "Custom":
                {
                    var list = s.StartupVideoCustomList;
                    if (list == null || list.Count == 0) return "";

                    int nextIndex;
                    if (s.StartupVideoOrder == "Random")
                    {
                        nextIndex = new Random().Next(list.Count);
                    }
                    else
                    {
                        nextIndex = (s.StartupVideoCustomIndex + 1) % list.Count;
                    }
                    s.StartupVideoCustomIndex = nextIndex;
                    SettingsService.Save();

                    return Path.Combine(folder, list[nextIndex]);
                }
                default: // Fixed
                {
                    if (!string.IsNullOrEmpty(s.StartupVideoFileName))
                        return Path.Combine(folder, s.StartupVideoFileName);

                    return GetFirstVideoInFolder(folder);
                }
            }
        }
        catch
        {
            return GetFirstVideoInFolder(folder);
        }
    }

    private void FadeOutAndClose()
    {
        if (_closing) return;
        _closing = true;

        CompositionTarget.Rendering -= OnCompositionRendering;
        _warmupStopwatch?.Stop();

        if (!_hostShown)
        {
            _hostShown = true;
            Application.Current?.Dispatcher.BeginInvoke(_onFinished);
        }

        var fade = new DoubleAnimation(Opacity, 0, FadeOut);
        fade.Completed += (s, e) =>
        {
            try { VideoPlayer.Stop(); } catch { }
            Close();
            _ownDispatcher?.InvokeShutdown();
        };
        BeginAnimation(Window.OpacityProperty, fade);
    }

    private int _renderFrameCount = 0;
    private System.Diagnostics.Stopwatch? _warmupStopwatch;
    private bool _revealed = false;

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        double cachedWarmupMs = SettingsService.Current.StartupVideoWarmupMs;

        if (cachedWarmupMs > 0)
        {
            // Known-good delay from a previous run: wait it out silently, then snap everything on at once.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(cachedWarmupMs) };
            timer.Tick += (s, e2) =>
            {
                timer.Stop();
                RevealInstantly();
                CacheVideoInfoForNextLaunch(warmupMs: null); // keep existing warmup value, just refresh size/color
            };
            timer.Start();
        }
        else
        {
            // First run ever (no calibration yet): detect real first-frame render, then reveal and record the delay.
            try { VideoPlayer.Position = TimeSpan.FromMilliseconds(1); } catch { }
            _renderFrameCount = 0;
            _warmupStopwatch = System.Diagnostics.Stopwatch.StartNew();
            CompositionTarget.Rendering += OnCompositionRendering;
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        _renderFrameCount++;
        if (_renderFrameCount < 3) return;

        CompositionTarget.Rendering -= OnCompositionRendering;
        _warmupStopwatch?.Stop();
        double measuredMs = (_warmupStopwatch?.Elapsed.TotalMilliseconds ?? 300) + 60; // small safety margin

        RevealInstantly();
        CacheVideoInfoForNextLaunch(warmupMs: measuredMs);
    }

    private void RevealInstantly()
    {
        if (_revealed) return;
        _revealed = true;

        VideoPlayer.Position = TimeSpan.Zero;

        int framesToWait = 2;
        void WaitForCleanFrame(object? s, EventArgs e)
        {
            framesToWait--;
            if (framesToWait > 0) return;

            CompositionTarget.Rendering -= WaitForCleanFrame;

            VideoPlayer.Volume = 1;
            Activate();
            BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0, 1, FadeIn));
        }
        CompositionTarget.Rendering += WaitForCleanFrame;
    }

    private void CacheVideoInfoForNextLaunch(double? warmupMs)
    {
        try
        {
            var s = SettingsService.Current;

            if (warmupMs.HasValue)
                s.StartupVideoWarmupMs = warmupMs.Value;

            int pixelW = VideoPlayer.NaturalVideoWidth;
            int pixelH = VideoPlayer.NaturalVideoHeight;
            if (pixelW > 0 && pixelH > 0)
            {
                double screenW = SystemParameters.WorkArea.Width;
                double screenH = SystemParameters.WorkArea.Height;
                double maxW = screenW * 0.6;
                double maxH = screenH * 0.6;
                double scale = Math.Min(Math.Min(maxW / pixelW, maxH / pixelH), 1.0);

                s.StartupVideoCachedWidth  = pixelW * scale;
                s.StartupVideoCachedHeight = pixelH * scale;

                var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(VideoPlayer);
                var cropped = new CroppedBitmap(rtb, new Int32Rect(0, 0, 1, 1));
                var pixels = new byte[4];
                cropped.CopyPixels(pixels, 4, 0);
                var color = Color.FromRgb(pixels[2], pixels[1], pixels[0]);
                s.StartupVideoBgColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }

            SettingsService.Save();
        }
        catch { }
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e) => FadeOutAndClose();

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e) => FadeOutAndClose();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => FadeOutAndClose();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Space || e.Key == Key.Enter)
            FadeOutAndClose();
    }
}