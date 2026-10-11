namespace Horizon.Stealth.Controls;

public static class HomeGlassMotion
{
    public static event Action<int>? Pulsed;

    public static void Pulse(int durationMs) => Pulsed?.Invoke(durationMs);
}