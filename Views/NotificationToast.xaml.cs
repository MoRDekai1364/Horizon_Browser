using System;
using System.Windows;
using System.Windows.Threading;

namespace Horizon.Stealth.Views;

public partial class NotificationToast : Window
{
    private readonly DispatcherTimer _timer = new();

    public NotificationToast(string origin, string title, string body)
    {
        InitializeComponent();

        TxtOrigin.Text = string.IsNullOrWhiteSpace(origin) ? "Notification" : origin;

        if (!string.IsNullOrWhiteSpace(title))
        { TxtTitle.Text = title; TxtTitle.Visibility = Visibility.Visible; }

        if (!string.IsNullOrWhiteSpace(body))
        { TxtBody.Text = body; TxtBody.Visibility = Visibility.Visible; }

        Loaded += (_, _) => PositionBottomRight();

        _timer.Interval = TimeSpan.FromSeconds(6);
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        _timer.Start();
    }

    private void PositionBottomRight()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right  - ActualWidth  - 16;
        Top  = work.Bottom - ActualHeight - 16;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }
}