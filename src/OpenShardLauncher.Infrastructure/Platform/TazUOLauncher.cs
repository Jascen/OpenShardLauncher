using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Infrastructure.Platform;

// Starts the TazUO launcher installed in <install folder>/<TazUO.InstallFolder>. Started the original way: shell
// execute, its own folder as the working directory and no arguments (connection settings live in its profile files),
// so macOS hands it to `open`. Installing it is the TazUO installer's job (phase 7).
public sealed class TazUOLauncher(LauncherOptions options, ILogger<TazUOLauncher> logger) : IGameLauncher
{
    public bool IsInstalled(string installFolder) => options.TazUO.Enabled && File.Exists(ExecutablePath(installFolder));

    public void Start(string installFolder)
    {
        var executable = ExecutablePath(installFolder);
        logger.LogInformation("Starting {Executable}", executable);
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable),
        });
    }

    private string ExecutablePath(string installFolder) =>
        Path.Combine(installFolder, options.TazUO.InstallFolder, options.TazUO.ExecutableName + (OperatingSystem.IsWindows() ? ".exe" : ""));
}
