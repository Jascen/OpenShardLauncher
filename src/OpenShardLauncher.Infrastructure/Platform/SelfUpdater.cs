using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Infrastructure.Platform;

// Hands off to the new version: starts the staged exe (.temp/staging/<exeName>) as the applier with
//   --apply-update <protocol> <pid> <appDir> <exeName>
// and the caller exits. The new version applies itself (LauncherSwap), so the running exe is never copied: on NTFS a
// copy would carry a browser's Mark of the Web along and could trigger SmartScreen mid-update. Files unpacked from the
// zip carry no mark. The arguments are a stable contract (docs/self-update.md): newer launchers must keep accepting
// every older protocol, because the old launcher starts the new version's exe.
public sealed class SelfUpdater(InstalledLauncher launcher, ILogger<SelfUpdater> logger) : ISelfUpdater
{
    public const string ApplyArgument = "--apply-update";
    public const int ProtocolVersion = 1;

    // <LauncherExeName>, plus .exe on Windows: the name every launcher package must contain.
    public static string ExeName { get; } =
        (Assembly.GetEntryAssembly()?.GetName().Name ?? "OpenShardLauncher") + (OperatingSystem.IsWindows() ? ".exe" : "");

    public Task<bool> HandOffAsync(string stagingFolder, string newVersion, CancellationToken cancellationToken)
    {
        // Run through `dotnet <dll>` (or a renamed exe): restarting "the launcher" afterwards would start something else.
        var processPath = Environment.ProcessPath;
        if (processPath is null
            || !string.Equals(Path.GetFileName(processPath), launcher.ExeName, StringComparison.OrdinalIgnoreCase)
            || !SamePath(Path.GetDirectoryName(processPath)!, launcher.Folder))
        {
            logger.LogError(
                "Launcher {Version} is staged, but this process ({Process}) isn't {Exe} in {Folder}, so it can't be replaced",
                newVersion, processPath, launcher.ExeName, launcher.Folder);
            return Task.FromResult(false);
        }

        var applier = Path.Combine(stagingFolder, launcher.ExeName);
        var start = new ProcessStartInfo(applier)
        {
            UseShellExecute = false,
            WorkingDirectory = stagingFolder,
        };
        foreach (var argument in Arguments(Environment.ProcessId, launcher.Folder, launcher.ExeName))
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            logger.LogInformation("Started the launcher {Version} applier {Applier}", newVersion, applier);
            return Task.FromResult(process is not null);
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            logger.LogError(e, "Could not start the launcher {Version} applier {Applier}", newVersion, applier);
            return Task.FromResult(false);
        }
    }

    // Protocol version first, then what it needs. appDir has no trailing separator.
    public static IReadOnlyList<string> Arguments(int pid, string appDir, string exeName) =>
    [
        ApplyArgument,
        ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir)),
        exeName,
    ];

    internal static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
