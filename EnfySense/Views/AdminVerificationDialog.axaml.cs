using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EnfyLiveScreenClient.Services;

namespace EnfyLiveScreenClient.Views;

public partial class AdminVerificationDialog : Window
{
    public bool IsVerified { get; private set; }

    public AdminVerificationDialog()
    {
        InitializeComponent();
        
        var input = this.FindControl<TextBox>("CodeInput");
        if (input != null)
        {
            input.AttachedToVisualTree += (s, e) => input.Focus();
        }
    }

    private void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        var input = this.FindControl<TextBox>("CodeInput");
        var errorText = this.FindControl<TextBlock>("ErrorText");

        if (input != null && AdminSecurityService.Instance.VerifyCode(input.Text ?? ""))
        {
            IsVerified = true;
            Close(true);
        }
        else
        {
            if (errorText != null)
            {
                errorText.IsVisible = true;
            }
            if (input != null)
            {
                input.Text = "";
                input.Focus();
            }
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        IsVerified = false;
        Close(false);
    }
}
