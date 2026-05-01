using System;
using System.Text.Json;

namespace EnfyLiveScreenClient.Services;

public record TrackedPolicyConfig(
    int ScreenshotIntervalSec,
    int ScreenshotQuality,
    int IdleThresholdSec
);

public class TodayStats
{
    public int WorkTimeSeconds { get; set; }
    public int OvertimeSeconds { get; set; }
    public int BreakTimeSeconds { get; set; }
}

public class TrackedPolicy
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TrackedPolicyConfig Config { get; set; } = new(60, 70, 600);
    public bool MaintenanceMode { get; set; }
    public TodayStats? TodayStats { get; set; }
}

public class PolicyManager
{
    private static readonly PolicyManager _instance = new();
    public static PolicyManager Instance => _instance;

    private TrackedPolicy _currentPolicy = new();
    public TrackedPolicy CurrentPolicy => _currentPolicy;

    public event Action<TrackedPolicy>? PolicyUpdated;

    private PolicyManager() { }

    public void UpdatePolicy(JsonElement json)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var newPolicy = JsonSerializer.Deserialize<TrackedPolicy>(json.GetRawText(), options);
            if (newPolicy != null)
            {
                _currentPolicy = newPolicy;
                AppLogger.Log($"Policy updated: {newPolicy.Name} (Interval: {newPolicy.Config.ScreenshotIntervalSec}s)", LogLevel.Info);
                PolicyUpdated?.Invoke(_currentPolicy);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "PolicyManager.UpdatePolicy");
        }
    }
}
