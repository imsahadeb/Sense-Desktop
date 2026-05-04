using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace EnfyLiveScreenClient.Views;

public partial class SenseWidget : Window
{
    public SenseWidget()
    {
        InitializeComponent();
        Deactivated += SenseWidget_Deactivated;
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
