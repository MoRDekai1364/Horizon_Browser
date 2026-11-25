using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Stealth.Views;
using Microsoft.Web.WebView2.Core;

namespace Horizon.Stealth.Services;

public sealed class RepairStepUpdate
{
    public string StepId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Status { get; init; } = "running"; // "running", "ok", "repaired", "warn"
    public double ProgressPercent { get; init; }
}

public sealed class SelfRepairOutcome
{
    public int AttemptNumber { get; init; }
    public bool Completed { get; init; }
    public int RepairsApplied { get; init; }
    public string RepairLogPath { get; init; } = "";
    public string Summary { get; init; } = "";
}

/// <summary>
/// Deep, multi-stage Browser Self-Repair / Restore Engine.
/// Automatically triggers on unhandled crashes for up to 2 escalating attempts within a rolling
/// 5-minute window (Attempt 1: Surgical Repair; Attempt 2: Deep Isolation & Recovery) before
/// escalating into the independent Crash Feedback + Background Debug Mode Reproduction workflow.
/// </summary>
public static class SelfRepairService
{
    public const int MaxRepairAttempts = 2;
    private static readonly TimeSpan RollingWindow = TimeSpan.FromMinutes(5);

    private sealed class RepairState
    {
        public int Attempts { get; set; }
        public DateTime LastAttemptUtc { get; set; } = DateTime.MinValue;
        public string LastRepairLogPath { get; set; } = "";
    }

    private static string CrashReportsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horizon_Browser", "CrashReports");

    private static string StateFilePath => Path.Combine(CrashReportsDir, "_repair_state.json");

