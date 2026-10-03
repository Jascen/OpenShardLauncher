using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Infrastructure.Tests.Http;

public sealed class UpdateServerClientTests : IDisposable
{
    private readonly P256SigningKey _key = P256SigningKey.Generate();

    public void Dispose() => _key.Dispose();

    [Fact]
    public async Task NoFileList_IsNothingPublished()
    {
        await using var server = await TestFeedServer.StartAsync();
        using var launcher = new TestLauncher(server.Url, [_key.PublicKey]);

        var result = await launcher.Server.GetFileListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateError.NothingPublished, result.Error);
    }

    [Fact]
    public async Task ChangedServerAddress_AppliesToTheNextRequest()
    {
        await using var defaultServer = await TestFeedServer.StartAsync();
        await using var mirror = await TestFeedServer.StartAsync();
        defaultServer.PublishFileList(new FileList(100, [], []), _key);
        mirror.PublishFileList(new FileList(200, [], []), _key);
        using var launcher = new TestLauncher(defaultServer.Url, [_key.PublicKey]);

        var before = await launcher.Server.GetFileListAsync(TestContext.Current.CancellationToken);
        launcher.Endpoint.SetOverride(mirror.Url.AbsoluteUri);
        var after = await launcher.Server.GetFileListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(100, before.Document!.Version);
        Assert.Equal(200, after.Document!.Version);
    }

    [Fact]
    public async Task PlainHttpToAnotherMachine_IsRefusedUnlessAllowed()
    {
        // TEST-NET address: nothing is ever sent there
        using var launcher = new TestLauncher(new Uri("http://192.0.2.1/"), [_key.PublicKey]);

        var result = await launcher.Server.GetFileListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateError.InsecureServer, result.Error);
    }
}
