namespace OpenShardLauncher.Core.Abstractions;

// Hands off to the new launcher: starts the staged exe as the applier (--apply-update) so this process can exit.
public interface ISelfUpdater
{
    // stagingFolder holds the extracted, verified package. Returns true once the applier is running and this process
    // should shut down; false when nothing was started and the launcher carries on with the old version.
    Task<bool> HandOffAsync(string stagingFolder, string newVersion, CancellationToken cancellationToken);
}
