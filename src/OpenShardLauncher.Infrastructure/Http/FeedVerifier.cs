using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Infrastructure.Http;

// A signed document as fetched: its exact bytes and its signature file (null when there is none).
public sealed record FeedDocument(byte[] Content, string? Signature);

// Decides whether files.json or the package manifest can be trusted, over the fetched bytes:
// - a valid signature from a trusted key: accepted
// - an invalid signature: never accepted. Both files are fetched once more first (a publish may have landed between
//   the two requests); if they still don't match, the feed is reported as FeedUpdating, since a list and signature
//   caught mid-upload look exactly like that
// - no signature: accepted as Unsigned only when the launcher is built with AllowUnsignedFeed, the server is the
//   default one and the transport is https (or loopback). Otherwise FeedUntrusted, or with AllowUnsignedFeed the reason
//   it was refused: UnsignedFeedNotDefaultServer or UnsignedFeedInsecure
// - no trusted keys and no unsigned mode: FeedUntrusted without fetching anything
// - a files.json with a lower version than the last one accepted from that server (a rollback): FeedUntrusted
public sealed class FeedVerifier(LauncherOptions options, ServerEndpoint endpoint, FeedStateStore feedState, ILogger<FeedVerifier> logger)
{
    // Raised (on the fetching thread) each time a document was accepted without a signature, for the security notice.
    public event EventHandler? UnsignedAccepted;

    // Names are checked against a stand-in root: whether a name stays inside a folder doesn't depend on the folder.
    private static readonly string ContainmentRoot = Path.Combine(Path.GetTempPath(), "openshardlauncher-containment");

    // fetch gets both files from server; it is called a second time after a mismatch.
    public Task<FeedResult<FileList>> VerifyFileListAsync(
        Uri server, Func<CancellationToken, Task<FeedDocument>> fetch, CancellationToken cancellationToken) =>
        VerifyAsync(server, FeedLayout.FileListPath, fetch, ParseFileList, (list, trust) => AcceptFileList(server, list, trust), cancellationToken);

    public Task<FeedResult<PackageManifest>> VerifyManifestAsync(
        Uri server, Func<CancellationToken, Task<FeedDocument>> fetch, CancellationToken cancellationToken) =>
        VerifyAsync(server, FeedLayout.ManifestPath, fetch, ParseManifest, FeedResult<PackageManifest>.Success, cancellationToken);

    private async Task<FeedResult<T>> VerifyAsync<T>(
        Uri server,
        string name,
        Func<CancellationToken, Task<FeedDocument>> fetch,
        Func<byte[], T> parse,
        Func<T, FeedTrust, FeedResult<T>> accept,
        CancellationToken cancellationToken)
        where T : class
    {
        var keys = options.TrustedPublicKeys;
        if (keys.Count == 0 && !options.AllowUnsignedFeed)
        {
            logger.LogError("The launcher has no trusted keys and doesn't allow unsigned feeds, so {Name} can't be trusted", name);
            return FeedResult<T>.Failure(UpdateError.FeedUntrusted);
        }

        var document = await fetch(cancellationToken).ConfigureAwait(false);
        var status = FeedSigning.Verify(document.Content, document.Signature, keys);

        // With no keys, a signature can't be a stale pairing: nothing could ever verify it.
        if (status == SignatureStatus.Invalid && keys.Count > 0)
        {
            logger.LogWarning("The signature of {Name} from {Server} doesn't verify; fetching both again", name, server);
            document = await fetch(cancellationToken).ConfigureAwait(false);
            status = FeedSigning.Verify(document.Content, document.Signature, keys);
            if (status == SignatureStatus.Invalid)
            {
                // A signature by an unknown key can't be told apart from a stale one, so this is reported as an upload
                // in progress. Either way nothing is accepted.
                logger.LogError(
                    "The signature of {Name} from {Server} still doesn't verify with any trusted key. If this persists, the server is not signed with this launcher's keys",
                    name, server);
                return FeedResult<T>.Failure(UpdateError.FeedUpdating);
            }
        }

        FeedTrust trust;
        switch (status)
        {
            case SignatureStatus.Valid:
                trust = FeedTrust.Signed;
                break;

            case SignatureStatus.Missing when RefuseUnsigned(server, name) is { } refused:
                return FeedResult<T>.Failure(refused);

            case SignatureStatus.Missing:
                logger.LogWarning("Accepted {Name} from {Server} without a signature (AllowUnsignedFeed)", name, server);
                trust = FeedTrust.Unsigned;
                break;

            default:
                logger.LogError("{Name} from {Server} has a signature, but the launcher has no keys to check it with", name, server);
                return FeedResult<T>.Failure(UpdateError.FeedUntrusted);
        }

        T parsed;
        try
        {
            parsed = parse(document.Content);
        }
        catch (InvalidDataException e)
        {
            logger.LogError(e, "{Name} from {Server} is not valid", name, server);
            return FeedResult<T>.Failure(UpdateError.BadData);
        }

        var accepted = accept(parsed, trust);
        if (accepted.Succeeded && trust == FeedTrust.Unsigned)
        {
            UnsignedAccepted?.Invoke(this, EventArgs.Empty);
        }

        return accepted;
    }

