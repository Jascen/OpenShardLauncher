using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Workflow;

namespace OpenShardLauncher.Core.Files;

// Differences: the files that need downloading, sorted by name. Compared: how many files were looked at, which is fewer
// than the list when a check stopped at the first difference.
public sealed record CompareResult(IReadOnlyList<FileToCompare> Differences, int Compared);

// Compares the filtered list with the install folder, several files at a time. A check only needs to know whether
// anything differs, so it stops at the first difference; a download compares everything.
public sealed class CompareStage(WorkflowOptions options, TimeProvider time, ILogger<CompareStage> logger)
{
    public async Task<CompareResult> RunAsync(
        IReadOnlyList<FileToCompare> files,
        FileComparer comparer,
        bool stopAtFirstDifference,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var differences = new ConcurrentBag<FileToCompare>();
        var compared = 0;
        var throttle = new ReportThrottle(time, TransferTracker.ReportInterval);
        progress?.Report(new UpdateProgress(UpdatePhase.Comparing, 0, files.Count));

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.CompareParallelism),
            CancellationToken = stop.Token,
        };

        try
        {
            await Parallel.ForEachAsync(files, parallel, async (file, token) =>
            {
                // ForEachAsync can keep handing out items after the stop when comparisons finish synchronously (missing
                // files), so a check could compare the whole list. Items started after the stop are skipped here.
                if (stop.IsCancellationRequested)
                {
                    return;
                }

                if (!await IsUpToDateAsync(comparer, file, token).ConfigureAwait(false))
                {
                    differences.Add(file);
                    if (stopAtFirstDifference)
                    {
                        await stop.CancelAsync().ConfigureAwait(false);
                    }
                }

                var done = Interlocked.Increment(ref compared);
                if (throttle.IsDue())
                {
                    progress?.Report(new UpdateProgress(UpdatePhase.Comparing, done, files.Count));
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopAtFirstDifference && !cancellationToken.IsCancellationRequested && !differences.IsEmpty)
        {
            // Stopped at the first difference.
        }

        // The skip above can let the loop end normally after the caller cancelled; that is still a cancel.
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new UpdateProgress(UpdatePhase.Comparing, compared, files.Count));
        var sorted = differences.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        logger.LogInformation("Compared {Compared} of {Total} files; {Differences} differ", compared, files.Count, sorted.Count);
        return new CompareResult(sorted, compared);
    }

    // A file that can't be read (open in another program, no permission) counts as different: the download then
    // reports exactly what is wrong with it.
    private async Task<bool> IsUpToDateAsync(FileComparer comparer, FileToCompare file, CancellationToken cancellationToken)
    {
        try
        {
            return await comparer.IsUpToDateAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not read {File}; treating it as different", file.Name);
            return false;
        }
    }
}
