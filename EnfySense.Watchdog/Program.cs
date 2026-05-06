using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace EnfySense.Watchdog;

internal static class Program
{
    private const string MainExeName = "EnfySense.exe";
    private const string MutexName = "Global\\EnfySense-Watchdog-Mutex-920f0f7f3f3f";

    public static void Main(string[] args)
    {
        // 1. Single Instance Check
        using var mutex = new Mutex(false, MutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.Zero, true))
            {
                return; // Already running
            }

            // 2. Handle silent termination (used during uninstall or authorized exit)
            if (args.Contains("--stop"))
            {
                return;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string mainExePath = Path.Combine(baseDir, MainExeName);

            // 3. Monitor Loop
            while (true)
            {
                try
                {
                    if (!IsProcessRunning("EnfySense"))
                    {
                        if (File.Exists(mainExePath))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = mainExePath,
                                Arguments = "--autostart",
                                UseShellExecute = true
                            });
                        }
                    }
                }
                catch
                {
                    // Ignore errors in loop
                }

                Thread.Sleep(5000); // Check every 5 seconds
            }
        }
        catch
        {
            // Fatal error
        }
    }

    private static bool IsProcessRunning(string name)
    {
        return Process.GetProcessesByName(name).Any();
    }
}
