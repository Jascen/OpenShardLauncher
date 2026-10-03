using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Server.Feed;

// Serves the feed folder with StaticFiles, which handles Range/If-Range, ETag/Last-Modified/304, HEAD, sendfile and
// rejects paths outside the root. Symlinks inside the folder are followed (the folder is operator-controlled).
public static class FeedStaticFiles
{
    // Blob contents never change under a hash name, so they can be cached for good
    public const string BlobCacheControl = "public, max-age=31536000, immutable";

    // Everything else (files.json, files.sig, the manifest and zips) must be revalidated, so a new publish is seen
    public const string RevalidateCacheControl = "no-cache";

    public static IApplicationBuilder UseFeedStaticFiles(this IApplicationBuilder app, string feedRoot)
    {
        var options = new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(feedRoot, ExclusionFilters.Hidden | ExclusionFilters.System),
            ContentTypeProvider = new FeedContentTypeProvider(),
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
            OnPrepareResponse = context =>
            {
                var isBlob = context.Context.Request.Path.StartsWithSegments("/" + FeedLayout.BlobsFolder, StringComparison.Ordinal);
                context.Context.Response.Headers.CacheControl = isBlob ? BlobCacheControl : RevalidateCacheControl;
            },
        };

        // No directory browser and no default files: only exact feed paths are served
        return app.UseWhen(context => FeedPathFilter.IsFeedPath(context.Request.Path), feed => feed.UseStaticFiles(options));
    }

    // JSON for files.json and manifest.json; everything else (blobs, zips, signatures) is application/octet-stream
    private sealed class FeedContentTypeProvider : IContentTypeProvider
    {
        public bool TryGetContentType(string subpath, out string contentType)
        {
            if (subpath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "application/json";
                return true;
            }

            contentType = string.Empty;
            return false;
        }
    }
}
