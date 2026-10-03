using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Extensions.Options;

namespace OpenShardLauncher.Server.Configuration;

// The "Server" section of appsettings.json. Unknown keys fail startup, so a typo can't silently leave a setting at
// its default. Endpoints and certificates use ASP.NET's standard "Kestrel" section.
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    // The feed folder produced by the Publisher. A relative path is resolved against the server's exe folder.
    [Required]
    public string FeedDirectory { get; set; } = "feed";

    // gzip/brotli for files.json and manifest.json only. Blobs and zips are never compressed.
    public bool Compression { get; set; }

    public RateLimitingSettings RateLimiting { get; set; } = new();

    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();

    public string FeedRoot => Path.GetFullPath(FeedDirectory, AppContext.BaseDirectory);
}

// Off by default: CDNs and reverse proxies usually rate-limit already
public sealed class RateLimitingSettings
{
    public bool Enabled { get; set; }

    // Requests one client (IPv4 address or IPv6 /64) may have in progress at once
    public int ConcurrentRequestsPerClient { get; set; } = 8;

    // Further requests that wait for a slot before getting 429
    public int QueueLimit { get; set; } = 32;
}

// Off by default. Only needed when rate limiting is on and the server sits behind a proxy (see README).
public sealed class ForwardedHeadersSettings
{
    public bool Enabled { get; set; }

    // Proxy addresses whose X-Forwarded-For is trusted, e.g. "10.0.0.5". Loopback is always trusted.
    public List<string> KnownProxies { get; set; } = [];

    // Proxy networks in CIDR form, e.g. "173.245.48.0/20" for a Cloudflare range
    public List<string> KnownNetworks { get; set; } = [];
}

internal sealed class ServerOptionsValidator : IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.FeedDirectory) && !Directory.Exists(options.FeedRoot))
        {
            failures.Add($"Server:FeedDirectory {options.FeedRoot} does not exist. Publish a feed there or fix the path.");
        }

        if (options.RateLimiting.ConcurrentRequestsPerClient is < 1 or > 10_000)
        {
            failures.Add("Server:RateLimiting:ConcurrentRequestsPerClient must be between 1 and 10000.");
        }

        if (options.RateLimiting.QueueLimit is < 0 or > 100_000)
        {
            failures.Add("Server:RateLimiting:QueueLimit must be between 0 and 100000.");
        }

        failures.AddRange(options.ForwardedHeaders.KnownProxies
            .Where(p => !IPAddress.TryParse(p, out _))
            .Select(p => $"Server:ForwardedHeaders:KnownProxies: '{p}' is not an IP address."));
        failures.AddRange(options.ForwardedHeaders.KnownNetworks
            .Where(n => !IPNetwork.TryParse(n, out _))
            .Select(n => $"Server:ForwardedHeaders:KnownNetworks: '{n}' is not a network like 10.0.0.0/8."));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
