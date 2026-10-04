namespace OpenShardLauncher.Core.Model;

// The player's choices, saved as settings.json in the launcher data folder. Property names are a stable contract.
// Properties are settable, not init: the JSON source generator assigns every init property, so one missing from the
// file would get false/0/null instead of the default below. With setters it assigns only what the file contains.
public sealed record UserSettings
{
    // Null means the default: LauncherOptions.DefaultInstallFolder next to the launcher exe.
    public string? InstallPath { get; set; }

    public bool VerifyOnLaunch { get; set; } = true;

    public bool WarnIfNotVerified { get; set; } = true;

    public bool AllowInsecureDownloads { get; set; }

    // Null unless the player entered a server different from LauncherOptions.UpdateUrl, so players who never change
    // it follow a new default shipped in a later launcher.
    public string? ServerUrlOverride { get; set; }
}
