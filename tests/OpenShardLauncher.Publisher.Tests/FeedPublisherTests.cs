using System.IO.Compression;
using System.Text;
using OpenShardLauncher.Publisher.Publishing;
using OpenShardLauncher.Publisher.Verification;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Publisher.Tests;

public sealed class FeedPublisherTests : IDisposable
{
    private readonly TestFeed _feed = new();

    public void Dispose() => _feed.Dispose();

    [Fact]
    public void PublishingTwice_WithOneChangedFile_WritesOneNewBlobAndAHigherVersion()
    {
        _feed.WriteSource("art.mul", "art v1");
        _feed.WriteSource("Data/map0.mul", "map");
        var first = _feed.Publish();
        var blobsBefore = _feed.BlobHashes();

        _feed.Clock.Advance(TimeSpan.FromMinutes(5));
        _feed.WriteSource("art.mul", "art version 2");
        var second = _feed.Publish();

        Assert.True(second.Version > first.Version);
        Assert.Equal(_feed.Clock.Now.ToUnixTimeSeconds(), second.Version);
        Assert.Equal(1, second.BlobsAdded);
        Assert.Equal(1, second.HashedCount); // map0.mul came from the hash cache
        Assert.Equal(["art.mul"], second.ChangedFiles);
        Assert.Single(_feed.BlobHashes().Except(blobsBefore));
        Assert.Equal(SignatureStatus.Valid, VerifySignature(FeedLayout.FileListPath, FeedLayout.FileListSignaturePath));
    }

    [Fact]
    public void ClockBehindTheExistingFeed_UsesExistingVersionPlusOne()
    {
        _feed.WriteSource("art.mul", "art");
        var first = _feed.Publish();

        _feed.Clock.Advance(TimeSpan.FromHours(-1));
        var second = _feed.Publish();

        Assert.Equal(first.Version + 1, second.Version);
    }

    [Fact]
    public void DeletingTheStateFolder_NeverLowersTheVersion()
    {
        _feed.WriteSource("art.mul", "art");
        var first = _feed.Publish();

        Directory.Delete(_feed.State, recursive: true);
        _feed.Clock.Advance(TimeSpan.FromDays(-30));
        var second = _feed.Publish();

        Assert.Equal(first.Version + 1, second.Version);
    }

    [Fact]
    public void DeletingTheStateFolder_ChangesNothingButRehashing()
    {
        _feed.WriteSource("art.mul", "art");
        _feed.WriteSource("Data/map0.mul", "map");
        _feed.Publish();
        var files = _feed.ReadFileList().Files;
        var blobs = _feed.BlobHashes();

        Directory.Delete(_feed.State, recursive: true);
        _feed.Clock.Advance(TimeSpan.FromMinutes(1));
        var again = _feed.Publish();

        Assert.Equal(2, again.HashedCount);
        Assert.Equal(0, again.BlobsAdded);
        Assert.Equal(0, again.BlobsPruned);
        Assert.Equal(files, _feed.ReadFileList().Files);
        Assert.Equal(blobs, _feed.BlobHashes());
    }

    [Fact]
    public void CollisionGuard_AbortsAndLeavesTheFeedUnchanged()
    {
        _feed.WriteSource("art.mul", "art");
        _feed.Publish();

        // Same size, different bytes under the same hash name
        File.WriteAllText(_feed.BlobOf("art.mul"), "ART");
        Directory.Delete(_feed.State, recursive: true); // Forces a re-hash, which compares the bytes
        _feed.WriteSource("new.mul", "new");
        var before = _feed.Snapshot();

        _feed.Clock.Advance(TimeSpan.FromMinutes(1));
        var refused = Assert.Throws<PublishException>(() => _feed.Publish());

        Assert.Contains("already exists with different content", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, _feed.Snapshot());
    }

