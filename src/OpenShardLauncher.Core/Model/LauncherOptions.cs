using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Core.Model;

// Build-time configuration from the embedded launcher.json. Forks edit that file, never this one.
public sealed record LauncherOptions
{
    public string Title { get; init; } = "OpenShardLauncher";

    public string Subtitle { get; init; } = "";

    public string UpdateUrl { get; init; } = "http://127.0.0.1:8080/";

    public IReadOnlyList<TrustedKey> TrustedPublicKeys { get; init; } = [];

    // Accept a feed with no signature file, from the default server over https/loopback only.
    public bool AllowUnsignedFeed { get; init; }

    public IReadOnlyList<NavLink> Links { get; init; } = [];

    // Name patterns (e.g. "*.cfg") for files players change themselves: downloaded only when missing, never replaced
    // and never removed.
    public IReadOnlyList<string> KeepLocalPatterns { get; init; } = [];

    // Folder name under %AppData% (or the platform equivalent) when the launcher folder isn't writable.
    public string AppDataFolderName { get; init; } = "OpenShardLauncher";

    // Subfolder next to the launcher exe used as the install folder until the player picks another.
    public string DefaultInstallFolder { get; init; } = "Game";

    public TazUOOptions TazUO { get; init; } = new();

    public TimeSpan PackageCheckInterval { get; init; } = TimeSpan.FromHours(4);
}

public sealed record NavLink
{
    // A Url of "verify" re-checks every file instead of opening a page.
    public const string VerifyTarget = "verify";

    public string Text { get; init; } = "";

    public string Url { get; init; } = "";

    public bool IsVerify => string.Equals(Url, VerifyTarget, StringComparison.OrdinalIgnoreCase);
}

public sealed record TazUOOptions
{
    public bool Enabled { get; init; } = true;

    // Subfolder of the install folder the TazUO launcher is installed into.
    public string InstallFolder { get; init; } = "TazUO";

    public string ExecutableName { get; init; } = "TazUOLauncher";

    // Created in the TazUO launcher once it is installed, unless a profile with that Id already exists (players' own
    // changes in the TazUO launcher are kept).
    public IReadOnlyList<TazUOProfile> Profiles { get; init; } = [];
}

// A TazUO launcher profile for this shard. Id is also the file name TazUO stores it under, so keep it stable once
// released.
public sealed record TazUOProfile
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Ip { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 2593;

    public string ClientVersion { get; init; } = "";
}
