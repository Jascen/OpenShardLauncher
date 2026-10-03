using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Files;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Infrastructure.Http;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Infrastructure;

public static class DependencyInjection
{
    // Registers IUpdateServer (typed HttpClients through IHttpClientFactory), FeedVerifier, TransportPolicy and the
    // retry pipelines. The composition root registers the Core services they use: LauncherOptions, ServerEndpoint and
    // FeedStateStore.
    public static IServiceCollection AddOpenShardLauncherInfrastructure(this IServiceCollection services, Action<DownloadOptions>? configure = null)
    {
        var options = services.AddOptions<DownloadOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TransportPolicy>();
        services.TryAddSingleton<ServerBackoff>();
        services.TryAddSingleton<FeedVerifier>();
        services.TryAddTransient<SecureTransportHandler>();
        services.TryAddTransient<ServerBusyHandler>();
        services.TryAddSingleton(new PlatformInfo(PlatformId.Current));
        services.AddRetryPipelines();

        services.AddHttpClient<ResumableDownloader>().AddFeedClientDefaults();
        services.AddHttpClient<IUpdateServer, UpdateServerClient>().AddFeedClientDefaults();
        return services;
    }

    // Registers the Core use cases (UpdateWorkflow and its stages, PackageCheckService) on top of the infrastructure.
    // The composition root also registers an IGameLauncher (phase 7: TazUOLauncher).
    public static IServiceCollection AddOpenShardLauncherWorkflow(this IServiceCollection services, Action<WorkflowOptions>? configure = null)
    {
        var options = new WorkflowOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<CompareStage>();
        services.TryAddSingleton<DownloadStage>();
        services.TryAddSingleton<PackageCheckService>();
        services.TryAddSingleton<UpdateWorkflow>();
        return services;
    }

    // Every request uses ResponseHeadersRead, so HttpClient.Timeout only limits the wait for headers; bodies are limited
    // by DownloadOptions.StallTimeout. ServerBusyHandler sits inside SecureTransportHandler, so it sees every redirect hop.
    // No automatic redirects (SecureTransportHandler follows and checks them) and no automatic decompression (it
    // doesn't mix with Range requests; blobs are served as stored). No retry handler: the retry pipelines wrap whole
    // operations from outside, so every attempt goes through SecureTransportHandler.
    private static IHttpClientBuilder AddFeedClientDefaults(this IHttpClientBuilder builder) =>
        builder
            .ConfigureHttpClient((services, client) =>
            {
                var options = services.GetRequiredService<IOptions<DownloadOptions>>().Value;
                client.Timeout = options.RequestTimeout;
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenShardLauncher", LauncherVersion.Current));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .AddHttpMessageHandler<SecureTransportHandler>()
            .AddHttpMessageHandler<ServerBusyHandler>();
}
