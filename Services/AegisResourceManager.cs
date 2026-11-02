using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Core;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth.Services;

public enum AegisTabTier
{
    Active = 0,
    Warm = 1,
    Dormant = 2,
    Suspended = 3,
    Cryo = 4
}

public enum AegisPressureLevel
{
    Nominal = 0,
    Guarded = 1,
    Elevated = 2,
    Critical = 3,
    Emergency = 4
}

/// <summary>
/// Aegis Smart Resource Management Engine.
/// An intelligent, multi-tiered defensive shield managing browser memory, process lifecycles,
/// and tab hydration across Active, Warm, Dormant, Suspended, and Cryo states.
/// </summary>
public static class AegisResourceManager
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    private static readonly ConcurrentDictionary<TabViewModel, BrowserView> _registeredTabs = new();
    private static DispatcherTimer? _governorTimer;
    private static readonly object _syncLock = new();

    private static AegisPressureLevel _currentPressure = AegisPressureLevel.Nominal;
    private static long _totalRamSavedMb = 0;
    private static bool _isRunning = false;

    public static event Action? AegisStateChanged;

    public static AegisPressureLevel CurrentPressure => _currentPressure;
    public static long TotalRamSavedMb => _totalRamSavedMb;
    public static bool IsAegisShieldActive => _isRunning && SettingsService.Current.SleepingTabsEnabled;

    public static int DormantTabsCount => _registeredTabs.Keys.Count(t => t.AegisTier == AegisTabTier.Dormant);
    public static int SuspendedTabsCount => _registeredTabs.Keys.Count(t => t.AegisTier == AegisTabTier.Suspended);
    public static int CryoTabsCount => _registeredTabs.Keys.Count(t => t.AegisTier == AegisTabTier.Cryo);

    /// <summary>
    /// Initializes the Aegis governor background heartbeat.
    /// </summary>
    public static void Initialize()
    {
        lock (_syncLock)
        {
            if (_isRunning) return;
            _isRunning = true;

            _governorTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _governorTimer.Tick += GovernorHeartbeat_Tick;
            _governorTimer.Start();

            LogService.Write("AEGIS", "Aegis Smart Resource Governor initialized and active.");
        }
    }

    public static void RegisterTab(TabViewModel tab, BrowserView bv)
    {
        if (tab == null || bv == null) return;
        _registeredTabs[tab] = bv;
    }

    public static void UnregisterTab(TabViewModel tab)
    {
        if (tab == null) return;
        _registeredTabs.TryRemove(tab, out _);
        RecalculateSavedRam();
    }

    public static void NotifyTabActivated(TabViewModel tab)
    {
        if (tab == null) return;
        tab.LastActiveUtc = DateTime.UtcNow;
        if (tab.AegisTier != AegisTabTier.Active)
        {
            _ = WakeTabAsync(tab);
        }
    }

    public static void NotifyTabDeactivated(TabViewModel tab)
    {
        if (tab == null) return;
        tab.LastActiveUtc = DateTime.UtcNow;
        if (tab.IsPlayingAudio || tab.IsActiveDownload)
        {
            tab.AegisTier = AegisTabTier.Warm;
        }
    }

    public static void NotifyAudioChanged(TabViewModel tab, bool isPlaying)
    {
        if (tab == null) return;
        if (isPlaying)
        {
            tab.AegisTier = AegisTabTier.Warm;
            if (tab.IsSleeping)
            {
                _ = WakeTabAsync(tab);
            }
        }
    }

    private static async void GovernorHeartbeat_Tick(object? sender, EventArgs e)
    {
        if (!SettingsService.Current.SleepingTabsEnabled) return;

        try
        {
            await RunGovernorCycleAsync();
        }
        catch (Exception ex)
        {
            LogService.Write("AEGIS", $"Error during Aegis governor heartbeat: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes a single evaluation cycle across system pressure and all open tabs.
    /// </summary>
    public static async Task RunGovernorCycleAsync()
    {
        UpdateSystemPressureRadar();

        var now = DateTime.UtcNow;
        int baseIdleMinutes = Math.Max(2, SettingsService.Current.SleepingTabsMinutes);

        // Dynamic multiplier based on Aegis pressure level
        double pressureMultiplier = _currentPressure switch
        {
            AegisPressureLevel.Nominal => 1.0,
            AegisPressureLevel.Guarded => 0.70,
            AegisPressureLevel.Elevated => 0.45,
            AegisPressureLevel.Critical => 0.25,
            AegisPressureLevel.Emergency => 0.10,
            _ => 1.0
        };

        TimeSpan dormantThreshold = TimeSpan.FromMinutes(Math.Max(1.5, (baseIdleMinutes * 0.4) * pressureMultiplier));
        TimeSpan suspendThreshold = TimeSpan.FromMinutes(Math.Max(3.0, baseIdleMinutes * pressureMultiplier));
        TimeSpan cryoThreshold    = TimeSpan.FromMinutes(Math.Max(8.0, (baseIdleMinutes * 2.5) * pressureMultiplier));

        foreach (var (tab, bv) in _registeredTabs.ToList())
        {
            // Skip active tab or tabs explicitly marked NeverSleep
            if (tab.NeverSleep) continue;
            if (Application.Current?.MainWindow is MainWindow mainWin && mainWin.CurrentTab == tab)
            {
                tab.AegisTier = AegisTabTier.Active;
                continue;
            }

            // Exemption guard
            bool isExempt = await TabExemptionService.IsTabExemptFromSuspensionAsync(tab, bv);
            if (isExempt)
            {
                tab.AegisTier = AegisTabTier.Warm;
                continue;
            }

            var idleDuration = now - tab.LastActiveUtc;

            // Tier 4: Cryo (Complete deconstruction under heavy idle or Critical pressure)
            if (SettingsService.Current.EnableTabDiscardAndStateCapture &&
                (idleDuration >= cryoThreshold || _currentPressure >= AegisPressureLevel.Critical))
            {
                if (tab.AegisTier != AegisTabTier.Cryo)
                {
                    await DehydrateToCryoAsync(tab, bv);
                }
            }
            // Tier 3: Suspended (View detached, snapshot captured, memory trimmed)
            else if (idleDuration >= suspendThreshold || _currentPressure >= AegisPressureLevel.Elevated)
            {
                if (tab.AegisTier != AegisTabTier.Suspended && tab.AegisTier != AegisTabTier.Cryo)
                {
                    await SuspendTabAsync(tab, bv);
                }
            }
            // Tier 2: Dormant (MemoryUsageTargetLevel = Low, GC requested)
            else if (idleDuration >= dormantThreshold)
            {
                if (tab.AegisTier == AegisTabTier.Active || tab.AegisTier == AegisTabTier.Warm)
                {
                    await TransitionToDormantAsync(tab, bv);
                }
            }
        }

        // Under Critical/Emergency pressure, shed main process working set
        if (_currentPressure >= AegisPressureLevel.Critical)
        {
            try
            {
                using var currentProc = Process.GetCurrentProcess();
                EmptyWorkingSet(currentProc.Handle);
                CacheJanitorService.ForceNextRun();
            }
            catch { }
        }

        RecalculateSavedRam();
        AegisStateChanged?.Invoke();
    }

    private static void UpdateSystemPressureRadar()
    {
        var memReport = HorizonCore.GetBrowserProcessMemory();
        if (HorizonCore.GetSystemMemoryInfo(out _, out ulong availMb, out uint loadPct))
        {
            if (loadPct >= 90 || availMb < 600)
            {
                _currentPressure = AegisPressureLevel.Emergency;
            }
            else if (loadPct >= 82 || availMb < 1200 || (memReport != null && memReport.TotalBrowserRamMb > 3500))
            {
                _currentPressure = AegisPressureLevel.Critical;
            }
            else if (loadPct >= 72 || availMb < 2000 || (memReport != null && memReport.TotalBrowserRamMb > 2200))
            {
                _currentPressure = AegisPressureLevel.Elevated;
            }
            else if (loadPct >= 60 || (memReport != null && memReport.TotalBrowserRamMb > 1400))
            {
                _currentPressure = AegisPressureLevel.Guarded;
            }
            else
            {
                _currentPressure = AegisPressureLevel.Nominal;
            }
            return;
        }

        var pressure = RuntimeMemoryPressureService.CurrentPressure;
        _currentPressure = pressure switch
        {
            MemoryPressureLevel.Critical => AegisPressureLevel.Critical,
            MemoryPressureLevel.Moderate => AegisPressureLevel.Elevated,
            _ => AegisPressureLevel.Nominal
        };
    }

    /// <summary>
    /// Promotes tab to Tier 2: Dormant (MemoryUsageTargetLevel = Low, CDP DOM GC).
    /// </summary>
    public static async Task TransitionToDormantAsync(TabViewModel tab, BrowserView bv)
    {
        try
        {
            if (bv?.MainWebView?.CoreWebView2 != null)
            {
                bv.MainWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                try
                {
                    await bv.MainWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("HeapProfiler.collectGarbage", "{}");
                }
                catch { }
            }
            tab.AegisTier = AegisTabTier.Dormant;
            tab.EstimatedRamSavedMb = 85;
        }
        catch (Exception ex)
        {
            LogService.Write("AEGIS", $"TransitionToDormant failed for '{tab.Title}': {ex.Message}");
        }
    }

    /// <summary>
    /// Suspends tab to Tier 3: Captured state, TrySuspendAsync, memory trimmed.
    /// </summary>
    public static async Task SuspendTabAsync(TabViewModel tab, BrowserView bv)
    {
        try
        {
            if (bv?.MainWebView?.CoreWebView2 != null)
            {
                if (tab.Thumbnail == null && !tab.IsLoading)
                {
                    if (Application.Current?.MainWindow is MainWindow mw)
                    {
                        await mw.CaptureTabSnapshotAsync(tab);
                    }
                }

                if (SettingsService.Current.EnableTabDiscardAndStateCapture)
                {
                    tab.CapturedState = await TabStateCaptureService.CaptureStateAsync(bv);
                }

                tab.SleepUrl = tab.Url;
                tab.IsSleeping = true;
                bool suspended = await bv.MainWebView.CoreWebView2.TrySuspendAsync();
                if (!suspended)
                {
                    tab.IsSleeping = false;
                    tab.SleepUrl = null;
                    tab.CapturedState = null;
                    tab.LastActiveUtc = DateTime.UtcNow;
                    return;
                }

                tab.AegisTier = AegisTabTier.Suspended;
                tab.EstimatedRamSavedMb = 190;
                LogService.Write("AEGIS", $"Tab suspended (Aegis Tier 3): '{tab.Title}'");
            }
        }
        catch (Exception ex)
        {
            LogService.Write("AEGIS", $"SuspendTabAsync failed for '{tab.Title}': {ex.Message}");
        }
    }

    /// <summary>
    /// Dehydrates tab to Tier 4: Cryo (releases DOM/render resources via about:blank, 0 MB active renderer footprint).
    /// </summary>
    public static async Task DehydrateToCryoAsync(TabViewModel tab, BrowserView bv)
    {
        try
        {
            if (bv?.MainWebView?.CoreWebView2 != null)
            {
                if (tab.CapturedState == null)
                {
                    if (bv.MainWebView.CoreWebView2.IsSuspended)
                    {
                        bv.MainWebView.CoreWebView2.Resume();
                        await Task.Delay(50);
                    }
                    tab.CapturedState = await TabStateCaptureService.CaptureStateAsync(bv);
                }

                if (tab.Thumbnail == null && !tab.IsLoading && !bv.MainWebView.CoreWebView2.IsSuspended)
                {
                    if (Application.Current?.MainWindow is MainWindow mw)
                    {
                        await mw.CaptureTabSnapshotAsync(tab);
                    }
                }

                tab.SleepUrl = tab.Url;
                tab.IsSleeping = true;
                tab.IsDiscarded = true;
                tab.AegisTier = AegisTabTier.Cryo;
                tab.EstimatedRamSavedMb = 340;

                bv.MainWebView.CoreWebView2.Navigate("about:blank");
                LogService.Write("AEGIS", $"Tab dehydrated to Cryo (Aegis Tier 4): '{tab.Title}'");
            }
        }
        catch (Exception ex)
        {
            LogService.Write("AEGIS", $"DehydrateToCryoAsync failed for '{tab.Title}': {ex.Message}");
        }
    }

    /// <summary>
    /// Seamlessly wakes a tab from Dormant, Suspended, or Cryo state back to Active Tier.
    /// </summary>
    public static async Task WakeTabAsync(TabViewModel tab)
    {
        if (tab == null) return;
        await Task.Yield();
        if (!_registeredTabs.TryGetValue(tab, out var bv))
        {
            if (Application.Current?.MainWindow is MainWindow mw)
            {
                bv = mw.GetBrowserViewForTab(tab);
                if (bv != null) _registeredTabs[tab] = bv;
            }
        }

        try
        {
            string wakeUrl = !string.IsNullOrEmpty(tab.SleepUrl) ? tab.SleepUrl : tab.Url;
            if (string.IsNullOrEmpty(wakeUrl) || wakeUrl == "about:blank")
            {
                wakeUrl = SettingsService.Current.HomePage;
            }

            tab.SleepUrl = null;
            tab.IsSleeping = false;

            if (bv?.MainWebView?.CoreWebView2 != null)
            {
                if (tab.IsDiscarded)
                {
                    tab.IsDiscarded = false;
                    bv.MainWebView.CoreWebView2.Navigate(wakeUrl);
                }
                else if (bv.MainWebView.CoreWebView2.IsSuspended)
                {
                    bv.MainWebView.CoreWebView2.Resume();
                    tab.CapturedState = null;
                }
                else
                {
                    bv.MainWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                    _ = bv.ClearBackgroundViewportAsync();
            }

            tab.AegisTier = AegisTabTier.Active;
            tab.EstimatedRamSavedMb = 0;
            tab.LastActiveUtc = DateTime.UtcNow;

            LogService.Write("AEGIS", $"Tab rehydrated to Active: '{tab.Title}'");
        }
        catch (Exception ex)
        {
            LogService.Write("AEGIS", $"WakeTabAsync failed for '{tab.Title}': {ex.Message}");
        }
        finally
        {
            RecalculateSavedRam();
            AegisStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Forces an immediate emergency memory shed across all background tabs.
    /// </summary>
    public static async Task TriggerEmergencyMemoryShedAsync()
    {
        LogService.Write("AEGIS", "Emergency memory shed triggered manually or by critical pressure.");
        _currentPressure = AegisPressureLevel.Emergency;

        foreach (var (tab, bv) in _registeredTabs.ToList())
        {
            if (tab.NeverSleep || tab.AegisTier == AegisTabTier.Active) continue;
            bool isExempt = await TabExemptionService.IsTabExemptFromSuspensionAsync(tab, bv);
            if (!isExempt)
            {
                await DehydrateToCryoAsync(tab, bv);
            }
        }

        try
        {
            using var currentProc = Process.GetCurrentProcess();
            EmptyWorkingSet(currentProc.Handle);
            CacheJanitorService.ForceNextRun();
        }
        catch { }

        RecalculateSavedRam();
        AegisStateChanged?.Invoke();
    }

    private static void RecalculateSavedRam()
    {
        long total = 0;
        foreach (var tab in _registeredTabs.Keys)
        {
            total += tab.EstimatedRamSavedMb;
        }
        _totalRamSavedMb = total;
    }
}
