using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using EnfyLiveScreenClient.ViewModels;
using EnfyLiveScreenClient.Views;
using EnfyLiveScreenClient.Services;

namespace EnfyLiveScreenClient;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            bool isStartup = false;
            if (desktop.Args != null)
            {
                foreach (var arg in desktop.Args)
                {
                    if (arg == "--startup") isStartup = true;
                }
            }

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var vm = new MainWindowViewModel();
            var window = new MainWindow
            {
                DataContext = vm,
            };
            
            desktop.MainWindow = window;

            if (isStartup)
            {
                // If started automatically, keep main window hidden and show only the widget
                window.WindowState = WindowState.Minimized;
                vm.IsWidgetActive = true;
            }
            else
            {
                window.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
