namespace OpenShardLauncher.Shared.Feed;

// Paths inside a feed, relative to its root. They are both the file layout the Publisher writes and the URLs a
// launcher requests. Stable once released (docs/feed-format.md).
public static class FeedLayout
{
    public const string FileListPath = "files.json";
    public const string FileListSignaturePath = "files.sig";
    public const string BlobsFolder = "blobs";
    public const string PackagesFolder = "packages";
    public const string ManifestFileName = "manifest.json";
    public const string ManifestSignatureFileName = "manifest.sig";
    public const string ManifestPath = PackagesFolder + "/" + ManifestFileName;
    public const string ManifestSignaturePath = PackagesFolder + "/" + ManifestSignatureFileName;

    // Content-addressed and sharded by the first two hex characters, so no folder grows too large
    public static string BlobPath(string sha256) => $"{BlobsFolder}/{sha256[..2]}/{sha256}";

    public static string PackagePath(string fileName) => $"{PackagesFolder}/{fileName}";
}
