const fs = require('fs');

const filePath = 'C:/Users/deb/Projects/Sense/Sense-Desktop/EnfySense/ViewModels/MainWindowViewModel.cs';
let lines = fs.readFileSync(filePath, 'utf8').split('\\n');

// We want to find the mess between the first "[RelayCommand]" before "ConnectAsync" (around line 524)
// and "private async Task DisconnectAsync()" (around line 798)

let messStart = -1;
let messEnd = -1;

for (let i = 0; i < lines.length; i++) {
    if (lines[i].includes('[RelayCommand]') && lines[i+1]?.includes('async Task ConnectAsync()')) {
        if (messStart === -1) messStart = i;
    }
    if (lines[i].includes('private async Task DisconnectAsync()')) {
        messEnd = i;
        break;
    }
}

console.log({ messStart, messEnd });

if (messStart !== -1 && messEnd !== -1) {
    const fixedMiddle = `    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(BackendUrl))
        {
            StatusMessage = "Backend URL is required.";
            return;
        }

        try
        {
            IsBusy = true;
            AuthStatus = "Starting Microsoft sign-in...";
            StatusMessage = "Opening Microsoft sign-in in your browser...";
            SaveSettings();

            _authSession = await _authApiClient.LoginWithMicrosoftAsync(
                BackendUrl,
                _config.KeycloakIssuer,
                _config.KeycloakClientId,
                SsoRedirectUri);

            IsAuthenticated = true;
            CurrentUser = _authSession.User.DisplayName;
            AuthStatus = "Signed in with Microsoft";
            StatusMessage = "Microsoft sign-in completed successfully.";

            if (_config.RememberMe)
            {
                _config.LastSession = _authSession;
                _config.Save();
            }

            _ = SyncAdminSecretsAsync();

            if (AutoConnect)
            {
                await ConnectAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Microsoft sign-in failed: {ex}");
            AuthStatus = "Microsoft sign-in failed";
            StatusMessage = GetFriendlyErrorMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!IsAuthenticated)
        {
            StatusMessage = "Sign in with Microsoft before connecting the client.";
            return;
        }

        if (IsConnected) return;

        try
        {
            SaveSettings();
            StatusMessage = "Connecting to tracker gateway...";

            _agent = new LiveStreamAgent(BackendUrl, DeviceName, _authSession?.User.Email ?? "");
            _agent.ConnectionStatusChanged += HandleConnectionStatusChanged;
            _agent.StreamStatusChanged += HandleStreamStatusChanged;

            DeviceId = _agent.DeviceId;
            DeviceName = _agent.DeviceName;

            // Subscribe to policy updates BEFORE starting the agent to avoid race condition
            PolicyManager.Instance.PolicyUpdated += HandlePolicyUpdated;

            await _agent.StartAsync();
            
            _backgroundService = new BackgroundCaptureService(_agent);
            _backgroundService.Start();
            
            _activityService = new ActivityMonitoringService(_agent)
            {
                IsEnabled = IsTrackingActive && !IsPaused
            };

            IsConnected = true;
            IsDataInSync = true;
            LastSyncDisplay = DateTime.Now.ToString("MMM dd, yyyy HH:mm:ss");
            StatusMessage = "Connected. Monitoring active.";
            
            _ = FetchTodayStatsAsync();
            _ = FetchActivityHistoryAsync();

            if (IsTrackingActive)
            {
                _ = _agent.ReportWorkStatusAsync(IsPaused ? "BREAK" : "WORKING",
                                             (int)_workTodayAccumulated.TotalSeconds,
                                             (int)_overtimeTodayAccumulated.TotalSeconds,
                                             (int)_breakTodayAccumulated.TotalSeconds);
            }
            else
            {
                _ = _agent.ReportWorkStatusAsync("IDLE",
                                             (int)_workTodayAccumulated.TotalSeconds,
                                             (int)_overtimeTodayAccumulated.TotalSeconds,
                                             (int)_breakTodayAccumulated.TotalSeconds);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to connect: {ex.Message}");
            StatusMessage = "Connection failed. Check backend URL and network.";
        }
    }

`;

    const newLines = [...lines.slice(0, messStart), fixedMiddle, ...lines.slice(messEnd)];
    fs.writeFileSync(filePath, newLines.join('\\n'), 'utf8');
    console.log("ViewModel Repair successful!");
} else {
    console.error("Markers not found!", { messStart, messEnd });
    process.exit(1);
}
