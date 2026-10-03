namespace OpenShardLauncher.Core.Model;

// The player's choices, saved as settings.json in the launcher data folder. Property names are a stable contract.
public sealed record UserSettings
{
    // Null means the default: LauncherOptions.DefaultInstallFolder next to the launcher exe.
    public string? InstallPath { get; init; }

    public bool VerifyOnLaunch { get; init; } = true;

    public bool WarnIfNotVerified { get; init; } = true;

    public bool AllowInsecureDownloads { get; init; }

    // Null unless the player entered a server different from LauncherOptions.UpdateUrl, so players who never change
    // it follow a new default shipped in a later launcher.
    public string? ServerUrlOverride { get; init; }
}
