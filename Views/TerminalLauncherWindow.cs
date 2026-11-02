using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Horizon.Stealth.Views;

public class TerminalLauncherWindow : Window
{
    private TextBox _cmdInput = null!;
    private TextBox _consoleOutput = null!;
    private TextBlock _statusBlock = null!;
    private Button _btnRun = null!;

    public TerminalLauncherWindow()
    {
        Title = "Terminal Command Launcher";
        Width = 720;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        BuildUI();
    }

    private void BuildUI()
    {
        var shell = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x10, 0x12, 0x16)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20)
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Preset Pills
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Console Output
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Input Command Row
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status Footer

        // 1. Header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

        var titleBlock = new TextBlock
        {
            Text = "\U0001F5A5\uFE0F Terminal Command Launcher (PowerShell Shell)",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(titleBlock);

        var closeBtn = new Button
        {
            Content = "\u2715",
            Width = 28,
            Height = 28,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeBtn.Click += (s, e) => Close();
        header.Children.Add(closeBtn);

        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // 2. Preset Diagnostics Strip
        var presetScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var presetStack = new StackPanel { Orientation = Orientation.Horizontal };

        Button PresetBtn(string label, string cmd)
        {
            var btn = new Button
            {
                Content = label,
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 10.5,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromArgb(0xD5, 0xFF, 0xFF, 0xFF))
            };
            btn.Click += (s, e) =>
            {
                _cmdInput.Text = cmd;
                RunCurrentCommand();
            };
            return btn;
        }

        presetStack.Children.Add(PresetBtn("IP Config", "ipconfig"));
        presetStack.Children.Add(PresetBtn("Ping Cloudflare", "ping -n 3 1.1.1.1"));
        presetStack.Children.Add(PresetBtn("Git Status", "git status -s"));
        presetStack.Children.Add(PresetBtn("System Info", "systeminfo | Select-String 'OS Name', 'Total Physical Memory'"));
        presetStack.Children.Add(PresetBtn("Top RAM Processes", "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 5 ProcessName, @{Name='RAM (MB)';Expression={[math]::Round($_.WorkingSet64/1MB,1)}}"));
        presetStack.Children.Add(PresetBtn("Active TCP Ports", "netstat -ano | Select-String 'ESTABLISHED' | Select-Object -First 6"));

        presetScroll.Content = presetStack;
        Grid.SetRow(presetScroll, 1);
        rootGrid.Children.Add(presetScroll);

        // 3. Console Output Screen
        _consoleOutput = new TextBox
        {
            FontSize = 11.5,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE)), // Matrix Green
            Background = new SolidColorBrush(Color.FromArgb(0x70, 0x05, 0x08, 0x0C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Text = "Horizon Browser Integrated Shell Engine [Version 1.0]\nType a PowerShell or system command below and press Enter to execute.\n\n"
        };
        Grid.SetRow(_consoleOutput, 2);
        rootGrid.Children.Add(_consoleOutput);

        // 4. Command Input Row
        var inputGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var promptBlock = new TextBlock
        {
            Text = "PS >",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0)
        };
        Grid.SetColumn(promptBlock, 0);
        inputGrid.Children.Add(promptBlock);

        _cmdInput = new TextBox
        {
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, monospace"),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6)
        };
        _cmdInput.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                RunCurrentCommand();
                e.Handled = true;
            }
        };
        Grid.SetColumn(_cmdInput, 1);
        inputGrid.Children.Add(_cmdInput);

        _btnRun = new Button
        {
            Content = "\u25B6 Execute",
            Height = 32,
            Padding = new Thickness(14, 0, 14, 0),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x50, 0x4A, 0x9E, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x4A, 0x9E, 0xFF)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White
        };
        _btnRun.Click += (s, e) => RunCurrentCommand();
        Grid.SetColumn(_btnRun, 2);
        inputGrid.Children.Add(_btnRun);

        var btnClear = new Button
        {
            Content = "\U0001F5D1",
            ToolTip = "Clear console output",
            Width = 32,
            Height = 32,
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Foreground = Brushes.White
        };
        btnClear.Click += (s, e) => _consoleOutput.Text = "Console cleared.\n\n";
        Grid.SetColumn(btnClear, 3);
        inputGrid.Children.Add(btnClear);

        Grid.SetRow(inputGrid, 3);
        rootGrid.Children.Add(inputGrid);

        // 5. Status Footer
        _statusBlock = new TextBlock
        {
            Text = "Ready. Commands execute asynchronously in safe user context.",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_statusBlock, 4);
        rootGrid.Children.Add(_statusBlock);

        shell.Child = rootGrid;
        Content = shell;

        Loaded += (s, e) => _cmdInput.Focus();
    }

    private async void RunCurrentCommand()
    {
        string cmd = _cmdInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(cmd)) return;

        _btnRun.IsEnabled = false;
        _cmdInput.IsEnabled = false;
        _statusBlock.Text = $"Running: {cmd}...";
        _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));

        _consoleOutput.AppendText($"\n> {cmd}\n");
        _consoleOutput.ScrollToEnd();

        try
        {
            string output = await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return "Failed to start process.";

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(8000);

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    return stdout + (string.IsNullOrWhiteSpace(stdout) ? "" : "\n") + "[Error]: " + stderr;
                }
                return stdout;
            });

            _consoleOutput.AppendText(string.IsNullOrWhiteSpace(output) ? "(Command completed with no output)\n" : output + "\n");
            _statusBlock.Text = "\u2713 Execution completed";
            _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x69, 0xF0, 0xAE));
        }
        catch (Exception ex)
        {
            _consoleOutput.AppendText($"[System Error]: {ex.Message}\n");
            _statusBlock.Text = "Command failed";
            _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
        }
        finally
        {
            _btnRun.IsEnabled = true;
            _cmdInput.IsEnabled = true;
            _cmdInput.Text = "";
            _cmdInput.Focus();
            _consoleOutput.ScrollToEnd();
        }
    }
}
