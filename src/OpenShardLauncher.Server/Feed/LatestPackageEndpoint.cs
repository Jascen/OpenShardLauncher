using Microsoft.Extensions.Options;
using OpenShardLauncher.Server.Configuration;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Server.Feed;

// A link that never changes, for a website or Discord: /latest/launcher/win-x64 redirects to the newest package of
// that role and platform in packages/manifest.json. The redirect is never cached; the zip it points to is served
// like any other package. The manifest isn't signature-checked here: the server is no more trusted than the files it
// serves, and launchers verify the zip themselves.
public static partial class LatestPackageEndpoint
{
    public const string Pattern = "/latest/{role}/{rid}";

    public static IEndpointRouteBuilder MapLatestPackage(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods(Pattern, [HttpMethods.Get, HttpMethods.Head], Redirect);
        return endpoints;
    }

    private static IResult Redirect(string role, string rid, HttpContext context, IOptions<ServerOptions> options, ILoggerFactory loggers)
    {
        context.Response.Headers.CacheControl = FeedStaticFiles.RevalidateCacheControl;
        if (role is not (PackageRole.Launcher or PackageRole.Client))
        {
            return Results.NotFound();
        }

        var package = ReadManifest(options.Value.FeedRoot, loggers.CreateLogger(typeof(LatestPackageEndpoint)))?.Find(role, rid);
        return package is null
            ? Results.NotFound()
            : Results.Redirect("/" + FeedLayout.PackagePath(Uri.EscapeDataString(package.File)));
    }

    // Read on every request: it's small, and a fresh publish is picked up without a restart
    private static PackageManifest? ReadManifest(string feedRoot, ILogger logger)
    {
        var path = Path.Combine(feedRoot, FeedLayout.ManifestPath);
        try
        {
            return File.Exists(path) ? PackageManifest.Parse(File.ReadAllBytes(path), problem => LogInvalidEntry(logger, path, problem)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogUnreadable(logger, e, path);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Manifest} for a /latest link")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, string manifest);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Manifest} {Problem}; skipping it for /latest links")]
    private static partial void LogInvalidEntry(ILogger logger, string manifest, string problem);
}
