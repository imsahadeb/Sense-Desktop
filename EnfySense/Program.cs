using Avalonia;
using System;
using System.Threading.Tasks;
using EnfyLiveScreenClient.Services;


namespace EnfyLiveScreenClient;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Handle command line arguments for uninstaller verification
        // This must happen before single instance check so the uninstaller can verify even if the app is running.
        if (args.Length >= 2 && args[0] == "--verify-totp")
        {
            string code = args[1];
            bool isValid = AdminSecurityService.Instance.VerifyCode(code);
            Environment.Exit(isValid ? 0 : 1);
        }

        // 2. Single instance check using a named Mutex
        // "Global\" prefix ensures the mutex is visible across all user sessions on the machine
        const string mutexName = "Global\\EnfySense-SingleInstance-73b9e40f-7b7e-4d8e-90f7-920f0f7f3f3f";
        using var mutex = new System.Threading.Mutex(false, mutexName);

        try
        {
            if (!mutex.WaitOne(TimeSpan.Zero, true))
            {
                // App is already running, exit immediately without logging or showing UI
                return;
            }

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

            var config = AppConfig.Load();
            AppLogger.Log($"EnfySense starting. Version: 1.0.15, OS: {Environment.OSVersion}, Writeable Config: {AppConfig.ConfigPath}", LogLevel.Info);
            AppLogger.Log($"Backend Target: {config.EffectiveBackendUrl}, Device Name: {Environment.MachineName}", LogLevel.Info);
            
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
