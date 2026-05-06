using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace EnfyLiveScreenClient.Services;

public class WatchdogService
{
    private static readonly Lazy<WatchdogService> _instance = new(() => new WatchdogService());
    public static WatchdogService Instance => _instance.Value;

    private const string WatchdogExeName = "EnfySenseWatchdog.exe";
    private DispatcherTimer? _monitorTimer;

    private WatchdogService() { }

    public void StartMonitoring()
    {
        if (_monitorTimer != null) return;

        _monitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _monitorTimer.Tick += (s, e) => EnsureWatchdogRunning();
        _monitorTimer.Start();

        EnsureWatchdogRunning();
    }

    public void EnsureWatchdogRunning()
    {
        try
        {
            if (!Process.GetProcessesByName("EnfySenseWatchdog").Any())
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string watchdogPath = Path.Combine(baseDir, WatchdogExeName);

                if (File.Exists(watchdogPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = watchdogPath,
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    AppLogger.Log("WatchdogService: Watchdog process restarted.");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WatchdogService error: {ex.Message}");
        }
    }

    public void StopWatchdog()
    {
        try
        {
            _monitorTimer?.Stop();
            
            // Tell the watchdog to stop gracefully so it doesn't restart us while we are exiting
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string watchdogPath = Path.Combine(baseDir, WatchdogExeName);
            
            if (File.Exists(watchdogPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = watchdogPath,
                    Arguments = "--stop",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
            }

            // Kill any existing instances just in case
            foreach (var p in Process.GetProcessesByName("EnfySenseWatchdog"))
            {
                try { p.Kill(); } catch { }
            }
        }
        catch { }
    }
}
