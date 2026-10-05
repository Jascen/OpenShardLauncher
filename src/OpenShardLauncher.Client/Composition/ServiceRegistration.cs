using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Client.ViewModels;
using OpenShardLauncher.Client.ViewModels.Dialogs;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Client.Composition;

public static class ServiceRegistration
{
    // Everything the launcher window needs: Core services, infrastructure, the workflow, UI services and view models.
    public static IServiceCollection AddLauncher(
        this IServiceCollection services, LauncherOptions options, LauncherDataFolder dataFolder, LauncherFolder launcherFolder) =>
        services
            .AddLauncherCore(options, dataFolder, launcherFolder)
            .AddOpenShardLauncherInfrastructure()
            .AddOpenShardLauncherWorkflow()
            .AddUiServices()
            .AddViewModels();

    // The Core services the infrastructure and workflow expect the composition root to register.
    public static IServiceCollection AddLauncherCore(
        this IServiceCollection services, LauncherOptions options, LauncherDataFolder dataFolder, LauncherFolder launcherFolder)
    {
        services.AddSingleton(options);
        services.AddSingleton(dataFolder);
        services.AddSingleton(launcherFolder);
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<FeedStateStore>();

        // SettingsService owns the effective server and the transport rule, so the player's settings apply from the
        // first request and saving them updates both. The transport rule is registered before the infrastructure,
        // which only adds its own if none exists.
        services.AddSingleton<SettingsService>();
        services.AddSingleton(sp => sp.GetRequiredService<SettingsService>().ServerEndpoint);
        services.AddSingleton(sp => sp.GetRequiredService<SettingsService>().TransportPolicy);
        services.TryAddSingleton<IGameLauncher, ClientLauncher>();
        services.TryAddSingleton(new InstalledLauncher(LauncherVersion.CurrentVersion, launcherFolder.Path, SelfUpdater.ExeName));
        return services;
    }

    public static IServiceCollection AddUiServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IUrlLauncher, AvaloniaUrlLauncher>();
        services.TryAddSingleton<IAppLifetime, AvaloniaAppLifetime>();
        services.TryAddSingleton<IFolderPicker, AvaloniaFolderPicker>();
        services.TryAddSingleton<IDialogService, DialogService>();

        // Started with the host (App): checks for launcher updates at startup and every PackageCheckInterval.
        services.AddHostedService<LauncherUpdatePoller>();
        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<LauncherUpdateBannerViewModel>(sp => new LauncherUpdateBannerViewModel(
            sp.GetRequiredService<Core.Packages.LauncherSelfUpdateService>(),
            sp.GetRequiredService<InstalledLauncher>(),
            sp.GetRequiredService<LauncherDataFolder>()));
        services.AddSingleton<SecurityNoticeViewModel>();

        // A fresh copy of the settings each time the dialog opens.
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<Func<SettingsViewModel>>(sp => sp.GetRequiredService<SettingsViewModel>);
        return services;
    }
}