    // Why a document without a signature can't be accepted, or null when it can.
    private UpdateError? RefuseUnsigned(Uri server, string name)
    {
        if (!options.AllowUnsignedFeed)
        {
            logger.LogError("{Name} from {Server} has no signature", name, server);
            return UpdateError.FeedUntrusted;
        }

        if (ServerEndpoint.Normalize(server) != endpoint.Default)
        {
            logger.LogError("Unsigned feeds are only accepted from the default server, not {Server}; refusing {Name}", server, name);
            return UpdateError.UnsignedFeedNotDefaultServer;
        }

        if (!TransportPolicy.IsSecure(server))
        {
            logger.LogError("Unsigned feeds are only accepted over https (or from this machine); refusing {Name} from {Server}", name, server);
            return UpdateError.UnsignedFeedInsecure;
        }

        return null;
    }

    private FeedResult<FileList> AcceptFileList(Uri server, FileList list, FeedTrust trust)
    {
        if (feedState.GetLastFileListVersion(server) is { } last && list.Version < last)
        {
            logger.LogError(
                "{Name} from {Server} has version {Version}, older than {Last} seen before; refusing a rollback",
                FeedLayout.FileListPath, server, list.Version, last);
            return FeedResult<FileList>.Failure(UpdateError.FeedUntrusted);
        }

        feedState.SetLastFileListVersion(server, list.Version);
        return FeedResult<FileList>.Success(list, trust);
    }

    // A signed list with a bad entry means a Publisher bug. Skip the entry, keep the rest, and say so loudly.
    private FileList ParseFileList(byte[] bytes)
    {
        var list = FileList.Parse(bytes, problem => logger.LogError("{Name} {Problem}; skipping it", FeedLayout.FileListPath, problem));

        var files = list.Files.Where(f => IsContained(f.Name)).ToList();
        var removed = list.Removed.Where(r => IsContained(r.Name)).ToList();
        return files.Count == list.Files.Count && removed.Count == list.Removed.Count
            ? list
            : list with { Files = files, Removed = removed };
    }

    // Same for the manifest: a bad entry (a stale package, or a role this launcher doesn't know) is skipped, the rest kept.
    private PackageManifest ParseManifest(byte[] bytes) =>
        PackageManifest.Parse(bytes, problem => logger.LogError("{Name} {Problem}; skipping it", FeedLayout.ManifestPath, problem));

    private bool IsContained(string name)
    {
        if (PathContainment.TryResolve(ContainmentRoot, name, out _))
        {
            return true;
        }

        logger.LogError("{Name} has '{Entry}', which resolves outside the install folder; skipping it", FeedLayout.FileListPath, name);
        return false;
    }
}
