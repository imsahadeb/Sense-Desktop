const fs = require('fs');

const filePath = 'C:/Users/deb/Projects/Sense/Sense-Desktop/EnfySense/ViewModels/MainWindowViewModel.cs';
let content = fs.readFileSync(filePath, 'utf8');

// 1. Repair the Stats Fetch area
const statsStartMarker = "private async Task FetchTodayStatsAsync()";
const statsEndMarker = "private async Task FetchActivityHistoryAsync()";

const statsStartIdx = content.indexOf(statsStartMarker);
const statsEndIdx = content.indexOf(statsEndMarker, statsStartIdx);

const fixedStats = `private async Task FetchTodayStatsAsync()
    {
        if (string.IsNullOrEmpty(DeviceId) || !IsConnected || string.IsNullOrEmpty(BackendUrl) || _hasInitialStatsSync)
        {
            return;
        }

        try
        {
            string url = \`\${BackendUrl}/Employee_Monitor/devices/\${DeviceId}/stats/today?userName=\${_authSession?.User.Email}\`;
            AppLogger.Log(\`FetchTodayStatsAsync: fetching from \${url}\`, LogLevel.Debug);
            
            var stats = await _authApiClient.GetAsync<TodayStatsResponse>(
                url,
                _authSession?.AccessToken);

            if (stats != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _workTodayAccumulated = TimeSpan.FromSeconds(stats.WorkTimeSeconds);
                    _overtimeTodayAccumulated = TimeSpan.FromSeconds(stats.OvertimeSeconds);
                    _breakTodayAccumulated = TimeSpan.FromSeconds(stats.BreakTimeSeconds);
                    _hasInitialStatsSync = true;
                    
                    AppLogger.Log(\`FetchTodayStatsAsync: Success. Work=\${_workTodayAccumulated}, Overtime=\${_overtimeTodayAccumulated}, Break=\${_breakTodayAccumulated}\`, LogLevel.Info);
                    
                    StatusMessage = "Today's stats synchronized.";
                    IsDataInSync = true;
                    LastSyncDisplay = DateTime.Now.ToString("MMM dd, yyyy HH:mm:ss");
                    UpdateDashboardTick(); 
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log(\`Failed to fetch today's stats: \${ex.Message}\`);
        }
    }

    public class TodayStatsResponse
    {
        public int WorkTimeSeconds { get; set; }
        public int OvertimeSeconds { get; set; }
        public int BreakTimeSeconds { get; set; }
    }

    `;

if (statsStartIdx !== -1 && statsEndIdx !== -1) {
    content = content.substring(0, statsStartIdx) + fixedStats + content.substring(statsEndIdx);
}

// 2. Repair the Login/Connect mess
// We find the first [RelayCommand] before the messy ConnectAsync
const messStartMarker = "[RelayCommand]\\n    private async Task LoginAsync()";
// Actually let's find the first DisconnectAsync to find the end of the mess
const messEndMarker = "private async Task DisconnectAsync()";

const messStartIdx = content.indexOf("[RelayCommand]\\n    private async Task LoginAsync()");
const messEndIdx = content.indexOf(messEndMarker);

const fixedLoginConnect = `[RelayCommand]
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
            AppLogger.Log(\`Microsoft sign-in failed: \${ex}\`);
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
            AppLogger.Log(\`Connect failed: \${ex}\`);
            StatusMessage = \`Connect failed: \${GetFriendlyErrorMessage(ex)}\`;
            IsConnected = false;
        }
    }

    `;

// Since there are multiple LoginAsync/ConnectAsync in the messy file, we find the FIRST one and the LAST mess end.
const firstLogin = content.indexOf("[RelayCommand]\\r\\n    private async Task LoginAsync()");
const lastDisconnect = content.indexOf("private async Task DisconnectAsync()");

if (firstLogin !== -1 && lastDisconnect !== -1) {
    content = content.substring(0, firstLogin) + fixedLoginConnect + content.substring(lastDisconnect);
    fs.writeFileSync(filePath, content, 'utf8');
    console.log("Final Repair successful!");
} else {
    // Try without \\r
    const firstLogin2 = content.indexOf("[RelayCommand]\\n    private async Task LoginAsync()");
    if (firstLogin2 !== -1 && lastDisconnect !== -1) {
        content = content.substring(0, firstLogin2) + fixedLoginConnect + content.substring(lastDisconnect);
        fs.writeFileSync(filePath, content, 'utf8');
        console.log("Final Repair successful (\\n version)!");
    } else {
        console.error("Markers not found for Login/Connect!", { firstLogin, firstLogin2, lastDisconnect });
        process.exit(1);
    }
}
