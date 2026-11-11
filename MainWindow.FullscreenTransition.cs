using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Horizon.Stealth.Services;

namespace Horizon.Stealth;

public partial class MainWindow
{
    private const int FsPreDarkenMs = 15;
    private const int FsStableMinMs = 10;
    private const int FsStableMaxMs = 60;
    private const int FsWebViewSettleMs = 0;
    private const int FsFadeOutMs = 40;
    private const int FsPageMinMs = 10;
    private const int FsPageStableMs = 10;
    private const int FsPageMaxMs = 60;
    private const int FsFallbackHoldMs = 15;
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

    private bool _fsDesired;
    private bool _fsBusy;

    private void ToggleFullscreen() => RequestFullscreen(!_fsDesired);

    private async void RequestFullscreen(bool on)
    {
        _fsDesired = on;
        if (_fsBusy) return;
        _fsBusy = true;

        try
        {
            while (_isFullscreen != _fsDesired)
            {
                bool target = _fsDesired;
                try
                {
                    await RunFullscreenTransitionAsync(target);
                }
                catch (Exception ex)
                {
                    LogService.RecordCrash(ex, "FullscreenTransition");
                    if (_isFullscreen != target) ApplyFullscreenImmediate();
                }
            }
        }
        finally
        {
            _fsBusy = false;
        }
    }

    private void ApplyFullscreenImmediate()
    {
        ApplyFullscreenToggle();
        SyncMobileHeaderForFullscreen();
        if (_isFullscreen && DynamicIslandPopup != null) DynamicIslandPopup.IsOpen = false;
        else if (!_isFullscreen) UpdateDynamicIslandVisibility();
    }

    private void SyncMobileHeaderForFullscreen()
    {
        MobileHeaderContainer.Visibility = _isNarrowMode && !_isFullscreen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetOsWindowTransitions(IntPtr hwnd, bool disabled)
    {
        try
        {
            int v = disabled ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref v, sizeof(int));
        }
        catch
        {
        }
    }

    private async Task WaitForPageSettledAsync(bool enter, FullscreenGlassCurtain curtain)
    {
        var core = _activeTabView?.MainWebView?.CoreWebView2;
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
                // Wait for Chromium to flush 2 rendered frames to the screen so reflow is 100% painted
                await core.CallDevToolsProtocolMethodAsync(
                    "Runtime.evaluate",
                    "{\"expression\":\"new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))\",\"awaitPromise\":true}");
            }
            catch { }
        }

        await Task.Delay(enter ? FsWebViewSettleMs : 20);
    }

    private async Task RunFullscreenTransitionAsync(bool enter)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool canAnimate = hwnd != IntPtr.Zero && IsLoaded && IsVisible && WindowState != WindowState.Minimized;

        if (!canAnimate || !SettingsService.Current.ShowFullscreenOverlay)
        {
            ApplyFullscreenImmediate();
            return;
        }

        using var curtain = FullscreenGlassCurtain.TryCreate(hwnd, enter ? "Entering fullscreen..." : "Exiting fullscreen...");
        if (curtain == null)
        {
            ApplyFullscreenImmediate();
            return;
        }

        SetOsWindowTransitions(hwnd, true);

        try
        {
            await curtain.FadeInAsync(FsPreDarkenMs);

            if (!curtain.OwnerAlive || WindowState == WindowState.Minimized || !IsVisible)
            {
                ApplyFullscreenImmediate();
                return;
            }

            curtain.CoverUnion();
            curtain.Flush();

            ApplyFullscreenImmediate();
            UpdateLayout();

            await curtain.WaitForOwnerStableAsync(FsStableMinMs, FsStableMaxMs);
            if (!curtain.OwnerAlive) return;

            UpdateLayout();
            ReflowTabs();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await WaitForPageSettledAsync(enter, curtain);
            if (!curtain.OwnerAlive) return;

            curtain.FitToOwner();
            curtain.Flush();

            int fadeOutMs = enter ? FsFadeOutMs : 60;
            await curtain.FadeOutAsync(fadeOutMs);
        }
        finally
        {
            if (curtain.OwnerAlive) SetOsWindowTransitions(hwnd, false);
        }
    }
}
