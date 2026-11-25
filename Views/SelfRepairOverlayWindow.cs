using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

/// <summary>
/// Sleek Ctrl+Tab glass Self-Repair Overlay window shown during automatic crash recovery.
/// Explicitly non-topmost (Topmost = false) so it appears cleanly on top of Horizon Browser
/// when triggered, without ever pinning permanently over third-party windows.
/// </summary>
public sealed class SelfRepairOverlayWindow : Window
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

    private readonly CrashReportPayload _payload;
    private readonly string _payloadPath;
    private readonly int _attemptNumber;
    private readonly CancellationTokenSource _cts = new();

    private readonly Border _cardBg;
    private readonly StackPanel _stepsPanel;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _statusLine;
    private readonly Dictionary<string, (TextBlock Icon, TextBlock Detail)> _stepRows = new();
    private bool _escalatedToReport;

    public SelfRepairOverlayWindow(CrashReportPayload payload, string payloadPath, int attemptNumber)
    {
        _payload = payload;
        _payloadPath = payloadPath;
        _attemptNumber = attemptNumber;

        Title = "Horizon Browser \u2014 Self-Repair";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = false; // Appears over browser when launched, NEVER permanently pinned over third-party windows!
        ShowInTaskbar = true;
        Background = Brushes.Transparent;

        _cardBg = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = SettingsService.Current.DisableBlurEffects
                ? new SolidColorBrush(Color.FromArgb(0xF5, 0x12, 0x14, 0x1A))
                : new SolidColorBrush(Color.FromArgb(0x96, 0x0E, 0x10, 0x16)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24, 22, 24, 20)
        };

        var root = new StackPanel();

        // Top header row (draggable)
        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        headerRow.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x28, 0x60, 0xCD, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x60, 0xCD, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = $"ATTEMPT {attemptNumber} OF {SelfRepairService.MaxRepairAttempts}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold
        };
        DockPanel.SetDock(badge, Dock.Right);
        headerRow.Children.Add(badge);

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "Horizon Self-Repair & Restore",
            Foreground = Brushes.White,
            FontSize = 17,
            FontWeight = FontWeights.Bold
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = attemptNumber == 1
                ? "Horizon encountered a crash and is automatically repairing itself."
                : "Running deep recovery to isolate the crash source and restore stability.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            FontSize = 12,
            Margin = new Thickness(0, 3, 10, 0),
            TextWrapping = TextWrapping.Wrap
        });
        headerRow.Children.Add(titleStack);
        root.Children.Add(headerRow);

        // Live checklist container
        _stepsPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 14) };
        root.Children.Add(_stepsPanel);

        // Progress bar
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 6,
            Height = 6,
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(_progressBar);

        // Footer status + Skip & Report button
        var footer = new DockPanel();
        var btnSkip = new Button
        {
            Content = "Skip & Report Crash",
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnSkip.Click += (_, _) => EscalateToCrashReporter();
        DockPanel.SetDock(btnSkip, Dock.Right);
        footer.Children.Add(btnSkip);

        _statusLine = new TextBlock
        {
            Text = "Initializing diagnostics...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 10, 0)
        };
        footer.Children.Add(_statusLine);
        root.Children.Add(footer);

        _cardBg.Child = root;
        Content = _cardBg;

        SourceInitialized += (_, _) => EnableCtrlTabGlass();
        Loaded += async (_, _) => await RunPipelineAsync();
        Closed += (_, _) => { try { _cts.Cancel(); } catch { } };
    }

    private void EnableCtrlTabGlass()
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

            int corner = 2;
            DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

            var accent = new AccentPolicy { AccentState = 3 }; // ACCENT_ENABLE_BLURBEHIND
            int size = Marshal.SizeOf(accent);
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new CompositionData { Attribute = 19, Data = ptr, SizeOfData = size };
            if (SetWindowCompositionAttribute(hwnd, ref data) == 0)
                _cardBg.Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x12, 0x14, 0x1A));
        }
        catch
        {
            _cardBg.Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x12, 0x14, 0x1A));
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
        }
    }

    private void ApplyStepUpdate(RepairStepUpdate u)
    {
        Dispatcher.Invoke(() =>
        {
            _progressBar.Value = u.ProgressPercent;
            _statusLine.Text = u.Detail;

            (string glyph, Color color) = u.Status switch
            {
                "ok"       => ("\u2713", Color.FromRgb(0x34, 0xD3, 0x99)),
                "repaired" => ("\u2728", Color.FromRgb(0x60, 0xCD, 0xFF)),
                "warn"     => ("\u26A0", Color.FromRgb(0xFB, 0xBF, 0x24)),
                _          => ("\u25CF", Color.FromRgb(0x94, 0xA3, 0xB8))
            };

            if (_stepRows.TryGetValue(u.StepId, out var existing))
            {
                existing.Icon.Text = glyph;
                existing.Icon.Foreground = new SolidColorBrush(color);
                existing.Detail.Text = u.Detail;
                return;
            }

            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconTb = new TextBlock
            {
                Text = glyph,
                Foreground = new SolidColorBrush(color),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 0, 0)
            };
            Grid.SetColumn(iconTb, 0);

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = u.Title,
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold
            });
            var detailTb = new TextBlock
            {
                Text = u.Detail,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            textStack.Children.Add(detailTb);
            Grid.SetColumn(textStack, 1);

            row.Children.Add(iconTb);
            row.Children.Add(textStack);
            _stepsPanel.Children.Add(row);
            _stepRows[u.StepId] = (iconTb, detailTb);
        });
    }

    private async Task RunPipelineAsync()
    {
        var outcome = await SelfRepairService.RunRepairAsync(_payload, _attemptNumber, ApplyStepUpdate, _cts.Token);
        if (_escalatedToReport) return;

        await Task.Delay(550);
        if (_escalatedToReport) return;

        // Relaunch Horizon Browser cleanly after self-repair
        try
        {
            string exe = DefaultBrowserService.ResolveExecutablePath();
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--post-repair={_attemptNumber}",
                    UseShellExecute = true
                });
            }
        }
        catch { }

        Close();
    }

    private void EscalateToCrashReporter()
    {
        if (_escalatedToReport) return;
        _escalatedToReport = true;
        try { _cts.Cancel(); } catch { }

        try
        {
            string exe = DefaultBrowserService.ResolveExecutablePath();
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe) && !string.IsNullOrWhiteSpace(_payloadPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--crash-report=\"{_payloadPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch { }

        Close();
    }
}
