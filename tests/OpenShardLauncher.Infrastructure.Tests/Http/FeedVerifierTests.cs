using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Http;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Infrastructure.Tests.Http;

// Over bytes: each test hands the verifier what the server "returned" for each fetch.
public sealed class FeedVerifierTests : IDisposable
{
    private static readonly Uri DefaultServer = new("https://updates.example.com/");
    private static readonly FileEntry Art = new("art.mul", new string('a', 64), 6);

    private readonly DirectoryInfo _dataFolder = Directory.CreateTempSubdirectory("osl-verifier-");
    private readonly P256SigningKey _key = P256SigningKey.Generate();
    private readonly P256SigningKey _unknownKey = P256SigningKey.Generate();
    private int _fetches;

    public void Dispose()
    {
        _key.Dispose();
        _unknownKey.Dispose();
        _dataFolder.Delete(recursive: true);
    }

    [Fact]
    public async Task SignedByTrustedKey_IsAccepted()
    {
        var result = await VerifyAsync(Verifier(), DefaultServer, Signed(List(100), _key));

        Assert.True(result.Succeeded);
        Assert.Equal(FeedTrust.Signed, result.Trust);
        Assert.Equal(100, result.Document!.Version);
    }

    [Fact]
    public async Task Mismatch_FixedByTheRefetch_IsAccepted()
    {
        var stale = new FeedDocument(List(101).ToJsonBytes(), Signed(List(100), _key).Signature);

        var result = await VerifyAsync(Verifier(), DefaultServer, stale, Signed(List(101), _key));

        Assert.True(result.Succeeded);
        Assert.Equal(101, result.Document!.Version);
        Assert.Equal(2, _fetches);
    }

    [Fact]
    public async Task Mismatch_StillThereAfterTheRefetch_IsFeedUpdating()
    {
        var unknown = Signed(List(100), _unknownKey);

        var result = await VerifyAsync(Verifier(), DefaultServer, unknown, unknown);

        Assert.Equal(UpdateError.FeedUpdating, result.Error);
        Assert.Equal(2, _fetches);
    }

    [Fact]
    public async Task Missing_WithUnsignedModeOnTheDefaultHttpsServer_IsAcceptedAsUnsigned()
    {
        var result = await VerifyAsync(Verifier(allowUnsigned: true), DefaultServer, Unsigned(List(100)));

        Assert.True(result.Succeeded);
        Assert.Equal(FeedTrust.Unsigned, result.Trust);
    }

    [Fact]
    public async Task UnsignedAccepted_IsRaisedOnlyForAnAcceptedUnsignedDocument()
    {
        var verifier = Verifier(allowUnsigned: true);
        var accepted = 0;
        verifier.UnsignedAccepted += (_, _) => accepted++;

        await VerifyAsync(verifier, DefaultServer, Signed(List(100), _key));
        Assert.Equal(0, accepted);

        await VerifyAsync(verifier, DefaultServer, Unsigned(List(200)));
        Assert.Equal(1, accepted);

        await VerifyAsync(verifier, DefaultServer, Unsigned(List(150))); // A rollback is refused
        Assert.Equal(1, accepted);
    }

    // The refusal says why, so the window can explain it.
    [Theory]
    [InlineData("https://updates.example.com/", "https://mirror.example.com/", true, UpdateError.UnsignedFeedNotDefaultServer)] // overridden server
    [InlineData("http://updates.example.com/", "http://updates.example.com/", true, UpdateError.UnsignedFeedInsecure)] // plain http
    [InlineData("https://updates.example.com/", "https://updates.example.com/", false, UpdateError.FeedUntrusted)] // unsigned mode off
    public async Task Missing_OtherwiseIsRefused(string updateUrl, string server, bool allowUnsigned, UpdateError expected)
    {
        var verifier = Verifier(allowUnsigned, updateUrl: updateUrl);
        var accepted = 0;
        verifier.UnsignedAccepted += (_, _) => accepted++;

        var result = await VerifyAsync(verifier, new Uri(server), Unsigned(List(100)));

        Assert.Equal(expected, result.Error);
        Assert.Equal(0, accepted);
    }

    [Fact]
    public async Task Invalid_IsRejectedEvenInUnsignedMode()
    {
        var unknown = Signed(List(100), _unknownKey);

        var result = await VerifyAsync(Verifier(allowUnsigned: true), DefaultServer, unknown, unknown);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Signature_WithNoKeysInUnsignedMode_IsUntrusted()
    {
        var result = await VerifyAsync(Verifier(allowUnsigned: true, keys: []), DefaultServer, Signed(List(100), _key));

        Assert.Equal(UpdateError.FeedUntrusted, result.Error);
    }

    [Fact]
    public async Task NoKeysAndNoUnsignedMode_IsUntrustedWithoutFetching()
    {
        var result = await VerifyAsync(Verifier(keys: []), DefaultServer, Unsigned(List(100)));

        Assert.Equal(UpdateError.FeedUntrusted, result.Error);
        Assert.Equal(0, _fetches);
    }

    [Fact]
    public async Task LowerVersionThanSeenBefore_IsRejected()
    {
        var verifier = Verifier();
        Assert.True((await VerifyAsync(verifier, DefaultServer, Signed(List(200), _key))).Succeeded);

        var older = await VerifyAsync(verifier, DefaultServer, Signed(List(100), _key));
        var same = await VerifyAsync(verifier, DefaultServer, Signed(List(200), _key));

        Assert.Equal(UpdateError.FeedUntrusted, older.Error);
        Assert.True(same.Succeeded);
    }

    [Fact]
    public async Task MalformedAndEscapingEntries_AreSkipped()
    {
        var list = List(100,
            Art with { Name = "../outside.dll" },
            Art with { Name = "bad-hash.mul", Sha256 = "XYZ" },
            Art with { Name = "ART.MUL" }); // repeats art.mul

        var result = await VerifyAsync(Verifier(), DefaultServer, Signed(list, _key));

        Assert.Equal(["art.mul"], result.Document!.Files.Select(f => f.Name));
    }

    private FeedVerifier Verifier(bool allowUnsigned = false, IReadOnlyList<TrustedKey>? keys = null, string? updateUrl = null)
    {
        var options = new LauncherOptions
        {
            UpdateUrl = updateUrl ?? DefaultServer.AbsoluteUri,
            TrustedPublicKeys = keys ?? [_key.PublicKey],
            AllowUnsignedFeed = allowUnsigned,
        };
        var dataFolder = LauncherDataFolder.Resolve(_dataFolder.FullName, "unused", NullLogger.Instance);
        return new FeedVerifier(
            options,
            new ServerEndpoint(options, serverUrlOverride: null),
            new FeedStateStore(dataFolder, NullLogger<FeedStateStore>.Instance),
            NullLogger<FeedVerifier>.Instance);
    }

    // Returns the responses in order, repeating the last one.
    private Task<FeedResult<FileList>> VerifyAsync(FeedVerifier verifier, Uri server, params FeedDocument[] responses) =>
        verifier.VerifyFileListAsync(
            server,
            _ => Task.FromResult(responses[Math.Min(_fetches++, responses.Length - 1)]),
            CancellationToken.None);

    // Art first, then any extra entries (written as is, even when invalid)
    private static FileList List(long version, params FileEntry[] extra) => new(version, [Art, .. extra], []);

    private static FeedDocument Signed(FileList list, IFeedSigner key)
    {
        var bytes = list.ToJsonBytes();
        return new FeedDocument(bytes, FeedSigning.CreateSignatureFile(bytes, [key]));
    }

    private static FeedDocument Unsigned(FileList list) => new(list.ToJsonBytes(), null);
}
