using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Horizon.Stealth.Services;

namespace Horizon.Stealth;

public partial class MainWindow
{
    private const int FsPreDarkenMs = 160;
    private const int FsStableMinMs = 60;
    private const int FsStableMaxMs = 1500;
    private const int FsWebViewSettleMs = 200;
    private const int FsFadeOutMs = 450;
    private const int FsPageMinMs = 350;
    private const int FsPageStableMs = 300;
    private const int FsPageMaxMs = 4000;
    private const int FsFallbackHoldMs = 600;
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

    private bool _fsDesired;
    private bool _fsBusy;
    private bool _fsAnimating;

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
            _fsAnimating = false;
            _fsBusy = false;
        }
    }

    private void ApplyFullscreenImmediate()
    {
        ApplyFullscreenToggle();
        SyncMobileHeaderForFullscreen();
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
            await Task.Delay(FsFallbackHoldMs);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? lastSig = null;
        long sigSince = 0;

        while (curtain.OwnerAlive && sw.ElapsedMilliseconds < FsPageMaxMs)
        {
            string raw;
            try
            {
                var probe = core.ExecuteScriptAsync(FsProbeScript);
                int remaining = (int)Math.Max(50, FsPageMaxMs - sw.ElapsedMilliseconds);
                if (await Task.WhenAny(probe, Task.Delay(remaining)) != probe) break;
                raw = await probe;
            }
            catch
            {
                await Task.Delay(FsFallbackHoldMs);
                return;
            }

            long now = sw.ElapsedMilliseconds;
            if (raw != lastSig)
            {
                lastSig = raw;
                sigSince = now;
            }

            if (now >= FsPageMinMs && now - sigSince >= FsPageStableMs && FsProbeFilled(raw, enter)) break;
            await Task.Delay(FsProbeIntervalMs);
        }

        await Task.Delay(FsWebViewSettleMs);
    }

    private async Task RunFullscreenTransitionAsync(bool enter)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool canAnimate = hwnd != IntPtr.Zero && IsLoaded && IsVisible && WindowState != WindowState.Minimized;

        if (!canAnimate)
        {
            _fsAnimating = false;
            ApplyFullscreenImmediate();
            return;
        }

        using var curtain = FullscreenGlassCurtain.TryCreate(hwnd, enter ? "Entering fullscreen..." : "Exiting fullscreen...");
        if (curtain == null)
        {
            _fsAnimating = false;
            ApplyFullscreenImmediate();
            return;
        }

        _fsAnimating = true;
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

            await curtain.FadeOutAsync(FsFadeOutMs);
        }
        finally
        {
            if (curtain.OwnerAlive) SetOsWindowTransitions(hwnd, false);
            _fsAnimating = false;
        }
    }
}