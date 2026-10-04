using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenShardLauncher.Client.Composition;
using OpenShardLauncher.Client.ViewModels;
using OpenShardLauncher.Client.Views;

namespace OpenShardLauncher.Client;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var host = LauncherHost.Build(AppContext.BaseDirectory);
            var viewModel = host.Services.GetRequiredService<MainWindowViewModel>();

            // Hosted services: the launcher update check.
            host.Start();
            var window = new MainWindow { DataContext = viewModel };
            window.Opened += (_, _) => viewModel.StartCommand.Execute(null);
            desktop.MainWindow = window;

            // Cancels anything still running (disposing the host stops the update check) and flushes the log.
            desktop.Exit += (_, _) =>
            {
                viewModel.Dispose();
                host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
