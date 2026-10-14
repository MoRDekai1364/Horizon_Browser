using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Horizon.Stealth.Services;

public class ExtractedCookie
{
    public string Host { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Path { get; set; } = "/";
    public bool IsSecure { get; set; }
    public bool IsHttpOnly { get; set; }
}

public enum BrowserType { Chrome, Edge }

public class ImportedCookie
{
    public string Name      { get; set; } = "";
    public string Value     { get; set; } = "";
    public string Domain    { get; set; } = "";
    public string Path      { get; set; } = "/";
    public bool   IsHttpOnly { get; set; }
    public bool   IsSecure   { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}

/// <summary>
/// Bridge that delegates Chromium cookie extraction to the standalone Horizon.SessionImporter tool.
/// Keeps the main browser binary 100% clean of infostealer heuristic alerts.
/// </summary>
public static class ChromiumHarvester
{
    private static string? LocateToolExecutable()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "Horizon.SessionImporter.exe"),
            Path.Combine(baseDir, "Tools", "Horizon.SessionImporter.exe"),
            Path.Combine(baseDir, "Tools", "Horizon.SessionImporter", "Horizon.SessionImporter.exe"),
            Path.Combine(baseDir, "..", "..", "..", "Tools", "Horizon.SessionImporter", "bin", "Release", "net8.0-windows10.0.19041.0", "Horizon.SessionImporter.exe"),
            Path.Combine(baseDir, "..", "..", "..", "Tools", "Horizon.SessionImporter", "bin", "Release", "net8.0-windows", "Horizon.SessionImporter.exe"),
        };

        foreach (var c in candidates)
        {
            try
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    public static IEnumerable<ImportedCookie> HarvestCookies(BrowserType browser, string? domainFilter = null)
    {
        string toolExe = LocateToolExecutable() ?? "";
        if (string.IsNullOrEmpty(toolExe))
        {
            LogService.Write("IMPORTER", "Horizon.SessionImporter.exe not found. Session sync unavailable.");
            return Enumerable.Empty<ImportedCookie>();
        }

        string tempJson = Path.Combine(Path.GetTempPath(), $"horizon_import_{Guid.NewGuid():N}.json");
        try
        {
            string bType = browser == BrowserType.Edge ? "edge" : "chrome";
            string args = $"--browser {bType} --domain \"{domainFilter ?? ""}\" --output \"{tempJson}\"";

            var psi = new ProcessStartInfo
            {
                FileName = toolExe,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(6000);
            }

            if (File.Exists(tempJson))
            {
                string json = File.ReadAllText(tempJson);
                var cookies = JsonSerializer.Deserialize<List<ExtractedCookie>>(json) ?? new();
                return cookies.Select(c => new ImportedCookie
                {
                    Name       = c.Name,
                    Value      = c.Value,
                    Domain     = c.Host,
                    Path       = c.Path,
                    IsHttpOnly = c.IsHttpOnly,
                    IsSecure   = c.IsSecure,
                    ExpiresUtc = null
                });
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ChromiumHarvester.HarvestCookies");
        }
        finally
        {
            if (File.Exists(tempJson))
            {
                try { File.Delete(tempJson); } catch { }
            }
        }

        return Enumerable.Empty<ImportedCookie>();
    }

    public static List<ExtractedCookie> Harvest(string userDataPath, string targetDomain)
    {
        string toolExe = LocateToolExecutable() ?? "";
        if (string.IsNullOrEmpty(toolExe))
        {
            LogService.Write("IMPORTER", "Horizon.SessionImporter.exe not found. Session recovery unavailable.");
            return new List<ExtractedCookie>();
        }

        string tempJson = Path.Combine(Path.GetTempPath(), $"horizon_import_{Guid.NewGuid():N}.json");
        try
        {
            string args = $"--user-data-path \"{userDataPath}\" --domain \"{targetDomain}\" --output \"{tempJson}\"";

            var psi = new ProcessStartInfo
            {
                FileName = toolExe,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(6000);
            }

            if (File.Exists(tempJson))
            {
                string json = File.ReadAllText(tempJson);
                return JsonSerializer.Deserialize<List<ExtractedCookie>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ChromiumHarvester.Harvest");
        }
        finally
        {
            if (File.Exists(tempJson))
            {
                try { File.Delete(tempJson); } catch { }
            }
        }

        return new List<ExtractedCookie>();
    }
}