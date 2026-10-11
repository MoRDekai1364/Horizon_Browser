namespace Horizon.Stealth.Controls;

public static class HomeGlassMotion
{
    public static event Action<int>? Pulsed;

    public static event Action? BlurSettingChanged;

    public static void Pulse(int durationMs) => Pulsed?.Invoke(durationMs);

    public static void RaiseBlurSettingChanged() => BlurSettingChanged?.Invoke();
}