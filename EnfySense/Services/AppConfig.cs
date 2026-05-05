using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnfyLiveScreenClient.Services;

public sealed class AppConfig
{
    public string? BackendUrl { get; set; }
    public string? SsoRedirectUri { get; set; }

    [JsonIgnore]
    public string EffectiveBackendUrl
    {
        get
        {
            var env = GetEnv("ENFYSENSE_BACKEND_URL");
            if (!string.IsNullOrEmpty(env)) return env;
            
            // If BackendUrl is null or empty, default to production
            if (string.IsNullOrWhiteSpace(BackendUrl)) 
                return "https://backend.enfycon.com";

            return BackendUrl;
        }
    }

    [JsonIgnore]
    public string EffectiveSsoRedirectUri
    {
        get
        {
            var env = GetEnv("ENFYSENSE_SSO_REDIRECT");
            if (!string.IsNullOrEmpty(env)) return env;
            return SsoRedirectUri ?? "http://localhost:3001/callback";
        }
    }

    private static string? GetEnv(string name)
    {
        return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
    }
    public bool AutoConnect { get; set; } = true;
    public string DeviceNameOverride { get; set; } = "";
    public string KeycloakIssuer { get; set; } = "https://auth.enfycon.com/realms/submission_tracker";
    public string KeycloakClientId { get; set; } = "submission_tracker_app";
    public bool RememberMe { get; set; } = true;
    public bool TermsAccepted { get; set; } = false;
    public AuthSession? LastSession { get; set; }

    /// <summary>
    /// Pre-configured TOTP secrets for offline/no-login admin verification.
    /// Add the Base32 TOTP secret here during deployment so admin unlock works
    /// without any network connection or user login.
    /// Example: ["DM1SDLJY7X1SM3RHD2ELRS1T0693LUAB"]
    /// </summary>
    public List<string> AdminTotpSecrets { get; set; } = new();

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
            // Debug logging for environment variables
            var envBackend = Environment.GetEnvironmentVariable("ENFYSENSE_BACKEND_URL", EnvironmentVariableTarget.Process)
                          ?? Environment.GetEnvironmentVariable("ENFYSENSE_BACKEND_URL", EnvironmentVariableTarget.User)
                          ?? Environment.GetEnvironmentVariable("ENFYSENSE_BACKEND_URL", EnvironmentVariableTarget.Machine);
            
            if (!string.IsNullOrEmpty(envBackend))
            {
                AppLogger.Log($"Detected ENFYSENSE_BACKEND_URL override: {envBackend}", LogLevel.Info);
            }

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
            // Security/routing guard: If BackendUrl matches an environment override 
            // or the default production URL, we save it as null to avoid hardcoding 
            // transient states into the config file.
            var envBackend = GetEnv("ENFYSENSE_BACKEND_URL");
            if (!string.IsNullOrEmpty(envBackend) && BackendUrl == envBackend)
            {
                BackendUrl = null;
            }
            if (BackendUrl == "https://backend.enfycon.com")
            {
                BackendUrl = null;
            }

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
