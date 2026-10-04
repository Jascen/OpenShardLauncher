using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Client.Services;

// Looks for a launcher update at startup, every PackageCheckInterval and whenever the server changes (another server
// may offer another version). Nothing runs when the launcher has no keys and no unsigned mode.
public sealed class LauncherUpdatePoller(
    LauncherSelfUpdateService selfUpdate,
    ServerEndpoint endpoint,
    LauncherOptions options,
    TimeProvider time,
    ILogger<LauncherUpdatePoller> logger) : BackgroundService
{
    private readonly SemaphoreSlim _serverChanged = new(0, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!selfUpdate.IsConfigured)
        {
            return;
        }

        endpoint.Changed += OnServerChanged;
        try
        {
            using var timer = options.PackageCheckInterval > TimeSpan.Zero ? new PeriodicTimer(options.PackageCheckInterval, time) : null;
            var tick = timer?.WaitForNextTickAsync(stoppingToken).AsTask() ?? Task.Delay(Timeout.Infinite, stoppingToken);
            var changed = _serverChanged.WaitAsync(stoppingToken);
            while (true)
            {
                await RefreshAsync(stoppingToken);

                // Wakes on the timer or a server change, whichever comes first; the other keeps waiting.
                var woke = await Task.WhenAny(tick, changed);
                await woke; // Throws once stopping
                if (woke == tick)
                {
                    tick = timer!.WaitForNextTickAsync(stoppingToken).AsTask();
                }
                else
                {
                    changed = _serverChanged.WaitAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            endpoint.Changed -= OnServerChanged;
        }
    }

    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            await selfUpdate.RefreshAsync(stoppingToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Checking for a launcher update failed");
        }
    }

    // At most one pending wake-up.
    private void OnServerChanged(object? sender, EventArgs e)
    {
        try
        {
            _serverChanged.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public override void Dispose()
    {
        _serverChanged.Dispose();
        base.Dispose();
    }
}
