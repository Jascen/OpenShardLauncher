using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Client.Tests.Presentation;

public sealed class LauncherStateMachineTests
{
    [Fact]
    public void A_cancelled_download_leaves_updates_ready()
    {
        var next = LauncherStateMachine.Completed(UpdateOutcome.Cancelled, wasDownload: true);

        Assert.Equal(LauncherState.UpdatesReady, next.State);
    }

    [Fact]
    public void A_cancelled_check_returns_to_idle()
    {
        var next = LauncherStateMachine.Completed(UpdateOutcome.Cancelled, wasDownload: false);

        Assert.Equal(LauncherState.Idle, next.State);
    }

    [Fact]
    public void A_removal_only_publish_offers_updates()
    {
        var outcome = new UpdateOutcome { Result = UpdateResult.UpdatesReady, PendingDownloads = 0, PendingRemovals = 2 };

        var next = LauncherStateMachine.Completed(outcome, wasDownload: false);

        Assert.Equal(LauncherState.UpdatesReady, next.State);
        Assert.Equal(StringKeys.RemovalsReady, next.Text.Key);
    }
}
