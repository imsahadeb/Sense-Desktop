# EnfySense Release Notes

## [Security & Resilience] - "The Immortal App"
- **New Watchdog System**: Implemented `EnfySenseWatchdog.exe`, a companion process that ensures the application cannot be killed via Task Manager. The app and watchdog now mutually monitor and restart each other.
- **Admin Verification Fix**: Re-ordered startup logic to allow the Uninstaller to verify Admin TOTP codes even while the application is active.
- **Enterprise-Grade Installer**: Migrated from Velopack to Inno Setup for a more stable and professional installation experience.

## [User Experience & Personalization]
- **App Settings Menu**: Added a new settings overlay accessible from the profile dropdown.
- **Customizable Floating Widget**: Users can now toggle the floating desktop widget on or off based on their preference.
- **Manual vs. Auto Intelligence**: Added an "Auto Intelligence Mode" toggle. Users can now choose between automatic activity detection or manual break management.
- **Hover Insights**: Added helpful tooltips across the UI to explain complex features on hover.

## [Stability & Fixes]
- **Auto-Resume Fixed**: Resolved an issue where work would not resume automatically after returning from an idle break.
- **Silent Background Updates**: Refined the auto-update mechanism to silently download and install updates in the background using Inno Setup's silent mode.
- **Encoding Correction**: Fixed a bug where release notes appeared scrambled on GitHub.
