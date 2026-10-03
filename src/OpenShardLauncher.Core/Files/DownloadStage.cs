using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Core.Files;

// FailedFiles: files that couldn't be written (the run carried on without them). StopError: why the run stopped
// starting new downloads (FeedUpdating, DiskFull, ...), with the file it happened on; null when every file was tried.
public sealed record DownloadStageResult(IReadOnlyList<FailedFile> FailedFiles, UpdateError? StopError, string? StopFile);

// Downloads the files that differ with a few workers reading from one queue. Each distinct hash is downloaded once;
// other names with the same content are copied from the first one locally. The same hash is never downloaded twice at
// once, because its partial file is named after the hash.
//
// IUpdateServer.DownloadBlobAsync already retries and only throws once retrying can't help, so nothing here retries:
// - a file that failed or is locked is recorded and the run carries on with the others
// - anything that affects every file (the feed is mid-upload, the disk is full, the folder isn't writable, the server
//   was refused) stops new downloads; running ones finish, and partial files are kept for the next attempt
public sealed class DownloadStage(IUpdateServer server, WorkflowOptions options, TimeProvider time, ILogger<DownloadStage> logger)
{
    public async Task<DownloadStageResult> RunAsync(
        IReadOnlyList<FileEntry> files,
        InstallFolder folder,
        HashCache hashCache,
        IProgress<UpdateProgress>? progress,
        IProgress<FileProgress>? fileProgress,
        CancellationToken cancellationToken)
    {
        var groups = files
            .GroupBy(f => f.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.ToList())
            .ToList();

        var queue = Channel.CreateUnbounded<List<FileEntry>>(new UnboundedChannelOptions { SingleWriter = true });
        foreach (var group in groups)
        {
            queue.Writer.TryWrite(group);
        }

        queue.Writer.Complete();

        // Bytes over the network: one copy of each hash.
        var run = new Run(folder, hashCache, new TransferTracker(files.Count, groups.Sum(g => g[0].Size), time), progress, fileProgress);
        logger.LogInformation("Downloading {Files} files ({Blobs} distinct)", files.Count, groups.Count);
        progress?.Report(run.Tracker.Snapshot());

        var workers = Enumerable.Range(0, Math.Max(1, options.DownloadWorkers))
            .Select(_ => WorkAsync(queue.Reader, run, cancellationToken))
            .ToList();
        await Task.WhenAll(workers).ConfigureAwait(false);

        progress?.Report(run.Tracker.Snapshot());
        var failed = run.Failed.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new DownloadStageResult(failed, run.StopError, run.StopFile);
    }

