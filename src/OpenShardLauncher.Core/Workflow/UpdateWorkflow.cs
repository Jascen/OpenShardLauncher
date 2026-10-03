using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Files;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Workflow;

// The update use cases: Check, Download and Retry, each over one InstallSession, each ending in an immutable
// UpdateOutcome. Progress goes to the IProgress<T> the caller passes (no events).
//
// The file list is verified before anything else happens; an untrusted list ends the run with nothing compared or
// downloaded. Package problems only add warnings: game files update regardless.
public sealed class UpdateWorkflow(
    IUpdateServer server,
    ServerEndpoint endpoint,
    LauncherOptions options,
    CompareStage compare,
    DownloadStage download,
    PackageCheckService packages,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<UpdateWorkflow>();
    private readonly KeepLocalRules _keepLocal = new(options.KeepLocalPatterns);

    // A session for this install folder on the current server.
    public InstallSession OpenSession(string installPath)
    {
        var folder = new InstallFolder(installPath);
        var hashCache = HashCache.Load(folder, loggerFactory.CreateLogger<HashCache>());
        _logger.LogInformation("Opened a session for {Folder} on {Server}", folder.Root, endpoint.Current);
        return new InstallSession(folder, endpoint.Current, hashCache, loggerFactory.CreateLogger<InstallSession>());
    }

    // File list → filter → compare until the first difference → package check. UpdatesReady when files differ or
    // need removing, PackagesReady when only TazUO does, otherwise Finished (verified).
    public Task<UpdateOutcome> CheckAsync(
        InstallSession session, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default) =>
        session.RunAsync(token => CheckCoreAsync(session, Gate(progress, session), token), cancellationToken);

    // Reuses the list from the last check (or fetches it), re-reads the ignore list, applies packages, compares
    // everything, downloads what differs and deletes what the feed removed.
    public Task<UpdateOutcome> DownloadAsync(
        InstallSession session,
        IProgress<UpdateProgress>? progress = null,
        IProgress<FileProgress>? fileProgress = null,
        CancellationToken cancellationToken = default) =>
        session.RunAsync(token => DownloadCoreAsync(session, Gate(progress, session), Gate(fileProgress, session), token), cancellationToken);

    // A fresh check, then a download if it found anything to do.
    public Task<UpdateOutcome> RetryAsync(
        InstallSession session,
        IProgress<UpdateProgress>? progress = null,
        IProgress<FileProgress>? fileProgress = null,
        CancellationToken cancellationToken = default) =>
        session.RunAsync(
            async token =>
            {
                var overall = Gate(progress, session);
                var check = await CheckCoreAsync(session, overall, token).ConfigureAwait(false);
                return check.Result is UpdateResult.UpdatesReady or UpdateResult.PackagesReady
                    ? await DownloadCoreAsync(session, overall, Gate(fileProgress, session), token).ConfigureAwait(false)
                    : check;
            },
            cancellationToken);

    private async Task<UpdateOutcome> CheckCoreAsync(InstallSession session, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking {Folder} against {Server}", session.Folder.Root, session.Server);
        session.FileList = null;
        session.Packages = null;

        var list = await FetchFileListAsync(session, progress, cancellationToken).ConfigureAwait(false);
        if (!list.Succeeded)
        {
            return UpdateOutcome.Failure(list.Error!.Value);
        }

        var plan = Plan(session, list.Document!);
        var pendingRemovals = plan.Removals.Count(r => File.Exists(r.FullPath));
        var compared = await compare.RunAsync(plan.Filter.ToCompare, session.Comparer, stopAtFirstDifference: true, progress, cancellationToken)
            .ConfigureAwait(false);
        var packageUpdates = await packages.CheckAsync(session.Folder, cancellationToken).ConfigureAwait(false);
        session.FileList = list;
        session.Packages = packageUpdates;

        var result = compared.Differences.Count > 0 || pendingRemovals > 0 ? UpdateResult.UpdatesReady
            : packageUpdates.TazUO is not null ? UpdateResult.PackagesReady
            : UpdateResult.Finished;
        _logger.LogInformation(
            "Check finished: {Result} ({Differences} differing found, {Removals} to remove)",
            result, compared.Differences.Count, pendingRemovals);

        return new UpdateOutcome
        {
            Result = result,
            IgnoredItems = plan.Filter.IgnoredItems,
            PackageWarnings = packageUpdates.Warnings,
            PendingDownloads = compared.Differences.Count,
            PendingRemovals = pendingRemovals,
            FeedUnsigned = list.Trust == FeedTrust.Unsigned,
        };
    }

    private async Task<UpdateOutcome> DownloadCoreAsync(
        InstallSession session, IProgress<UpdateProgress>? progress, IProgress<FileProgress>? fileProgress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading into {Folder} from {Server}", session.Folder.Root, session.Server);
        var list = session.FileList ?? await FetchFileListAsync(session, progress, cancellationToken).ConfigureAwait(false);
        if (!list.Succeeded)
        {
            return UpdateOutcome.Failure(list.Error!.Value);
        }

        session.FileList = list;
        var plan = Plan(session, list.Document!);
        var packageUpdates = session.Packages ?? await packages.CheckAsync(session.Folder, cancellationToken).ConfigureAwait(false);
        session.Packages = packageUpdates;
        ApplyPackages(packageUpdates);

        var compared = await compare.RunAsync(plan.Filter.ToCompare, session.Comparer, stopAtFirstDifference: false, progress, cancellationToken)
            .ConfigureAwait(false);
        var downloaded = await download.RunAsync(
            [.. compared.Differences.Select(f => f.Entry)], session.Folder, session.HashCache, progress, fileProgress, cancellationToken)
            .ConfigureAwait(false);

        if (downloaded.StopError is { } error)
        {
            // Fetch the list again next time: the feed may have changed under it (FeedUpdating).
            session.FileList = null;
            session.Packages = null;
            _logger.LogWarning("Download stopped: {Error} ({File})", error, downloaded.StopFile);
            return UpdateOutcome.Failure(error, downloaded.StopFile) with
            {
                FailedFiles = downloaded.FailedFiles,
                IgnoredItems = plan.Filter.IgnoredItems,
                PackageWarnings = packageUpdates.Warnings,
                FeedUnsigned = list.Trust == FeedTrust.Unsigned,
            };
        }

        var notRemoved = RemoveFiles(plan.Removals, session, progress, cancellationToken);
        var failed = downloaded.FailedFiles.Concat(notRemoved).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _logger.LogInformation("Download finished; {Failed} files could not be updated", failed.Count);

        return new UpdateOutcome
        {
            Result = UpdateResult.Finished,
            FailedFiles = failed,
            IgnoredItems = plan.Filter.IgnoredItems,
            PackageWarnings = packageUpdates.Warnings,
            FeedUnsigned = list.Trust == FeedTrust.Unsigned,
        };
    }

    private async Task<FeedResult<FileList>> FetchFileListAsync(
        InstallSession session, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new UpdateProgress(UpdatePhase.FetchingFileList, 0, 0));
        if (endpoint.Current != session.Server)
        {
            // The owner disposes a session when the server changes; a stale one must not mix two servers' files.
            throw new InvalidOperationException($"The server changed to {endpoint.Current}; open a new session.");
        }

        var list = await server.GetFileListAsync(cancellationToken).ConfigureAwait(false);
        if (!list.Succeeded)
        {
            _logger.LogWarning("No usable file list from {Server}: {Error}", session.Server, list.Error);
        }

        return list;
    }

    // The ignore list is read from disk on every run, so edits apply straight away.
    private Plan Plan(InstallSession session, FileList list)
    {
        var ignoreRules = LoadIgnoreRules(session.Folder);
        var filter = new FileListFilter(_keepLocal).Apply(list.Files, session.Folder, ignoreRules);
        var removals = new RemovalPlanner(_keepLocal).Plan(list, session.Folder, ignoreRules);
        return new Plan(filter, removals);
    }

    private IgnoreRules LoadIgnoreRules(InstallFolder folder)
    {
        try
        {
            return folder.LoadIgnoreRules();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not read the ignore list {Path}; ignoring nothing", folder.IgnoreFilePath);
            return IgnoreRules.None;
        }
    }

    // Phase 7 plugs the TazUO installer in here (download the package, install it, record its version).
    private void ApplyPackages(PackageUpdates packageUpdates)
    {
        if (packageUpdates.TazUO is { } tazuo)
        {
            _logger.LogInformation("TazUO {Version} is available; installing packages isn't supported yet", tazuo.Version);
        }
    }

    // Deletes what the feed removed. A file that is already gone is fine; one that can't be deleted is reported like a
    // file that couldn't be downloaded.
    private List<FailedFile> RemoveFiles(
        IReadOnlyList<PlannedRemoval> removals, InstallSession session, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        var failed = new List<FailedFile>();
        for (var i = 0; i < removals.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new UpdateProgress(UpdatePhase.RemovingFiles, i, removals.Count));
            var removal = removals[i];
            try
            {
                var file = new FileInfo(removal.FullPath);
                if (file.Exists)
                {
                    if (file.IsReadOnly)
                    {
                        file.IsReadOnly = false;
                    }

                    file.Delete();
                    _logger.LogInformation("Removed {File} (no longer published)", removal.Name);
                }

                session.HashCache.Remove(removal.Name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(e, "Could not remove {File}; it is probably in use", removal.Name);
                failed.Add(new FailedFile(removal.Name, Locked: true));
            }
        }

        if (removals.Count > 0)
        {
            progress?.Report(new UpdateProgress(UpdatePhase.RemovingFiles, removals.Count, removals.Count));
        }

        return failed;
    }

    private static IProgress<T>? Gate<T>(IProgress<T>? progress, InstallSession session) =>
        ProgressGate<T>.Wrap(progress, session.Token);
}

internal sealed record Plan(FilterResult Filter, IReadOnlyList<PlannedRemoval> Removals);
