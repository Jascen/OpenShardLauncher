using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Server.Feed;

// Only the feed layout is served: /files.json, /files.sig, /blobs/** and /packages/**. Anything else in the feed
// folder (notes, backups, a stray state folder) is never reachable and falls through to a 404.
public static class FeedPathFilter
{
    private static readonly PathString Blobs = "/" + FeedLayout.BlobsFolder;
    private static readonly PathString Packages = "/" + FeedLayout.PackagesFolder;

    public static bool IsFeedPath(PathString path) =>
        path.Value is "/" + FeedLayout.FileListPath or "/" + FeedLayout.FileListSignaturePath
        || IsBelow(path, Blobs)
        || IsBelow(path, Packages);

    // Case-sensitive, so the filter means the same on every file system
    private static bool IsBelow(PathString path, PathString folder) =>
        path.StartsWithSegments(folder, StringComparison.Ordinal, out var rest) && rest.HasValue && rest.Value != "/";
}
