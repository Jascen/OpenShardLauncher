using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Client.Presentation;

// Where the launcher is with the game files. Everything the window shows or enables about them follows from this.
public enum LauncherState
{
    Idle, // Nothing is known about the files: no check yet, verify on launch is off, or a check was cancelled
    Working, // A check, a download or a launcher update is running
    UpdatesReady, // Files (or removals, or the TazUO launcher) are waiting for the player to click Download
    Verified, // Every file was checked against the server and any updates downloaded
    Failed, // The last run failed or couldn't download some files, so Retry is offered
}

public sealed record LauncherTransition(LauncherState State, LocalizedText Text);

// Pure state transitions, so they can be tested without a view model.
public static class LauncherStateMachine
{
    public static LauncherTransition Started(bool isDownload) =>
        new(LauncherState.Working, new LocalizedText(isDownload ? StringKeys.StartingDownload : StringKeys.CheckingForUpdates));

    public static LauncherTransition Completed(UpdateOutcome outcome, bool wasDownload) => outcome.Result switch
    {
        UpdateResult.UpdatesReady => new(LauncherState.UpdatesReady, new LocalizedText(
            outcome.PendingDownloads == 0 && outcome.PendingRemovals > 0 ? StringKeys.RemovalsReady : StringKeys.UpdatesReady)),
        UpdateResult.PackagesReady => new(LauncherState.UpdatesReady, new LocalizedText(StringKeys.PackagesReady)),
        UpdateResult.Finished when outcome.FailedFiles.Count > 0 =>
            new(LauncherState.Failed, new LocalizedText(StringKeys.FinishedWithFailures, outcome.FailedFiles.Count)),
        UpdateResult.Finished => new(LauncherState.Verified, new LocalizedText(StringKeys.Finished)),
        UpdateResult.Failed => new(LauncherState.Failed,
            new LocalizedText(wasDownload ? StringKeys.DownloadFailed : StringKeys.CheckFailed)),

        // The check already found updates before the download started, so they're still waiting. A cancelled check
        // proves nothing either way.
        UpdateResult.Cancelled => new(wasDownload ? LauncherState.UpdatesReady : LauncherState.Idle,
            new LocalizedText(StringKeys.Cancelled)),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Result, null),
    };
}
