using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Core.Model;

// Build-time configuration from the embedded launcher.json. Forks edit that file, never this one. A property left out of
// launcher.json keeps the default below.
// Properties are settable, not init: the JSON source generator assigns every init property, so one missing from the
// file would get false/0/null instead of the default below. With setters it assigns only what the file contains.
public sealed record LauncherOptions
{
    public string Title { get; set; } = "OpenShardLauncher";

    public string Subtitle { get; set; } = "";

    public string UpdateUrl { get; set; } = "http://127.0.0.1:8080/";

    public IReadOnlyList<TrustedKey> TrustedPublicKeys { get; set; } = [];

    // Accept a feed with no signature file, from the default server over https/loopback only.
    public bool AllowUnsignedFeed { get; set; }

    public IReadOnlyList<NavLink> Links { get; set; } = [];

    // Name patterns (e.g. "*.cfg") for files players change themselves: downloaded only when missing, never replaced
    // and never removed.
    public IReadOnlyList<string> KeepLocalPatterns { get; set; } = [];

    // Folder name under %AppData% (or the platform equivalent) when the launcher folder isn't writable.
    public string AppDataFolderName { get; set; } = "OpenShardLauncher";

    // Subfolder next to the launcher exe used as the install folder until the player picks another.
    public string DefaultInstallFolder { get; set; } = "Game";

    public ClientOptions Client { get; set; } = new();

    public TimeSpan PackageCheckInterval { get; set; } = TimeSpan.FromHours(4);
}

public sealed record NavLink
{
    // A Url of "verify" re-checks every file instead of opening a page.
    public const string VerifyTarget = "verify";

    public string Text { get; set; } = "";

    public string Url { get; set; } = "";

    public bool IsVerify => string.Equals(Url, VerifyTarget, StringComparison.OrdinalIgnoreCase);
}

// The game client Play starts: the TazUO launcher, or a pre-configured TazUO/ClassicUO exe. It comes from a `client`
// package in the feed, or is simply part of the game files.
public sealed record ClientOptions
{
    // Placeholders replaced in Arguments.
    public const string GameFolderPlaceholder = "{GameFolder}"; // The install folder (the game files)
    public const string ClientFolderPlaceholder = "{ClientFolder}"; // The folder below

    public bool Enabled { get; set; } = true;

    // Subfolder of the install folder the client is in. A `client` package is unpacked into it.
    public string InstallFolder { get; set; } = "TazUO";

    // In InstallFolder, without ".exe" (added on Windows).
    public string ExecutableName { get; set; } = "TazUOLauncher";

    // Passed to the executable, e.g. ["-uopath", "{GameFolder}", "-ip", "play.example.com", "-port", "2593"] for
    // ClassicUO. Empty: started without arguments.
    public IReadOnlyList<string> Arguments { get; set; } = [];

    // Only when the client is the TazUO launcher: created in it unless a profile with that Id already exists (players'
    // own changes in the TazUO launcher are kept).
    public IReadOnlyList<TazUOProfile> TazUOProfiles { get; set; } = [];
}

// A TazUO launcher profile for this shard. Id is also the file name TazUO stores it under, so keep it stable once
// released.
public sealed record TazUOProfile
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Ip { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 2593;

    public string ClientVersion { get; set; } = "";
}
