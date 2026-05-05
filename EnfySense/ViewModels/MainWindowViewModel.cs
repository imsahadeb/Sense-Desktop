using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnfyLiveScreenClient.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

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
    private TimeSpan _overtimeTodayAccumulated = TimeSpan.Zero;
    private TimeSpan _breakTodayAccumulated = TimeSpan.Zero;
    private bool _hasInitialStatsSync = false;
    private int _heartbeatTickCounter = 0;
    private readonly UpdateService _updateService = new();
    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _adminLockTimer;
    private int _adminAutoLockSecondsRemaining = 0;
    private const int DefaultAdminAutoLockSeconds = 60; // 1 minutes

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _releaseNotes;

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
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanDisconnect))]
    private bool _isConnected;

    [ObservableProperty]
    private bool _autoConnect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLogin))]
    [NotifyPropertyChangedFor(nameof(CanLogout))]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isMaintenanceActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLogin))]
    [NotifyPropertyChangedFor(nameof(CanLogout))]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanDisconnect))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayActive))]
    private bool _isApplyingUpdate;

    [ObservableProperty]
    private bool _isOverlayVisible;

    [ObservableProperty]
    private string _workTodayDisplay = "0h 0m 0s";

    [ObservableProperty]
    private string _overtimeTodayDisplay = "0h 0m 0s";

    [ObservableProperty]
    private string _breakTodayDisplay = "0h 0m 0s";

    [ObservableProperty]
    private string _widgetTimeDisplay = "0h 0m 0s";

    [ObservableProperty]
    private string _widgetStatusColor = "#10B981";

    [ObservableProperty]
    private string _timeRemainingToClose = "";

    [ObservableProperty]
    private string _orgTimeDisplay = "--:-- --";

    [ObservableProperty]
    private string _timezoneDisplay = "America/New_York";

    [ObservableProperty]
    private string _workThisWeekDisplay = "0:00:00";

    [ObservableProperty]
    private string _workThisMonthDisplay = "0:00:00";

    [ObservableProperty]
    private string _tasksCount = "0";

    [ObservableProperty]
    private string _breaksCount = "0m";

    [ObservableProperty]
    private string _activityLevel = "0%";

    [ObservableProperty]
    private string _last7DaysDisplay = "0h 0m";

    [ObservableProperty]
    private string _last7DaysBreakDisplay = "0h 0m";

    [ObservableProperty]
    private string _last30DaysDisplay = "0h 0m";

    [ObservableProperty]
    private string _last30DaysBreakDisplay = "0h 0m";

    [ObservableProperty]
    private string _avgPerDayDisplay = "0h 0m";

    [ObservableProperty]
    private string _avgPerDayBreakDisplay = "0h 0m";

    [ObservableProperty]
    private string _todayHoursDisplay = "0h 0m";

    [ObservableProperty]
    private string _todaySummaryBreakDisplay = "0h 0m";

    [ObservableProperty]
    private string _clockInDisplay = "--:--";

    private string _clockOutDisplay = "--:--";
    public string ClockOutDisplay
    {
        get => IsTrackingActive ? "--:--" : _clockOutDisplay;
        set => SetProperty(ref _clockOutDisplay, value);
    }

    [ObservableProperty]
    private string _statusText = "IDLE";

    [ObservableProperty]
    private string _statusColor = "#94A3B8";

    [ObservableProperty]
    private string _orgNameDisplay = "enfycon Inc";

    [ObservableProperty]
    private bool _isWidgetActive = false;

    [RelayCommand]
    public void ExpandToFullView()
    {
        IsWidgetActive = false;
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null) { desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal; desktop.MainWindow.Show(); }
            desktop.MainWindow?.Activate();
        }
    }

    [RelayCommand]
    public void ShowWidget()
    {
        IsWidgetActive = true;
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Hide();
        }
    }

    [RelayCommand]
    private void UnlockAdmin()
    {
        _ = UnlockAdminAsync();
    }

    [RelayCommand]
    private async Task AcceptTerms()
    {
        if (_authSession == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Recording your acceptance...";
            var success = await _authApiClient.AcceptTermsAsync(BackendUrl, _authSession.AccessToken);
            if (success)
            {
                IsTermsVisible = false;
                _config.TermsAccepted = true;
                _config.Save();
                
                StatusMessage = "Acceptance recorded. Welcome to EnfySense.";
                
                if (AutoConnect)
                {
                    await ConnectAsync();
                }
            }
            else
            {
                StatusMessage = "Failed to record terms acceptance. Please try again.";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"AcceptTerms error: {ex.Message}");
            StatusMessage = $"Error recording acceptance: {GetFriendlyErrorMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ObservableProperty]
    private string _lastSyncDisplay = "Never";

    [ObservableProperty]
    private bool _isDataInSync = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTrackingStopped))]
    [NotifyPropertyChangedFor(nameof(CanShowPause))]
    [NotifyPropertyChangedFor(nameof(CanShowStart))]
    private bool _isTrackingActive = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotPaused))]
    [NotifyPropertyChangedFor(nameof(CanShowPause))]
    [NotifyPropertyChangedFor(nameof(CanShowStart))]
    private bool _isPaused = false;

    [ObservableProperty]
    private bool _isAdminMode = false;
    
    [ObservableProperty]
    private string _adminRemainingDisplay = "30:00";

    [ObservableProperty]
    private string _appVersion = "1.0.15";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayActive))]
    private bool _showFinishConfirmation = false;

    [ObservableProperty]
    private bool _isTermsVisible;

    public bool IsOverlayActive => ShowFinishConfirmation || IsApplyingUpdate || IsTermsVisible;

    public bool IsTrackingStopped => !IsTrackingActive;
    public bool IsNotPaused => !IsPaused;
    public bool CanShowPause => IsTrackingActive && !IsPaused;
    public bool CanShowStart => !IsTrackingActive || IsPaused;

    [ObservableProperty]
    private string _trackingStatusText = "Tracking Stopped";

    [ObservableProperty]
    private string _trackingStatusDetail = "Work time is not being tracked and no data is being collected.";

    public ObservableCollection<ActivityBarItem> DailyActivityItems { get; } = new();
    public ObservableCollection<ActivityBarItem> HourlyActivityItems { get; } = new();
    public ObservableCollection<string> DailyYAxisLabels { get; } = new();
    public ObservableCollection<string> HourlyYAxisLabels { get; } = new();

    public string LogFilePath => AppLogger.CurrentLogFilePath;
    public bool CanLogin => !IsAuthenticated && !IsBusy;
    public bool CanLogout => IsAuthenticated && !IsBusy;
    public bool CanConnect => IsAuthenticated && !IsConnected && !IsBusy && !IsTermsVisible;
    public bool CanDisconnect => IsConnected && !IsBusy;

    public MainWindowViewModel()
    {
        _config = AppConfig.Load();
        BackendUrl = _config.EffectiveBackendUrl;
        SsoRedirectUri = _config.EffectiveSsoRedirectUri;
        DeviceName = string.IsNullOrWhiteSpace(_config.DeviceNameOverride)
            ? Environment.MachineName
            : _config.DeviceNameOverride;
        
        DeviceId = new LiveStreamAgent(BackendUrl, DeviceName).DeviceId;
        AutoConnect = _config.AutoConnect;

        if (AutoConnect)
        {
            StatusMessage = "Auto-connect is enabled. Sign in with Microsoft to start the agent.";
        }

        if (IsConnected)
        {
            _ = FetchActivityHistoryAsync();
        }

        InitializeUiTimer();
        InitializeUpdateService();

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = version?.ToString(3) ?? "1.0.0";

        _ = TryAutoLoginAsync();
    }

    private void InitializeUpdateService()
    {
        _updateService.UpdateDownloaded += () => Dispatcher.UIThread.Post(() => {
            ReleaseNotes = _updateService.ReleaseNotes;
            IsUpdateAvailable = true;
        });
        
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            await _updateService.CheckForUpdatesAsync();
        });

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(4)
        };
        _updateTimer.Tick += async (s, e) => await _updateService.CheckForUpdatesAsync();
        _updateTimer.Start();
    }

    private async Task FetchTodayStatsAsync()
    {
        if (string.IsNullOrEmpty(DeviceId) || !IsConnected || string.IsNullOrEmpty(BackendUrl) || _hasInitialStatsSync)
        {
            return;
        }

        try
        {
            string url = $"{BackendUrl}/sense/devices/{DeviceId}/stats/today?userName={_authSession?.User.Email}";
            AppLogger.Log($"FetchTodayStatsAsync: fetching from {url}", LogLevel.Debug);
            
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
                    
                    AppLogger.Log($"FetchTodayStatsAsync: Success. Work={_workTodayAccumulated}, Overtime={_overtimeTodayAccumulated}, Break={_breakTodayAccumulated}", LogLevel.Info);
                    
                    StatusMessage = "Today's stats synchronized.";
                    IsDataInSync = true;
                    LastSyncDisplay = GetEasternTimeNow().ToString("MMM dd, yyyy hh:mm:ss tt");
                    UpdateDashboardTick(); 
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to fetch today's stats: {ex.Message}");
        }
    }

    public class TodayStatsResponse
    {
        public int WorkTimeSeconds { get; set; }
        public int OvertimeSeconds { get; set; }
        public int BreakTimeSeconds { get; set; }
    }

    private async Task FetchActivityHistoryAsync()
    {
        if (string.IsNullOrEmpty(DeviceId) || !IsConnected || string.IsNullOrEmpty(BackendUrl))
        {
            return;
        }

        try
        {
            string url = $"{BackendUrl}/sense/devices/{DeviceId}/stats/dashboard";
            var dashboard = await _authApiClient.GetAsync<DashboardStatsResponse>(
                url,
                _authSession?.AccessToken);

            if (dashboard != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    // 1. Update Stats Labels
                    WorkThisWeekDisplay = FormatSimpleTime(dashboard.Week.WorkTimeSeconds + dashboard.Week.OvertimeSeconds);
                    WorkThisMonthDisplay = FormatSimpleTime(dashboard.Month.WorkTimeSeconds + dashboard.Month.OvertimeSeconds);
                    ActivityLevel = $"{dashboard.ActivityLevel}%";
                    BreaksCount = FormatSimpleTime(dashboard.Today.BreakTimeSeconds);
                    Last7DaysDisplay = dashboard.Last7DaysWorkHours;
                    Last7DaysBreakDisplay = dashboard.Last7DaysBreakHours;
                    Last30DaysDisplay = dashboard.Last30DaysWorkHours;
                    Last30DaysBreakDisplay = dashboard.Last30DaysBreakHours;
                    AvgPerDayDisplay = dashboard.AvgPerDayWorkHours;
                    AvgPerDayBreakDisplay = dashboard.AvgPerDayBreakHours;
                    TodayHoursDisplay = dashboard.TodayWorkHours;
                    TodaySummaryBreakDisplay = dashboard.TodayBreakHours;
                    ClockInDisplay = ReformatTimeWithSeconds(dashboard.ClockIn);
                    _clockOutDisplay = ReformatTimeWithSeconds(dashboard.ClockOut);
                    OnPropertyChanged(nameof(ClockOutDisplay));
                    TimezoneDisplay = dashboard.Timezone;
                    OrgNameDisplay = dashboard.OrgName;

                    // 2. Update Daily Chart (Last 30 Days)
                    DailyActivityItems.Clear();
                    DailyYAxisLabels.Clear();
                    // Stabilize scale: Use 8h as baseline, or the max if it exceeds 8h
                    double dailyMax = dashboard.DailyData.Any() ? dashboard.DailyData.Max(h => h.WorkTimeSeconds + h.OvertimeSeconds + h.BreakTimeSeconds) : 0;
                    if (dailyMax < 8 * 3600) dailyMax = 8 * 3600; 
                    // Round up to nearest 2 hours for stable labels
                    dailyMax = Math.Ceiling(dailyMax / 7200.0) * 7200.0;
                    double dailyScale = 140.0 / dailyMax;

                    // Populate Daily Y Labels
                    for (int i = 5; i >= 0; i--) {
                        DailyYAxisLabels.Add(FormatSimpleTime((dailyMax / 5) * i));
                    }

                    foreach (var item in dashboard.DailyData)
                    {
                        // Show labels every 3 days to match screenshot
                        DateTime date = DateTime.TryParse(item.Date, out var dt) ? dt : DateTime.Today;
                        string label = (dashboard.DailyData.IndexOf(item) % 3 == 0 || dashboard.DailyData.IndexOf(item) == dashboard.DailyData.Count - 1) 
                                       ? date.ToString("MMM d") : "";
                        
                        DailyActivityItems.Add(new ActivityBarItem
                        {
                            Day = label,
                            WorkHeight = item.WorkTimeSeconds * dailyScale,
                            OvertimeHeight = item.OvertimeSeconds * dailyScale,
                            BreakHeight = item.BreakTimeSeconds * dailyScale,
                            Tooltip = $"{item.Date}\nWork: {FormatSimpleTime(item.WorkTimeSeconds)}\nBreak: {FormatSimpleTime(item.BreakTimeSeconds)}"
                        });
                    }

                    // 3. Update Hourly Chart (Today)
                    HourlyActivityItems.Clear();
                    HourlyYAxisLabels.Clear();
                    // Stabilize scale: Hourly buckets can never exceed 3600s
                    double hourlyMax = 3600; 
                    double hourlyScale = 140.0 / hourlyMax;

                    // Populate Hourly Y Labels
                    for (int i = 5; i >= 0; i--) {
                        HourlyYAxisLabels.Add(FormatSimpleTime((hourlyMax / 5) * i));
                    }

                    foreach (var item in dashboard.HourlyData)
                    {
                        string label = "";
                        if (item.Hour % 2 == 0) {
                            label = item.Hour == 0 ? "12 AM" : item.Hour == 12 ? "12 PM" : (item.Hour > 12 ? (item.Hour - 12) + " PM" : item.Hour + " AM");
                        }
                        
                        HourlyActivityItems.Add(new ActivityBarItem
                        {
                            Day = label,
                            WorkHeight = item.WorkTimeSeconds * hourlyScale,
                            OvertimeHeight = item.OvertimeSeconds * hourlyScale,
                            BreakHeight = item.BreakTimeSeconds * hourlyScale,
                            Tooltip = $"{item.Hour}:00 - {item.Hour}:59\nWork: {FormatSimpleTime(item.WorkTimeSeconds)}\nBreak: {FormatSimpleTime(item.BreakTimeSeconds)}"
                        });
                    }

                    StatusMessage = "Dashboard synchronized.";
                    LastSyncDisplay = GetEasternTimeNow().ToString("hh:mm:ss tt");
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to fetch dashboard stats: {ex.Message}");
        }
    }

    private string FormatSimpleTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours}h {ts.Minutes}m";
    }

    public class ActivityBarItem
    {
        public string Day { get; set; } = "";
        public double WorkHeight { get; set; }
        public double OvertimeHeight { get; set; }
        public double BreakHeight { get; set; }
        public string Tooltip { get; set; } = "";
    }

    public class DashboardStatsResponse
    {
        public TodayStatsResponse Today { get; set; } = new();
        public TodayStatsResponse Week { get; set; } = new();
        public TodayStatsResponse Month { get; set; } = new();
        public int ActivityLevel { get; set; }
        public string Last7DaysWorkHours { get; set; } = "0h 0m";
        public string Last7DaysBreakHours { get; set; } = "0h 0m";
        public string Last30DaysWorkHours { get; set; } = "0h 0m";
        public string Last30DaysBreakHours { get; set; } = "0h 0m";
        public string AvgPerDayWorkHours { get; set; } = "0h 0m";
        public string AvgPerDayBreakHours { get; set; } = "0h 0m";
        public string TodayWorkHours { get; set; } = "0h 0m";
        public string TodayBreakHours { get; set; } = "0h 0m";
        public string ClockIn { get; set; } = "--:--";
        public string ClockOut { get; set; } = "--:--";
        public List<HourStats> HourlyData { get; set; } = new();
        public List<ActivityHistoryResponse> DailyData { get; set; } = new();
        public string Timezone { get; set; } = "Asia/Calcutta";
        public string OrgName { get; set; } = "enfycon";
    }

    public class HourStats
    {
        public int Hour { get; set; }
        public double WorkTimeSeconds { get; set; }
        public double OvertimeSeconds { get; set; }
        public double BreakTimeSeconds { get; set; }
    }

    public class ActivityHistoryResponse
    {
        public string Date { get; set; } = "";
        public int Day { get; set; }
        public double WorkTimeSeconds { get; set; }
        public double OvertimeSeconds { get; set; }
        public double BreakTimeSeconds { get; set; }
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

    private DateTime _lastCheckedDate = DateTime.Today;

    private void UpdateDashboardTick()
    {
        var localZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localZone);

        if (localTime.Date != _lastCheckedDate)
        {
            AppLogger.Log($"Day changed from {_lastCheckedDate:yyyy-MM-dd} to {localTime.Date:yyyy-MM-dd} (US/Eastern). Resetting daily counters.", LogLevel.Info);
            _lastCheckedDate = localTime.Date;
            _workTodayAccumulated = TimeSpan.Zero;
            _overtimeTodayAccumulated = TimeSpan.Zero;
            _breakTodayAccumulated = TimeSpan.Zero;
            if (IsTrackingActive)
            {
                _trackingStartedAt = DateTime.UtcNow;
            }
        }

        OrgTimeDisplay = localTime.ToString("hh:mm:ss tt");
        
        TimeSpan totalWork = _workTodayAccumulated;
        TimeSpan totalOvertime = _overtimeTodayAccumulated;
        TimeSpan totalBreak = _breakTodayAccumulated;
        int currentUtcHour = DateTime.UtcNow.Hour;

        TimeSpan dayGoal = TimeSpan.FromHours(8);

        if (IsTrackingActive && _trackingStartedAt.HasValue)
        {
            var currentSession = DateTime.UtcNow - _trackingStartedAt.Value;
            if (IsPaused) totalBreak += currentSession;
            else 
            {
                // Calculate 8h split live
                TimeSpan currentTotalWork = _workTodayAccumulated + _overtimeTodayAccumulated + currentSession;
                
                if (currentTotalWork > dayGoal)
                {
                    totalWork = dayGoal;
                    totalOvertime = currentTotalWork - dayGoal;
                }
                else
                {
                    totalWork = currentTotalWork;
                    totalOvertime = TimeSpan.Zero;
                }
            }
        }
        else
        {
            // Calculate 8h split from accumulated
            TimeSpan currentTotalWork = _workTodayAccumulated + _overtimeTodayAccumulated;
            if (currentTotalWork > dayGoal)
            {
                totalWork = dayGoal;
                totalOvertime = currentTotalWork - dayGoal;
            }
            else
            {
                totalWork = currentTotalWork;
                totalOvertime = TimeSpan.Zero;
            }
        }
        
        WorkTodayDisplay = FormatTimeSpan(totalWork + totalOvertime);
        OvertimeTodayDisplay = FormatTimeSpan(totalOvertime);
        BreakTodayDisplay = FormatTimeSpan(totalBreak);
        TodayHoursDisplay = FormatTimeSpanShort(totalWork + totalOvertime);
        TodaySummaryBreakDisplay = FormatTimeSpanShort(totalBreak);

        // Update Status Badge
        if (!IsTrackingActive)
        {
            StatusText = "IDLE";
            StatusColor = "#94A3B8";
        }
        else if (IsPaused)
        {
            StatusText = "ON BREAK";
            StatusColor = "#F59E0B";
        }
        else
        {
            StatusText = "WORKING";
            StatusColor = "#10B981";
        }

        // Update Widget display (8-Hour Rule)
        if (totalWork <= dayGoal)
        {
            WidgetTimeDisplay = totalWork.ToString(@"hh\:mm\:ss");
            WidgetStatusColor = IsPaused ? "#F59E0B" : "#10B981"; // Yellow if break, Green if work
        }
        else
        {
            // If we are over 8 hours, show total overtime in the widget
            WidgetTimeDisplay = totalOvertime.ToString(@"hh\:mm\:ss");
            WidgetStatusColor = IsPaused ? "#F59E0B" : "#EF4444"; // Yellow if break, Red if overtime
        }

        // Update Time Remaining (Countdown to 8h Goal)
        if (totalWork < dayGoal)
        {
            var remainingToGoal = dayGoal - totalWork;
            TimeRemainingToClose = $"{remainingToGoal.Hours}h {remainingToGoal.Minutes}m until 8h goal";
        }
        else
        {
            TimeRemainingToClose = "Daily Goal Reached (Overtime)";
        }

        if (IsConnected && _agent != null)
        {
            _heartbeatTickCounter++;
            if (_heartbeatTickCounter >= 10)
            {
                _heartbeatTickCounter = 0;
                _ = _agent.SendHeartbeatAsync((int)totalWork.TotalSeconds, (int)totalOvertime.TotalSeconds, (int)totalBreak.TotalSeconds);
                _ = FetchActivityHistoryAsync();
            }
        }
    }

    private string FormatTimeSpanShort(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours}h {ts.Minutes}m";
    }

    private string FormatTimeSpan(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
    }

    [RelayCommand]
    private async Task ManualCheckForUpdates()
    {
        StatusMessage = "Checking for updates...";
        await _updateService.CheckForUpdatesAsync();
        if (!IsUpdateAvailable)
        {
            StatusMessage = "You are using the latest version.";
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _config.BackendUrl = BackendUrl;
        _config.SsoRedirectUri = SsoRedirectUri;
        _config.AutoConnect = AutoConnect;
        _config.DeviceNameOverride = DeviceName == Environment.MachineName ? "" : DeviceName;
        
        _config.Save();
        StatusMessage = "Settings saved successfully.";
    }

    [RelayCommand]
    private async Task Logout()
    {
        if (!IsAdminMode)
        {
            StatusMessage = "Admin access required to log out.";
            return;
        }

        try
        {
            await Disconnect();
            IsAuthenticated = false;
            IsAdminMode = false;
            ShowFinishConfirmation = false;
            IsMaintenanceActive = false;
            _hasInitialStatsSync = false;
            
            CurrentUser = "Not signed in";
            _authSession = null;
            _config.LastSession = null;
            _config.Save();
            AuthStatus = "Signed out. Microsoft account required.";
            StatusMessage = "You have been signed out.";
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Logout failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ExitApp()
    {
        if (!IsAdminMode)
        {
            StatusMessage = "Admin access required to exit the application.";
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand]
    private void LockAdmin()
    {
        IsAdminMode = false;
        _adminLockTimer?.Stop();
        StatusMessage = "Admin mode locked.";
    }

    [RelayCommand]
    private void ApplyUpdate()
    {
        IsApplyingUpdate = true;
        StatusMessage = "Applying update and restarting...";
        AppLogger.Log("User clicked ApplyUpdate. Setting IsApplyingUpdate = true and calling ApplyUpdatesAndRestart.", LogLevel.Info);
        
        // Give the UI a moment to show the overlay before restarting
        Dispatcher.UIThread.Post(async () => {
            await Task.Delay(500);
            _updateService.ApplyUpdatesAndRestart();
        }, DispatcherPriority.Normal);
    }

    [RelayCommand]
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
            _config.Save();

            _authSession = await _authApiClient.LoginWithMicrosoftAsync(
                BackendUrl,
                _config.KeycloakIssuer,
                _config.KeycloakClientId,
                SsoRedirectUri);

            IsAuthenticated = true;
            CurrentUser = _authSession.User.DisplayName;
            AuthStatus = "Signed in with Microsoft";
            StatusMessage = "Microsoft sign-in completed successfully.";

            // Check if user needs to accept terms
            if (!_authSession.User.TermsAccepted)
            {
                IsTermsVisible = true;
                StatusMessage = "Please review and accept the Company Usage Disclosure.";
            }

            if (_config.RememberMe)
            {
                _config.LastSession = _authSession;
                _config.TermsAccepted = _authSession.User.TermsAccepted;
                _config.Save();
            }

            _ = SyncAdminSecretsAsync();

            if (AutoConnect && !IsTermsVisible)
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

        if (IsTermsVisible)
        {
            StatusMessage = "Please accept the usage disclosure before connecting.";
            return;
        }

        if (IsConnected) return;

        try
        {
            _config.Save();
            StatusMessage = "Connecting to Sense gateway...";

            _agent = new LiveStreamAgent(BackendUrl, DeviceName, _authSession?.User.Email ?? "");
            _agent.ConnectionStatusChanged += HandleConnectionStatusChanged;
            _agent.StreamStatusChanged += HandleStreamStatusChanged;

            DeviceId = _agent.DeviceId;
            DeviceName = _agent.DeviceName;

            PolicyManager.Instance.PolicyUpdated += HandlePolicyUpdated;

            await _agent.StartAsync();
            
            _backgroundService = new BackgroundCaptureService(_agent);
            _backgroundService.Start();
            
            _activityService = new ActivityMonitoringService(_agent)
            {
                IsEnabled = IsTrackingActive && !IsPaused
            };

            _activityService.InactivityTimeout += () => Dispatcher.UIThread.Post(() => {
                if (IsTrackingActive && !IsPaused)
                {
                    AppLogger.Log("Inactivity timeout received in ViewModel. Triggering PauseTracking.");
                    PauseTracking();
                }
            });

            _activityService.InactivityResumed += () => Dispatcher.UIThread.Post(() => {
                if (IsTrackingActive && IsPaused)
                {
                    AppLogger.Log("Inactivity resumed received in ViewModel. Triggering PauseTracking to resume.");
                    PauseTracking();
                }
            });

            IsConnected = true;
            IsDataInSync = true;
            LastSyncDisplay = GetEasternTimeNow().ToString("MMM dd, yyyy hh:mm:ss tt");
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
            AppLogger.Log($"Connect failed: {ex}");
            StatusMessage = $"Connect failed: {GetFriendlyErrorMessage(ex)}";
            IsConnected = false;
        }
    }

    [RelayCommand]
    private async Task Disconnect()
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
            }
            PolicyManager.Instance.PolicyUpdated -= HandlePolicyUpdated;
            IsConnected = false;
            StatusMessage = "Disconnected from Sense gateway.";
        }
    }

    [RelayCommand]
    private void StartTracking()
    {
        if (IsTermsVisible) return;
        if (IsTrackingActive && _trackingStartedAt.HasValue)
        {
            var session = DateTime.UtcNow - _trackingStartedAt.Value;
            if (IsPaused)
            {
                _breakTodayAccumulated += session;
            }
            // If already tracking and not paused, we don't need to add anything, 
            // but the UI tick handles live accumulation anyway.
        }

        IsTrackingActive = true;
        IsPaused = false;
        _trackingStartedAt = DateTime.UtcNow;
        TrackingStatusText = "Working";
        TrackingStatusDetail = "Work time is being tracked and desktop captures are active.";
        
        if (_activityService != null)
        {
            _activityService.IsEnabled = true;
        }

        if (_agent != null)
        {
            _ = _agent.ReportWorkStatusAsync("WORKING", 
                (int)_workTodayAccumulated.TotalSeconds,
                (int)_overtimeTodayAccumulated.TotalSeconds,
                (int)_breakTodayAccumulated.TotalSeconds);
        }
    }

    [RelayCommand]
    private void StopTracking()
    {
        if (_trackingStartedAt.HasValue)
        {
            var session = DateTime.UtcNow - _trackingStartedAt.Value;
            if (IsPaused)
            {
                _breakTodayAccumulated += session;
            }
            else
            {
                // 8-Hour Rule: Add to total work first, then split
                TimeSpan dayGoal = TimeSpan.FromHours(8);
                TimeSpan existingTotal = _workTodayAccumulated + _overtimeTodayAccumulated;
                TimeSpan newTotal = existingTotal + session;

                if (newTotal > dayGoal)
                {
                    _workTodayAccumulated = dayGoal;
                    _overtimeTodayAccumulated = newTotal - dayGoal;
                }
                else
                {
                    _workTodayAccumulated = newTotal;
                    _overtimeTodayAccumulated = TimeSpan.Zero;
                }
            }
        }

        IsTrackingActive = false;
        IsPaused = false;
        _trackingStartedAt = null;
        TrackingStatusText = "Tracking Stopped";
        TrackingStatusDetail = "Work time is not being tracked and no data is being collected.";
        
        if (_activityService != null)
        {
            _activityService.IsEnabled = false;
        }

        if (_agent != null)
        {
            _ = _agent.ReportWorkStatusAsync("STOPPED",
                (int)_workTodayAccumulated.TotalSeconds,
                (int)_overtimeTodayAccumulated.TotalSeconds,
                (int)_breakTodayAccumulated.TotalSeconds);
        }
    }

    [RelayCommand]
    private void PauseTracking()
    {
        if (!IsTrackingActive) return;

        if (_trackingStartedAt.HasValue)
        {
            var session = DateTime.UtcNow - _trackingStartedAt.Value;
            if (IsPaused)
            {
                _breakTodayAccumulated += session;
            }
            else
            {
                // 8-Hour Rule: Add to total work first, then split
                TimeSpan dayGoal = TimeSpan.FromHours(8);
                TimeSpan existingTotal = _workTodayAccumulated + _overtimeTodayAccumulated;
                TimeSpan newTotal = existingTotal + session;

                if (newTotal > dayGoal)
                {
                    _workTodayAccumulated = dayGoal;
                    _overtimeTodayAccumulated = newTotal - dayGoal;
                }
                else
                {
                    _workTodayAccumulated = newTotal;
                    _overtimeTodayAccumulated = TimeSpan.Zero;
                }
            }
        }

        IsPaused = !IsPaused;
        _trackingStartedAt = DateTime.UtcNow;

        if (IsPaused)
        {
            TrackingStatusText = "On Break";
            TrackingStatusDetail = "Monitoring is active but work time is not being counted.";
        }
        else
        {
            TrackingStatusText = "Working";
            TrackingStatusDetail = "Work time is being tracked and desktop captures are active.";
        }

        if (_activityService != null)
        {
            _activityService.IsEnabled = !IsPaused;
        }

    }

    [RelayCommand]
    private void RequestFinishWork()
    {
        ShowFinishConfirmation = true;
        if (IsWidgetActive)
        {
            ExpandToFullView();
        }
    }

    [RelayCommand]
    private void ConfirmFinish()
    {
        ShowFinishConfirmation = false;
        StopTracking();
    }

    [RelayCommand]
    private void CancelFinishWork()
    {
        ShowFinishConfirmation = false;
    }

    private void StartTrackingInternal()
    {
        StartTracking();
    }

    private void HandlePolicyUpdated(TrackedPolicy policy)
    {
        if (policy.TodayStats != null && !_hasInitialStatsSync)
        {
            _workTodayAccumulated = TimeSpan.FromSeconds(policy.TodayStats.WorkTimeSeconds);
            _overtimeTodayAccumulated = TimeSpan.FromSeconds(policy.TodayStats.OvertimeSeconds);
            _breakTodayAccumulated = TimeSpan.FromSeconds(policy.TodayStats.BreakTimeSeconds);
            _hasInitialStatsSync = true;
            AppLogger.Log($"Policy initial stats sync: Work={_workTodayAccumulated}, Overtime={_overtimeTodayAccumulated}, Break={_breakTodayAccumulated}", LogLevel.Info);
            UpdateDashboardTick();
        }
        
        IsMaintenanceActive = policy.MaintenanceMode;
        StatusMessage = policy.MaintenanceMode 
            ? "Server is currently in maintenance mode." 
            : "Policy updated.";
    }

    [RelayCommand]
    private void RequestUninstall()
    {
        if (!IsAdminMode)
        {
            StatusMessage = "Admin access required for uninstallation.";
            return;
        }

        try
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var uninstaller = Path.Combine(appPath, "unins000.exe");

            if (File.Exists(uninstaller))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uninstaller,
                    UseShellExecute = true
                });
            }
            else
            {
                StatusMessage = "Uninstaller not found. Please use Windows Control Panel.";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "launching uninstaller");
            StatusMessage = "Failed to launch uninstaller.";
        }
    }

    private async Task UnlockAdminAsync()
    {
        try
        {
            Window? owner = null;
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                owner = desktop.MainWindow;
            }

            var dialog = new EnfyLiveScreenClient.Views.AdminVerificationDialog();
            var result = await dialog.ShowDialog<bool>(owner!);
            
            if (result)
            {
                IsAdminMode = true;
                _adminAutoLockSecondsRemaining = DefaultAdminAutoLockSeconds;
                AdminRemainingDisplay = "01:00";
                
                _adminLockTimer?.Stop();
                _adminLockTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _adminLockTimer.Tick += (s, e) =>
                {
                    _adminAutoLockSecondsRemaining--;
                    int mins = _adminAutoLockSecondsRemaining / 60;
                    int secs = _adminAutoLockSecondsRemaining % 60;
                    AdminRemainingDisplay = $"{mins:D2}:{secs:D2}";
                    
                    if (_adminAutoLockSecondsRemaining <= 0)
                    {
                        IsAdminMode = false;
                        _adminLockTimer.Stop();
                        StatusMessage = "Admin mode auto-locked.";
                    }
                };
                _adminLockTimer.Start();
                
                StatusMessage = "Admin access granted.";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Admin verification error: {ex.Message}");
        }
    }

    private async Task SyncAdminSecretsAsync()
    {
        if (_authSession == null) return;
        try
        {
            string url = $"{BackendUrl}/auth/admin-secrets";
            var secrets = await _authApiClient.GetAsync<List<string>>(url, _authSession.AccessToken);
            if (secrets != null)
            {
                AdminSecurityService.Instance.UpdateSecrets(secrets);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to sync admin secrets: {ex.Message}");
        }
    }

    private void HandleConnectionStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() => ConnectionStatus = status);
    }

    private void HandleStreamStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() => StreamStatus = status);
    }

    private string GetFriendlyErrorMessage(Exception ex)
    {
        if (ex.Message.Contains("401")) return "Unauthorized. Please sign in again.";
        if (ex.Message.Contains("403")) return "Access denied. Admin role may be required.";
        if (ex.Message.Contains("Failed to connect")) return "Cannot reach the server. Check your internet and Backend URL.";
        return ex.Message;
    }

    private async Task TryAutoLoginAsync()
    {
        if (_config.LastSession == null || !_config.RememberMe)
        {
            return;
        }

        try
        {
            IsBusy = true;
            AuthStatus = "Restoring session...";
            StatusMessage = "Restoring your Microsoft sign-in session...";
            
            var newSession = await _authApiClient.RefreshTokenAsync(
                BackendUrl,
                _config.LastSession.RefreshToken);

            if (newSession.User == null)
            {
                throw new InvalidOperationException("Restored session is missing user profile data.");
            }

            _authSession = newSession;
            CurrentUser = _authSession.User.DisplayName;
            AuthStatus = "Signed in (Session Restored)";
            StatusMessage = "Session restored successfully.";
            IsAuthenticated = true;

            // Check if user needs to accept terms
            if (!_authSession.User.TermsAccepted)
            {
                IsTermsVisible = true;
                StatusMessage = "Please review and accept the Company Usage Disclosure.";
            }

            if (_config.RememberMe)
            {
                _config.LastSession = _authSession;
                _config.TermsAccepted = _authSession.User.TermsAccepted;
                _config.Save();
            }

            _ = SyncAdminSecretsAsync();

            if (AutoConnect && !IsTermsVisible)
            {
                await ConnectAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Auto-login failed: {ex.Message}", LogLevel.Warn);
            IsAuthenticated = false;
            _authSession = null;
            _config.LastSession = null;
            _config.Save();
            StatusMessage = "Session expired or invalid. Please sign in again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string ReformatTimeWithSeconds(string? timeStr)
    {
        if (string.IsNullOrEmpty(timeStr) || timeStr == "--:--") return "--:--:--";
        try
        {
            if (DateTime.TryParse(timeStr, out var dt))
            {
                return dt.ToString("hh:mm:ss tt");
            }
            // If it's just HH:mm
            if (timeStr.Contains(":") && timeStr.Split(':').Length == 2)
            {
                return DateTime.ParseExact(timeStr, "HH:mm", null).ToString("hh:mm:ss tt");
            }
            return timeStr;
        }
        catch
        {
            return timeStr;
        }
    }

    private DateTime GetEasternTimeNow()
    {
        try
        {
            // Windows ID for US Eastern Time
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone);
        }
        catch
        {
            // Fallback to UTC-5 (EST) if zone not found
            return DateTime.UtcNow.AddHours(-5);
        }
    }
}

public class StatusColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isActive && isActive)
            return Avalonia.Media.Brush.Parse("#10B981");
        return Avalonia.Media.Brush.Parse("#64748B");
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
            return "Resume Work";
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