    [Fact]
    public void Pruning_KeepsBlobsOfTheNewAndPreviousListsOnly()
    {
        _feed.WriteSource("art.mul", "art 1");
        _feed.Publish();
        var v1 = _feed.BlobOf("art.mul");

        _feed.Clock.Advance(TimeSpan.FromMinutes(1));
        _feed.WriteSource("art.mul", "art 22");
        _feed.Publish();
        var v2 = _feed.BlobOf("art.mul");

        _feed.Clock.Advance(TimeSpan.FromMinutes(1));
        _feed.WriteSource("art.mul", "art 333");
        var third = _feed.Publish();
        var v3 = _feed.BlobOf("art.mul");

        Assert.Equal(1, third.BlobsPruned);
        Assert.False(File.Exists(v1));
        Assert.True(File.Exists(v2)); // A launcher may still be downloading the previous list
        Assert.True(File.Exists(v3));
    }

    [Fact]
    public void RemovedList_RemovingAdds_ReaddingDrops_ForgetTrims()
    {
        _feed.WriteSource("art.mul", "art");
        _feed.WriteSource("old.mul", "old");
        _feed.WriteSource("older.mul", "older");
        _feed.Publish();

        _feed.Clock.Advance(TimeSpan.FromDays(1));
        _feed.DeleteSource("older.mul");
        var removedOlder = _feed.Publish();
        Assert.Equal([new RemovedEntry("older.mul", removedOlder.Version)], _feed.ReadFileList().Removed);
        Assert.Equal(["older.mul"], removedOlder.RemovedFiles);

        _feed.Clock.Advance(TimeSpan.FromDays(10));
        _feed.DeleteSource("old.mul");
        var removedOld = _feed.Publish();
        Assert.Equal(
            [new RemovedEntry("old.mul", removedOld.Version), new RemovedEntry("older.mul", removedOlder.Version)],
            _feed.ReadFileList().Removed);

        // Re-adding drops the entry; a name differing only by case counts as the same file
        _feed.Clock.Advance(TimeSpan.FromDays(1));
        _feed.WriteSource("OLD.mul", "back");
        _feed.Publish();
        Assert.Equal([new RemovedEntry("older.mul", removedOlder.Version)], _feed.ReadFileList().Removed);

        _feed.Clock.Advance(TimeSpan.FromDays(1));
        _feed.Publish(_feed.Options with { ForgetRemovedBefore = _feed.Clock.Now.AddDays(-5) });
        Assert.Empty(_feed.ReadFileList().Removed);
    }

