using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Core.Abstractions;

// The update feed on the current server (ServerEndpoint.Current), implemented over HTTP in Infrastructure.
// Documents come back already verified (signature, rollback and AllowUnsignedFeed rules), so Core never sees an
// untrusted list.
public interface IUpdateServer
{
    // files.json, verified. Fails with FeedUntrusted, FeedUpdating, NothingPublished, ConnectionFailed, ...
    Task<FeedResult<FileList>> GetFileListAsync(CancellationToken cancellationToken);

    // packages/manifest.json, verified the same way.
    Task<FeedResult<PackageManifest>> GetPackageManifestAsync(CancellationToken cancellationToken);

    // Downloads blobs/<aa>/<sha256>, checks its size and hash and moves it to destinationPath (replacing a file
    // there). Reports bytes of this file received so far. Throws UpdateException (e.g. FileLocked, FeedUpdating for
    // a missing blob) or OperationCanceledException.
    Task DownloadBlobAsync(FileEntry file, string destinationPath, IProgress<long>? progress, CancellationToken cancellationToken);

    // Downloads and checks packages/<File> the same way.
    Task DownloadPackageAsync(PackageEntry package, string destinationPath, IProgress<long>? progress, CancellationToken cancellationToken);
}

public enum FeedTrust
{
    Signed,
    Unsigned, // Accepted without a signature because the launcher allows unsigned feeds
}

public sealed record FeedResult<T>
    where T : class
{
    public T? Document { get; private init; }

    public FeedTrust Trust { get; private init; }

    public UpdateError? Error { get; private init; }

    public bool Succeeded => Document is not null;

    public static FeedResult<T> Success(T document, FeedTrust trust) => new() { Document = document, Trust = trust };

    public static FeedResult<T> Failure(UpdateError error) => new() { Error = error };
}
