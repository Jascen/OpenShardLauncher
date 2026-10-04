using OpenShardLauncher.Core.Files;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Workflow;

namespace OpenShardLauncher.Core.Packages;

// Turns the byte count of a package download into UpdateProgress for its phase, at most every ReportInterval.
internal sealed class PackageProgress(IProgress<UpdateProgress> progress, UpdatePhase phase, long size, TimeProvider time) : IProgress<long>
{
    private readonly ReportThrottle _throttle = new(time, TransferTracker.ReportInterval);

    public static PackageProgress? For(IProgress<UpdateProgress>? progress, UpdatePhase phase, long size, TimeProvider time) =>
        progress is null ? null : new PackageProgress(progress, phase, size, time);

    public void Report(long value)
    {
        if (value >= size || _throttle.IsDue())
        {
            progress.Report(new UpdateProgress(phase, 0, 1, value, size));
        }
    }
}
