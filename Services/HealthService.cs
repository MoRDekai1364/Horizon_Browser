using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Horizon.Stealth.Services;

public static class HealthService
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    public static void NuclearPurge()
    {
        try
        {
            var mainProc = Process.GetCurrentProcess();
            EmptyWorkingSet(mainProc.Handle);
            
            var renderers = Process.GetProcessesByName("msedgewebview2");
            foreach (var p in renderers)
            {
                try { EmptyWorkingSet(p.Handle); } catch { }
            }

            LogService.Write("HEALTH", "Nuclear Memory Purge executed.");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex);
        }
    }

    public static string GetDiagnostics()
    {
        var proc = Process.GetCurrentProcess();
        long mem = proc.WorkingSet64 / 1024 / 1024;
        return $"Shell (Horizon): {mem} MB\n" +
               $"Threads: {proc.Threads.Count}\n" +
               $"Uptime: {(DateTime.Now - proc.StartTime).ToString(@"hh\:mm\:ss")}";
    }
}