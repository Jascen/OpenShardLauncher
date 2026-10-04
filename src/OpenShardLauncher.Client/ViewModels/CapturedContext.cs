namespace OpenShardLauncher.Client.ViewModels;

// Runs a service event's handler on the thread that created the view model (the UI thread), like Progress<T> does.
// Services such as the self-update poller raise their events on background threads. Inline when there is no context
// (tests) or when already on it.
internal sealed class CapturedContext
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Post(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
        {
            action();
            return;
        }

        // What Progress<T> does; posting never blocks.
#pragma warning disable VSTHRD001
        _context.Post(_ => action(), null);
#pragma warning restore VSTHRD001
    }
}
