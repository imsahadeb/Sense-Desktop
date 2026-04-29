using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EnfyLiveScreenClient.Services;

public sealed class BackgroundCaptureService : IDisposable
{
    private readonly CaptureService _captureService = new();
    private readonly LiveStreamAgent _agent;
    private readonly Random _random = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public BackgroundCaptureService(LiveStreamAgent agent)
    {
        _agent = agent;
        PolicyManager.Instance.PolicyUpdated += OnPolicyUpdated;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
        AppLogger.Log("Background capture service started.", LogLevel.Info);
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        AppLogger.Log("Background capture service stopped.", LogLevel.Info);
    }

    private void OnPolicyUpdated(TrackedPolicy policy)
    {
        // We could restart the loop here if the interval changed significantly, 
        // but the loop checks the interval dynamically each iteration.
        AppLogger.Log($"Background capture service updated for policy: {policy.Name}", LogLevel.Info);
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        AppLogger.Log("Background capture loop started.", LogLevel.Info);
        
        // Initial delay to let the app settle and policies sync
        AppLogger.Log("Background capture loop waiting 5s for initial startup...", LogLevel.Debug);
        await Task.Delay(5000, token);

        while (!token.IsCancellationRequested)
        {
            var policy = PolicyManager.Instance.CurrentPolicy;
            AppLogger.Log($"Background loop iteration started. Policy: {policy.Name}, Interval: {policy.Config.ScreenshotIntervalSec}s, Maintenance: {policy.MaintenanceMode}", LogLevel.Debug);
            
            if (policy.MaintenanceMode)
            {
                AppLogger.Log("Maintenance mode active. Skipping capture and waiting 5s.", LogLevel.Debug);
                await Task.Delay(5000, token);
                continue;
            }

            try
            {
                // Check Idle status
                var idleSeconds = GetIdleTimeSeconds();
                bool isIdle = idleSeconds >= (policy.Config.IdleThresholdSec > 0 ? policy.Config.IdleThresholdSec : 600);

                AppLogger.Log($"Triggering capture attempt. Trigger: {(isIdle ? "IDLE" : "SCHEDULED")}, Idle: {idleSeconds}s", LogLevel.Info);
                
                // Capture screen
                var bytes = _captureService.CaptureScreen(0, 0, policy.Config.ScreenshotQuality);
                AppLogger.Log($"Screen captured. Size: {bytes.Length} bytes. Quality: {policy.Config.ScreenshotQuality}", LogLevel.Info);
                
                var metadata = new
                {
                    capturedAt = DateTime.UtcNow.ToString("o"),
                    triggerType = isIdle ? "IDLE" : "SCHEDULED",
                    userName = _agent.UserName,
                    idleSeconds = idleSeconds,
                    policyId = policy.Id
                };

                AppLogger.Log($"Attempting to send capture to agent... (User: {_agent.UserName}, Device: {_agent.DeviceId})", LogLevel.Debug);
                bool sent = await _agent.SendBackgroundCaptureAsync(bytes, metadata);
                
                if (sent)
                {
                    AppLogger.Log("Background capture successfully sent to server.", LogLevel.Info);
                }
                else
                {
                    AppLogger.Log("Agent failed to send capture (maybe disconnected). Saving to offline queue.", LogLevel.Warn);
                    await OfflineQueueService.Instance.SaveToQueueAsync(bytes, metadata);
                }

                // Apply jitter: ±15% of the interval
                var baseIntervalMs = Math.Max(10000, policy.Config.ScreenshotIntervalSec * 1000);
                var jitterRange = (int)(baseIntervalMs * 0.15);
                var actualIntervalMs = baseIntervalMs + _random.Next(-jitterRange, jitterRange);

                AppLogger.Log($"Background capture session complete. Next capture scheduled in {actualIntervalMs / 1000}s.", LogLevel.Info);
                
                // Responsive wait: sleep in 5s increments to pick up policy changes/cancellation faster
                var remainingMs = actualIntervalMs;
                while (remainingMs > 0 && !token.IsCancellationRequested)
                {
                    var sleepChunk = Math.Min(5000, remainingMs);
                    await Task.Delay(sleepChunk, token);
                    remainingMs -= sleepChunk;
                    
                    // Check if policy changed while we were waiting
                    var updatedPolicy = PolicyManager.Instance.CurrentPolicy;
                    if (updatedPolicy.Id != policy.Id || updatedPolicy.Config.ScreenshotIntervalSec != policy.Config.ScreenshotIntervalSec)
                    {
                        AppLogger.Log($"Policy changed from {policy.Config.ScreenshotIntervalSec}s to {updatedPolicy.Config.ScreenshotIntervalSec}s. Restarting loop immediately.", LogLevel.Info);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) 
            {
                AppLogger.Log("Background capture loop cancellation requested. Exiting loop.", LogLevel.Info);
                break; 
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "Background capture loop CRASHED with exception");
                AppLogger.Log("Waiting 30s before retrying capture loop...", LogLevel.Warn);
                await Task.Delay(30000, token); 
            }
        }
    }

    private static uint GetIdleTimeSeconds()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        
        var lastInputInfo = new LASTINPUTINFO();
        lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
        
        if (GetLastInputInfo(ref lastInputInfo))
        {
            var elapsedMs = (uint)Environment.TickCount - lastInputInfo.dwTime;
            return elapsedMs / 1000;
        }
        return 0;
    }

    public void Dispose()
    {
        Stop();
        _captureService.Dispose();
        PolicyManager.Instance.PolicyUpdated -= OnPolicyUpdated;
    }
}
