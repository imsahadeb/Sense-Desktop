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
            
            // If environment variable exists AND is not localhost, use it
            if (!string.IsNullOrEmpty(env) && !env.Contains("localhost")) 
                return env.StartsWith("http") ? env : $"https://{env}";
            
            // Fallback to config or default production URL
            var url = BackendUrl;
            if (string.IsNullOrWhiteSpace(url) || url.Contains("localhost")) 
                url = "https://backend.enfycon.com";

            return url.StartsWith("http") ? url : $"https://{url}";
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

    [JsonIgnore]
    public static string GlobalConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EnfySense",
        "Config",
        "appsettings.json");

    public static AppConfig Load()
    {
        try
        {
            AppConfig config;

            // 1. Try Local User Config (Highest priority)
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            // 2. Try Global Machine Config (From Installer)
            else if (File.Exists(GlobalConfigPath))
            {
                var json = File.ReadAllText(GlobalConfigPath);
                config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            else
            {
                // 3. New Default Config
                config = new AppConfig();
            }

            // Migration: If the config is pointing to localhost (dev leftover), force it to production
            if (config.BackendUrl != null && config.BackendUrl.Contains("localhost"))
            {
                config.BackendUrl = "https://backend.enfycon.com";
                config.Save();
            }

            return config;
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
