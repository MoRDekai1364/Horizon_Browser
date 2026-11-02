using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Horizon.Stealth.Services;

public static class PerformanceTelemetryService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        IntPtr hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    public class ProcessCpuMemorySnapshot
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public long WorkingSetBytes { get; set; }
        public long PrivateBytes { get; set; }
        public double WorkingSetMb => WorkingSetBytes / 1048576.0;
        public double PrivateBytesMb => PrivateBytes / 1048576.0;
        public long KernelCpuTicks { get; set; }
        public long UserCpuTicks { get; set; }
        public double TotalCpuTimeMs => (KernelCpuTicks + UserCpuTicks) / 10000.0;
    }

    public class SystemMemoryInfo
    {
        public ulong TotalPhysicalBytes { get; set; }
        public ulong AvailablePhysicalBytes { get; set; }
        public uint MemoryLoadPercent { get; set; }
        public double TotalPhysicalMb => TotalPhysicalBytes / 1048576.0;
        public double AvailablePhysicalMb => AvailablePhysicalBytes / 1048576.0;
        public int LogicalCores { get; set; }
        public string HardwareTier { get; set; } = string.Empty;
    }

    public class FixedEnvironmentConditions
    {
        public string PowerSource { get; set; } = string.Empty;
        public string DisplayRefreshRate { get; set; } = string.Empty;
        public string ActivePowerPlan { get; set; } = string.Empty;
        public string WindowFocusState { get; set; } = string.Empty;
        public string EnvironmentIsolation { get; set; } = string.Empty;
    }

    public class TelemetrySnapshot
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public SystemMemoryInfo SystemMemory { get; set; } = new();
        public List<ProcessCpuMemorySnapshot> Processes { get; set; } = new();
        public double TotalWorkingSetMb => Processes.Sum(p => p.WorkingSetMb);
        public double TotalPrivateBytesMb => Processes.Sum(p => p.PrivateBytesMb);
        public double TotalCpuTimeMs => Processes.Sum(p => p.TotalCpuTimeMs);
        public int ChildProcessCount => Math.Max(0, Processes.Count - 1);
    }

    public class MetricDistribution
    {
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double Threshold2Sigma => Mean + (2.0 * StdDev);
    }

    public class PageFrameMetrics
    {
        public double FrameTimeP50Ms { get; set; }
        public double FrameTimeP99Ms { get; set; }
        public int LongTasksCount { get; set; }
        public int SampledFrames { get; set; }
    }

    public class StateBenchmarkSummary
    {
        public string StateName { get; set; } = string.Empty;
        public int SampleCount { get; set; }
        public MetricDistribution WorkingSetMb { get; set; } = new();
        public MetricDistribution PrivateBytesMb { get; set; } = new();
        public MetricDistribution CpuTimeMs { get; set; } = new();
        public PageFrameMetrics PageFrames { get; set; } = new();
        public List<TelemetrySnapshot> RawSamples { get; set; } = new();
    }

    public class LaunchStateResult
    {
        public int LaunchId { get; set; }
        public string StateName { get; set; } = string.Empty;
        public double WorkingSetMb { get; set; }
        public double PrivateBytesMb { get; set; }
        public double CpuTimeMs { get; set; }
        public double FrameTimeP50Ms { get; set; }
        public double FrameTimeP99Ms { get; set; }
        public int LongTasksCount { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    public class StateAggregatedSummary
    {
        public string StateName { get; set; } = string.Empty;
        public int CompletedLaunches { get; set; }
        public MetricDistribution WorkingSetMb { get; set; } = new();
        public MetricDistribution PrivateBytesMb { get; set; } = new();
        public MetricDistribution CpuTimeMs { get; set; } = new();
        public MetricDistribution FrameTimeP50Ms { get; set; } = new();
        public MetricDistribution FrameTimeP99Ms { get; set; } = new();
        public double MeanLongTasks { get; set; }
    }

    public class Phase0BaselineReport
    {
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
        public string MachineName { get; set; } = Environment.MachineName;
        public string OsVersion { get; set; } = Environment.OSVersion.ToString();
        public SystemMemoryInfo SystemMemory { get; set; } = new();
        public FixedEnvironmentConditions FixedConditions { get; set; } = new();
        public List<StateBenchmarkSummary> States { get; set; } = new();
        public List<StateAggregatedSummary> AggregatedStates { get; set; } = new();
    }

    private static readonly string DiagnosticsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horizon_Browser", "Diagnostics");

    public static string OutputJsonPath => Path.Combine(DiagnosticsDir, "phase0_baseline.json");
    public static string OutputCsvPath => Path.Combine(DiagnosticsDir, "phase0_baseline.csv");
    public static string OutputLaunchesCsvPath => Path.Combine(DiagnosticsDir, "phase0_baseline_launches.csv");

    public static FixedEnvironmentConditions QueryFixedConditions()
    {
        var cond = new FixedEnvironmentConditions
        {
            WindowFocusState = "Visible & Focused",
            EnvironmentIsolation = "No external concurrent browser instances"
        };

        try
        {
            if (GetSystemPowerStatus(out var pStatus))
            {
                cond.PowerSource = pStatus.ACLineStatus switch
                {
                    1 => "AC Online (Mains Power)",
                    0 => "Battery (Offline)",
                    _ => "Unknown"
                };
            }
            else
            {
                cond.PowerSource = "AC Online (Assumed)";
            }
        }
        catch
        {
            cond.PowerSource = "AC Online";
        }

        try
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, -1, ref dm) && dm.dmDisplayFrequency > 0)
            {
                cond.DisplayRefreshRate = $"{dm.dmDisplayFrequency} Hz";
            }
            else
            {
                cond.DisplayRefreshRate = "60 Hz";
            }
        }
        catch
        {
            cond.DisplayRefreshRate = "60 Hz";
        }

        try
        {
            string? scheme = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes",
                "ActivePowerScheme", "")?.ToString();
            cond.ActivePowerPlan = string.IsNullOrWhiteSpace(scheme) ? "Balanced" : scheme;
        }
        catch
        {
            cond.ActivePowerPlan = "Balanced";
        }

        return cond;
    }

    public static string DeriveHardwareTier(ulong totalPhysBytes, int logicalCores)
    {
        double totalGb = totalPhysBytes / (1024.0 * 1024.0 * 1024.0);
        if (totalGb <= 2.4) return "Tier 1: Survival (<=2GB)";
        if (totalGb <= 4.5) return "Tier 2: Tight (4GB)";
        if (totalGb <= 9.0) return "Tier 3: Balanced (8GB)";
        if (totalGb <= 18.0) return "Tier 4: High (16GB)";
        return "Tier 5: Sovereign (32GB+)";
    }

    public static SystemMemoryInfo QuerySystemMemory()
    {
        var mem = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(mem))
        {
            return new SystemMemoryInfo
            {
                TotalPhysicalBytes = mem.ullTotalPhys,
                AvailablePhysicalBytes = mem.ullAvailPhys,
                MemoryLoadPercent = mem.dwMemoryLoad,
                LogicalCores = Environment.ProcessorCount,
                HardwareTier = DeriveHardwareTier(mem.ullTotalPhys, Environment.ProcessorCount)
            };
        }

        return new SystemMemoryInfo
        {
            TotalPhysicalBytes = 0,
            AvailablePhysicalBytes = 0,
            MemoryLoadPercent = 0,
            LogicalCores = Environment.ProcessorCount,
            HardwareTier = "Unknown"
        };
    }

    public static TelemetrySnapshot CaptureSnapshot()
    {
        var snapshot = new TelemetrySnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            SystemMemory = QuerySystemMemory()
        };

        const uint TH32CS_SNAPPROCESS = 0x00000002;
        var childMap = new Dictionary<int, List<int>>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

        if (snap != new IntPtr(-1))
        {
            try
            {
                var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
                if (Process32FirstW(snap, ref entry))
                {
                    do
                    {
                        int pid = (int)entry.th32ProcessID;
                        int ppid = (int)entry.th32ParentProcessID;
                        if (!childMap.ContainsKey(ppid)) childMap[ppid] = new List<int>();
                        childMap[ppid].Add(pid);
                    }
                    while (Process32NextW(snap, ref entry));
                }
            }
            finally
            {
                CloseHandle(snap);
            }
        }

        int rootPid = Process.GetCurrentProcess().Id;
        var queue = new Queue<int>();
        queue.Enqueue(rootPid);

        var treePids = new List<int>();
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            treePids.Add(cur);
            if (childMap.TryGetValue(cur, out var kids))
            {
                foreach (var k in kids) queue.Enqueue(k);
            }
        }

        foreach (int pid in treePids)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Refresh();

                long kTime = 0, uTime = 0;
                if (GetProcessTimes(proc.Handle, out _, out _, out long k, out long u))
                {
                    kTime = k;
                    uTime = u;
                }

                snapshot.Processes.Add(new ProcessCpuMemorySnapshot
                {
                    Pid = pid,
                    ProcessName = proc.ProcessName,
                    WorkingSetBytes = proc.WorkingSet64,
                    PrivateBytes = proc.PrivateMemorySize64,
                    KernelCpuTicks = kTime,
                    UserCpuTicks = uTime
                });
            }
            catch
            {
            }
        }

        return snapshot;
    }

    public static MetricDistribution ComputeDistribution(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return new MetricDistribution { Mean = 0, StdDev = 0 };
        double mean = list.Average();
        if (list.Count == 1) return new MetricDistribution { Mean = mean, StdDev = 0 };
        double sumSq = list.Sum(x => Math.Pow(x - mean, 2));
        double stdDev = Math.Sqrt(sumSq / (list.Count - 1));
        return new MetricDistribution { Mean = mean, StdDev = stdDev };
    }

    public static async Task<PageFrameMetrics> QueryForegroundFrameMetricsAsync(Func<string, Task<string>>? evaluateScriptAsync)
    {
        if (evaluateScriptAsync == null) return new PageFrameMetrics();
        try
        {
            string raw = await evaluateScriptAsync("window.__hzGetFrameMetrics ? JSON.stringify(window.__hzGetFrameMetrics()) : '{}'");
            if (!string.IsNullOrWhiteSpace(raw) && raw != "{}" && raw != "null")
            {
                string json = raw;
                if (json.StartsWith("\"") && json.EndsWith("\""))
                {
                    json = JsonSerializer.Deserialize<string>(json) ?? "{}";
                }
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new PageFrameMetrics
                {
                    FrameTimeP50Ms = root.TryGetProperty("p50", out var p50) ? p50.GetDouble() : 0,
                    FrameTimeP99Ms = root.TryGetProperty("p99", out var p99) ? p99.GetDouble() : 0,
                    LongTasksCount = root.TryGetProperty("longTasks", out var lt) ? lt.GetInt32() : 0,
                    SampledFrames = root.TryGetProperty("sampleCount", out var sc) ? sc.GetInt32() : 0
                };
            }
        }
        catch
        {
        }
        return new PageFrameMetrics();
    }

    public static async Task<StateBenchmarkSummary> SampleStateAsync(
        string stateName,
        int sampleCount,
        int intervalMs = 1000,
        Func<string, Task<string>>? evaluateScriptAsync = null)
    {
        var summary = new StateBenchmarkSummary
        {
            StateName = stateName,
            SampleCount = sampleCount
        };

        for (int i = 0; i < sampleCount; i++)
        {
            var snap = CaptureSnapshot();
            summary.RawSamples.Add(snap);
            if (i < sampleCount - 1)
                await Task.Delay(intervalMs);
        }

        summary.WorkingSetMb = ComputeDistribution(summary.RawSamples.Select(s => s.TotalWorkingSetMb));
        summary.PrivateBytesMb = ComputeDistribution(summary.RawSamples.Select(s => s.TotalPrivateBytesMb));
        summary.CpuTimeMs = ComputeDistribution(summary.RawSamples.Select(s => s.TotalCpuTimeMs));

        if (evaluateScriptAsync != null)
        {
            summary.PageFrames = await QueryForegroundFrameMetricsAsync(evaluateScriptAsync);
        }

        return summary;
    }

    public static List<LaunchStateResult> LoadAllLaunchRecords()
    {
        var records = new List<LaunchStateResult>();
        if (!File.Exists(OutputLaunchesCsvPath)) return records;

        var lines = File.ReadAllLines(OutputLaunchesCsvPath);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length < 9) continue;

            if (int.TryParse(parts[0], out int lid) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double ws) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double pb) &&
                double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double cpu) &&
                double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double p50) &&
                double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double p99) &&
                int.TryParse(parts[7], out int lt) &&
                DateTime.TryParse(parts[8].Trim('\"'), out DateTime dt))
            {
                records.Add(new LaunchStateResult
                {
                    LaunchId = lid,
                    StateName = parts[1].Trim('\"'),
                    WorkingSetMb = ws,
                    PrivateBytesMb = pb,
                    CpuTimeMs = cpu,
                    FrameTimeP50Ms = p50,
                    FrameTimeP99Ms = p99,
                    LongTasksCount = lt,
                    TimestampUtc = dt
                });
            }
        }
        return records;
    }

    public static void SaveLaunchResults(int launchId, Phase0BaselineReport report)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsDir);

            if (!File.Exists(OutputLaunchesCsvPath))
            {
                File.WriteAllText(OutputLaunchesCsvPath, "LaunchId,State,WorkingSetMb,PrivateBytesMb,CpuTimeMs,FrameTimeP50Ms,FrameTimeP99Ms,LongTasksCount,TimestampUtc\n", Encoding.UTF8);
            }

            var launchSb = new StringBuilder();
            foreach (var state in report.States)
            {
                launchSb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},\"{1}\",{2:F2},{3:F2},{4:F1},{5:F2},{6:F2},{7},\"{8:O}\"",
                    launchId,
                    state.StateName,
                    state.WorkingSetMb.Mean,
                    state.PrivateBytesMb.Mean,
                    state.CpuTimeMs.Mean,
                    state.PageFrames.FrameTimeP50Ms,
                    state.PageFrames.FrameTimeP99Ms,
                    state.PageFrames.LongTasksCount,
                    DateTime.UtcNow));
            }
            File.AppendAllText(OutputLaunchesCsvPath, launchSb.ToString(), Encoding.UTF8);

            var allRecords = LoadAllLaunchRecords();
            var grouped = allRecords.GroupBy(r => r.StateName).ToList();

            report.AggregatedStates.Clear();
            var csvSb = new StringBuilder();
            csvSb.AppendLine("State,LaunchId,WorkingSetMb,PrivateBytesMb,CpuTimeMs,FrameTimeP50Ms,FrameTimeP99Ms,LongTasksCount,TimestampUtc");

            foreach (var grp in grouped)
            {
                var list = grp.OrderBy(x => x.LaunchId).ToList();
                foreach (var item in list)
                {
                    csvSb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "\"{0}\",{1},{2:F2},{3:F2},{4:F1},{5:F2},{6:F2},{7},\"{8:O}\"",
                        item.StateName, item.LaunchId, item.WorkingSetMb, item.PrivateBytesMb,
                        item.CpuTimeMs, item.FrameTimeP50Ms, item.FrameTimeP99Ms, item.LongTasksCount, item.TimestampUtc));
                }

                var wsDist = ComputeDistribution(list.Select(x => x.WorkingSetMb));
                var pbDist = ComputeDistribution(list.Select(x => x.PrivateBytesMb));
                var cpuDist = ComputeDistribution(list.Select(x => x.CpuTimeMs));
                var p50Dist = ComputeDistribution(list.Select(x => x.FrameTimeP50Ms));
                var p99Dist = ComputeDistribution(list.Select(x => x.FrameTimeP99Ms));
                double meanLt = list.Average(x => x.LongTasksCount);

                report.AggregatedStates.Add(new StateAggregatedSummary
                {
                    StateName = grp.Key,
                    CompletedLaunches = list.Count,
                    WorkingSetMb = wsDist,
                    PrivateBytesMb = pbDist,
                    CpuTimeMs = cpuDist,
                    FrameTimeP50Ms = p50Dist,
                    FrameTimeP99Ms = p99Dist,
                    MeanLongTasks = meanLt
                });

                csvSb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "\"{0}_MEAN\",,{1:F2},{2:F2},{3:F1},{4:F2},{5:F2},{6:F1},",
                    grp.Key, wsDist.Mean, pbDist.Mean, cpuDist.Mean, p50Dist.Mean, p99Dist.Mean, meanLt));

                csvSb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "\"{0}_STDDEV\",,{1:F2},{2:F2},{3:F1},{4:F2},{5:F2},,",
                    grp.Key, wsDist.StdDev, pbDist.StdDev, cpuDist.StdDev, p50Dist.StdDev, p99Dist.StdDev));

                csvSb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "\"{0}_2SIGMA_GATE\",,{1:F2},{2:F2},{3:F1},{4:F2},{5:F2},,",
                    grp.Key, wsDist.Threshold2Sigma, pbDist.Threshold2Sigma, cpuDist.Threshold2Sigma, p50Dist.Threshold2Sigma, p99Dist.Threshold2Sigma));
            }

            File.WriteAllText(OutputCsvPath, csvSb.ToString(), Encoding.UTF8);

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(report, jsonOptions);
            File.WriteAllText(OutputJsonPath, json, Encoding.UTF8);

            LogService.Write("TELEMETRY", $"Phase 0 baseline updated at:\n  CSV: {OutputCsvPath}\n  JSON: {OutputJsonPath}");
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "PerformanceTelemetryService.SaveLaunchResults");
        }
    }

    public static class BaselineWorkloads
    {
        private static string GetFixtureUrl(string filename)
        {
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_fixtures", "benchmark", filename);
            if (!File.Exists(localPath))
            {
                string altPath = Path.Combine(Directory.GetCurrentDirectory(), "test_fixtures", "benchmark", filename);
                if (File.Exists(altPath)) localPath = altPath;
            }
            return new Uri(localPath).AbsoluteUri;
        }

        public static string InteractiveForegroundPage => GetFixtureUrl("page_interactive.html");
        public static string TimerHeavyBackgroundPage => GetFixtureUrl("page_timer_heavy.html");
        public static string StandardStaticPage => GetFixtureUrl("page_standard.html");

        public static string StateA_SingleTab => InteractiveForegroundPage;

        public static string[] GetStateB_FiveTabs() => new[]
        {
            InteractiveForegroundPage,
            TimerHeavyBackgroundPage,
            StandardStaticPage,
            StandardStaticPage,
            "horizon://home"
        };

        public static string[] GetStateC_TwentyTabs()
        {
            var list = new List<string>
            {
                InteractiveForegroundPage
            };
            for (int i = 0; i < 4; i++) list.Add(TimerHeavyBackgroundPage);
            for (int i = 0; i < 13; i++) list.Add(StandardStaticPage);
            list.Add("horizon://home");
            list.Add("horizon://home");
            return list.ToArray();
        }
    }

    public static async Task<Phase0BaselineReport> RunAutomatedBaselineSuiteAsync(
        Func<string, Task>? openTabAsync = null,
        Func<string, Task<string>>? evaluateScriptAsync = null,
        int repeatsPerState = 10,
        int hiddenDwellSeconds = 360,
        int launchId = 1,
        Action<string>? progressCallback = null)
    {
        var report = new Phase0BaselineReport
        {
            GeneratedUtc = DateTime.UtcNow,
            SystemMemory = QuerySystemMemory(),
            FixedConditions = QueryFixedConditions()
        };

        progressCallback?.Invoke($"Phase 0 Benchmark Launch {launchId} starting on {report.SystemMemory.HardwareTier} ({report.SystemMemory.LogicalCores} cores, Refresh: {report.FixedConditions.DisplayRefreshRate}, Power: {report.FixedConditions.PowerSource})...");

        progressCallback?.Invoke($"Sampling State A: Single Tab ({repeatsPerState} repeats)...");
        if (evaluateScriptAsync != null)
        {
            int targetFrames = hiddenDwellSeconds > 10 ? 5000 : 50;
            progressCallback?.Invoke($"Waiting for foreground frame probe to accumulate {targetFrames} frames...");
            for (int f = 0; f < 120; f++)
            {
                var fm = await QueryForegroundFrameMetricsAsync(evaluateScriptAsync);
                if (fm.SampledFrames >= targetFrames) break;
                await Task.Delay(1000);
            }
        }
        var stateA = await SampleStateAsync("State_A_SingleTab", repeatsPerState, intervalMs: 1000, evaluateScriptAsync);
        report.States.Add(stateA);

        if (openTabAsync != null)
        {
            progressCallback?.Invoke("Opening 4 background tabs for State B (including timer-heavy)...");
            var stateBTabs = BaselineWorkloads.GetStateB_FiveTabs();
            for (int i = 1; i < stateBTabs.Length; i++)
            {
                await openTabAsync(stateBTabs[i]);
                await Task.Delay(500);
            }
            progressCallback?.Invoke($"Applying single {hiddenDwellSeconds}s dwell for State B background timer throttling...");
            await Task.Delay(hiddenDwellSeconds * 1000);
        }
        progressCallback?.Invoke($"Sampling State B: 5 Tabs ({repeatsPerState} repeats)...");
        var stateB = await SampleStateAsync("State_B_FiveTabs", repeatsPerState, intervalMs: 1000, evaluateScriptAsync);
        report.States.Add(stateB);

        if (openTabAsync != null)
        {
            progressCallback?.Invoke("Opening 15 background tabs for State C (total 20 tabs)...");
            var stateCTabs = BaselineWorkloads.GetStateC_TwentyTabs();
            for (int i = BaselineWorkloads.GetStateB_FiveTabs().Length; i < stateCTabs.Length; i++)
            {
                await openTabAsync(stateCTabs[i]);
                await Task.Delay(400);
            }
            progressCallback?.Invoke($"Applying single {hiddenDwellSeconds}s dwell for State C background timer throttling...");
            await Task.Delay(hiddenDwellSeconds * 1000);
        }
        progressCallback?.Invoke($"Sampling State C: 20 Tabs ({repeatsPerState} repeats)...");
        var stateC = await SampleStateAsync("State_C_TwentyTabs", repeatsPerState, intervalMs: 1000, evaluateScriptAsync);
        report.States.Add(stateC);

        SaveLaunchResults(launchId, report);
        progressCallback?.Invoke($"Launch {launchId} Complete. Results aggregated in:\n  CSV: {OutputCsvPath}\n  JSON: {OutputJsonPath}");

        return report;
    }
}
