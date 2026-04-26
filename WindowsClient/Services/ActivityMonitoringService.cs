using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using EnfyLiveScreenClient.Services;

namespace EnfyLiveScreenClient.Services;

public class ActivityMonitoringService : IDisposable
{
    private readonly LiveStreamAgent _agent;
    private readonly Timer _monitorTimer;
    private readonly List<ActivityLog> _buffer = new();
    private string? _lastApp;
    private string? _lastTitle;
    private string? _lastUrl;
    private DateTime _startTime;
    private DateTime _lastFlushTime = DateTime.UtcNow;
    private bool _isCurrentlyIdle;
    private const int IDLE_THRESHOLD_SECONDS = 300; // 5 minutes

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public ActivityMonitoringService(LiveStreamAgent agent)
    {
        _agent = agent;
        _startTime = DateTime.UtcNow;
        _monitorTimer = new Timer(OnTick, null, 1000, 1000);
    }

    private void OnTick(object? state)
    {
        try
        {
            CheckIdleStatus();

            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;

            // Get Process Info
            GetWindowThreadProcessId(hWnd, out int pid);
            string appName = "Unknown";
            try
            {
                using var process = Process.GetProcessById(pid);
                appName = process.ProcessName;
            }
            catch { }

            // Get Window Title
            StringBuilder titleBuilder = new StringBuilder(256);
            GetWindowText(hWnd, titleBuilder, 256);
            string title = titleBuilder.ToString();

            // Get URL if browser
            string? url = null;
            if (IsBrowser(appName))
            {
                url = GetBrowserUrl(hWnd);
            }

            // Detect change
            if (appName != _lastApp || title != _lastTitle || (url != null && url != _lastUrl))
            {
                RecordCurrentActivity();
                _lastApp = appName;
                _lastTitle = title;
                _lastUrl = url;
                _startTime = DateTime.UtcNow;
            }
            
            // Check if we should flush buffer (every 30 seconds)
            if (_buffer.Count >= 60 || (DateTime.UtcNow - _lastFlushTime).TotalSeconds >= 30)
            {
                FlushBuffer();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "ActivityMonitoringTick");
        }
    }

    private void CheckIdleStatus()
    {
        LASTINPUTINFO lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(lii);
        
        if (GetLastInputInfo(ref lii))
        {
            uint idleTimeMs = (uint)Environment.TickCount - lii.dwTime;
            double idleSeconds = idleTimeMs / 1000.0;

            if (idleSeconds >= IDLE_THRESHOLD_SECONDS)
            {
                if (!_isCurrentlyIdle)
                {
                    _isCurrentlyIdle = true;
                    AppLogger.Log($"User is now IDLE (Idle for {idleSeconds:F0}s)", LogLevel.Info);
                    _ = _agent.ReportWorkStatusAsync("IDLE");
                }
            }
            else
            {
                if (_isCurrentlyIdle)
                {
                    _isCurrentlyIdle = false;
                    AppLogger.Log("User has returned from IDLE.", LogLevel.Info);
                    // Report active status again. 
                    // The ViewModel usually handles this but we want the dashboard to update immediately.
                    // However, we don't know if they were WORKING or BREAK here.
                    // We'll rely on the next manual session update or we can query the ViewModel (not ideal dependency).
                    // For now, let's just report "WORKING" if they were active before, or better just let the manual state prevail.
                }
            }
        }
    }

    private void RecordCurrentActivity()
    {
        if (string.IsNullOrEmpty(_lastApp)) return;

        var duration = (int)(DateTime.UtcNow - _startTime).TotalSeconds;
        if (duration < 1) return;

        _buffer.Add(new ActivityLog
        {
            deviceId = _agent.DeviceKey,
            userName = _agent.UserName,
            appName = _lastApp,
            windowTitle = _lastTitle,
            url = _lastUrl,
            startTime = _startTime,
            endTime = DateTime.UtcNow,
            duration = duration,
            activityScore = 100.0f // Placeholder
        });
    }

    private bool IsBrowser(string appName)
    {
        string[] browsers = { "chrome", "msedge", "firefox", "brave", "opera" };
        return browsers.Contains(appName.ToLower());
    }

    private string? GetBrowserUrl(IntPtr hWnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hWnd);
            if (element == null) return null;

            var editBox = element.FindFirst(TreeScope.Descendants, 
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.AccessKeyProperty, "Ctrl+L")
                ));

            if (editBox == null)
            {
                editBox = element.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, "Address and search bar"));
            }

            if (editBox != null)
            {
                var pattern = editBox.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                return pattern?.Current.Value;
            }
        }
        catch { }
        return null;
    }

    private async void FlushBuffer()
    {
        if (!_buffer.Any()) return;

        var logsToFlush = _buffer.ToList();
        _buffer.Clear();
        _lastFlushTime = DateTime.UtcNow;

        try
        {
            await _agent.SendActivityLogsAsync(logsToFlush);
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "FlushActivityBuffer failed");
            // In a real app we'd queue these offline
        }
    }

    public void Dispose()
    {
        _monitorTimer.Dispose();
        RecordCurrentActivity();
        FlushBuffer();
    }
}

public class ActivityLog
{
    public string deviceId { get; set; } = "";
    public string? userName { get; set; }
    public string appName { get; set; } = "";
    public string? windowTitle { get; set; }
    public string? url { get; set; }
    public DateTime startTime { get; set; }
    public DateTime endTime { get; set; }
    public int duration { get; set; }
    public float activityScore { get; set; }
}