    [Fact]
    public void RefusesOutInsideSource()
    {
        _feed.WriteSource("art.mul", "art");

        var refused = Assert.Throws<PublishException>(() =>
            _feed.Publish(_feed.Options with { Out = Path.Combine(_feed.Source, "feed"), State = Path.Combine(_feed.Root, "state") }));

        Assert.Contains("--out", refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_feed.Source, "feed")));
    }

    [Fact]
    public void RefusesAPrivateKeyInsideSource_WhateverItsName()
    {
        _feed.WriteSource("art.mul", "art");
        _feed.WriteSource("Backup/notes.txt", _feed.Key.ExportEncryptedPem("password"));

        var refused = Assert.Throws<PublishException>(() => _feed.Publish());

        Assert.Contains("Backup/notes.txt", refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_feed.Out));
    }

    [Fact]
    public void RefusesAKeyFileInsideSource()
    {
        _feed.WriteSource("art.mul", "art");

        Assert.Throws<PublishException>(() =>
            FeedPublisher.ResolvePaths(_feed.Options with { KeyFiles = [Path.Combine(_feed.Source, "feed-signing.key")] }));
    }

    [Fact]
    public void DefaultExclusionsAndPublishIgnore_AreApplied()
    {
        _feed.WriteSource("art.mul", "art");
        _feed.WriteSource("Data/appsettings.json", "{}");
        _feed.WriteSource("appsettings.Production.json", "{}");
        _feed.WriteSource(".git/config", "[core]");
        _feed.WriteSource(".env.local", "SECRET=1");
        _feed.WriteSource("cert.pem", "public certificate");
        _feed.WriteSource("Logs/today.log", "log");
        _feed.WriteSource("Saves/Accounts.xml", "<accounts/>");
        _feed.WriteSource(".publishignore", """
            *.log
            /Saves/
            !Data/appsettings.json
            """);

        _feed.Publish();

        Assert.Equal(["Data/appsettings.json", "art.mul"], _feed.ReadFileList().Files.Select(f => f.Name));
    }

    [Fact]
    public void Unsigned_WritesNoSignatures_AndRefusesKeys()
    {
        _feed.WriteSource("art.mul", "art");
        _feed.Publish();
        Assert.True(File.Exists(Path.Combine(_feed.Out, FeedLayout.FileListSignaturePath)));

        _feed.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Throws<PublishException>(() => _feed.Publish(_feed.Options with { Unsigned = true }));
        var unsigned = _feed.Publish(_feed.Options with { Unsigned = true }, signers: []);

        Assert.False(unsigned.Signed);
        Assert.False(File.Exists(Path.Combine(_feed.Out, FeedLayout.FileListSignaturePath)));
        Assert.False(File.Exists(Path.Combine(_feed.Out, FeedLayout.ManifestSignaturePath)));
        Assert.True(FeedVerifier.Verify(_feed.Out, []).IsValid);
    }

    [Fact]
    public void Packages_NewestPerRoleAndPlatformIsSignedAndCopied_AndANameIsPublishedOnce()
    {
        _feed.WriteSource("art.mul", "art");
        WriteZip("launcher-1.0.0.win-x64.zip", "old");
        WriteZip("launcher-1.1.0.win-x64.zip", "new");
        WriteZip("client-3.0.0.linux-x64.zip", "client");
        WriteZip("tazuo-3.0.0.linux-x64.zip", "not a role"); // Roles are only launcher and client
        File.WriteAllText(Path.Combine(_feed.Packages, "notes.zip"), "not a package");

        var summary = _feed.Publish();

        var manifestBytes = File.ReadAllBytes(Path.Combine(_feed.Out, FeedLayout.ManifestPath));
        var manifest = PackageManifest.Parse(manifestBytes);
        Assert.Equal(["client-3.0.0.linux-x64.zip", "launcher-1.1.0.win-x64.zip"], manifest.Packages.Select(p => p.File));
        Assert.Equal(["notes.zip", "tazuo-3.0.0.linux-x64.zip"], summary.IgnoredPackages.Order());
        Assert.Equal(SignatureStatus.Valid, VerifySignature(FeedLayout.ManifestPath, FeedLayout.ManifestSignaturePath));
        Assert.True(FeedVerifier.Verify(_feed.Out, [_feed.Key.PublicKey]).IsValid);

        // The same version rebuilt with different bytes must get a new version number
        File.Delete(Path.Combine(_feed.Packages, "launcher-1.1.0.win-x64.zip"));
        WriteZip("launcher-1.1.0.win-x64.zip", "rebuilt");
        var before = _feed.Snapshot();
        _feed.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Throws<PublishException>(() => _feed.Publish());
        Assert.Equal(before, _feed.Snapshot());
    }

    [Fact]
    public void Verify_CatchesATamperedBlob()
    {
        _feed.WriteSource("art.mul", "art");
        _feed.Publish();
        Assert.True(FeedVerifier.Verify(_feed.Out, [_feed.Key.PublicKey]).IsValid);

        File.WriteAllText(_feed.BlobOf("art.mul"), "ART");
        var result = FeedVerifier.Verify(_feed.Out, [_feed.Key.PublicKey]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("art.mul", StringComparison.Ordinal) && p.Contains("SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_RejectsAFeedSignedWithAnotherKey()
    {
        _feed.WriteSource("art.mul", "art");
        _feed.Publish();
        using var other = P256SigningKey.Generate();

        Assert.False(FeedVerifier.Verify(_feed.Out, [other.PublicKey]).IsValid);
    }

    private SignatureStatus VerifySignature(string path, string signaturePath) =>
        FeedSigning.Verify(
            File.ReadAllBytes(Path.Combine(_feed.Out, path)),
            File.ReadAllText(Path.Combine(_feed.Out, signaturePath), Encoding.UTF8),
            [_feed.Key.PublicKey]);

    private void WriteZip(string name, string content)
    {
        using var zip = ZipFile.Open(Path.Combine(_feed.Packages, name), ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open());
        writer.Write(content);
    }
}
