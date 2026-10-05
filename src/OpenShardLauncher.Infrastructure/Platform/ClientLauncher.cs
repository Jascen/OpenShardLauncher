using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Infrastructure.Platform;

// Starts the game client in <install folder>/<Client.InstallFolder>, with its own folder as the working directory.
// Without Arguments it is started the original TazUO way: shell execute and no arguments (connection settings live in
// its profile files), so macOS hands it to `open`. With Arguments (e.g. a pre-configured ClassicUO) it is started
// directly with them. Installing it is ClientInstaller's job.
public sealed class ClientLauncher(LauncherOptions options, ILogger<ClientLauncher> logger) : IGameLauncher
{
    public bool IsInstalled(string installFolder) =>
        options.Client.Enabled && CreateStartInfo(installFolder, options.Client) is { } startInfo && File.Exists(startInfo.FileName);

    public void Start(string installFolder)
    {
        var startInfo = CreateStartInfo(installFolder, options.Client)
            ?? throw new InvalidOperationException($"Client.InstallFolder '{options.Client.InstallFolder}' isn't inside the install folder.");
        logger.LogInformation("Starting {Executable}", startInfo.FileName);
        using var process = Process.Start(startInfo);
    }

    // Null when Client.InstallFolder isn't inside the install folder.
    internal static ProcessStartInfo? CreateStartInfo(string installFolder, ClientOptions client)
    {
        var folder = new InstallFolder(installFolder);
        if (!folder.TryGetClientFolder(client, out var clientFolder))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(InstallFolder.ClientExecutablePath(clientFolder, client))
        {
            UseShellExecute = client.Arguments.Count == 0,
            WorkingDirectory = clientFolder,
        };
        foreach (var argument in client.Arguments)
        {
            startInfo.ArgumentList.Add(argument
                .Replace(ClientOptions.GameFolderPlaceholder, folder.Root, StringComparison.OrdinalIgnoreCase)
                .Replace(ClientOptions.ClientFolderPlaceholder, clientFolder, StringComparison.OrdinalIgnoreCase));
        }

        return startInfo;
    }
}
