using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace EnfyLiveScreenClient.Services;

public class UpdateService
{
    private readonly string _githubApiUrl = "https://api.github.com/repos/imsahadeb/Sense-Desktop/releases/latest";
    private string _currentVersion = "1.0.15";
    private string? _pendingInstallerPath;
    
    public event Action? UpdateDownloaded;
    public string? ReleaseNotes { get; private set; }

    public UpdateService()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null) _currentVersion = version.ToString(3);
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            AppLogger.Log($"Checking for updates... Current version: {_currentVersion}", LogLevel.Info);
            
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EnfySense-Client");
            
            var release = await client.GetFromJsonAsync<GithubRelease>(_githubApiUrl);
            if (release == null || string.IsNullOrEmpty(release.TagName)) return;

            string latestVersion = release.TagName.TrimStart('v');
            if (IsNewerVersion(latestVersion, _currentVersion))
            {
                AppLogger.Log($"New version available: {latestVersion}. Downloading installer...", LogLevel.Info);
                ReleaseNotes = release.Body;

                var asset = release.Assets?.Find(a => a.Name != null && a.Name.EndsWith(".exe") && a.Name.Contains("Setup"));
                if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                {
                    AppLogger.Log("No setup executable found in the latest release.", LogLevel.Warn);
                    return;
                }

                string tempPath = Path.Combine(Path.GetTempPath(), asset.Name!);
                using var response = await client.GetAsync(asset.BrowserDownloadUrl);
                using var fs = new FileStream(tempPath, FileMode.Create);
                await response.Content.CopyToAsync(fs);
                
                _pendingInstallerPath = tempPath;
                AppLogger.Log($"Update downloaded to {tempPath}. Notifying UI...", LogLevel.Info);
                UpdateDownloaded?.Invoke();
            }
            else
            {
                AppLogger.Log("App is up to date.", LogLevel.Info);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "Error checking for updates");
        }
    }

    private bool IsNewerVersion(string latest, string current)
    {
        try
        {
            var vLatest = new Version(latest);
            var vCurrent = new Version(current);
            return vLatest > vCurrent;
        }
        catch { return latest != current; }
    }

    public void ApplyUpdatesAndRestart()
    {
        if (string.IsNullOrEmpty(_pendingInstallerPath) || !File.Exists(_pendingInstallerPath))
        {
            AppLogger.Log("No pending update to apply.", LogLevel.Warn);
            return;
        }

        try
        {
            AppLogger.Log("Launching installer in silent mode...", LogLevel.Info);
            
            Process.Start(new ProcessStartInfo
            {
                FileName = _pendingInstallerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                UseShellExecute = true,
                Verb = "runas"
            });

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "Failed to launch update installer");
        }
    }

    private class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
        
        [JsonPropertyName("body")]
        public string? Body { get; set; }
        
        [JsonPropertyName("assets")]
        public List<GithubAsset>? Assets { get; set; }
    }

    private class GithubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
