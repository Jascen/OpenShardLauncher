namespace OpenShardLauncher.Client.Services;

// The folder the launcher exe runs from. The default install folder and the portable data folder sit next to it, and
// the install folder may never be it or contain it.
public sealed record LauncherFolder(string Path);
