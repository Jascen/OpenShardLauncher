using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OpenShardLauncher.Infrastructure.Http;

namespace OpenShardLauncher.Infrastructure.Tests.Http;

public sealed class ServerBackoffTests
{
    private static readonly DownloadOptions Options = new() { BusyPause = TimeSpan.FromSeconds(5), MaxBusyPause = TimeSpan.FromMinutes(2) };

    private readonly FakeTimeProvider _time = new();
    private readonly ServerBackoff _backoff;

    public ServerBackoffTests() =>
        _backoff = new ServerBackoff(_time, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<ServerBackoff>.Instance);

    [Fact]
    public void BusyAnswer_PausesEveryRequestUntilRetryAfter()
    {
        ReportBusy(TimeSpan.FromSeconds(30));
        var worker1 = _backoff.WaitAsync(TestContext.Current.CancellationToken);
        var worker2 = _backoff.WaitAsync(TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(worker1.IsCompleted);
        Assert.False(worker2.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(worker1.IsCompletedSuccessfully);
        Assert.True(worker2.IsCompletedSuccessfully);
        Assert.True(_backoff.WaitAsync(TestContext.Current.CancellationToken).IsCompleted);
    }

    [Fact]
    public void ShorterBusyAnswer_DoesNotShortenThePause()
    {
        ReportBusy(TimeSpan.FromSeconds(30));
        ReportBusy(TimeSpan.FromSeconds(5));
        var wait = _backoff.WaitAsync(TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(wait.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public void RetryAfter_AsADate_IsHonoured()
    {
        var now = _time.GetUtcNow();

        Assert.Equal(TimeSpan.FromSeconds(45), ServerBackoff.PauseFor(new RetryConditionHeaderValue(now.AddSeconds(45)), now, Options));
    }

    [Fact]
    public void RetryAfter_TooLong_IsCapped() =>
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            ServerBackoff.PauseFor(new RetryConditionHeaderValue(TimeSpan.FromHours(6)), _time.GetUtcNow(), Options));

    private void ReportBusy(TimeSpan retryAfter)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        _backoff.ReportBusy(response);
    }
}
