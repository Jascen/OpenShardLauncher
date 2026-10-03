using System.Runtime.InteropServices;

namespace OpenShardLauncher.Infrastructure.Platform;

// This machine's .NET runtime identifier (win-x64, linux-x64, osx-arm64, ...), which picks the package for this
// platform from the manifest. The values are a stable contract (docs/feed-format.md).
public static class PlatformId
{
    // Null on an operating system or architecture no package is built for.
    public static string? Current { get; } = For(
        OperatingSystem.IsWindows() ? "win"
        : OperatingSystem.IsMacOS() ? "osx"
        : OperatingSystem.IsLinux() ? "linux"
        : null,
        RuntimeInformation.ProcessArchitecture);

    internal static string? For(string? os, Architecture architecture)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => null,
        };

        return os is null || arch is null ? null : $"{os}-{arch}";
    }
}
