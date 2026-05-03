using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EnfyLiveScreenClient.Services;
using System;

namespace EnfyLiveScreenClient.Views;

public partial class InactivityPromptWindow : Window
{
    private int _remainingSeconds = 30;
    private readonly DispatcherTimer _timer;
    public bool IsConfirmed { get; private set; }

    public InactivityPromptWindow()
    {
        InitializeComponent();
        
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        // Set dynamic description text based on policy
        var threshold = PolicyManager.Instance.CurrentPolicy.IdleThresholdSec;
        var minutes = threshold / 60;
        var seconds = threshold % 60;
        var timeStr = minutes > 0 
            ? $"{minutes} minute{(minutes > 1 ? "s" : "")}" 
            : $"{seconds} seconds";
            
        var descText = this.FindControl<TextBlock>("DescriptionText");
        if (descText != null)
        {
            descText.Text = $"Inactivity detected for {timeStr}. Please confirm your status.";
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        
        var timerText = this.FindControl<TextBlock>("TimerText");
        if (timerText != null)
        {
            timerText.Text = _remainingSeconds.ToString();
            
            // Change color to red when low on time
            if (_remainingSeconds <= 10)
            {
                timerText.Foreground = Avalonia.Media.Brushes.Red;
            }
        }

        if (_remainingSeconds <= 0)
        {
            _timer.Stop();
            Close(false);
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _timer.Stop();
        IsConfirmed = true;
        Close(true);
    }
}
