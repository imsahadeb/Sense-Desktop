using System;
using System.Diagnostics;
using System.IO;

namespace EnfyLiveScreenClient.Services;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}

public static class AppLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnfySense",
        "Logs");
    private static readonly string LogFilePath = Path.Combine(LogDir, "enfysense.log");

    private static readonly object SyncRoot = new();
    public static string CurrentLogFilePath => LogFilePath;

    static AppLogger()
    {
        try
        {
            if (!Directory.Exists(LogDir))
            {
                Directory.CreateDirectory(LogDir);
            }
        }
        catch { /* Cannot even log if this fails */ }
    }

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level.ToString().ToUpper()}] {message}";
        
        // Write to Debug for IDE visibility
        Debug.WriteLine(logLine);
        Console.WriteLine(logLine);

        try
        {
            lock (SyncRoot)
            {
                File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
            }
        }
        catch
        {
            // Avoid crashing because of logging.
        }
    }

    public static void Log(Exception ex, string? context = null)
    {
        var message = string.IsNullOrEmpty(context) 
            ? $"Exception: {ex.Message}" 
            : $"Exception in {context}: {ex.Message}";
            
        Log($"{message}{Environment.NewLine}{ex.StackTrace}", LogLevel.Error);
        
        if (ex.InnerException != null)
        {
            Log(ex.InnerException, "Inner Exception");
        }
    }
}
