using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace EnfyLiveScreenClient.Views;

public partial class MainWindow : Window
{
    private bool _isDialogOpen = false;

    private bool _isRealClose = false;

    private SenseWidget? _widget;

    public MainWindow()
    {
        InitializeComponent();
        
        DataContextChanged += (s, e) => {
            if (DataContext is ViewModels.MainWindowViewModel vm) {
                vm.PropertyChanged += (sender, args) => {
                    if (args.PropertyName == nameof(ViewModels.MainWindowViewModel.IsWidgetActive)) {
                        ToggleWidget(vm.IsWidgetActive);
                    }
                };
            }
        };

        Closing += (s, e) => {
            if (!_isRealClose)
            {
                e.Cancel = true;
                if (DataContext is ViewModels.MainWindowViewModel vm) {
                    vm.ShowWidget();
                }
            }
            else {
                _widget?.Close();
            }
        };

        // Show widget when minimized
        this.PropertyChanged += (s, e) => {
            if (e.Property == WindowStateProperty) {
                var state = (WindowState)e.NewValue!;
                if (state == WindowState.Minimized) {
                    if (DataContext is ViewModels.MainWindowViewModel vm && !vm.IsWidgetActive) {
                        vm.ShowWidget();
                    }
                }
            }
        };
    }

    private void ToggleWidget(bool show) {
        if (show) {
            if (_widget == null) {
                _widget = new SenseWidget { DataContext = DataContext };
            }
            _widget.Show();
        } else {
            _widget?.Hide();
        }
    }

    public void Shutdown()
    {
        _isRealClose = true;
        Close();
    }

    /// <summary>
    /// Helper method for ViewModels to trigger protected actions (like Logout)
    /// </summary>
    public async Task<bool> VerifyAdminAsync()
    {
        if (_isDialogOpen)
        {
            return false;
        }

        var vm = DataContext as ViewModels.MainWindowViewModel;
        if (vm != null) vm.IsOverlayVisible = true;

        _isDialogOpen = true;
        try
        {
            var dialog = new AdminVerificationDialog();
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            _isDialogOpen = false;
            if (vm != null) vm.IsOverlayVisible = false;
        }
    }
}

