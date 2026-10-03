using OpenShardLauncher.Core.Files;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Tests.Files;

public sealed class FileListFilterTests : IDisposable
{
    private readonly TempFolder _temp = new();

    [Fact]
    public void Applies_ignore_and_keep_local_rules_and_drops_reserved_names()
    {
        FileEntry[] files =
        [
            File("art.mul"),
            File("Client.cfg"),
            File("Music/a.mp3"),
            File("Music/sub/b.mp3"),
            File("readme.txt"),
            File(".openshardignore"),
            File(".openshardlauncher-cache/hashes.json"),
            File("../escape.mul"),
        ];
        var filter = new FileListFilter(new KeepLocalRules(["*.cfg"]));

        var result = filter.Apply(files, new InstallFolder(_temp.Path), IgnoreRules.Parse("# comment\nmusic/\nREADME.txt"));

        Assert.Equal(["art.mul", "Client.cfg"], result.ToCompare.Select(f => f.Name));
        Assert.False(result.ToCompare[0].KeepLocal);
        Assert.True(result.ToCompare[1].KeepLocal);

        // An ignored folder is listed once, with a trailing '/'; reserved names aren't reported as ignored.
        Assert.Equal(["Music/", "readme.txt"], result.IgnoredItems);
    }

    public void Dispose() => _temp.Dispose();

    private static FileEntry File(string name) => new(name, new string('0', 64), 1);
}
