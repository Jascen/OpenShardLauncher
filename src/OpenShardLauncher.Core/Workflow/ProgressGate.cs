namespace OpenShardLauncher.Core.Workflow;

// Passes reports on until the session it belongs to is disposed, so a run that is still winding down after its session
// was replaced never reaches the view model.
internal sealed class ProgressGate<T>(IProgress<T>? inner, CancellationToken sessionToken) : IProgress<T>
{
    public static IProgress<T>? Wrap(IProgress<T>? inner, CancellationToken sessionToken) =>
        inner is null ? null : new ProgressGate<T>(inner, sessionToken);

    public void Report(T value)
    {
        if (!sessionToken.IsCancellationRequested)
        {
            inner!.Report(value);
        }
    }
}

// At most one report per interval; the first is always due. Thread-safe.
internal sealed class ReportThrottle(TimeProvider time, TimeSpan interval)
{
    private long _last = long.MinValue;

    public bool IsDue()
    {
        var now = time.GetTimestamp();
        var last = Interlocked.Read(ref _last);
        return (last == long.MinValue || time.GetElapsedTime(last, now) >= interval)
            && Interlocked.CompareExchange(ref _last, now, last) == last;
    }
}
