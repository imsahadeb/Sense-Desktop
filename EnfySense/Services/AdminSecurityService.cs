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

    private List<string> _authorizedSecrets = new();
    private readonly object _lock = new();

    private AdminSecurityService()
    {
        LoadFromCache();
    }

    /// <summary>
    /// Updates the list of authorized secrets (typically called after fetching from backend).
    /// </summary>
    public void UpdateSecrets(IEnumerable<string> secrets)
    {
        lock (_lock)
        {
            _authorizedSecrets = secrets.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            SaveToCache();
        }
    }

    /// <summary>
    /// Verifies if the provided 6-digit code matches ANY of the authorized admin secrets.
    /// </summary>
    public bool VerifyCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
            return false;

        lock (_lock)
        {
            foreach (var secret in _authorizedSecrets)
            {
                try
                {
                    byte[] secretBytes = Base32Encoding.ToBytes(secret);
                    var totp = new Totp(secretBytes);
                    
                    long timeStepMatched;
                    if (totp.VerifyTotp(code, out timeStepMatched, new VerificationWindow(1, 1)))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Skip invalid secrets
                }
            }
        }

        return false;
    }

    private void LoadFromCache()
    {
        try
        {
            if (File.Exists(CachePath))
            {
                var json = File.ReadAllText(CachePath);
                _authorizedSecrets = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to load admin secrets cache: {ex.Message}");
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

            var json = JsonSerializer.Serialize(_authorizedSecrets);
            File.WriteAllText(CachePath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to save admin secrets cache: {ex.Message}");
        }
    }
}
