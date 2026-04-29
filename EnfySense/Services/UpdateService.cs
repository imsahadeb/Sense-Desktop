using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace EnfyLiveScreenClient.Services;

public class UpdateService
{
    private readonly UpdateManager _updateManager;
    private readonly string _githubUrl = "https://github.com/imsahadeb/Sense-Desktop";
    
    // Event to notify the UI when an update is ready
    public event Action? UpdateDownloaded;
    public string? ReleaseNotes { get; private set; }

    public UpdateService()
    {
        _updateManager = new UpdateManager(new GithubSource(_githubUrl, null, false));
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            AppLogger.Log("Checking for updates...", LogLevel.Info);
            
            var newVersion = await _updateManager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                AppLogger.Log("No updates available.", LogLevel.Info);
                return;
            }

            AppLogger.Log($"Update available: {newVersion.TargetFullRelease.Version}. Downloading...", LogLevel.Info);

            await _updateManager.DownloadUpdatesAsync(newVersion);

            AppLogger.Log("Update downloaded. Notifying UI...", LogLevel.Info);
            
            // Capture release notes
            ReleaseNotes = newVersion.TargetFullRelease.NotesMarkdown;

            // Notify the UI that the update is ready
            UpdateDownloaded?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "Error checking for updates");
        }
    }

    public void ApplyUpdatesAndRestart()
    {
        if (_updateManager.UpdatePendingRestart != null)
        {
            AppLogger.Log("Applying update and restarting...", LogLevel.Info);
            _updateManager.ApplyUpdatesAndRestart(null);
        }
    }
}
