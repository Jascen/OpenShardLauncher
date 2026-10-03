using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenShardLauncher.Core.Model;
using Polly.Registry;

namespace OpenShardLauncher.Infrastructure.Http;

// What to download and where. The partial file must be on the destination's volume so the final move is atomic, and
// is named after the hash, so a resumed part always belongs to the same content.
public sealed record DownloadRequest(
    Uri Source,
    string Sha256,
    long Size,
    string PartialPath,
    string DestinationPath,
    string Label); // The name the player knows it by (file name or package), for errors and logs

// Downloads to a partial file, resumes it with a Range request after an interruption, checks size and SHA-256 and
// only then moves it into place. Each attempt runs through a retry pipeline (RetryPipelines); errors retrying can't
// fix (a locked destination, a full disk, a missing blob) end the download straight away.
//
// Server bandwidth may be scarce, so received bytes are only thrown away when they are known to be wrong:
// - the response headers are checked before the body is read: when the server's copy has a different size than the
//   list says (usually an upload still in progress), nothing is downloaded, the part is kept and the download ends
//   with FeedUpdating; it resumes once the server has the whole file, and the hash check then decides
// - after a hash mismatch the file is downloaded again once; a second mismatch means the server's copy is wrong, and
//   downloading it again can't help
public sealed class ResumableDownloader(
    HttpClient http,
    ResiliencePipelineProvider<string> pipelines,
    ServerBackoff backoff,
    IOptions<DownloadOptions> options,
    ILogger<ResumableDownloader> logger)
{
    public const int MaxHashMismatchRetries = 1;

    private const int BufferSize = 1 << 16;

    // Reports the bytes of this file on disk so far (including a resumed part). Throws UpdateException or
    // OperationCanceledException; a download that still fails after the retries is UpdateException(FileFailed).
    public async Task DownloadAsync(DownloadRequest request, string pipelineKey, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var pipeline = pipelines.GetPipeline(pipelineKey);
        var state = new Attempts();
        try
        {
            await pipeline.ExecuteAsync(
                async token => await AttemptAsync(request, state, progress, token).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (RetryPipelines.IsTransient(e))
        {
            logger.LogError(e, "Giving up on {File} after the retries", request.Label);
            throw new UpdateException(UpdateError.FileFailed, request.Label, e);
        }
    }

    private async Task AttemptAsync(DownloadRequest request, Attempts state, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        await backoff.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DownloadPartialAsync(request, state, progress, cancellationToken).ConfigureAwait(false);
            MoveIntoPlace(request);
            progress?.Report(request.Size);
        }
        catch (IOException e) when (IoErrors.IsDiskFull(e))
        {
            throw new UpdateException(UpdateError.DiskFull, request.Label, e);
        }
    }

    private async Task DownloadPartialAsync(DownloadRequest request, Attempts state, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.PartialPath)!);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new UpdateException(UpdateError.InstallFolderNotWritable, request.Label, e);
        }

        await using var partial = OpenPartial(request);
        if (partial.Length > request.Size)
        {
            partial.SetLength(0); // Longer than the content can be, so it isn't this content
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (partial.Length < request.Size || request.Size == 0)
        {
            await ReceiveAsync(request, partial, hash, progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogDebug("{File} is already fully downloaded; checking it", request.Label);
            await HashAsync(partial, partial.Length, hash, cancellationToken).ConfigureAwait(false);
        }

        if (partial.Length != request.Size)
        {
            // The body ended early without an error. Keep what arrived and resume on the next attempt.
            throw new IOException($"{request.Label}: received {partial.Length} of {request.Size} bytes.");
        }

        if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), request.Sha256, StringComparison.Ordinal))
        {
            // Which bytes are wrong can't be known, so all of them go. Once is worth it (a damaged part on disk);
            // a fresh download that mismatches again means the server's copy is wrong.
            partial.SetLength(0);
            var mismatch = new InvalidDataException($"{request.Label}: the downloaded content doesn't match its SHA-256.");
            if (++state.HashMismatches > MaxHashMismatchRetries)
            {
                logger.LogError("{File} doesn't match its SHA-256 again; the server's copy is wrong", request.Label);
                throw new UpdateException(UpdateError.FileFailed, request.Label, mismatch);
            }

            logger.LogWarning("{File} doesn't match its SHA-256; downloading it once more", request.Label);
            throw mismatch;
        }
    }

    private async Task ReceiveAsync(
        DownloadRequest request, FileStream partial, IncrementalHash hash, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var resumeFrom = partial.Length;
        using var message = new HttpRequestMessage(HttpMethod.Get, request.Source);
        if (resumeFrom > 0)
        {
            message.Headers.Range = new RangeHeaderValue(resumeFrom, null);
        }

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var contentRange = response.Content.Headers.ContentRange;
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                // The list names content the server doesn't have (yet): an upload in progress.
                throw new UpdateException(UpdateError.FeedUpdating, request.Label);

            case HttpStatusCode.RequestedRangeNotSatisfiable:
                // The server's copy is no longer than our part, which is shorter than the list says.
                throw ServerCopyDiffers(request, contentRange?.Length);

            case HttpStatusCode.PartialContent when contentRange?.Length is { } total && total != request.Size:
                throw ServerCopyDiffers(request, total);

            case HttpStatusCode.PartialContent when contentRange?.From == resumeFrom:
                logger.LogInformation("Resuming {File} at byte {Offset}", request.Label, resumeFrom);
                await HashAsync(partial, resumeFrom, hash, cancellationToken).ConfigureAwait(false);
                break;

            case HttpStatusCode.PartialContent:
                // A broken server; asking again won't fix it, and the part is still good.
                throw new UpdateException(UpdateError.FileFailed, request.Label, new HttpRequestException(
                    $"The server answered a request for byte {resumeFrom} on with '{contentRange}'."));

            default:
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != request.Size)
                {
                    throw ServerCopyDiffers(request, length);
                }

                if (resumeFrom > 0)
                {
                    logger.LogWarning("The server doesn't support resuming; downloading {File} from the start", request.Label);
                }

                partial.SetLength(0);
                break;
        }

        partial.Position = partial.Length;
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await CopyAsync(request, body, partial, hash, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task CopyAsync(
        DownloadRequest request, Stream body, FileStream partial, IncrementalHash hash, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var stallTimeout = options.Value.StallTimeout;
        var start = partial.Length;
        var buffer = new byte[BufferSize];
        progress?.Report(partial.Length);

        while (true)
        {
            var read = await StallGuard.ReadAsync(body, buffer, stallTimeout, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            if (partial.Length + read > request.Size)
            {
                // Only possible without a Content-Length. Drop this response's bytes, keep the part from before.
                partial.SetLength(start);
                throw ServerCopyDiffers(request, serverSize: null);
            }

            await partial.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            progress?.Report(partial.Length);
        }
    }

    // The server's copy has another size than files.json says, almost always because it is still being uploaded.
    // Not retried: the part is kept, and the next check resumes it once the upload has finished.
    private UpdateException ServerCopyDiffers(DownloadRequest request, long? serverSize)
    {
        logger.LogWarning(
            "The server's copy of {File} is {ServerSize} bytes, not {Size}; it is probably still being uploaded",
            request.Label, serverSize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "more than", request.Size);
        return new UpdateException(UpdateError.FeedUpdating, request.Label);
    }

    private static async Task HashAsync(FileStream partial, long length, IncrementalHash hash, CancellationToken cancellationToken)
    {
        partial.Position = 0;
        var buffer = new byte[BufferSize];
        for (var remaining = length; remaining > 0;)
        {
            var read = await partial.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static FileStream OpenPartial(DownloadRequest request)
    {
        try
        {
            return new FileStream(request.PartialPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, BufferSize, useAsync: true);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new UpdateException(UpdateError.InstallFolderNotWritable, request.Label, e);
        }
    }

    // A file the game has open can't be replaced: report it as locked rather than retrying. The part stays, so a later
    // Retry only has to check it and move it.
    private void MoveIntoPlace(DownloadRequest request)
    {
        var destination = request.DestinationPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var existing = new FileInfo(destination);
            if (existing.Exists && existing.IsReadOnly)
            {
                existing.IsReadOnly = false;
            }

            File.Move(request.PartialPath, destination, overwrite: true);
        }
        catch (IOException e) when (IoErrors.IsLocked(e))
        {
            logger.LogWarning("{File} is in use by another program", request.Label);
            throw new UpdateException(UpdateError.FileLocked, request.Label, e);
        }
        catch (UnauthorizedAccessException e) when (File.Exists(destination))
        {
            // Windows refuses to replace a running program, or a file opened without delete sharing, this way.
            logger.LogWarning("{File} is in use by another program", request.Label);
            throw new UpdateException(UpdateError.FileLocked, request.Label, e);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new UpdateException(UpdateError.InstallFolderNotWritable, request.Label, e);
        }
    }

    // Shared by the attempts of one download.
    private sealed class Attempts
    {
        public int HashMismatches { get; set; }
    }
}
