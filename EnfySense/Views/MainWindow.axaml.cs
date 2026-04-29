using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace EnfyLiveScreenClient.Views;

public partial class MainWindow : Window
{
    private bool _canClose = false;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_canClose)
        {
            base.OnClosing(e);
            return;
        }

        // Always cancel the initial close attempt
        e.Cancel = true;

        // Show the verification dialog
        var dialog = new AdminVerificationDialog();
        var result = await dialog.ShowDialog<bool>(this);

        if (result)
        {
            _canClose = true;
            Close();
        }
    }

    /// <summary>
    /// Helper method for ViewModels to trigger protected actions (like Logout)
    /// </summary>
    public async Task<bool> VerifyAdminAsync()
    {
        var dialog = new AdminVerificationDialog();
        return await dialog.ShowDialog<bool>(this);
    }
}

