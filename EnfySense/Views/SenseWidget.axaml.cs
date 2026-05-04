using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace EnfyLiveScreenClient.Views;

public partial class SenseWidget : Window
{
    public SenseWidget()
    {
        InitializeComponent();
        Deactivated += SenseWidget_Deactivated;
        
        // Periodically re-assert topmost status to handle aggressive apps like Terminal
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (s, e) => {
            if (Topmost)
            {
                // Toggling Topmost forces the OS to re-evaluate the Z-order
                Topmost = false;
                Topmost = true;
            }
        };
        timer.Start();
    }

    private void SenseWidget_Deactivated(object? sender, EventArgs e)
    {
        // Ensure the widget stays on top even when it loses focus
        if (Topmost)
        {
            Topmost = false;
            Topmost = true;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
