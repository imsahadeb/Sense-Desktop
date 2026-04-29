using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnfyLiveScreenClient.Services;

public sealed class AppConfig
{
    public string BackendUrl { get; set; } = "http://192.168.1.9:5000";
    public bool AutoConnect { get; set; } = true;
    public string DeviceNameOverride { get; set; } = "";
    public string KeycloakIssuer { get; set; } = "https://auth.enfycon.com/realms/submission_tracker";
    public string KeycloakClientId { get; set; } = "submission_tracker_app";
    public string SsoRedirectUri { get; set; } = "http://localhost:3001/callback";

    [JsonIgnore]
    public static string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnfySense",
        "Config");

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(ConfigDir, "appsettings.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var config = new AppConfig();
                config.Save();
                return config;
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to load config: {ex}");
            return new AppConfig();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to save config: {ex}");
        }
    }
}
