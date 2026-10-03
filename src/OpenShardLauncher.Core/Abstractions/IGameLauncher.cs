namespace OpenShardLauncher.Core.Abstractions;

// What Play needs: whether there is something to start in the install folder, and starting it (a process).
public interface IGameLauncher
{
    bool IsInstalled(string installFolder);

    // Throws when the process can't be started.
    void Start(string installFolder);
}
