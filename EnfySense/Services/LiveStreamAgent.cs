using SocketIOClient;
using System;
using System.Management;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EnfyLiveScreenClient.Services;

public sealed class LiveStreamAgent : IDisposable
{
    private readonly CaptureService _captureService = new();
    private readonly string _backendUrl;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly string _userName;
    private SocketIOClient.SocketIO? _socket;
    private CancellationTokenSource? _streamCts;
    private bool _isConnected;

    public event Action<string>? ConnectionStatusChanged;
    public event Action<string>? StreamStatusChanged;

    public LiveStreamAgent(string backendUrl, string deviceNameOverride = "", string userName = "")
    {
        _backendUrl = backendUrl.Trim().TrimEnd('/');
        _deviceId = GetHardwareId();
        _deviceName = string.IsNullOrWhiteSpace(deviceNameOverride)
            ? Environment.MachineName
            : deviceNameOverride.Trim();
        _userName = string.IsNullOrWhiteSpace(userName)
            ? Environment.UserName
            : userName.Trim();
    }

    public string DeviceId => _deviceId;
    public string DeviceKey => _deviceId; // Alias for cleaner access
    public string DeviceName => _deviceName;
    public string UserName => _userName;

    public async Task StartAsync()
    {
        if (_socket != null)
        {
            return;
        }

        _socket = new SocketIOClient.SocketIO($"{_backendUrl}/tracker", new SocketIOOptions
        {
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue,
        });

        _socket.OnConnected += async (_, _) =>
        {
            _isConnected = true;
            SetConnectionStatus($"Connected to {_backendUrl}");
            AppLogger.Log($"Socket connected. Device ID: {_deviceId}, Device Name: {_deviceName}", LogLevel.Info);
            await _socket.EmitAsync("join", new
            {
                deviceId = _deviceId,
                deviceName = _deviceName,
                userName = _userName,
                clientType = "agent",
            });

            // Trigger offline queue processing
            _ = Task.Run(() => OfflineQueueService.Instance.ProcessQueueAsync(this));
            
            // Note: status update will be triggered by ViewModel after connection is confirmed
        };

        _socket.OnDisconnected += (_, reason) =>
        {
            _isConnected = false;
            AppLogger.Log($"Socket disconnected. Reason: {reason}", LogLevel.Warn);
            StopStreaming("Live stream stopped.");
            SetConnectionStatus($"Disconnected: {reason}");
        };

        _socket.On("start-stream", response =>
        {
            try
            {
                var payload = response.GetValue<JsonElement>();
                var quality = payload.TryGetProperty("quality", out var value)
                    ? value.GetString() ?? "MED"
                    : "MED";

                _ = Task.Run(() => StartStreamingAsync(quality));
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "Handle start-stream");
            }
        });

        _socket.On("stop-stream", _ =>
        {
            StopStreaming("Live stream stopped by admin.");
        });

