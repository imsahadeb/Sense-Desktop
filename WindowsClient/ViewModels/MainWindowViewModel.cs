using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnfyLiveScreenClient.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace EnfyLiveScreenClient.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly AuthApiClient _authApiClient = new();
    private LiveStreamAgent? _agent;
    private BackgroundCaptureService? _backgroundService;
    private ActivityMonitoringService? _activityService;
    private AuthSession? _authSession;
    private DispatcherTimer? _uiTimer;
    private DateTime? _trackingStartedAt;
    private TimeSpan _workTodayAccumulated = TimeSpan.Zero;
    private TimeSpan _breakTodayAccumulated = TimeSpan.Zero;
    private bool _hasInitialStatsSync = false;

    [ObservableProperty]
    private string _backendUrl = string.Empty;

    [ObservableProperty]
    private string _ssoRedirectUri = string.Empty;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _deviceId = "Unknown";

    [ObservableProperty]
    private string _currentUser = "Not signed in";

    [ObservableProperty]
    private string _authStatus = "Signed out. Microsoft account required.";

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private string _streamStatus = "Idle";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _autoConnect;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isMaintenanceActive;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _workTodayDisplay = "0h 0m 0s";

    [ObservableProperty]
    private string _breakTodayDisplay = "0h 0m 0s";

    [ObservableProperty]
    private string _orgTimeDisplay = "--:-- --";

    [ObservableProperty]
    private string _timezoneDisplay = "Asia/Calcutta";

    [ObservableProperty]
    private string _orgNameDisplay = "enfy";

    [ObservableProperty]
    private string _lastSyncDisplay = "Never synced";

    [ObservableProperty]
    private bool _isDataInSync = false;

    [ObservableProperty]
    private bool _isTrackingActive = false;

    [ObservableProperty]
    private bool _isPaused = false;

    public bool IsTrackingStopped => !IsTrackingActive;
    public bool IsNotPaused => !IsPaused;
    public bool CanShowPause => IsTrackingActive && !IsPaused;
    public bool CanShowStart => !IsTrackingActive || IsPaused;

    [ObservableProperty]
    private string _trackingStatusText = "Tracking Stopped";

    [ObservableProperty]
    private string _trackingStatusDetail = "Work time is not being tracked and no data is being collected.";

    public ObservableCollection<ActivityBarItem> DailyActivityItems { get; } = new();

    public string LogFilePath => AppLogger.CurrentLogFilePath;
    public bool CanLogin => !IsAuthenticated && !IsBusy;
    public bool CanLogout => IsAuthenticated && !IsBusy;
    public bool CanConnect => IsAuthenticated && !IsConnected && !IsBusy;
    public bool CanDisconnect => IsConnected && !IsBusy;

    public MainWindowViewModel()
    {
        _config = AppConfig.Load();
        BackendUrl = "http://192.168.1.54:3000";
        SsoRedirectUri = "http://localhost:3001/callback";
        DeviceName = string.IsNullOrWhiteSpace(_config.DeviceNameOverride)
            ? Environment.MachineName
            : _config.DeviceNameOverride;
        AutoConnect = _config.AutoConnect;

        if (AutoConnect)
        {
            StatusMessage = "Auto-connect is enabled. Sign in with Microsoft to start the agent.";
        }

        SeedMockActivityData();
        InitializeUiTimer();
    }

    private void SeedMockActivityData()
    {
        DailyActivityItems.Clear();
        var random = new Random();
        for (int i = 0; i < 30; i++)
        {
            var date = DateTime.Today.AddDays(-29 + i);
            double hours = 0;
            if (i > 25) hours = random.NextDouble() * 8 + 2; 
            
            DailyActivityItems.Add(new ActivityBarItem
            {
                Day = date.Day.ToString(),
                Height = (int)(hours * 15),
                Tooltip = $"{date:MMM dd}: {hours:F1} hrs"
            });
        }
    }

    public class ActivityBarItem
    {
        public string Day { get; set; } = "";
        public int Height { get; set; }
        public string Tooltip { get; set; } = "";
    }

    private void InitializeUiTimer()
    {
        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _uiTimer.Tick += (s, e) => UpdateDashboardTick();
        _uiTimer.Start();
    }

    private void UpdateDashboardTick()
    {
        OrgTimeDisplay = DateTime.Now.ToString("hh:mm tt");
        
        if (IsTrackingActive && _trackingStartedAt.HasValue)
        {
            var currentSession = DateTime.UtcNow - _trackingStartedAt.Value;
            if (IsPaused)
            {
                var totalBreak = _breakTodayAccumulated + currentSession;
                BreakTodayDisplay = FormatTimeSpan(totalBreak);
                WorkTodayDisplay = FormatTimeSpan(_workTodayAccumulated);
            }
            else
            {
                var totalWork = _workTodayAccumulated + currentSession;
                WorkTodayDisplay = FormatTimeSpan(totalWork);
                BreakTodayDisplay = FormatTimeSpan(_breakTodayAccumulated);
            }
        }
        else
        {
            WorkTodayDisplay = FormatTimeSpan(_workTodayAccumulated);
            BreakTodayDisplay = FormatTimeSpan(_breakTodayAccumulated);
        }
    }

    private string FormatTimeSpan(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

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

        if (IsConnected)
        {
            return;
        }

        try
        {
            SaveSettings();
            StatusMessage = "Connecting to tracker gateway...";

            _agent = new LiveStreamAgent(BackendUrl, DeviceName, _authSession?.User.Email ?? "");
            _agent.ConnectionStatusChanged += HandleConnectionStatusChanged;
            _agent.StreamStatusChanged += HandleStreamStatusChanged;

            DeviceId = _agent.DeviceId;
            DeviceName = _agent.DeviceName;

            await _agent.StartAsync();
            
             _backgroundService = new BackgroundCaptureService(_agent);
            _backgroundService.Start();
            
            _activityService = new ActivityMonitoringService(_agent);

            PolicyManager.Instance.PolicyUpdated += HandlePolicyUpdated;

            IsConnected = true;
            IsDataInSync = true;
            LastSyncDisplay = DateTime.Now.ToString("MMM dd, yyyy HH:mm:ss");
            StatusMessage = "Connected. Monitoring active.";

            if (IsTrackingActive)
            {
                _ = _agent.ReportWorkStatusAsync(IsPaused ? "BREAK" : "WORKING");
            }
            else
            {
                _ = _agent.ReportWorkStatusAsync("STOPPED");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Connect failed: {ex}");
            StatusMessage = $"Connect failed: {GetFriendlyErrorMessage(ex)}";
            IsConnected = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_agent != null)
        {
            await _agent.StopAsync();
            _agent.ConnectionStatusChanged -= HandleConnectionStatusChanged;
            _agent.StreamStatusChanged -= HandleStreamStatusChanged;
            _agent = null;
            if (_backgroundService != null)
            {
                _backgroundService.Stop();
                _backgroundService.Dispose();
                _backgroundService = null;
                PolicyManager.Instance.PolicyUpdated -= HandlePolicyUpdated;
            }

            if (_activityService != null)
            {
                _activityService.Dispose();
                _activityService = null;
            }
        }

        IsConnected = false;
        IsTrackingActive = false;
        TrackingStatusText = "Tracking Stopped";
        TrackingStatusDetail = "Work time is not being tracked and no data is being collected.";
        ConnectionStatus = "Disconnected";
        StreamStatus = "Idle";
        StatusMessage = "Disconnected.";
        _hasInitialStatsSync = false;
    }

    [RelayCommand]
    private Task StartTrackingAsync()
    {
        if (IsTrackingActive && !IsPaused) return Task.CompletedTask;
        
        if (IsPaused)
        {
            // Resuming from pause
            if (_trackingStartedAt.HasValue)
            {
                _breakTodayAccumulated += DateTime.UtcNow - _trackingStartedAt.Value;
            }
            IsPaused = false;
        }
        else
        {
            // Initial start
            IsTrackingActive = true;
        }

        _trackingStartedAt = DateTime.UtcNow;
        TrackingStatusText = "Tracking Active";
        TrackingStatusDetail = "Your work time is currently being recorded.";
        StatusMessage = "Work tracking started.";

        if (_agent != null)
        {
            _ = _agent.ReportWorkStatusAsync("WORKING");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task PauseTrackingAsync()
    {
        if (!IsTrackingActive || IsPaused) return Task.CompletedTask;

        if (_trackingStartedAt.HasValue)
        {
            _workTodayAccumulated += DateTime.UtcNow - _trackingStartedAt.Value;
        }

        IsPaused = true;
        _trackingStartedAt = DateTime.UtcNow; // Now tracking break time
        TrackingStatusText = "On a Break";
        TrackingStatusDetail = "You are currently on a break. Cumulative break time is being recorded.";
        StatusMessage = "Break mode active.";

        if (_agent != null)
        {
            _ = _agent.ReportWorkStatusAsync("BREAK");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task StopTrackingAsync()
    {
        if (!IsTrackingActive) return Task.CompletedTask;

        if (_trackingStartedAt.HasValue)
        {
            if (IsPaused)
                _breakTodayAccumulated += DateTime.UtcNow - _trackingStartedAt.Value;
            else
                _workTodayAccumulated += DateTime.UtcNow - _trackingStartedAt.Value;
        }

        IsTrackingActive = false;
        IsPaused = false;
        _trackingStartedAt = null;
        TrackingStatusText = "Tracking Stopped";
        TrackingStatusDetail = "Work time is not being tracked and no data is being collected.";
        StatusMessage = "Work tracking stopped.";

        if (_agent != null)
        {
            _ = _agent.ReportWorkStatusAsync("STOPPED");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Signing out...";

            if (IsConnected)
            {
                await DisconnectAsync();
            }

            if (_authSession != null)
            {
                await _authApiClient.LogoutAsync(BackendUrl, _authSession.RefreshToken);
            }

            ClearAuthState();
            StatusMessage = "Signed out.";
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Logout warning: {ex}");
            ClearAuthState();
            StatusMessage = $"Signed out locally. Backend logout warning: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _config.BackendUrl = BackendUrl.Trim();
        _config.SsoRedirectUri = SsoRedirectUri.Trim();
        _config.DeviceNameOverride = DeviceName.Trim();
        _config.AutoConnect = AutoConnect;
        _config.Save();
        StatusMessage = $"Settings saved to {AppConfig.ConfigPath}";
    }

    private void ClearAuthState()
    {
        _authSession = null;
        IsAuthenticated = false;
        CurrentUser = "Not signed in";
        AuthStatus = "Signed out. Microsoft account required.";
    }

    private string GetFriendlyErrorMessage(Exception ex)
    {
        var message = ex.Message;
        if (!string.IsNullOrWhiteSpace(BackendUrl) &&
            BackendUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
        {
            return "Backend connection was refused. If the Windows client is running on a different machine, replace localhost with your backend server IP, for example http://192.168.1.54:3000.";
        }

        return message;
    }

    private void HandleConnectionStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ConnectionStatus = status;
            if (!status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
            {
                IsConnected = false;
            }
        });
    }

    private void HandleStreamStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StreamStatus = status;
            StatusMessage = status;
        });
    }

    private void HandlePolicyUpdated(TrackedPolicy policy)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsMaintenanceActive = policy.MaintenanceMode;
            if (policy.MaintenanceMode)
            {
                StatusMessage = "Device is in Maintenance Mode. Tracking is suspended and usage is restricted.";
            }

            if (policy.TodayStats != null && !_hasInitialStatsSync)
            {
                _workTodayAccumulated = TimeSpan.FromSeconds(policy.TodayStats.WorkTimeSeconds);
                _breakTodayAccumulated = TimeSpan.FromSeconds(policy.TodayStats.BreakTimeSeconds);
                _hasInitialStatsSync = true;
                
                // If tracking is active, reset the session start time to avoid double counting
                if (IsTrackingActive)
                {
                    _trackingStartedAt = DateTime.UtcNow;
                }
                
                AppLogger.Log($"Initial stats sync: Work={_workTodayAccumulated}, Break={_breakTodayAccumulated}", LogLevel.Info);
                IsDataInSync = true;
                LastSyncDisplay = DateTime.Now.ToString("MMM dd, yyyy HH:mm:ss");
                UpdateDashboardTick();
            }
        });
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
    }

    partial void OnIsAuthenticatedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLogin));
        OnPropertyChanged(nameof(CanLogout));
        OnPropertyChanged(nameof(CanConnect));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLogin));
        OnPropertyChanged(nameof(CanLogout));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
    }

    partial void OnIsTrackingActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTrackingStopped));
        OnPropertyChanged(nameof(CanShowPause));
        OnPropertyChanged(nameof(CanShowStart));
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotPaused));
        OnPropertyChanged(nameof(CanShowPause));
        OnPropertyChanged(nameof(CanShowStart));
    }
}

public class StatusColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isActive && isActive)
            return Avalonia.Media.Brush.Parse("#10B981"); // Green
        return Avalonia.Media.Brush.Parse("#64748B"); // Gray
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PauseToResumeConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isPaused && isPaused)
            return "Resume";
        return "Start";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StatusPulseConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isActive && isActive)
            return 1.0;
        return 0.4;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DarkenColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Avalonia.Media.ISolidColorBrush brush)
        {
            var color = brush.Color;
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(
                color.A,
                (byte)(color.R * 0.8),
                (byte)(color.G * 0.8),
                (byte)(color.B * 0.8)));
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
