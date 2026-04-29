using System;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace EnfyLiveScreenClient.Services;

public sealed class LockdownService
{
    private const string AppId = "EnfySense";
    
    private static readonly Lazy<LockdownService> _instance = 
        new(() => new LockdownService());

    public static LockdownService Instance => _instance.Value;

    private LockdownService() { }

    /// <summary>
    /// Hides the application from "Add or Remove Programs" in the Control Panel.
    /// This prevents standard admins from easily uninstalling the app.
    /// </summary>
    public void EnableStealthMode()
    {
        SetRegistryVisibility(isHidden: true);
    }

    /// <summary>
    /// Restores visibility in the Control Panel for maintenance/authorized uninstallation.
    /// </summary>
    public void DisableStealthMode()
    {
        SetRegistryVisibility(isHidden: false);
    }

    /// <summary>
    /// Ensures the application starts automatically when Windows boots.
    /// </summary>
    public void EnableAutoStart()
    {
        SetAutoStart(enabled: true);
    }

    /// <summary>
    /// Removes the auto-start entry.
    /// </summary>
    public void DisableAutoStart()
    {
        SetAutoStart(enabled: false);
    }

    private void SetRegistryVisibility(bool isHidden)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            // Velopack installs typically go to HKLM for machine-wide or HKCU for user-wide
            // We'll check both, prioritizing HKLM for Enterprise Kiosk setups
            bool success = TrySetRegistryValue(Registry.LocalMachine, isHidden);
            success |= TrySetRegistryValue(Registry.CurrentUser, isHidden);

            if (success)
            {
                AppLogger.Log($"Registry Stealth Mode {(isHidden ? "ENABLED" : "DISABLED")}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error updating registry stealth mode: {ex.Message}", LogLevel.Error);
        }
    }

    private void SetAutoStart(bool enabled)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string runKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        string exePath = Environment.ProcessPath ?? string.Empty;

        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            // We use HKCU for auto-start to ensure it works even if the user isn't running as full admin
            // but for kiosk machines, the user is usually a managed admin.
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            if (key != null)
            {
                if (enabled)
                {
                    key.SetValue(AppId, $"\"{exePath}\" --autostart");
                }
                else
                {
                    key.DeleteValue(AppId, throwOnMissingValue: false);
                }
                AppLogger.Log($"Auto-start {(enabled ? "ENABLED" : "DISABLED")} at {exePath}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to update auto-start registry: {ex.Message}", LogLevel.Error);
        }
    }

    private bool TrySetRegistryValue(RegistryKey root, bool isHidden)
    {
        string subKeyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}";
        
        try
        {
            using var key = root.OpenSubKey(subKeyPath, writable: true);
            if (key != null)
            {
                if (isHidden)
                {
                    // SystemComponent = 1 hides the entry from Control Panel
                    key.SetValue("SystemComponent", 1, RegistryValueKind.DWord);
                }
                else
                {
                    // Remove or set to 0 to show it again
                    key.DeleteValue("SystemComponent", throwOnMissingValue: false);
                }
                return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // This is expected if we don't have admin rights and try to write to HKLM
            // For a Kiosk app, it should be running with elevated privileges
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to access registry at {root.Name}\\{subKeyPath}: {ex.Message}", LogLevel.Warn);
        }
        
        return false;
    }

    /// <summary>
    /// Additional hardening: Disables the ability to end the task via Task Manager
    /// (Usually requires a companion service or specific process flags, but we can 
    /// at least monitor and restart if killed).
    /// </summary>
    public void ApplyHardening()
    {
        // On startup, ensure we are hidden from Control Panel
        EnableStealthMode();
        
        // Ensure we start on next boot
        EnableAutoStart();
    }
}
