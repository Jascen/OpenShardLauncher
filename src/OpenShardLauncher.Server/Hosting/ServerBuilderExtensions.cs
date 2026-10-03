using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OpenShardLauncher.Server.Configuration;
using OpenShardLauncher.Server.Feed;
using Serilog;
using Serilog.Settings.Configuration;

namespace OpenShardLauncher.Server.Hosting;

public static class ServerBuilderExtensions
{
    public static WebApplicationBuilder AddOpenShardLauncherServer(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        ResolveLogPaths(configuration);
        services.AddSerilog(logger => logger.ReadFrom.Configuration(
            configuration,
            // Explicit sink assemblies, so reading the config also works in a single-file publish
            new ConfigurationReaderOptions(typeof(ConsoleLoggerConfigurationExtensions).Assembly, typeof(FileLoggerConfigurationExtensions).Assembly)));

        services.AddOptions<ServerOptions>()
            .Bind(configuration.GetSection(ServerOptions.SectionName), binder => binder.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();

        // Registered unconditionally; UseOpenShardLauncherFeed only adds the middleware that is switched on.
        // The options are read lazily, so settings applied late (tests, other config sources) still count.
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<ServerOptions>>((limiter, server) => ConfigureRateLimiter(limiter, server.Value.RateLimiting));
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ServerOptions>>((forwarded, server) => ConfigureForwardedHeaders(forwarded, server.Value.ForwardedHeaders));
        services.AddResponseCompression(compression =>
        {
            // The JSON is public and holds no secrets, so compressing it over https is safe (no BREACH concern)
            compression.EnableForHttps = true;
            compression.MimeTypes = ["application/json"];
        });

        services.AddHealthChecks();
        services.AddHostedService<StartupBanner>();
        return builder;
    }

    // Order matters: forwarded headers set the client IP the rate limiter partitions on, and the limiter must run
    // before StaticFiles (endpoint rate-limit policies don't apply to static files).
    public static WebApplication UseOpenShardLauncherFeed(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
        if (options.ForwardedHeaders.Enabled)
        {
            app.UseForwardedHeaders();
        }

        if (options.RateLimiting.Enabled)
        {
            app.UseRateLimiter();
        }

        if (options.Compression)
        {
            app.UseResponseCompression();
        }

        app.UseFeedStaticFiles(options.FeedRoot);
        return app;
    }

    private static void ConfigureRateLimiter(RateLimiterOptions limiter, RateLimitingSettings settings)
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetConcurrencyLimiter(ClientKey(context.Connection.RemoteIpAddress), _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = settings.ConcurrentRequestsPerClient,
                QueueLimit = settings.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
    }

    // One bucket per IPv4 address, and per IPv6 /64 (one client usually owns a whole /64 and could rotate within it)
    private static string ClientKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? new System.Net.IPNetwork(address, 64).BaseAddress + "/64"
            : address.ToString();
    }

    // Loopback stays trusted (a proxy on the same machine); configured proxies and networks are added to it
    private static void ConfigureForwardedHeaders(ForwardedHeadersOptions forwarded, ForwardedHeadersSettings settings)
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var proxy in settings.KnownProxies)
        {
            forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in settings.KnownNetworks)
        {
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    }

    // Serilog's file sink resolves relative paths against the current directory; make them relative to the exe
    // folder instead, like FeedDirectory, so a service started from anywhere logs next to itself.
    private static void ResolveLogPaths(ConfigurationManager configuration)
    {
        var resolved = new Dictionary<string, string?>();
        foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
        {
            var path = sink["Args:path"];
            if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
            {
                resolved[$"{sink.Path}:Args:path"] = Path.Combine(AppContext.BaseDirectory, path);
            }
        }

        configuration.AddInMemoryCollection(resolved);
    }
}
