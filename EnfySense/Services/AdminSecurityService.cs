using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OtpNet;

namespace EnfyLiveScreenClient.Services;

public sealed class AdminSecurityService
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EnfySense",
        "Config",
        "admin_secrets.json");

    private static readonly Lazy<AdminSecurityService> _instance = 
        new(() => new AdminSecurityService());

    public static AdminSecurityService Instance => _instance.Value;

    /// <summary>
    /// Secrets fetched from the backend (cached to disk on successful sync).
    /// </summary>
    private List<string> _remoteSecrets = new();

    /// <summary>
    /// Secrets pre-configured in the local appsettings.json for offline/no-login use.
    /// These are NEVER overwritten by backend syncs.
    /// </summary>
    private List<string> _localConfigSecrets = new();

    private readonly object _lock = new();

    private AdminSecurityService()
    {
        LoadLocalConfigSecrets();
        LoadFromCache();
    }

    /// <summary>
    /// Updates the list of authorized secrets fetched from the backend.
    /// Local config secrets are preserved separately and always active.
    /// </summary>
    public void UpdateSecrets(IEnumerable<string> secrets)
    {
        lock (_lock)
        {
            _remoteSecrets = secrets.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            SaveToCache();
            AppLogger.Log($"AdminSecurityService: remote secrets updated ({_remoteSecrets.Count} entries).", LogLevel.Info);
        }
    }

    /// <summary>
    /// Returns true if the provided 6-digit TOTP code matches ANY authorized secret
    /// (local config OR backend-synced). Works fully offline and without login.
    /// </summary>
    public bool VerifyCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            AppLogger.Log($"VerifyCode rejected: code is null/empty or not 6 digits (length={code?.Length})", LogLevel.Warn);
            return false;
        }

        lock (_lock)
        {
            // Merge all available secrets — local config + remote cache
            var allSecrets = _localConfigSecrets
                .Concat(_remoteSecrets)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            AppLogger.Log(
                $"VerifyCode: checking against {allSecrets.Count} secret(s) " +
                $"({_localConfigSecrets.Count} local, {_remoteSecrets.Count} remote).",
                LogLevel.Debug);

            if (allSecrets.Count == 0)
            {
                AppLogger.Log(
                    "VerifyCode: NO secrets available. " +
                    "Add AdminTotpSecrets to appsettings.json or ensure the device has logged in at least once to cache backend secrets.",
                    LogLevel.Error);
                return false;
            }

            foreach (var secret in allSecrets)
            {
                try
                {
                    byte[] secretBytes = Base32Encoding.ToBytes(secret);
                    var totp = new Totp(secretBytes);

                    // Window of 2 steps = ±60 seconds clock drift tolerance
                    long timeStepMatched;
                    if (totp.VerifyTotp(code, out timeStepMatched, new VerificationWindow(2, 2)))
                    {
                        AppLogger.Log($"VerifyCode: TOTP matched at timeStep {timeStepMatched}.", LogLevel.Info);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"VerifyCode: failed to check a secret — {ex.Message}", LogLevel.Warn);
                }
            }

            AppLogger.Log("VerifyCode: No secret matched the provided code.", LogLevel.Warn);
        }

        return false;
    }

    /// <summary>
    /// Loads secrets pre-configured in the local appsettings.json (AdminTotpSecrets array).
    /// These work offline and without any login. Called once on startup.
    /// </summary>
    private void LoadLocalConfigSecrets()
    {
        try
        {
            var config = AppConfig.Load();
            _localConfigSecrets = config.AdminTotpSecrets
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (_localConfigSecrets.Count > 0)
            {
                AppLogger.Log($"AdminSecurityService: loaded {_localConfigSecrets.Count} local config secret(s) from appsettings.json.", LogLevel.Info);
            }
            else
            {
                AppLogger.Log("AdminSecurityService: no local config secrets found in appsettings.json (AdminTotpSecrets is empty).", LogLevel.Debug);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"AdminSecurityService: failed to load local config secrets — {ex.Message}", LogLevel.Warn);
        }
    }

    /// <summary>
    /// Loads previously backend-fetched secrets from the local cache file.
    /// </summary>
    private void LoadFromCache()
    {
        try
        {
            if (File.Exists(CachePath))
            {
                var json = File.ReadAllText(CachePath);
                _remoteSecrets = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                AppLogger.Log($"AdminSecurityService: loaded {_remoteSecrets.Count} remote secret(s) from disk cache.", LogLevel.Debug);
            }
            else
            {
                AppLogger.Log("AdminSecurityService: no disk cache found (admin_secrets.json missing).", LogLevel.Debug);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"AdminSecurityService: failed to load disk cache — {ex.Message}", LogLevel.Warn);
        }
    }

    private void SaveToCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_remoteSecrets);
            File.WriteAllText(CachePath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"AdminSecurityService: failed to save disk cache — {ex.Message}", LogLevel.Warn);
        }
    }
}
