using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Horizon.Stealth.Services;

namespace Horizon.Stealth;

public partial class MainWindow
{
    private const int FsPreDarkenMs = 80;
    private const int FsStableMinMs = 40;
    private const int FsStableMaxMs = 700;
    private const int FsWebViewSettleMs = 110;
    private const int FsFadeOutMs = 220;
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

    private async Task RunFullscreenTransitionAsync(bool enter)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool canAnimate = hwnd != IntPtr.Zero && IsLoaded && IsVisible && WindowState != WindowState.Minimized;

        if (!canAnimate)
        {
            _fsAnimating = false;
            ApplyFullscreenImmediate();
            if (enter && _isFullscreen) PlayFullscreenNotify();
            return;
        }

        using var curtain = FullscreenCurtain.TryCreate(hwnd);
        if (curtain == null)
        {
            _fsAnimating = false;
            ApplyFullscreenImmediate();
            if (enter && _isFullscreen) PlayFullscreenNotify();
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
            await Task.Delay(FsWebViewSettleMs);
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

        if (enter && _isFullscreen && _fsDesired) PlayFullscreenNotify();
    }
}