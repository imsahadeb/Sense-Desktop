using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EnfyLiveScreenClient.Services;

public sealed class OfflineQueueService
{
    private static readonly OfflineQueueService _instance = new();
    public static OfflineQueueService Instance => _instance;

    private readonly string _queuePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isProcessing;

    private OfflineQueueService()
    {
        _queuePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Enfy",
            "OfflineQueue");

        if (!Directory.Exists(_queuePath))
        {
            Directory.CreateDirectory(_queuePath);
        }
    }

    public async Task SaveToQueueAsync(byte[] frameData, object metadata)
    {
        await _lock.WaitAsync();
        try
        {
            // Prune if queue is too large (max 100 files)
            var files = Directory.GetFiles(_queuePath, "*.jpg")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.CreationTime)
                .ToList();

            if (files.Count >= 100)
            {
                var oldest = files[0];
                AppLogger.Log($"Queue limit reached (100). Pruning oldest file: {oldest.Name}", LogLevel.Warn);
                oldest.Delete();
                var metaPath = Path.ChangeExtension(oldest.FullName, ".json");
                if (File.Exists(metaPath)) File.Delete(metaPath);
            }

            var id = Guid.NewGuid().ToString();
            var imagePath = Path.Combine(_queuePath, $"{id}.jpg");
            var metaPathNew = Path.Combine(_queuePath, $"{id}.json");

            await File.WriteAllBytesAsync(imagePath, frameData);
            await File.WriteAllTextAsync(metaPathNew, JsonSerializer.Serialize(metadata));
            
            AppLogger.Log($"Screenshot saved to offline queue: {id}", LogLevel.Info);
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "SaveToQueueAsync");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ProcessQueueAsync(LiveStreamAgent agent)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        try
        {
            // 1. Process Screenshots
            await ProcessScreenshotQueueAsync(agent);

            // 2. Process Activities
            await ProcessActivityQueueAsync(agent);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private async Task ProcessScreenshotQueueAsync(LiveStreamAgent agent)
    {
        List<string> filesToProcess;
        await _lock.WaitAsync();
        try
        {
            filesToProcess = Directory.GetFiles(_queuePath, "*.jpg")
                .OrderBy(f => new FileInfo(f).CreationTime)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }

        if (filesToProcess.Count == 0) return;

        AppLogger.Log($"Processing {filesToProcess.Count} screenshots in offline queue...", LogLevel.Info);

        foreach (var filePath in filesToProcess)
        {
            try
            {
                var metaPath = Path.ChangeExtension(filePath, ".json");
                if (!File.Exists(metaPath)) continue;

                var bytes = await File.ReadAllBytesAsync(filePath);
                var metaJson = await File.ReadAllTextAsync(metaPath);
                var metadata = JsonSerializer.Deserialize<JsonElement>(metaJson);

                bool sent = await agent.SendBackgroundCaptureAsync(bytes, metadata);
                if (sent)
                {
                    File.Delete(filePath);
                    File.Delete(metaPath);
                    await Task.Delay(200); 
                }
                else break;
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex, $"Queue processing screenshot - {Path.GetFileName(filePath)}");
            }
        }
    }

    private async Task ProcessActivityQueueAsync(LiveStreamAgent agent)
    {
        var activityFile = Path.Combine(_queuePath, "activities_queue.json");
        if (!File.Exists(activityFile)) return;

        List<object>? logs = null;
        await _lock.WaitAsync();
        try
        {
            var content = await File.ReadAllTextAsync(activityFile);
            logs = JsonSerializer.Deserialize<List<object>>(content);
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "Read activity queue file");
            File.Delete(activityFile); // Corrupted file?
        }
        finally
        {
            _lock.Release();
        }

        if (logs == null || logs.Count == 0) return;

        AppLogger.Log($"Processing {logs.Count} activity logs in offline queue...", LogLevel.Info);

        try
        {
            bool sent = await agent.SendActivityLogsAsync(logs);
            if (sent)
            {
                File.Delete(activityFile);
                AppLogger.Log("Offline activity queue sent successfully.", LogLevel.Info);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "ProcessActivityQueueAsync upload");
        }
    }

    public async Task SaveActivitiesToQueueAsync(IEnumerable<object> logs)
    {
        var activityFile = Path.Combine(_queuePath, "activities_queue.json");
        await _lock.WaitAsync();
        try
        {
            List<object> existing = new();
            if (File.Exists(activityFile))
            {
                var content = await File.ReadAllTextAsync(activityFile);
                var decoded = JsonSerializer.Deserialize<List<object>>(content);
                if (decoded != null) existing.AddRange(decoded);
            }

            existing.AddRange(logs);
            
            // Limit to 5000 logs to prevent huge files
            if (existing.Count > 5000)
            {
                existing = existing.Skip(existing.Count - 5000).ToList();
            }

            await File.WriteAllTextAsync(activityFile, JsonSerializer.Serialize(existing));
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "SaveActivitiesToQueueAsync");
        }
        finally
        {
            _lock.Release();
        }
    }
}
