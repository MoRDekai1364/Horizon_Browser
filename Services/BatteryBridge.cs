using System;
using System.Windows.Forms;
using System.Windows.Threading;

namespace Horizon.Stealth.Services;

public static class BatteryBridge
{
    private static DispatcherTimer? _timer;
    private static bool _hasBattery;
    private static int _percent;
    private static bool _isCharging;
    private static TimeSpan? _timeRemaining;

    public static event Action? Updated;

    public static bool HasBattery => _hasBattery;
    public static int Percent => _percent;
    public static bool IsCharging => _isCharging;
    public static TimeSpan? TimeRemaining => _timeRemaining;

    public static void Start()
    {
        if (_timer != null) return;
        Refresh();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    public static void Refresh()
    {
        var status = SystemInformation.PowerStatus;
        _hasBattery = status.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery;
        _percent = (int)Math.Round(status.BatteryLifePercent * 100.0);
        _isCharging = status.PowerLineStatus == PowerLineStatus.Online;
        _timeRemaining = status.BatteryLifeRemaining >= 0
            ? TimeSpan.FromSeconds(status.BatteryLifeRemaining)
            : null;
        Updated?.Invoke();
    }
}