    private static RepairState LoadState()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                var st = JsonSerializer.Deserialize<RepairState>(File.ReadAllText(StateFilePath, Encoding.UTF8));
                if (st != null)
                {
                    if (DateTime.UtcNow - st.LastAttemptUtc > RollingWindow)
                    {
                        st.Attempts = 0;
                    }
                    return st;
                }
            }
        }
        catch { }
        return new RepairState();
    }

    private static void SaveState(RepairState state)
    {
        try
        {
            Directory.CreateDirectory(CrashReportsDir);
            string tmp = StateFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            File.Move(tmp, StateFilePath, true);
        }
        catch { }
    }

    /// <summary>
    /// Schedules an automatic reset of the crash/repair counter after 60 seconds of healthy uptime.
    /// </summary>
    public static void ScheduleHealthyReset()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                var st = LoadState();
                if (st.Attempts > 0)
                {
                    st.Attempts = 0;
                    SaveState(st);
                    LogService.Write("SELFREPAIR", "Browser ran stably for 60s; self-repair attempt counter reset to 0.");
                }
            }
            catch { }
        });
    }

    /// <summary>
    /// Entry point on unhandled crash: writes crash payload JSON, decides whether to launch
    /// Self-Repair (Attempts 1 & 2) or escalate directly to Crash Feedback + Debug Mode (Attempt 3+).
    /// </summary>
    public static bool HandleCrash(Exception ex, string crashSource)
    {
        try
        {
            string exe = DefaultBrowserService.ResolveExecutablePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;

            var state = LoadState();
            string? payloadPath = CrashFeedbackWindow.WriteCrashPayloadFile(
                ex, crashSource, state.Attempts, state.LastRepairLogPath);
            if (string.IsNullOrEmpty(payloadPath)) return false;

            if (state.Attempts < MaxRepairAttempts)
            {
                state.Attempts++;
                state.LastAttemptUtc = DateTime.UtcNow;
                SaveState(state);

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--self-repair=\"{payloadPath}\" --repair-attempt={state.Attempts}",
                    UseShellExecute = true
                });
                return true;
            }

            // 2 self-repair attempts already exhausted within the rolling window -> escalate to Crash Feedback + Debug Mode!
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--crash-report=\"{payloadPath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Executes the full multi-stage self-repair pipeline for Attempt 1 (Surgical) or Attempt 2 (Deep).
    /// Reports live step progress to the overlay UI and writes a comprehensive self_repair_*.log.
    /// </summary>
    public static async Task<SelfRepairOutcome> RunRepairAsync(
        CrashReportPayload payload,
        int attemptNumber,
        Action<RepairStepUpdate> onProgress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(CrashReportsDir);
        string repairLogPath = Path.Combine(CrashReportsDir, $"self_repair_a{attemptNumber}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
        var log = new StringBuilder();
        int repairs = 0;

        void LogLine(string msg)
        {
            log.AppendLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {msg}");
        }

        LogLine("======================================================================");
        LogLine($"HORIZON BROWSER SELF-REPAIR ENGINE — ATTEMPT {attemptNumber} OF {MaxRepairAttempts}");
        LogLine($"Mode: {(attemptNumber == 1 ? "Surgical Non-Destructive Repair" : "Deep Isolation & Recovery")}");
        LogLine($"Triggering Crash: {payload.ExceptionType}: {payload.ExceptionMessage}");
        LogLine($"Version: {payload.BrowserVersion} ({payload.UpdateChannel}) | Build: {payload.ReleaseBuildId}");
        LogLine("======================================================================");

        string userDataRoot = ConfigService.UserDataRoot;
        string appDir = AppDomain.CurrentDomain.BaseDirectory;

        try
        {
            // ── STAGE 1: Terminate orphaned WebView2 / zombie browser locks ─────────────
            ct.ThrowIfCancellationRequested();
            onProgress(new RepairStepUpdate
            {
                StepId = "procs",
                Title = "Clearing orphaned browser & WebView2 locks",
                Detail = "Scanning for hung renderer processes holding profile locks...",
                Status = "running",
                ProgressPercent = 12
            });
            int killed = await Task.Run(() => CleanupOrphanedProcesses(LogLine), ct);
            if (killed > 0) repairs++;
            onProgress(new RepairStepUpdate
            {
                StepId = "procs",
                Title = "Clearing orphaned browser & WebView2 locks",
                Detail = killed > 0 ? $"Released {killed} stuck process(es)." : "No stuck processes detected.",
                Status = killed > 0 ? "repaired" : "ok",
                ProgressPercent = 22
            });
            await Task.Delay(260, ct);

            // ── STAGE 2: Purge stale Chromium lockfiles & corrupted temp files ──────────
            ct.ThrowIfCancellationRequested();
            onProgress(new RepairStepUpdate
            {
                StepId = "locks",
                Title = "Purging stale profile lockfiles & temp journals",
                Detail = "Checking SingletonLock, Lockfile, and incomplete .tmp writes...",
                Status = "running",
                ProgressPercent = 32
            });
            int locksCleared = await Task.Run(() => PurgeStaleLocksAndTemps(userDataRoot, LogLine), ct);
            if (locksCleared > 0) repairs++;
            onProgress(new RepairStepUpdate
            {
                StepId = "locks",
                Title = "Purging stale profile lockfiles & temp journals",
                Detail = locksCleared > 0 ? $"Cleaned {locksCleared} stale lock/temp file(s)." : "Profile locks are clean.",
                Status = locksCleared > 0 ? "repaired" : "ok",
                ProgressPercent = 42
            });
            await Task.Delay(260, ct);

            // ── STAGE 3: Audit & atomic self-heal of JSON stores ────────────────────────
            ct.ThrowIfCancellationRequested();
            onProgress(new RepairStepUpdate
            {
                StepId = "json",
                Title = "Auditing configuration, bookmarks & manifest stores",
                Detail = "Validating JSON integrity and restoring any corrupted files from backups...",
                Status = "running",
                ProgressPercent = 52
            });
            int jsonHealed = await Task.Run(() => AuditAndHealJsonStores(userDataRoot, LogLine), ct);
            if (jsonHealed > 0) repairs++;
            onProgress(new RepairStepUpdate
            {
                StepId = "json",
                Title = "Auditing configuration, bookmarks & manifest stores",
                Detail = jsonHealed > 0 ? $"Repaired {jsonHealed} corrupted JSON store(s)." : "All configuration & data stores verified healthy.",
                Status = jsonHealed > 0 ? "repaired" : "ok",
                ProgressPercent = 62
            });
            await Task.Delay(260, ct);

            // ── STAGE 4: Flush corrupted GPU / Shader / Code caches (cookies preserved) ─
            ct.ThrowIfCancellationRequested();
            onProgress(new RepairStepUpdate
            {
                StepId = "cache",
                Title = "Rebuilding GPU shader & Chromium code caches",
                Detail = "Flushing volatile shader/code caches while preserving logins & cookies...",
                Status = "running",
                ProgressPercent = 72
            });
            int cachesFlushed = await Task.Run(() => FlushVolatileCaches(userDataRoot, deep: attemptNumber >= 2, LogLine), ct);
            if (cachesFlushed > 0) repairs++;
            onProgress(new RepairStepUpdate
            {
                StepId = "cache",
                Title = "Rebuilding GPU shader & Chromium code caches",
                Detail = cachesFlushed > 0 ? $"Rebuilt {cachesFlushed} volatile cache folder(s)." : "Caches already clean.",
                Status = cachesFlushed > 0 ? "repaired" : "ok",
                ProgressPercent = 80
            });
            await Task.Delay(260, ct);

            // ── STAGE 5: Graphics / DWM crash mitigation check ──────────────────────────
            ct.ThrowIfCancellationRequested();
            bool graphicsMitigated = await Task.Run(() => CheckAndMitigateGraphicsCrash(payload, LogLine), ct);
            if (graphicsMitigated)
            {
                repairs++;
                onProgress(new RepairStepUpdate
                {
                    StepId = "gfx",
                    Title = "Applying safe graphics & blur fallback",
                    Detail = "Detected rendering/DWM fault; switched to safe non-blur rendering mode.",
                    Status = "repaired",
                    ProgressPercent = 86
                });
                await Task.Delay(240, ct);
            }

            // ── STAGE 6 (Attempt 2 ONLY): Deep Isolation — Quarantine Extensions & Session
            if (attemptNumber >= 2)
            {
                ct.ThrowIfCancellationRequested();
                onProgress(new RepairStepUpdate
                {
                    StepId = "deep",
                    Title = "Deep recovery: isolating third-party extensions & crash-looping tabs",
                    Detail = "Backing up session and temporarily disabling third-party extensions for safe boot...",
                    Status = "running",
                    ProgressPercent = 90
                });
                int deepActions = await Task.Run(() => RunDeepIsolationRecovery(userDataRoot, appDir, LogLine), ct);
                if (deepActions > 0) repairs += deepActions;
                onProgress(new RepairStepUpdate
                {
                    StepId = "deep",
                    Title = "Deep recovery: isolating third-party extensions & crash-looping tabs",
                    Detail = deepActions > 0 ? $"Isolated {deepActions} potential crash source(s) (reversible backups saved)." : "Verified runtime & native core integrity.",
                    Status = deepActions > 0 ? "repaired" : "ok",
                    ProgressPercent = 96
                });
                await Task.Delay(300, ct);
            }

            // Persist repair log path in state so Attempt 3 escalation attaches it automatically
            File.WriteAllText(repairLogPath, log.ToString(), Encoding.UTF8);
            var st = LoadState();
            st.LastRepairLogPath = repairLogPath;
            SaveState(st);

            onProgress(new RepairStepUpdate
            {
                StepId = "done",
                Title = "Self-repair complete — relaunching Horizon Browser",
                Detail = $"Applied {repairs} repair action(s). Starting Horizon cleanly...",
                Status = "ok",
                ProgressPercent = 100
            });

            return new SelfRepairOutcome
            {
                AttemptNumber = attemptNumber,
                Completed = true,
                RepairsApplied = repairs,
                RepairLogPath = repairLogPath,
                Summary = $"Self-Repair Attempt {attemptNumber} completed ({repairs} repair action(s))."
            };
        }
        catch (OperationCanceledException)
        {
            LogLine("Self-repair cancelled by user (escalating to Crash Feedback).");
            try { File.WriteAllText(repairLogPath, log.ToString(), Encoding.UTF8); } catch { }
            return new SelfRepairOutcome
            {
                AttemptNumber = attemptNumber,
                Completed = false,
                RepairsApplied = repairs,
                RepairLogPath = repairLogPath,
                Summary = "Cancelled by user."
            };
        }
        catch (Exception ex)
        {
            LogLine($"Self-repair internal error: {ex}");
            try { File.WriteAllText(repairLogPath, log.ToString(), Encoding.UTF8); } catch { }
            return new SelfRepairOutcome
            {
                AttemptNumber = attemptNumber,
                Completed = false,
                RepairsApplied = repairs,
                RepairLogPath = repairLogPath,
                Summary = ex.Message
            };
        }
    }

    private static int CleanupOrphanedProcesses(Action<string> log)
    {
        int killed = 0;
        int myPid = Environment.ProcessId;
        try
        {
            foreach (var p in Process.GetProcessesByName("Horizon.Browser"))
            {
                try
                {
                    if (p.Id == myPid) continue;
                    // Give dying instance a tiny grace moment, then terminate if stuck
                    if (!p.WaitForExit(350))
                    {
                        p.Kill(entireProcessTree: true);
                        killed++;
                        log($"Terminated stuck Horizon.Browser process tree (PID={p.Id}).");
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
        return killed;
    }

    private static int PurgeStaleLocksAndTemps(string userDataRoot, Action<string> log)
    {
        int removed = 0;
        string[] lockNames = { "SingletonLock", "SingletonCookie", "SingletonSocket", "Lockfile", "LOCK" };

        var scanDirs = new List<string>
        {
            userDataRoot,
            Path.Combine(userDataRoot, "EBWebView"),
            Path.Combine(userDataRoot, "EBWebView", "Default"),
            WebAppJournalService.JournalDir
        };

        foreach (string dir in scanDirs.Where(Directory.Exists))
        {
            foreach (string name in lockNames)
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path))
                {
                    try { File.Delete(path); removed++; log($"Removed stale lockfile: {path}"); }
                    catch { }
                }
            }
            try
            {
                foreach (string tmp in Directory.GetFiles(dir, "*.tmp"))
                {
                    try { File.Delete(tmp); removed++; log($"Removed stale temp file: {tmp}"); }
                    catch { }
                }
            }
            catch { }
        }
        return removed;
    }

    private static int AuditAndHealJsonStores(string userDataRoot, Action<string> log)
    {
        int healed = 0;
        var jsonFiles = new List<string>
        {
            Path.Combine(userDataRoot, "config.json"),
            Path.Combine(userDataRoot, "extensions.json"),
            Path.Combine(userDataRoot, "bookmarks.json"),
            Path.Combine(userDataRoot, "history.json"),
            Path.Combine(userDataRoot, "session.json")
        };

        foreach (string file in jsonFiles)
        {
            if (!File.Exists(file)) continue;
            try
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) throw new JsonException("Empty JSON file");
                using var _ = JsonDocument.Parse(text);
                log($"JSON OK: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                log($"JSON CORRUPT: {Path.GetFileName(file)} ({ex.Message})");
                string bak = file + ".bak";
                string quarantine = file + $".corrupt_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                try { File.Move(file, quarantine, true); } catch { }

                if (File.Exists(bak))
                {
                    try
                    {
                        File.Copy(bak, file, true);
                        log($"Restored {Path.GetFileName(file)} from .bak backup.");
                    }
                    catch { }
                }
                healed++;
            }
        }
        return healed;
    }

    private static int FlushVolatileCaches(string userDataRoot, bool deep, Action<string> log)
    {
        int flushed = 0;
        var cacheSubDirs = new List<string>
        {
            Path.Combine(userDataRoot, "EBWebView", "GrShaderCache"),
            Path.Combine(userDataRoot, "EBWebView", "GraphiteDawnCache"),
            Path.Combine(userDataRoot, "EBWebView", "ShaderCache"),
            Path.Combine(userDataRoot, "EBWebView", "Default", "GPUCache"),
            Path.Combine(userDataRoot, "EBWebView", "Default", "Code Cache"),
            Path.Combine(userDataRoot, "EBWebView", "Default", "DawnGraphiteCache"),
            Path.Combine(userDataRoot, "EBWebView", "Default", "DawnWebGPUCache")
        };

        if (deep)
        {
            cacheSubDirs.Add(Path.Combine(userDataRoot, "EBWebView", "Default", "Cache"));
            cacheSubDirs.Add(Path.Combine(userDataRoot, "EBWebView", "Default", "Service Worker", "CacheStorage"));
        }

        foreach (string dir in cacheSubDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                flushed++;
                log($"Flushed volatile cache: {dir}");
            }
            catch (Exception ex)
            {
                log($"Could not flush {dir}: {ex.Message}");
            }
        }
        return flushed;
    }

    private static bool CheckAndMitigateGraphicsCrash(CrashReportPayload payload, Action<string> log)
    {
        string combined = $"{payload.ExceptionType} {payload.ExceptionMessage} {payload.StackTrace}";
        bool isGraphicsFault =
            combined.Contains("UCEERR_RENDERTHREADFAILURE", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Dwm", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("BlurEffect", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("MilCore", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Direct3D", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) && combined.Contains("Render", StringComparison.OrdinalIgnoreCase);

        if (!isGraphicsFault) return false;

        try
        {
            SettingsService.Load();
            if (!SettingsService.Current.DisableBlurEffects)
            {
                SettingsService.Current.DisableBlurEffects = true;
                SettingsService.Save();
                log("Graphics/DWM crash signature detected -> enabled safe non-blur mode (DisableBlurEffects=true).");
                return true;
            }
        }
        catch { }
        return false;
    }

    private static int RunDeepIsolationRecovery(string userDataRoot, string appDir, Action<string> log)
    {
        int actions = 0;

        // 1. Quarantine crash-looping session file (with full backup)
        try
        {
            string sessionFile = Path.Combine(userDataRoot, "session.json");
            if (File.Exists(sessionFile))
            {
                string backup = Path.Combine(CrashReportsDir, $"session_quarantine_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                File.Copy(sessionFile, backup, true);
                File.Delete(sessionFile);
                actions++;
                log($"Quarantined session.json -> {backup} to break potential crash-looping tab.");
            }
        }
        catch (Exception ex) { log("Session quarantine error: " + ex.Message); }

        // 2. Temporarily disable non-bundled third-party extensions (reversible)
        try
        {
            ExtensionService.EnsureInstalled();
            int disabledCount = 0;
            foreach (var ext in ExtensionService.All.ToList())
            {
                if (ext.Enabled && ext.Source != ExtensionSource.Bundled)
                {
                    ExtensionService.SetEnabled(ext.Id, false);
                    disabledCount++;
                }
            }
            if (disabledCount > 0)
            {
                actions++;
                log($"Temporarily disabled {disabledCount} third-party extension(s) for safe recovery boot.");
            }
        }
        catch (Exception ex) { log("Extension quarantine error: " + ex.Message); }

        // 3. Verify WebView2 Runtime & native horizon_core.dll health
        try
        {
            string? wv2 = CoreWebView2Environment.GetAvailableBrowserVersionString();
            log($"WebView2 Runtime verification: {wv2 ?? "MISSING"}");
        }
        catch (Exception ex) { log("WebView2 Runtime verification FAILED: " + ex.Message); }

        string coreDll = Path.Combine(appDir, "horizon_core.dll");
        log($"Native horizon_core.dll present: {File.Exists(coreDll)}");

        return actions;
    }
}
