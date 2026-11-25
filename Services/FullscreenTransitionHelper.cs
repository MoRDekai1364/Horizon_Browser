using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Horizon.Stealth.Services;

/// <summary>
/// Shared fullscreen transition probe and DWM animation suppression helpers used by both
/// MainWindow and WebAppWindow so fullscreen animation rules are 100% consistent.
/// </summary>
internal static class FullscreenTransitionHelper
{
    public const int FsPreDarkenMs = 15;
    public const int FsStableMinMs = 10;
    public const int FsStableMaxMs = 60;
    public const int FsWebViewSettleMs = 0;
    public const int FsFadeOutMs = 40;
    public const int FsPageMinMs = 10;
    public const int FsPageStableMs = 10;
    public const int FsPageMaxMs = 60;
    public const int FsFallbackHoldMs = 15;
    public const int FsProbeIntervalMs = 20;

    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

    public const string FsProbeScript =
        "(function(){var d=document,f=d.fullscreenElement,p=f||d.getElementById('movie_player'),v=d.querySelector('video'),sec=d.querySelector('#secondary,#related,ytd-watch-flexy #secondary');function g(e){if(!e)return[0,0,0,0];var r=e.getBoundingClientRect();return[Math.round(r.left),Math.round(r.top),Math.round(r.width),Math.round(r.height)];}var sIn=sec?1:0,sH=sec?Math.round(sec.getBoundingClientRect().height):0;return [innerWidth,innerHeight].concat(g(p),g(v),[f?1:0,sIn,sH]).join(',');})()";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void SetOsWindowTransitions(IntPtr hwnd, bool disabled)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int v = disabled ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref v, sizeof(int));
        }
        catch { }
    }

    public static bool FsProbeFilled(string raw, bool on)
    {
        try
        {
            var parts = raw.Trim('"').Split(',');
            if (parts.Length < 11) return true;

            double iw = double.Parse(parts[0], CultureInfo.InvariantCulture);
            double ih = double.Parse(parts[1], CultureInfo.InvariantCulture);
            double pw = double.Parse(parts[4], CultureInfo.InvariantCulture);
            double ph = double.Parse(parts[5], CultureInfo.InvariantCulture);
            bool isFs = parts[10] == "1";

            if (on)
            {
                if (!isFs) return false;
                return pw >= iw - 2 && ph >= ih - 2;
            }
            else
            {
                if (isFs) return false;
                if (pw > 0 && pw >= iw - 2) return false;

                if (parts.Length >= 13)
                {
                    int secInDom = int.Parse(parts[11], CultureInfo.InvariantCulture);
                    double secH = double.Parse(parts[12], CultureInfo.InvariantCulture);
                    if (secInDom == 1 && secH < 50) return false;
                }

                return true;
            }
        }
        catch
        {
            return true;
        }
    }

    public static async Task WaitForPageSettledAsync(CoreWebView2? core, bool enter, FullscreenGlassCurtain curtain)
    {
        if (core == null)
        {
            await Task.Delay(enter ? FsFallbackHoldMs : 120);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? lastSig = null;
        long sigSince = 0;

        int pageMinMs = enter ? FsPageMinMs : 120;
        int pageStableMs = enter ? FsPageStableMs : 35;
        int pageMaxMs = enter ? FsPageMaxMs : 380;

        while (curtain.OwnerAlive && sw.ElapsedMilliseconds < pageMaxMs)
        {
            string raw;
            try
            {
                var probe = core.ExecuteScriptAsync(FsProbeScript);
                int remaining = (int)Math.Max(50, pageMaxMs - sw.ElapsedMilliseconds);
                if (await Task.WhenAny(probe, Task.Delay(remaining)) != probe) break;
                raw = await probe;
            }
            catch
            {
                await Task.Delay(enter ? FsFallbackHoldMs : 100);
                return;
            }

            long now = sw.ElapsedMilliseconds;
            if (raw != lastSig)
            {
                lastSig = raw;
                sigSince = now;
            }

            if (now >= pageMinMs && now - sigSince >= pageStableMs && FsProbeFilled(raw, enter)) break;
            await Task.Delay(FsProbeIntervalMs);
        }

        if (!enter && core != null)
        {
            try
            {
                await core.CallDevToolsProtocolMethodAsync(
                    "Runtime.evaluate",
                    "{\"expression\":\"new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))\",\"awaitPromise\":true}");
            }
            catch { }
        }

        await Task.Delay(enter ? FsWebViewSettleMs : 20);
    }
}
