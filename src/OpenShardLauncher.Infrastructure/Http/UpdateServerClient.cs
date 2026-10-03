using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;
using Polly.Registry;

namespace OpenShardLauncher.Infrastructure.Http;

// IUpdateServer over HTTP. Every request is built from ServerEndpoint.Current at the time of the call (no fixed
// BaseAddress), so a new server address applies without a restart. Each operation reads Current once, so a document,
// its signature and the trust decision all refer to the same server.
public sealed class UpdateServerClient(
    HttpClient http,
    ServerEndpoint endpoint,
    FeedVerifier verifier,
    ResumableDownloader downloader,
    ResiliencePipelineProvider<string> pipelines,
    ServerBackoff backoff,
    IOptions<DownloadOptions> options,
    ILogger<UpdateServerClient> logger) : IUpdateServer
{
    public Task<FeedResult<FileList>> GetFileListAsync(CancellationToken cancellationToken)
    {
        var server = endpoint.Current;
        return GetDocumentAsync(
            server,
            FeedLayout.FileListPath,
            token => verifier.VerifyFileListAsync(server, t => FetchAsync(server, FeedLayout.FileListPath, FeedLayout.FileListSignaturePath, t), token),
            cancellationToken);
    }

    public Task<FeedResult<PackageManifest>> GetPackageManifestAsync(CancellationToken cancellationToken)
    {
        var server = endpoint.Current;
        return GetDocumentAsync(
            server,
            FeedLayout.ManifestPath,
            token => verifier.VerifyManifestAsync(server, t => FetchAsync(server, FeedLayout.ManifestPath, FeedLayout.ManifestSignaturePath, t), token),
            cancellationToken);
    }

    public Task DownloadBlobAsync(FileEntry file, InstallFolder folder, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // The verified list has already dropped such entries; this is the last line of defence before writing.
        if (!Sha256Hex.IsValid(file.Sha256) || !folder.TryGetPathFor(file.Name, out var destination))
        {
            logger.LogError("Refusing to write '{File}': invalid hash, outside the install folder or reserved", file.Name);
            throw new UpdateException(UpdateError.FileFailed, file.Name);
        }

        var request = new DownloadRequest(
            new Uri(endpoint.Current, FeedLayout.BlobPath(file.Sha256)),
            file.Sha256,
            file.Size,
            Path.Combine(folder.DownloadsFolder, file.Sha256 + ".part"),
            destination,
            file.Name);
        return downloader.DownloadAsync(request, RetryPipelines.Files, progress, cancellationToken);
    }

    public Task DownloadPackageAsync(PackageEntry package, string destinationPath, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(destinationPath);
        var request = new DownloadRequest(
            new Uri(endpoint.Current, FeedLayout.PackagePath(package.File)),
            package.Sha256,
            package.Size,
            destination + ".part",
            destination,
            package.File);
        return downloader.DownloadAsync(request, RetryPipelines.Packages, progress, cancellationToken);
    }

    private async Task<FeedResult<T>> GetDocumentAsync<T>(
        Uri server, string name, Func<CancellationToken, Task<FeedResult<T>>> verify, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await verify(cancellationToken).ConfigureAwait(false);
        }
        catch (DocumentNotFoundException)
        {
            logger.LogWarning("{Server} has no {Name}", server, name);
            return FeedResult<T>.Failure(UpdateError.NothingPublished);
        }
        catch (UpdateException e)
        {
            logger.LogError(e, "Could not get {Name} from {Server}", name, server);
            return FeedResult<T>.Failure(e.Error);
        }
        catch (Exception e) when (RetryPipelines.IsTransient(e))
        {
            logger.LogError(e, "Could not get {Name} from {Server}", name, server);
            return FeedResult<T>.Failure(UpdateError.ConnectionFailed);
        }
    }

    // The document and its signature, retried as a pair. A missing document is DocumentNotFoundException; a missing
    // signature is null.
    private async Task<FeedDocument> FetchAsync(Uri server, string path, string signaturePath, CancellationToken cancellationToken) =>
        await pipelines.GetPipeline(RetryPipelines.Packages).ExecuteAsync(
            async token =>
            {
                await backoff.WaitAsync(token).ConfigureAwait(false);
                var content = await GetBytesAsync(new Uri(server, path), token).ConfigureAwait(false)
                    ?? throw new DocumentNotFoundException();
                var signature = await GetBytesAsync(new Uri(server, signaturePath), token).ConfigureAwait(false);
                return new FeedDocument(content, signature is null ? null : Encoding.UTF8.GetString(signature));
            },
            cancellationToken).ConfigureAwait(false);

    // Null for a 404. Read with the stall timeout rather than a limit on the whole transfer, so a large files.json on
    // a slow server isn't abandoned and fetched again from the start.
    private async Task<byte[]?> GetBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var max = options.Value.MaxDocumentBytes;
        if (response.Content.Headers.ContentLength > max)
        {
            throw TooLarge(uri, max);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var content = new MemoryStream();
        var buffer = new byte[1 << 16];
        int read;
        while ((read = await StallGuard.ReadAsync(body, buffer, options.Value.StallTimeout, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (content.Length + read > max)
            {
                throw TooLarge(uri, max);
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return content.ToArray();
    }

    // Not retried: fetching it again would only waste the same bandwidth.
    private static UpdateException TooLarge(Uri uri, int max) =>
        new(UpdateError.BadData, inner: new InvalidDataException($"{uri} is larger than {max} bytes."));

    private sealed class DocumentNotFoundException : Exception;
}
