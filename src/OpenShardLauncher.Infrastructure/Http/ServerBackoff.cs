using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenShardLauncher.Infrastructure.Http;

// One pause shared by every request to the feed server. When the server says it is overloaded (429 or 503), all
// downloads wait (for its Retry-After, or BusyPause), not just the one that was told, so the download workers don't
// keep hitting a server that is short of bandwidth. Waiting happens before each attempt, outside the HttpClient, so a
// long pause doesn't count against the request timeout.
public sealed class ServerBackoff(TimeProvider time, IOptions<DownloadOptions> options, ILogger<ServerBackoff> logger)
{
    private long _resumeAtTicks; // UTC; 0 when not paused

    public void ReportBusy(HttpResponseMessage response)
    {
        var now = time.GetUtcNow();
        var pause = PauseFor(response.Headers.RetryAfter, now, options.Value);
        var resumeAt = (now + pause).UtcTicks;

        // Only ever extends the pause
        long current;
        do
        {
            current = Interlocked.Read(ref _resumeAtTicks);
            if (current >= resumeAt)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _resumeAtTicks, resumeAt, current) != current);

        logger.LogWarning(
            "The update server is busy ({Status}); pausing downloads for {Seconds:0}s",
            (int)response.StatusCode, pause.TotalSeconds);
    }

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        var remaining = new DateTimeOffset(Interlocked.Read(ref _resumeAtTicks), TimeSpan.Zero) - time.GetUtcNow();
        return remaining > TimeSpan.Zero ? Task.Delay(remaining, time, cancellationToken) : Task.CompletedTask;
    }

    // Retry-After as seconds or as a date, capped at MaxBusyPause; BusyPause when it is missing.
    internal static TimeSpan PauseFor(RetryConditionHeaderValue? retryAfter, DateTimeOffset now, DownloadOptions options)
    {
        var pause = retryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - now,
            _ => options.BusyPause,
        };

        return pause < TimeSpan.Zero ? TimeSpan.Zero
            : pause > options.MaxBusyPause ? options.MaxBusyPause
            : pause;
    }
}

// Tells ServerBackoff about every 429/503 the server sends, including on redirect hops. The response still goes back
// to the caller, which retries it like any other server error.
public sealed class ServerBusyHandler(ServerBackoff backoff) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            backoff.ReportBusy(response);
        }

        return response;
    }
}
