using Avalonia;
using System;
using System.Threading.Tasks;
using EnfyLiveScreenClient.Services;
using Velopack;

namespace EnfyLiveScreenClient;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // It's important to run Velopack as early as possible in your App startup.
        VelopackApp.Build().Run();

        // Setup global exception handling
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                AppLogger.Log(ex, "AppDomain.UnhandledException");
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            AppLogger.Log(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };

        try
        {
            var config = AppConfig.Load();
            AppLogger.Log($"EnfySense starting. Version: 1.0.0, OS: {Environment.OSVersion}, Writeable Config: {AppConfig.ConfigPath}", LogLevel.Info);
            AppLogger.Log($"Backend Target: {config.BackendUrl}, Device Name: {Environment.MachineName}", LogLevel.Info);
            
            // Apply Kiosk Hardening (Hide from Control Panel, etc.)
            LockdownService.Instance.ApplyHardening();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "Fatal Startup Error");
            throw;
        }
        finally
        {
            AppLogger.Log("Application shutting down.", LogLevel.Info);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
