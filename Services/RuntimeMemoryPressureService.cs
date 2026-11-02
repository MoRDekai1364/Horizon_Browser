using System;
using System.Runtime.InteropServices;

namespace Horizon.Stealth.Services;

public enum HardwareTierLevel
{
    Tier1_Survival = 1,
    Tier2_Tight = 2,
    Tier3_Balanced = 3,
    Tier4_High = 4,
    Tier5_Sovereign = 5
}

public enum MemoryPressureLevel
{
    Normal = 0,
    Moderate = 1,
    Critical = 2
}

public static class RuntimeMemoryPressureService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateMemoryResourceNotification(int notificationType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryMemoryResourceNotification(IntPtr resourceNotificationHandle, out bool resourceState);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    private static IntPtr _lowMemoryHandle = IntPtr.Zero;
    private static readonly object _syncLock = new();
    private static DateTime _lastSampleTime = DateTime.MinValue;
    private static HardwareTierLevel _cachedTier = HardwareTierLevel.Tier3_Balanced;
    private static MemoryPressureLevel _cachedPressure = MemoryPressureLevel.Normal;
    private static double _cachedRamLoadPercent = 0;
    private static ulong _cachedAvailPhysBytes = 0;

    static RuntimeMemoryPressureService()
    {
        try
        {
            _lowMemoryHandle = CreateMemoryResourceNotification(0);
        }
        catch
        {
            _lowMemoryHandle = IntPtr.Zero;
        }
        DeriveBaselineTier();
    }

    public static HardwareTierLevel CurrentTier
    {
        get
        {
            EnsureFreshMetrics();
            return _cachedTier;
        }
    }

    public static MemoryPressureLevel CurrentPressure
    {
        get
        {
            EnsureFreshMetrics();
            return _cachedPressure;
        }
    }

    private static void DeriveBaselineTier()
    {
        var mem = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(mem))
        {
            double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            int cores = Environment.ProcessorCount;

            if (totalGb <= 2.4 || cores <= 2)
            {
                _cachedTier = HardwareTierLevel.Tier1_Survival;
            }
            else if (totalGb <= 4.5 || cores <= 4)
            {
                _cachedTier = HardwareTierLevel.Tier2_Tight;
            }
            else if (totalGb <= 9.0)
            {
                _cachedTier = HardwareTierLevel.Tier3_Balanced;
            }
            else if (totalGb <= 18.0)
            {
                _cachedTier = HardwareTierLevel.Tier4_High;
            }
            else
            {
                _cachedTier = HardwareTierLevel.Tier5_Sovereign;
            }
        }
    }

    private static void EnsureFreshMetrics()
    {
        if ((DateTime.UtcNow - _lastSampleTime).TotalSeconds < 5) return;

        lock (_syncLock)
        {
            if ((DateTime.UtcNow - _lastSampleTime).TotalSeconds < 5) return;
            _lastSampleTime = DateTime.UtcNow;

            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                _cachedRamLoadPercent = mem.dwMemoryLoad;
                _cachedAvailPhysBytes = mem.ullAvailPhys;
            }

            bool kernelLowMem = false;
            if (_lowMemoryHandle != IntPtr.Zero)
            {
                QueryMemoryResourceNotification(_lowMemoryHandle, out kernelLowMem);
            }

            bool lowBattery = false;
            if (GetSystemPowerStatus(out var pStatus))
            {
                if (pStatus.ACLineStatus == 0 && pStatus.BatteryLifePercent <= 20)
                {
                    lowBattery = true;
                }
            }

            if (kernelLowMem || _cachedRamLoadPercent >= 90)
            {
                _cachedPressure = MemoryPressureLevel.Critical;
            }
            else if (_cachedRamLoadPercent >= 80 || lowBattery)
            {
                _cachedPressure = MemoryPressureLevel.Moderate;
            }
            else
            {
                _cachedPressure = MemoryPressureLevel.Normal;
            }
        }
    }

    public static int GetMinTabsToSleep()
    {
        if (!SettingsService.Current.EnableDynamicRamTiering)
            return 6;

        return CurrentTier switch
        {
            HardwareTierLevel.Tier1_Survival => 3,
            HardwareTierLevel.Tier2_Tight => 4,
            HardwareTierLevel.Tier3_Balanced => 5,
            HardwareTierLevel.Tier4_High => 6,
            _ => 8
        };
    }

    public static long GetModerateRamMb()
    {
        if (!SettingsService.Current.EnableDynamicRamTiering)
            return 2000;

        long baseMb = CurrentTier switch
        {
            HardwareTierLevel.Tier1_Survival => 500,
            HardwareTierLevel.Tier2_Tight => 800,
            HardwareTierLevel.Tier3_Balanced => 1500,
            HardwareTierLevel.Tier4_High => 2500,
            _ => 4000
        };

        if (CurrentPressure == MemoryPressureLevel.Critical)
            return (long)(baseMb * 0.7);

        return baseMb;
    }

    public static long GetHighRamMb()
    {
        if (!SettingsService.Current.EnableDynamicRamTiering)
            return 2500;

        long baseMb = CurrentTier switch
        {
            HardwareTierLevel.Tier1_Survival => 800,
            HardwareTierLevel.Tier2_Tight => 1300,
            HardwareTierLevel.Tier3_Balanced => 2200,
            HardwareTierLevel.Tier4_High => 3800,
            _ => 6000
        };

        if (CurrentPressure == MemoryPressureLevel.Critical)
            return (long)(baseMb * 0.75);

        return baseMb;
    }

    public static double GetIdleTimeoutMultiplier()
    {
        if (!SettingsService.Current.EnableDynamicRamTiering)
            return 1.0;

        return CurrentPressure switch
        {
            MemoryPressureLevel.Critical => 0.5,
            MemoryPressureLevel.Moderate => 0.75,
            _ => 1.0
        };
    }

    public static string GetTierDescription()
    {
        return CurrentTier switch
        {
            HardwareTierLevel.Tier1_Survival => "Tier 1: Survival (<=2GB)",
            HardwareTierLevel.Tier2_Tight => "Tier 2: Tight (4GB)",
            HardwareTierLevel.Tier3_Balanced => "Tier 3: Balanced (8GB)",
            HardwareTierLevel.Tier4_High => "Tier 4: High (16GB)",
            _ => "Tier 5: Sovereign (32GB+)"
        };
    }
}
