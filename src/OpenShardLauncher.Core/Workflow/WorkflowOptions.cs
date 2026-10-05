namespace OpenShardLauncher.Core.Workflow;

// How much work runs at once. The defaults are what players get.
public sealed class WorkflowOptions
{
    // Downloads running at the same time. Server bandwidth may be scarce, so this stays low.
    public int DownloadWorkers { get; set; } = 2;

    // Files compared (and hashed when not cached) at the same time.
    public int CompareParallelism { get; set; } = 4;

    // Caps for unpacking package zips (the client and the launcher's own update).
    public Packages.SafeZipLimits PackageLimits { get; set; } = Packages.SafeZipLimits.Default;
}
