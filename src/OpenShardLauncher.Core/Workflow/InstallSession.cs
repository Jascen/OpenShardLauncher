using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Files;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Core.Workflow;

// Everything tied to one install folder and server: the verified file list and package check from the last check,
// the hash cache, the last outcome, and a cancellation token for all of its work. Changing the folder or the server
// means disposing this session and opening a new one (UpdateWorkflow.OpenSession). Disposing cancels whatever is
// running and silences its progress, so a stale run can never update the window.
public sealed class InstallSession : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private readonly ILogger _logger;
    private int _running;
    private volatile bool _disposed;

    internal InstallSession(InstallFolder folder, Uri server, HashCache hashCache, ILogger logger)
    {
        Folder = folder;
        Server = server;
        HashCache = hashCache;
        Comparer = new FileComparer(folder, hashCache);
        _logger = logger;
        _token = _lifetime.Token;
    }

    public InstallFolder Folder { get; }

    // The server this session was opened for (ServerEndpoint.Current at the time).
    public Uri Server { get; }

    public bool IsDisposed => _disposed;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    // The outcome of the last check or download that ran to an end (including a cancel), null before the first.
    public UpdateOutcome? LastOutcome { get; private set; }

    // Cancelled when the session is disposed.
    public CancellationToken Token => _token;

    internal HashCache HashCache { get; }

    internal FileComparer Comparer { get; }

    // The list the last check verified, reused by a download that follows it.
    internal FeedResult<FileList>? FileList { get; set; }

    internal PackageUpdates? Packages { get; set; }

    // Like Progress<T>: the handler runs on the synchronization context of the caller (the UI thread), or inline when
    // there is none. It never runs once the session is disposed, even for a report already queued before that.
    public IProgress<T> CreateProgress<T>(Action<T> handler)
    {
        var context = SynchronizationContext.Current;
        return new SessionProgress<T>(this, context, handler);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.LogInformation("Closing the session for {Folder} on {Server}", Folder.Root, Server);
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    // Runs one use case: one at a time per session, cancelled by either token. A cancelled run returns
    // UpdateOutcome.Cancelled. The hash cache is saved whatever happens.
    internal async Task<UpdateOutcome> RunAsync(Func<CancellationToken, Task<UpdateOutcome>> run, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            throw new InvalidOperationException("A check or download is already running in this session.");
        }

        UpdateOutcome outcome;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_token, cancellationToken);
            outcome = await run(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _token.IsCancellationRequested)
        {
            _logger.LogInformation("Cancelled");
            outcome = UpdateOutcome.Cancelled;
        }
        finally
        {
            HashCache.Save();
            Volatile.Write(ref _running, 0);
        }

        if (!_disposed)
        {
            LastOutcome = outcome;
        }

        return outcome;
    }

    private sealed class SessionProgress<T>(InstallSession session, SynchronizationContext? context, Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            if (session._disposed)
            {
                return;
            }

            if (context is null)
            {
                handler(value);
                return;
            }

            // What Progress<T> does; Core has no JoinableTaskFactory, and posting never blocks.
#pragma warning disable VSTHRD001
            context.Post(
                _ =>
                {
                    if (!session._disposed)
                    {
                        handler(value);
                    }
                },
                null);
#pragma warning restore VSTHRD001
        }
    }
}
