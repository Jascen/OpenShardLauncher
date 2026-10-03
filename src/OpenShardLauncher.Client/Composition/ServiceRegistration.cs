using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Client.ViewModels;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure;
using OpenShardLauncher.Infrastructure.Http;
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
        services.AddSingleton<SettingsService>();
        services.AddSingleton<FeedStateStore>();
        services.AddSingleton(sp => new ServerEndpoint(options, sp.GetRequiredService<SettingsService>().Current.ServerUrlOverride));

        // Registered before the infrastructure (which only adds its own if none exists) so the player's setting applies
        // from the first request. Nothing else feeds the setting to the HTTP layer; the settings dialog updates it.
        services.AddSingleton(sp => new TransportPolicy
        {
            AllowInsecureDownloads = sp.GetRequiredService<SettingsService>().Current.AllowInsecureDownloads,
        });
        services.TryAddSingleton<IGameLauncher, TazUOLauncher>();
        return services;
    }

    public static IServiceCollection AddUiServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IUrlLauncher, AvaloniaUrlLauncher>();
        services.TryAddSingleton<IAppLifetime, AvaloniaAppLifetime>();
        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }
}
