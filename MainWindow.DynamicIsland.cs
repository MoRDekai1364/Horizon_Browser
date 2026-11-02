using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth;

public partial class MainWindow : Window
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Dynamic Island / Executive Frosted Glass Hub Multi-Mode Engine
    // ─────────────────────────────────────────────────────────────────────────────
    public enum DynamicIslandMode
    {
        Ambient,
        Timer,
        Notes,
        Download,
        Media
    }

    private DynamicIslandMode _currentIslandMode = DynamicIslandMode.Ambient;
    private string _userSelectedDeck = "Ambient";
    private bool _isDynamicIslandExpanded = false;
    private DispatcherTimer? _islandLiveTimer;
    private DispatcherTimer? _islandWaveformTimer;
    private readonly Random _islandRnd = new();
    private DateTime _lastNoteModified = DateTime.MinValue;

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

    private void SetupDynamicIsland()
    {
        if (DynamicIslandPopup == null) return;
        DynamicIslandPopup.CustomPopupPlacementCallback = DynamicIslandPlacementCallback;
        // Hardware blur disabled to prevent Win32 layered popup black rectangle

        MediaBridge.Updated += () => Dispatcher.BeginInvoke(new Action(UpdateDynamicIslandContent));

        _islandWaveformTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _islandWaveformTimer.Tick += (_, _) =>
        {
            if (MediaBridge.IsPlaying)
            {
                if (WaveBar1 != null && WaveBar2 != null && WaveBar3 != null && WaveBar4 != null)
                {
                    WaveBar1.Height = _islandRnd.Next(4, 13);
                    WaveBar2.Height = _islandRnd.Next(6, 16);
                    WaveBar3.Height = _islandRnd.Next(4, 12);
                    WaveBar4.Height = _islandRnd.Next(7, 15);
                }

                if (DynamicIslandAudioGlow != null && AudioGlowShadow != null)
                {
                    // Gentle breathing visualizer glow around the pill edges
                    double targetOpacity = 0.60 + _islandRnd.NextDouble() * 0.35;
                    double targetBlur = 18.0 + _islandRnd.NextDouble() * 8.0;
                    DynamicIslandAudioGlow.Opacity = targetOpacity;
                    AudioGlowShadow.BlurRadius = targetBlur;
                }
            }
            else
            {
                if (DynamicIslandAudioGlow != null && DynamicIslandAudioGlow.Opacity > 0)
                {
                    DynamicIslandAudioGlow.Opacity = 0.0;
                }
            }
        };

        _islandLiveTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _islandLiveTimer.Tick += (_, _) =>
        {
            if (DynamicIslandPopup != null && DynamicIslandPopup.IsOpen)
            {
                UpdateDynamicIslandContent();
            }
        };

        UpdateDynamicIslandContent();
    }

    private void PnlIslandTabInfo_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleHeader(true);
    }

    private double? _islandUserOffsetX = null;
    private bool _isDraggingIsland = false;
    private Point _islandDragStartMouse;
    private double _islandDragInitialX;

    private CustomPopupPlacement[] DynamicIslandPlacementCallback(Size popupSize, Size targetSize, Point offset)
    {
        double defaultX = Math.Max(0.0, (targetSize.Width - popupSize.Width) / 2.0);
        double x = _islandUserOffsetX ?? defaultX;
        double maxX = Math.Max(10.0, targetSize.Width - popupSize.Width - 10.0);
        x = Math.Clamp(x, 10.0, maxX);
        double y = 2.0; // Flush top notch
        return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal) };
    }

    private void DynamicIslandCompact_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ExpandDynamicIsland(true);
    }

    private void DynamicIsland_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_isDraggingIsland && !_isDynamicIslandExpanded)
        {
            if (DynamicIslandScale != null)
            {
                var sAnim = new DoubleAnimation(1.0, 1.03, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                DynamicIslandScale.BeginAnimation(ScaleTransform.ScaleXProperty, sAnim);
                DynamicIslandScale.BeginAnimation(ScaleTransform.ScaleYProperty, sAnim);
            }
            if (DynamicIslandShadow != null)
            {
                DynamicIslandShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                    new DoubleAnimation(0.60, 0.85, TimeSpan.FromMilliseconds(160)));
            }
        }
    }

    private void DynamicIsland_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingIsland && !_isDynamicIslandExpanded)
        {
            if (DynamicIslandScale != null)
            {
                var sAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                DynamicIslandScale.BeginAnimation(ScaleTransform.ScaleXProperty, sAnim);
                DynamicIslandScale.BeginAnimation(ScaleTransform.ScaleYProperty, sAnim);
            }
            if (DynamicIslandShadow != null)
            {
                DynamicIslandShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                    new DoubleAnimation(0.60, TimeSpan.FromMilliseconds(180)));
            }
        }
    }

    private void DynamicIsland_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as FrameworkElement;
        if (src?.Name != null && (src.Name.StartsWith("BtnIsland") || src.Name.StartsWith("Chip") || src.Name.StartsWith("TxtIsland")))
            return;

        _isDraggingIsland = true;
        _islandDragStartMouse = e.GetPosition(this);
        double defaultX = Math.Max(0.0, (this.ActualWidth - (DynamicIslandPopup?.ActualWidth ?? 600)) / 2.0);
        _islandDragInitialX = _islandUserOffsetX ?? defaultX;
        DynamicIslandPill?.CaptureMouse();
    }

    private void DynamicIsland_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingIsland && e.LeftButton == MouseButtonState.Pressed && DynamicIslandPopup != null)
        {
            var curMouse = e.GetPosition(this);
            double deltaX = curMouse.X - _islandDragStartMouse.X;
            if (Math.Abs(deltaX) > 2.0)
            {
                double targetW = this.ActualWidth;
                double popupW = DynamicIslandPopup.ActualWidth > 0 ? DynamicIslandPopup.ActualWidth : 600;
                double maxX = Math.Max(10.0, targetW - popupW - 10.0);
                _islandUserOffsetX = Math.Clamp(_islandDragInitialX + deltaX, 10.0, maxX);
                DynamicIslandPopup.HorizontalOffset += 0.0001;
                DynamicIslandPopup.HorizontalOffset -= 0.0001;
            }
        }
    }

    private void DynamicIsland_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingIsland)
        {
            _isDraggingIsland = false;
            DynamicIslandPill?.ReleaseMouseCapture();
            var curMouse = e.GetPosition(this);
            if (Math.Abs(curMouse.X - _islandDragStartMouse.X) < 4.0)
            {
                // Only expand when in compact resting state; never close on interior pill clicks
                if (!_isDynamicIslandExpanded)
                {
                    ExpandDynamicIsland(true);
                }
            }
        }
    }

    private void AnimatePillDimensions(double targetW, double targetH, double targetRadius)
    {
        if (DynamicIslandPill == null) return;

        double curW = DynamicIslandPill.ActualWidth > 0 ? DynamicIslandPill.ActualWidth : (double.IsNaN(DynamicIslandPill.Width) ? 120.0 : DynamicIslandPill.Width);
        double curH = DynamicIslandPill.ActualHeight > 0 ? DynamicIslandPill.ActualHeight : (double.IsNaN(DynamicIslandPill.Height) ? 28.0 : DynamicIslandPill.Height);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var wAnim = new DoubleAnimation(curW, targetW, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
        var hAnim = new DoubleAnimation(curH, targetH, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };

        DynamicIslandPill.BeginAnimation(FrameworkElement.WidthProperty, wAnim);
        DynamicIslandPill.BeginAnimation(FrameworkElement.HeightProperty, hAnim);
        DynamicIslandPill.CornerRadius = new CornerRadius(targetRadius);
    }

    private void ExpandDynamicIsland(bool expand)
    {
        _isDynamicIslandExpanded = expand;
        if (DynamicIslandCompact == null || DynamicIslandExpanded == null || DynamicIslandPill == null) return;

        if (expand)
        {
            DynamicIslandCompact.Visibility = Visibility.Collapsed;
            DynamicIslandExpanded.Visibility = Visibility.Visible;
            AnimatePillDimensions(580.0, 82.0, 18.0);
            SelectExpandedDeck(string.IsNullOrEmpty(_userSelectedDeck) ? "Ambient" : _userSelectedDeck);
        }
        else
        {
            DynamicIslandExpanded.Visibility = Visibility.Collapsed;
            DynamicIslandCompact.Visibility = Visibility.Visible;
            AnimatePillDimensions(120.0, 28.0, 14.0);
        }

        if (DynamicIslandPopup != null)
        {
            DynamicIslandPopup.HorizontalOffset += 0.0001;
            DynamicIslandPopup.HorizontalOffset -= 0.0001;
        }
    }

    private void BtnIslandChip_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            _userSelectedDeck = tag;
            SelectExpandedDeck(tag);
        }
    }

    private void SelectExpandedDeck(string deck)
    {
        // Deactivate all decks
        if (IslandDeckAmbient  != null) IslandDeckAmbient.Visibility  = Visibility.Collapsed;
        if (IslandDeckTimer    != null) IslandDeckTimer.Visibility    = Visibility.Collapsed;
        if (IslandDeckMedia    != null) IslandDeckMedia.Visibility    = Visibility.Collapsed;
        if (IslandDeckNotes    != null) IslandDeckNotes.Visibility    = Visibility.Collapsed;
        if (IslandDeckDownload != null) IslandDeckDownload.Visibility = Visibility.Collapsed;

        // Reset chip styles
        void StyleChip(Border? chip, bool active)
        {
            if (chip == null) return;
            chip.Background = active ? new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            chip.BorderBrush = active ? new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            if (chip.Child is TextBlock tb)
                tb.Foreground = active ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x8E, 0x92, 0x97));
        }

        StyleChip(ChipAmbient,  deck == "Ambient");
        StyleChip(ChipTimer,    deck == "Timer");
        StyleChip(ChipMedia,    deck == "Media");
        StyleChip(ChipNotes,    deck == "Notes");
        StyleChip(ChipDownload, deck == "Download");

        switch (deck)
        {
            case "Timer":
                if (IslandDeckTimer != null) IslandDeckTimer.Visibility = Visibility.Visible;
                if (BtnIslandMute != null) BtnIslandMute.Visibility = Visibility.Collapsed;
                break;
            case "Media":
                if (IslandDeckMedia != null) IslandDeckMedia.Visibility = Visibility.Visible;
                if (BtnIslandMute != null) BtnIslandMute.Visibility = Visibility.Visible;
                break;
            case "Notes":
                if (IslandDeckNotes != null) IslandDeckNotes.Visibility = Visibility.Visible;
                if (BtnIslandMute != null) BtnIslandMute.Visibility = Visibility.Collapsed;
                break;
            case "Download":
                if (IslandDeckDownload != null) IslandDeckDownload.Visibility = Visibility.Visible;
                if (BtnIslandMute != null) BtnIslandMute.Visibility = Visibility.Collapsed;
                break;
            default:
                if (IslandDeckAmbient != null) IslandDeckAmbient.Visibility = Visibility.Visible;
                if (BtnIslandMute != null) BtnIslandMute.Visibility = Visibility.Collapsed;
                break;
        }

        UpdateDynamicIslandContent();
    }

    private void BtnIslandCollapse_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ExpandDynamicIsland(false);
    }

    private void BtnIslandPlayPause_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        MediaBridge.SendCommand("PLAYPAUSE");
    }

    private void BtnIslandPrev_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        MediaBridge.SendCommand("PREV");
    }

    private void BtnIslandNext_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        MediaBridge.SendCommand("NEXT");
    }

    private void BtnIslandMute_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        MediaBridge.SendCommand("MUTE");
    }

    private void BtnIslandRevealHeader_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleHeader(true);
    }

    private void BtnIslandTimerToggle_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_swClock.IsRunning)
        {
            _swClock.Stop();
        }
        else if (_timerIsRunning)
        {
            _timerIsRunning = false;
        }
        else if (_clockMode == 4)
        {
            _swClock.Start();
        }
        else
        {
            _timerStartedAt = DateTime.Now;
            _timerIsRunning = true;
        }
        UpdateDynamicIslandContent();
    }

    private void BtnIslandTimerReset_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_clockMode == 4 || _swClock.Elapsed > TimeSpan.Zero)
        {
            _swClock.Reset();
        }
        else
        {
            _timerIsRunning = false;
        }
        UpdateDynamicIslandContent();
    }

    private void BtnIslandOpenTimerWindow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_clockMode == 4) OpenStopwatchWindow();
        else OpenTimerWindow();
    }

    private void BtnIslandOpenNotesWindow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenNotesWindow();
    }

    private void BtnIslandOpenDownloads_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            CurrentBrowser?.MainWebView?.CoreWebView2?.OpenDefaultDownloadDialog();
        }
        catch { }
    }

    public void UpdateDynamicIslandVisibility()
    {
        if (DynamicIslandPopup == null) return;

        bool headerHidden = !_isHeaderOpen || (HeaderContainer != null && (HeaderContainer.ActualHeight <= 4 || HeaderContainer.Height == 0));
        bool autoHideEnabled = SettingsService.Current.AutoHideHeader;
        bool shouldShow = autoHideEnabled && headerHidden && !_isFullscreen && this.IsVisible;

        if (DynamicIslandPopup.IsOpen != shouldShow)
        {
            DynamicIslandPopup.IsOpen = shouldShow;
            if (shouldShow)
            {
                // Smooth drop-in entrance animation
                if (DynamicIslandTranslate != null)
                {
                    DynamicIslandTranslate.Y = -24;
                    var yAnim = new DoubleAnimation(-24, 0, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                    };
                    DynamicIslandTranslate.BeginAnimation(TranslateTransform.YProperty, yAnim);
                }
                if (DynamicIslandPill != null)
                {
                    DynamicIslandPill.Opacity = 0.0;
                    var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
                    DynamicIslandPill.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                }

                _islandLiveTimer?.Start();
                UpdateDynamicIslandContent();
            }
            else
            {
                _islandLiveTimer?.Stop();
                _islandWaveformTimer?.Stop();
            }
        }
    }

    private DynamicIslandMode DetermineCurrentMode(bool hasMediaPlaying, bool hasActiveDownload, bool hasActiveTimer, bool hasRecentNotes)
    {
        if (hasActiveTimer)    return DynamicIslandMode.Timer;
        if (hasActiveDownload) return DynamicIslandMode.Download;
        if (hasMediaPlaying)   return DynamicIslandMode.Media;
        if (hasRecentNotes)    return DynamicIslandMode.Notes;
        return DynamicIslandMode.Ambient;
    }

    private void UpdateDynamicIslandContent()
    {
        if (DynamicIslandPill == null) return;

        bool isPlaying = MediaBridge.IsPlaying && !string.IsNullOrEmpty(MediaBridge.Title);
        bool hasMedia = MediaBridge.HasMedia && !string.IsNullOrEmpty(MediaBridge.Title);
        bool isDownloading = _allTabs.Any(t => t.IsActiveDownload);
        bool isTimerActive = _timerIsRunning || _swClock.IsRunning;
        string notesPreview = GetNotesPreview();
        bool hasRecentNotes = !string.IsNullOrWhiteSpace(notesPreview);

        _currentIslandMode = DetermineCurrentMode(isPlaying, isDownloading, isTimerActive, hasRecentNotes);

        // Update compact icons (Image 3)
        if (TxtCompactLeftIcon != null)
        {
            TxtCompactLeftIcon.Text = isPlaying ? "\u266B" : "\u2726";
            TxtCompactLeftIcon.Foreground = isPlaying
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        }
        if (TxtCompactRightIcon != null)
        {
            TxtCompactRightIcon.Text = "\u23F1";
            TxtCompactRightIcon.Foreground = isTimerActive
                ? new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF))
                : new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xD5, 0x4F));
        }

        // Mode 1: Ambient Data (Image 0 & 2)
        var curTab = CurrentTab;
        string tabTitle = curTab?.Title ?? "Horizon Browser";
        string shortTitle = tabTitle.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Horizon Browser";
        if (IslandAmbientTabTitle != null) IslandAmbientTabTitle.Text = shortTitle.Trim();

        if (IslandAmbientTabDomain != null)
        {
            string domain = "";
            if (curTab != null && !string.IsNullOrEmpty(curTab.Url) && Uri.TryCreate(curTab.Url, UriKind.Absolute, out var uri))
                domain = uri.Host;
            IslandAmbientTabDomain.Text = !string.IsNullOrEmpty(domain) ? domain : "horizon://browser";
        }

        // Favicon resolution
        if (IslandTabFaviconImage != null && IslandTabFaviconFallback != null)
        {
            string? favUrl = null;
            try
            {
                favUrl = CurrentBrowser?.MainWebView?.CoreWebView2?.FaviconUri;
            }
            catch { }

            if (!string.IsNullOrEmpty(favUrl) && Uri.TryCreate(favUrl, UriKind.Absolute, out var favUri) && (favUri.Scheme == "http" || favUri.Scheme == "https" || favUri.Scheme == "data"))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = favUri;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    IslandTabFaviconImage.Source = bmp;
                    IslandTabFaviconImage.Visibility = Visibility.Visible;
                    IslandTabFaviconFallback.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    IslandTabFaviconImage.Visibility = Visibility.Collapsed;
                    IslandTabFaviconFallback.Visibility = Visibility.Visible;
                }
            }
            else
            {
                IslandTabFaviconImage.Visibility = Visibility.Collapsed;
                IslandTabFaviconFallback.Visibility = Visibility.Visible;
            }
        }

        // Status Badges on right of Ambient Deck
        if (TxtIslandBadgeTimer != null)
        {
            if (isTimerActive)
                TxtIslandBadgeTimer.Text = _clockMode == 4 ? FormatElapsed(_swClock.Elapsed) : FormatTimer();
            else
                TxtIslandBadgeTimer.Text = "Timer";
        }
        if (TxtIslandBadgeNotes != null)
        {
            string noteSub = (notesPreview.Length > 12 ? notesPreview.Substring(0, 12) + "..." : notesPreview).Replace('\r', ' ').Replace('\n', ' ');
            TxtIslandBadgeNotes.Text = string.IsNullOrEmpty(noteSub) ? "Notes" : noteSub;
        }

        // Mode 2: Timer Data
        string timerString = _clockMode == 4 ? FormatElapsed(_swClock.Elapsed) : FormatTimer();
        if (IslandExpTimerDigits != null) IslandExpTimerDigits.Text = timerString;
        if (TxtIslandExpTimerToggle != null) TxtIslandExpTimerToggle.Text = (_swClock.IsRunning || _timerIsRunning) ? "\u23F8 Pause" : "\u25B6 Start";
        if (IslandExpTimerStatus != null)    IslandExpTimerStatus.Text    = (_swClock.IsRunning || _timerIsRunning) ? "Running" : "Ready";

        // Mode 3: Notes Data
        if (IslandExpNotesSnippet != null) IslandExpNotesSnippet.Text = string.IsNullOrEmpty(notesPreview) ? "No notes recorded yet. Click below to add." : notesPreview;

        // Mode 4: Download Data
        if (isDownloading)
        {
            if (IslandExpDownloadInfo != null) IslandExpDownloadInfo.Text = "Active file download in progress";
        }

        // Mode 5: Media Data
        if (isPlaying)
        {
            if (IslandWaveform != null) IslandWaveform.Visibility = Visibility.Visible;
            if (_islandWaveformTimer?.IsEnabled == false) _islandWaveformTimer.Start();
        }
        else
        {
            if (IslandWaveform != null) IslandWaveform.Visibility = Visibility.Collapsed;
            if (_islandWaveformTimer?.IsEnabled == true) _islandWaveformTimer.Stop();
        }

        string mediaTitle = (MediaBridge.Title ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Live Audio";
        if (IslandExpandedTrack != null) IslandExpandedTrack.Text = mediaTitle.Trim();
        if (IslandExpandedArtist != null)
        {
            IslandExpandedArtist.Text = !string.IsNullOrEmpty(MediaBridge.Artist)
                ? MediaBridge.Artist
                : (!string.IsNullOrEmpty(MediaBridge.SourceHost) ? MediaBridge.SourceHost : "Web Media");
        }

        string playPauseGlyph = isPlaying ? "\u23F8" : "\u25B6";
        if (TxtIslandExpandedPlayPause != null) TxtIslandExpandedPlayPause.Text = playPauseGlyph;

        if (TxtIslandMute != null)
        {
            TxtIslandMute.Text = MediaBridge.IsMuted ? "\U0001F507" : "\U0001F50A";
        }
    }
}
