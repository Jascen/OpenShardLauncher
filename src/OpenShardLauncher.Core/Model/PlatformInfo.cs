namespace OpenShardLauncher.Core.Model;

// This machine's runtime identifier (win-x64, osx-arm64, ...), which picks a package from the manifest. Null on a
// platform no package is built for. Infrastructure registers it from PlatformId.Current; Core can't detect it itself.
public sealed record PlatformInfo(string? Rid);
