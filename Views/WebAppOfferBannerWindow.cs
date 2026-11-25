using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

/// <summary>
/// Floating top-left WebApp external-link OfferBar rendered with the exact Ctrl+Tab native DWM blur
/// (#8C0E0E0E + ACCENT_ENABLE_BLURBEHIND), so live webpage pixels blur cleanly underneath it while
/// leaving the top-right 260px auto-show header sensor completely unobstructed.
/// </summary>
internal sealed class WebAppOfferBannerWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GlassMargins { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref GlassMargins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);

    private readonly Border _cardBg;
    private readonly TextBlock _offerText;

    public event Action? StayClicked;
    public event Action? HorizonClicked;
    public event Action? BrowserClicked;
    public event Action? NeverClicked;
    public event Action? DismissClicked;

    public WebAppOfferBannerWindow(Window owner)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;

        _cardBg = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = SettingsService.Current.DisableBlurEffects
                ? new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x12, 0x12))
                : new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x0E, 0x0E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6)
        };

        var dock = new DockPanel();

        var iconBadge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x28, 0x3B, 0x82, 0xF6)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x60, 0xA5, 0xFA)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "\u2197",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        DockPanel.SetDock(iconBadge, Dock.Left);
        dock.Children.Add(iconBadge);

        var btnStay = MakeButton("Stay here", accent: true, subtle: false);
        btnStay.Click += (_, _) => StayClicked?.Invoke();
        DockPanel.SetDock(btnStay, Dock.Left);
        dock.Children.Add(btnStay);

        var btnHorizon = MakeButton("Open in Horizon", accent: false, subtle: false);
        btnHorizon.Click += (_, _) => HorizonClicked?.Invoke();
        DockPanel.SetDock(btnHorizon, Dock.Left);
        dock.Children.Add(btnHorizon);

        var btnBrowser = MakeButton("Default browser", accent: false, subtle: false);
        btnBrowser.Click += (_, _) => BrowserClicked?.Invoke();
        DockPanel.SetDock(btnBrowser, Dock.Left);
        dock.Children.Add(btnBrowser);

        var btnNever = MakeButton("Don't ask again", accent: false, subtle: true);
        btnNever.Click += (_, _) => NeverClicked?.Invoke();
        DockPanel.SetDock(btnNever, Dock.Left);
        dock.Children.Add(btnNever);

        var btnDismiss = MakeButton("\u2715", accent: false, subtle: true);
        btnDismiss.Margin = new Thickness(6, 0, 0, 0);
        btnDismiss.Padding = new Thickness(7, 0, 7, 0);
        btnDismiss.Click += (_, _) => DismissClicked?.Invoke();
        DockPanel.SetDock(btnDismiss, Dock.Right);
        dock.Children.Add(btnDismiss);

        _offerText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEB, 0xF1, 0xF7)),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 4, 0)
        };
        dock.Children.Add(_offerText);

        _cardBg.Child = dock;
        Content = _cardBg;

        SourceInitialized += (_, _) => EnableCtrlTabBlur();
    }

    public void ShowOffer(string message, Window owner)
    {
        _offerText.Text = message;
        SyncToOwner(owner);
        if (!IsVisible) Show();
        SyncToOwner(owner);
    }

    public void SyncToOwner(Window owner)
    {
        if (owner == null || owner.WindowState == WindowState.Minimized || !owner.IsVisible)
        {
            if (IsVisible) Hide();
            return;
        }

        try
        {
            var source = PresentationSource.FromVisual(owner);
            if (source?.CompositionTarget == null) return;
            Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
            Point tlPx = owner.PointToScreen(new Point(0, 0));
            Point tlDip = fromDevice.Transform(tlPx);

            Left = tlDip.X + 8;
            Top = tlDip.Y + 8;
            MaxWidth = Math.Max(320, owner.ActualWidth - 276);
        }
        catch { }
    }

    private void EnableCtrlTabBlur()
    {
        if (SettingsService.Current.DisableBlurEffects) return;

        IntPtr ptr = IntPtr.Zero;
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;

            var margins = new GlassMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            int corner = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

            var accent = new AccentPolicy { AccentState = 3 }; // ACCENT_ENABLE_BLURBEHIND (Ctrl+Tab exact blur)
            int size = Marshal.SizeOf(accent);
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new CompositionData { Attribute = 19, Data = ptr, SizeOfData = size };
            if (SetWindowCompositionAttribute(hwnd, ref data) == 0)
                _cardBg.Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x12, 0x14, 0x19));
        }
        catch
        {
            _cardBg.Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x12, 0x14, 0x19));
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
        }
    }

    private static Button MakeButton(string text, bool accent, bool subtle)
    {
        var btn = new Button
        {
            Content = text,
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = accent
                ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
                : (subtle ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))),
            Foreground = accent
                ? Brushes.White
                : (subtle ? new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)) : new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6))),
            BorderBrush = accent
                ? new SolidColorBrush(Color.FromArgb(0x66, 0x93, 0xC5, 0xFD))
                : (subtle ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))),
            BorderThickness = new Thickness(subtle ? 0 : 1)
        };

        var factory = new FrameworkElementFactory(typeof(Border));
        factory.Name = "Bd";
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(cp);

        var tpl = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(accent ? Color.FromRgb(0x25, 0x63, 0xEB) : Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF)),
            "Bd"));
        tpl.Triggers.Add(hover);
        btn.Template = tpl;
        return btn;
    }
}
