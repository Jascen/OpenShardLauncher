namespace OpenShardLauncher.Core.Model;

// The running launcher: its version (null for a non-numeric dev version, which is never offered an update), the
// folder its exe runs from and the exe's file name (<LauncherExeName>, plus .exe on Windows). The composition root
// registers it; Core can't detect it itself.
public sealed record InstalledLauncher(Version? Version, string Folder, string ExeName)
{
    // The working folder of a self-update next to the exe: the downloaded package and staging/, what it unpacks to.
    public const string TempFolderName = ".temp";

    public string TempFolder => Path.Combine(Folder, TempFolderName);

    public string StagingFolder => Path.Combine(TempFolder, "staging");
}
