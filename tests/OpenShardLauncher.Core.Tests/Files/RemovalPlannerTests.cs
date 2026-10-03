using OpenShardLauncher.Core.Files;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Tests.Files;

public sealed class RemovalPlannerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    [Fact]
    public void Deletes_only_names_that_are_safe_to_delete()
    {
        var list = new FileList(
            Version: 2,
            Files: [new FileEntry("maps/map0.mul", new string('a', 64), 1)],
            Removed:
            [
                Removed("old/art.mul"), // deleted
                Removed("Client.cfg"), // keep-local
                Removed("Music/theme.mp3"), // in an ignored folder
                Removed("notes.txt"), // ignored by name
                Removed(".openshardignore"), // reserved
                Removed(".openshardlauncher-cache/hashes.json"), // reserved
                Removed("x/../.OpenShardLauncher-Cache/downloads/blob"), // reserved after resolving
                Removed("../outside.mul"), // outside the install folder
                Removed("maps/map0.mul"), // still published
            ]);
        var planner = new RemovalPlanner(new KeepLocalRules(["*.cfg"]));

        var planned = planner.Plan(list, new InstallFolder(_temp.Path), IgnoreRules.Parse("Music/\nnotes.txt"));

        var removal = Assert.Single(planned);
        Assert.Equal("old/art.mul", removal.Name);
        Assert.Equal(_temp.Combine("old", "art.mul"), removal.FullPath);
    }

    public void Dispose() => _temp.Dispose();

    private static RemovedEntry Removed(string name) => new(name, RemovedIn: 1);
}
