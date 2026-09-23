using System.IO;
using System.Text;
using System.Windows;

namespace Horizon.Stealth.Services;

public static class LogService
{
    private static readonly object _lock = new();
    private static string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    private static string _debugLogFile;
    private static string _crashTapeFile;

    static LogService()
    {
        if (!Directory.Exists(_logDirectory))
            Directory.CreateDirectory(_logDirectory);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _debugLogFile = Path.Combine(_logDirectory, $"debug_{timestamp}.log");
        _crashTapeFile = Path.Combine(_logDirectory, $"crash_tape_{timestamp}.err");
    }

    public static void Initialize()
    {
        Write("SYSTEM", "Black Box Logging Initialized.");
        Write("SYSTEM", $"Log Path: {_debugLogFile}");
    }

    public static void Write(string source, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] [{source.ToUpper()}] {message}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_debugLogFile, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                
                try { File.WriteAllText("emergency_log_failure.txt", ex.Message); } catch { }
            } 
        }
    }

    public static void RecordCrash(Exception ex, string context = "Global")
    {
        var sb = new StringBuilder();
        sb.AppendLine("==========================================");
        sb.AppendLine("===       HORIZON BLACK BOX TAPE       ===");
        sb.AppendLine("==========================================");
        sb.AppendLine($"Timestamp: {DateTime.Now}");
        sb.AppendLine($"Context:   {context}");
        sb.AppendLine($"Exception: {ex.GetType().Name}");
        sb.AppendLine($"Message:   {ex.Message}");
        sb.AppendLine("------------------------------------------");
        sb.AppendLine("Stack Trace:");
        sb.AppendLine(ex.StackTrace);
        
        if (ex.InnerException != null)
        {
            sb.AppendLine("------------------------------------------");
            sb.AppendLine("Inner Exception:");
            sb.AppendLine(ex.InnerException.ToString());
        }
        sb.AppendLine("==========================================");

        lock (_lock)
        {
            try
            {
                File.WriteAllText(_crashTapeFile, sb.ToString());

                File.AppendAllText(_debugLogFile, Environment.NewLine + "[CRITICAL FAILURE DETECTED - MERGING TAPE]" + Environment.NewLine);
                File.AppendAllText(_debugLogFile, sb.ToString());
                File.AppendAllText(_debugLogFile, "[TAPE MERGE COMPLETE]" + Environment.NewLine);
            }
            catch { /* System is likely dying, nothing more to do */ }
        }
    }
}