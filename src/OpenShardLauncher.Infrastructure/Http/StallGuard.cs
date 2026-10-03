namespace OpenShardLauncher.Infrastructure.Http;

// Reads a response body with a limit on how long a single read may wait, instead of a limit on the whole transfer,
// so a slow server never makes the launcher throw away what it has received.
internal static class StallGuard
{
    public static async ValueTask<int> ReadAsync(Stream body, Memory<byte> buffer, TimeSpan stallTimeout, CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(stallTimeout);
        try
        {
            return await body.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No data from the server for {stallTimeout.TotalSeconds:0}s.", e);
        }
    }
}