    private async Task WorkAsync(ChannelReader<List<FileEntry>> queue, Run run, CancellationToken cancellationToken)
    {
        while (!run.Stopped && queue.TryRead(out var group))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DownloadGroupAsync(group, run, cancellationToken).ConfigureAwait(false);
        }
    }

    // The names of one hash: the first is downloaded, the rest are copied from it. If the first can't be replaced (in
    // use), the next name is tried with the same download, which finds the finished partial file and only moves it.
    private async Task DownloadGroupAsync(List<FileEntry> group, Run run, CancellationToken cancellationToken)
    {
        string? source = null;
        for (var i = 0; i < group.Count; i++)
        {
            var file = group[i];
            try
            {
                if (source is null)
                {
                    await server.DownloadBlobAsync(file, run.Folder, new BlobProgress(file, run), cancellationToken).ConfigureAwait(false);
                    source = run.Folder.PathFor(file.Name);
                    Completed(file, file.Size, run);
                }
                else
                {
                    CopyLocally(source, file, run.Folder);
                    Completed(file, 0, run);
                }
            }
            catch (UpdateException e) when (e.Error == UpdateError.FileLocked)
            {
                Failed(file, locked: true, run);
            }
            catch (UpdateException e) when (e.Error == UpdateError.FileFailed && source is null)
            {
                // The server's copy is wrong or unreachable, so no other name of this content can be had either.
                foreach (var rest in group.Skip(i))
                {
                    Failed(rest, locked: false, run);
                }

                return;
            }
            catch (UpdateException e) when (e.Error == UpdateError.FileFailed)
            {
                Failed(file, locked: false, run);
            }
            catch (UpdateException e)
            {
                logger.LogWarning("Stopping downloads: {Error} on {File}", e.Error, file.Name);
                run.Stop(e.Error, e.FileName ?? file.Name);
                run.Tracker.OnFileFailed(file.Name);
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Unexpected error downloading {File}", file.Name);
                Failed(file, locked: false, run);
            }
        }
    }

    private static void Completed(FileEntry file, long downloadedBytes, Run run)
    {
        // Record the known hash, so the next check doesn't read the file again.
        var info = new FileInfo(run.Folder.PathFor(file.Name));
        run.HashCache.Set(file.Name, info.Length, info.LastWriteTimeUtc, file.Sha256.ToLowerInvariant());

        run.Tracker.OnFileCompleted(file.Name, downloadedBytes);
        run.Progress?.Report(run.Tracker.Snapshot());
        run.FileProgress?.Report(new FileProgress(file.Name, file.Size, file.Size));
    }

    private void Failed(FileEntry file, bool locked, Run run)
    {
        logger.LogWarning("{File} could not be updated{Reason}", file.Name, locked ? " (in use by another program)" : "");
        run.Failed.Add(new FailedFile(file.Name, locked));
        run.Tracker.OnFileFailed(file.Name);
        run.Progress?.Report(run.Tracker.Snapshot());
    }

    // Copies through a temp file in the downloads cache (same volume), then replaces the destination, so a reader
    // never sees half a file.
    private void CopyLocally(string source, FileEntry file, InstallFolder folder)
    {
        var destination = folder.PathFor(file.Name);
        Directory.CreateDirectory(folder.DownloadsFolder);
        var temp = Path.Combine(folder.DownloadsFolder, $"{file.Sha256}.{Guid.NewGuid():N}.copy");
        try
        {
            try
            {
                File.Copy(source, temp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new UpdateException(UpdateError.FileFailed, file.Name, e);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var existing = new FileInfo(destination);
                if (existing.Exists && existing.IsReadOnly)
                {
                    existing.IsReadOnly = false;
                }

                File.Move(temp, destination, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Replacing fails almost only because the file is open in another program.
                throw new UpdateException(UpdateError.FileLocked, file.Name, e);
            }

            logger.LogDebug("Copied {Source} to {File} (same content)", source, file.Name);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    // State shared by the workers of one run.
    private sealed class Run(
        InstallFolder folder,
        HashCache hashCache,
        TransferTracker tracker,
        IProgress<UpdateProgress>? progress,
        IProgress<FileProgress>? fileProgress)
    {
        private readonly Lock _lock = new();
        private volatile bool _stopped;

        public InstallFolder Folder { get; } = folder;

        public HashCache HashCache { get; } = hashCache;

        public TransferTracker Tracker { get; } = tracker;

        public IProgress<UpdateProgress>? Progress { get; } = progress;

        public IProgress<FileProgress>? FileProgress { get; } = fileProgress;

        public ConcurrentBag<FailedFile> Failed { get; } = [];

        public bool Stopped => _stopped;

        public UpdateError? StopError { get; private set; }

        public string? StopFile { get; private set; }

        // The first reason wins; later ones are usually consequences of it.
        public void Stop(UpdateError error, string file)
        {
            lock (_lock)
            {
                if (StopError is null)
                {
                    StopError = error;
                    StopFile = file;
                }

                _stopped = true;
            }
        }
    }

    // The download reports the bytes of the file on disk, including a resumed part. The chunk (for the speed) is the
    // difference from the last report; the first report is the starting point, so resumed bytes don't count as received.
    private sealed class BlobProgress(FileEntry file, Run run) : IProgress<long>
    {
        private long? _last;

        public void Report(long value)
        {
            var chunk = _last is { } last ? Math.Max(0, value - last) : 0;
            _last = value;
            if (run.Tracker.OnBytes(file.Name, value, chunk))
            {
                run.Progress?.Report(run.Tracker.Snapshot());
                run.FileProgress?.Report(new FileProgress(file.Name, value, file.Size));
            }
        }
    }
}
