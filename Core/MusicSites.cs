using System;
using System.Linq;

namespace Horizon.Stealth.Core;

public static class MusicSites
{
    private static readonly string[] HostSuffixes =
    {
        "music.youtube.com",
        "spotify.com",
        "music.apple.com",
        "deezer.com",
        "tidal.com",
        "soundcloud.com",
        "qobuz.com",
        "bandcamp.com",
        "iheart.com",
        "pandora.com",
        "tunein.com",
        "anghami.com",
        "y.qq.com",
        "music.163.com",
        "jiosaavn.com",
        "saavn.com",
        "wynk.in",
        "gaana.com",
        "idagio.com"
    };

    private static readonly string[] HostPrefixes =
    {
        "music.amazon.",
        "music.yandex."
    };

    private static readonly string[] BrandNames =
    {
        "YouTube Music",
        "Apple Music",
        "Amazon Music",
        "Deezer",
        "TIDAL",
        "Qobuz",
        "Bandcamp",
        "iHeart",
        "iHeartRadio",
        "Pandora",
        "TuneIn",
        "Anghami",
        "QQ Music",
        "NetEase Cloud Music",
        "JioSaavn",
        "Wynk Music",
        "Gaana",
        "Yandex Music",
        "IDAGIO"
    };

    public static string JsHostTest { get; } = BuildJsHostTest();

    public static bool IsMusicHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        host = host.ToLowerInvariant();
        foreach (var s in HostSuffixes)
            if (host == s || host.EndsWith("." + s, StringComparison.Ordinal)) return true;
        foreach (var p in HostPrefixes)
            if (host.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsBrandSuffix(string suffix)
    {
        foreach (var brand in BrandNames)
            if (suffix.Equals(brand, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string BuildJsHostTest()
    {
        string suffixes = string.Join("|", HostSuffixes.Select(h => h.Replace(".", "\\.")));
        string prefixes = string.Join("|", HostPrefixes.Select(h => h.Replace(".", "\\.")));
        return "(/(^|\\.)(" + suffixes + ")$/.test(location.hostname)||/^(" + prefixes + ")/.test(location.hostname))";
    }
}