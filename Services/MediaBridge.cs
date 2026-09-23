using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Horizon.Stealth.Services;

public static class MediaBridge
{
    public static string Title         { get; private set; } = "";
    public static string Artist        { get; private set; } = "";
    public static bool   IsPlaying     { get; private set; } = false;
    public static bool   IsMuted       { get; private set; } = false;
    public static bool   HasVideo      { get; private set; } = false;
    public static bool   HasMedia      { get; private set; } = false;
    public static bool   IsAudioOnly   { get; private set; } = false;
    public static string SourceHost    { get; private set; } = "";
    public static IReadOnlyList<Color> PaletteColors { get; private set; } = Array.Empty<Color>();

    public static event Action? Updated;

    // Mirrors the exact live gradient the tab-coloring engine (MainWindow.StartColorAnimation)
    // computes each tick, so the homepage visualizer can reuse it directly instead of animating independently.
    public static Color  GradientC0         { get; private set; } = Colors.Transparent;
    public static Color  GradientC1         { get; private set; } = Colors.Transparent;
    public static Color  GradientC2         { get; private set; } = Colors.Transparent;
    public static double GradientMidOffset  { get; private set; } = 0.5;

    public static event Action? GradientUpdated;

    public static void PublishGradient(Color c0, Color c1, Color c2, double midOffset)
    {
        GradientC0 = c0;
        GradientC1 = c1;
        GradientC2 = c2;
        GradientMidOffset = midOffset;
        GradientUpdated?.Invoke();
    }

    public static void Publish(string title, bool isPlaying, bool isMuted, bool hasVideo, bool hasMedia, bool isAudioOnly = false, string artist = "", string sourceHost = "", IReadOnlyList<Color>? paletteColors = null)
    {
        Title         = title;
        Artist        = artist;
        IsPlaying     = isPlaying;
        IsMuted       = isMuted;
        HasVideo      = hasVideo;
        HasMedia      = hasMedia;
        IsAudioOnly   = isAudioOnly;
        SourceHost    = sourceHost;
        PaletteColors = paletteColors ?? Array.Empty<Color>();
        Updated?.Invoke();
    }

    // Reuses the same hook MainWindow already wires for background/taskbar media
    // control (BackgroundKeepAliveService.OnMediaCommandReceived = ExecuteMediaWidgetCommand).
    // Valid commands: PLAYPAUSE, PREV, NEXT, MUTE, AUDIOONLY.
    public static void SendCommand(string cmd) => BackgroundKeepAliveService.OnMediaCommandReceived?.Invoke(cmd);
}