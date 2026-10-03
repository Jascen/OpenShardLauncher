using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using OpenShardLauncher.Server.Configuration;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Server.Hosting;

// Logs what is being served and how, once the server is listening
internal sealed partial class StartupBanner(
    IOptions<ServerOptions> options,
    IServer server,
    IHostApplicationLifetime lifetime,
    ILogger<StartupBanner> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(Log);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Log()
    {
        var settings = options.Value;
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        LogServing(logger, settings.FeedRoot, string.Join(", ", addresses));
        LogFeatures(logger, settings.RateLimiting.Enabled, settings.ForwardedHeaders.Enabled, settings.Compression);

        if (!File.Exists(Path.Combine(settings.FeedRoot, FeedLayout.FileListPath)))
        {
            LogNoFeed(logger, FeedLayout.FileListPath);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenShardLauncher feed server: serving {FeedRoot} on {Addresses}")]
    private static partial void LogServing(ILogger logger, string feedRoot, string addresses);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate limiting: {RateLimiting}. Forwarded headers: {ForwardedHeaders}. Compression: {Compression}")]
    private static partial void LogFeatures(ILogger logger, bool rateLimiting, bool forwardedHeaders, bool compression);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The feed has no {FileList} yet. Publish one with the Publisher; launchers can't update until then.")]
    private static partial void LogNoFeed(ILogger logger, string fileList);
}
