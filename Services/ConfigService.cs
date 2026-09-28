using System.IO;

namespace Horizon.Stealth.Services;

public static class ConfigService
{
    public static readonly string AppRoot = AppContext.BaseDirectory;
    public static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horizon_Browser");
    public static readonly string LogsRoot = Path.Combine(DataRoot, "logs");
    public static readonly string UserDataRoot = Path.Combine(DataRoot, "HorizonData");
    public static readonly string ExtensionsRoot = Path.Combine(DataRoot, "Extensions");
    
    public static readonly string UBlockPath = Path.Combine(ExtensionsRoot, "uBlockOrigin");
    public static readonly string SponsorBlockPath = Path.Combine(ExtensionsRoot, "SponsorBlock");

    public static string DebugLogFile { get; private set; } = Path.Combine(LogsRoot, "debug_startup.log");
    public static readonly string CrashTapeFile = Path.Combine(LogsRoot, "crash_tape.err");

    public static void InitializeFileSystem()
    {
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(UserDataRoot);
        Directory.CreateDirectory(ExtensionsRoot);
        Directory.CreateDirectory(UBlockPath); 
        Directory.CreateDirectory(SponsorBlockPath);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        DebugLogFile = Path.Combine(LogsRoot, "debug_" + timestamp + ".log");
    }
}