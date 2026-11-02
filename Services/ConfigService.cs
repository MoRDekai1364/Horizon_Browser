using System;
using System.IO;
using System.Linq;

namespace Horizon.Stealth.Services;

public static class ConfigService
{
    public static bool IsDevMode { get; private set; }
    public static string ProfileName { get; private set; } = "Default";

    public static string AppRoot { get; } = AppContext.BaseDirectory;
    public static string DataRoot { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horizon_Browser");
    public static string LogsRoot { get; private set; } = Path.Combine(DataRoot, "logs");
    public static string UserDataRoot { get; private set; } = Path.Combine(DataRoot, "HorizonData");
    public static string ExtensionsRoot { get; private set; } = Path.Combine(DataRoot, "Extensions");
    
    public static string UBlockPath => Path.Combine(ExtensionsRoot, "uBlockOrigin");
    public static string SponsorBlockPath => Path.Combine(ExtensionsRoot, "SponsorBlock");

    public static string DebugLogFile { get; private set; } = Path.Combine(LogsRoot, "debug_startup.log");
    public static string CrashTapeFile => Path.Combine(LogsRoot, "crash_tape.err");

    public static void Initialize(string[]? args = null)
    {
        args ??= Environment.GetCommandLineArgs();

        // 1. Explicit command line flags
        bool forceDev = args.Any(a => a.Equals("--dev", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("-dev", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("--test", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("-test", StringComparison.OrdinalIgnoreCase));

        string? customProfile = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))
            {
                customProfile = arg.Substring("--profile=".Length).Trim('"', ' ');
                break;
            }
        }

        // 2. Check if running from development / build directories
        string baseDir = AppRoot;
        bool isDebugDir = baseDir.IndexOf(@"\bin\Debug", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isRepoRelease = baseDir.IndexOf(@"\bin\Release", StringComparison.OrdinalIgnoreCase) >= 0 &&
                             (File.Exists(Path.Combine(baseDir, @"..\..\Horizon.Browser.csproj")) ||
                              File.Exists(Path.Combine(baseDir, @"..\..\..\Horizon.Browser.csproj")));

        // 3. Determine if Dev Mode should be active:
        // - Explicit flag (--dev / --test / --profile)
        // - Running from bin\Debug
        // - Running from bin\Release inside repo while main daily driver is already open
        if (forceDev || !string.IsNullOrEmpty(customProfile) || isDebugDir)
        {
            IsDevMode = true;
        }
        else if (isRepoRelease && BackgroundKeepAliveService.IsMainInstanceRunning())
        {
            IsDevMode = true;
        }

        string folderName = "Horizon_Browser";
        if (IsDevMode)
        {
            folderName = !string.IsNullOrEmpty(customProfile)
                ? $"Horizon_Browser_{customProfile}"
                : "Horizon_Browser_Dev";
            ProfileName = customProfile ?? "Dev";
        }

        DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folderName);
        LogsRoot = Path.Combine(DataRoot, "logs");
        UserDataRoot = Path.Combine(DataRoot, "HorizonData");
        ExtensionsRoot = Path.Combine(DataRoot, "Extensions");

        InitializeFileSystem();
    }

    public static void InitializeFileSystem()
    {
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(UserDataRoot);
        Directory.CreateDirectory(ExtensionsRoot);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        DebugLogFile = Path.Combine(LogsRoot, "debug_" + timestamp + ".log");
    }
}