        _socket.On("apply-policy", response =>
        {
            try
            {
                var payload = response.GetValue<JsonElement>();
                PolicyManager.Instance.UpdatePolicy(payload);
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, "Handle apply-policy");
            }
        });

        _socket.On("take-capture", response =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    AppLogger.Log("Manual capture requested via socket.", LogLevel.Info);
                    var policy = PolicyManager.Instance.CurrentPolicy;
                    // Trigger a high-quality capture
                    var bytes = _captureService.CaptureScreen(0, 0, policy.Config.ScreenshotQuality);
                    var metadata = new
                    {
                        capturedAt = DateTime.UtcNow.ToString("o"),
                        triggerType = "MANUAL",
                        userName = _userName,
                        policyId = policy.Id
                    };
                    await SendBackgroundCaptureAsync(bytes, metadata);
                    AppLogger.Log("Manual capture sent successfully via socket.", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    AppLogger.Log(ex, "Handle take-capture socket event");
                }
            });
        });

        _socket.On("release-session", response =>
        {
            AppLogger.Log("Session release requested by admin. Stopping agent services.", LogLevel.Warn);
            _ = StopAsync();
        });

        await _socket.ConnectAsync();
    }

    public async Task StopAsync()
    {
        StopStreaming("Live stream stopped.");

        if (_socket != null)
        {
            await _socket.DisconnectAsync();
            _socket.Dispose();
            _socket = null;
        }

        _isConnected = false;
        SetConnectionStatus("Disconnected");
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "Dispose agent");
        }
    }

    private async Task StartStreamingAsync(string quality)
    {
        if (!_isConnected || _socket == null)
        {
            return;
        }

        StopStreaming("Switching stream profile...");

        var profile = StreamProfile.FromLabel(quality);
        _streamCts = new CancellationTokenSource();
        var token = _streamCts.Token;

        SetStreamStatus($"Streaming {profile.Label} at {profile.Width}x{profile.Height}");
        AppLogger.Log($"Starting live stream with profile {profile.Label}.");

        try
        {
            while (!token.IsCancellationRequested && _isConnected)
            {
                var startedAt = DateTime.UtcNow;
                var bytes = _captureService.CaptureScreen(
                    profile.Width,
                    profile.Height,
                    profile.JpegQuality);

                await _socket.EmitAsync("live-frame", new
                {
                    deviceId = _deviceId,
                    userName = _userName,
                    frameData = bytes, // Raw byte array for binary transport
                    format = "jpeg",
                    width = profile.Width,
                    height = profile.Height,
                    capturedAt = startedAt.ToString("o"),
                });

                var elapsed = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                var delay = Math.Max(1, profile.IntervalMs - elapsed);
                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop path.
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "Streaming loop");
            SetStreamStatus($"Stream error: {ex.Message}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                SetStreamStatus("Idle");
            }
        }
    }

    private void StopStreaming(string status)
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;
        SetStreamStatus(status);
    }

    private void SetConnectionStatus(string status)
    {
        AppLogger.Log(status);
        ConnectionStatusChanged?.Invoke(status);
    }

    private void SetStreamStatus(string status)
    {
        AppLogger.Log(status);
        StreamStatusChanged?.Invoke(status);
    }

    public async Task<bool> SendBackgroundCaptureAsync(byte[] frameData, object metadata)
    {
        if (!_isConnected || _socket == null) return false;

        try
        {
            var payload = new
            {
                deviceId = _deviceId,
                frameData = frameData, // Binary transport
                metadata = metadata
            };

            // Use a timeout to prevent hanging the background capture loop if the socket buffer is full
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            
            var emitTask = _socket.EmitAsync("background-capture", payload);
            var completedTask = await Task.WhenAny(emitTask, Task.Delay(-1, cts.Token));

            if (completedTask == emitTask)
            {
                return true;
            }
            else
            {
                AppLogger.Log("SendBackgroundCaptureAsync timed out after 15s.", LogLevel.Warn);
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            AppLogger.Log("SendBackgroundCaptureAsync cancelled/timed out.", LogLevel.Warn);
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "SendBackgroundCaptureAsync");
            return false;
        }
    }

    public async Task<bool> SendActivityLogsAsync(object logs)
    {
        if (!_isConnected || _socket == null) return false;

        try
        {
            await _socket.EmitAsync("track-activity", logs);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "SendActivityLogsAsync");
            return false;
        }
    }

    public async Task<bool> ReportWorkStatusAsync(string status, int workTime = -1, int overtime = -1, int breakTime = -1)
    {
        if (!_isConnected || _socket == null) return false;

        try
        {
            AppLogger.Log($"Reporting work status: {status}", LogLevel.Info);
            var payload = new
            {
                deviceId = _deviceId,
                status = status, // WORKING, BREAK, STOPPED
                userName = _userName,
                timestamp = DateTime.UtcNow.ToString("o"),
                workTimeSeconds = workTime >= 0 ? (int?)workTime : null,
                overtimeSeconds = overtime >= 0 ? (int?)overtime : null,
                breakTimeSeconds = breakTime >= 0 ? (int?)breakTime : null
            };
            await _socket.EmitAsync("work-status-update", payload);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "ReportWorkStatusAsync");
            return false;
        }
    }

    public async Task<bool> SendHeartbeatAsync(int workTimeSeconds, int overtimeSeconds, int breakTimeSeconds)
    {
        if (!_isConnected || _socket == null) return false;

        try
        {
            await _socket.EmitAsync("device-heartbeat", new
            {
                deviceId = _deviceId,
                userName = _userName,
                workTimeSeconds = workTimeSeconds,
                overtimeSeconds = overtimeSeconds,
                breakTimeSeconds = breakTimeSeconds
            });
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "SendHeartbeatAsync");
            return false;
        }
    }

    private static string GetHardwareId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT UUID FROM Win32_ComputerSystemProduct");

            foreach (var obj in searcher.Get())
            {
                var uuid = obj["UUID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(uuid) &&
                    uuid != "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" &&
                    uuid != "00000000-0000-0000-0000-000000000000")
                {
                    return uuid;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Hardware ID lookup failed: {ex.Message}");
        }

        return $"FALLBACK-{Environment.MachineName}";
    }

    private sealed class StreamProfile
    {
        public required string Label { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int IntervalMs { get; init; }
        public required long JpegQuality { get; init; }

        public static StreamProfile FromLabel(string quality)
        {
            return quality.ToUpperInvariant() switch
            {
                "LOW" => new StreamProfile
                {
                    Label = "LOW",
                    Width = 960,
                    Height = 540,
                    IntervalMs = 800,
                    JpegQuality = 50,
                },
                "HIGH" => new StreamProfile
                {
                    Label = "HIGH",
                    Width = 1600,
                    Height = 900,
                    IntervalMs = 150,
                    JpegQuality = 65,
                },
                "ULTRA" => new StreamProfile
                {
                    Label = "ULTRA",
                    Width = 1280,
                    Height = 720,
                    IntervalMs = 70,
                    JpegQuality = 55,
                },
                _ => new StreamProfile
                {
                    Label = "MED",
                    Width = 1280,
                    Height = 720,
                    IntervalMs = 300,
                    JpegQuality = 60,
                },

            };
        }
    }
}
