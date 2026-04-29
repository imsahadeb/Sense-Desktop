using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace EnfyLiveScreenClient.Services;

public class UpdateService
{
    private readonly UpdateManager _updateManager;
    private readonly string _githubUrl = "https://github.com/imsahadeb/Sense-Desktop";

    public UpdateService()
    {
        // GitHubSource automatically handles checking the latest release on GitHub
        _updateManager = new UpdateManager(new GithubSource(_githubUrl, null, false));
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            AppLogger.Log("Checking for updates...", LogLevel.Info);
            
            // Check for new version
            var newVersion = await _updateManager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                AppLogger.Log("No updates available.", LogLevel.Info);
                return;
            }

            AppLogger.Log($"Update available: {newVersion.TargetFullRelease.Version}. Downloading...", LogLevel.Info);

            // Download new version
            await _updateManager.DownloadUpdatesAsync(newVersion);

            AppLogger.Log("Update downloaded. It will be applied on next restart.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "Error checking for updates");
        }
    }

    public void ApplyUpdatesAndRestart()
    {
        // UpdatePendingRestart is the modern replacement for IsUpdatePendingRestart
        if (_updateManager.UpdatePendingRestart != null)
        {
            // Passing null explicitly to apply the latest downloaded update
            _updateManager.ApplyUpdatesAndRestart(null);
        }
    }
}
