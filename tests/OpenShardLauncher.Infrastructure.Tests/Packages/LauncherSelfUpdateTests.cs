using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Infrastructure.Tests.Packages;

// The launcher's self-update against the in-process feed server: what is offered (downgrade protection), and the way
// to the hand-off (download into .temp/, check again, stage, marker). The hand-off itself is a fake.
public sealed class LauncherSelfUpdateTests : IAsyncLifetime
{
    private readonly P256SigningKey _key = P256SigningKey.Generate();
    private TestFeedServer _server = null!;
    private TestLauncher _launcher = null!;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private LauncherSelfUpdateService SelfUpdate => _launcher.Get<LauncherSelfUpdateService>();

    public async ValueTask InitializeAsync()
    {
        _server = await TestFeedServer.StartAsync();
        _launcher = new TestLauncher(_server.Url, [_key.PublicKey], launcherVersion: "1.0.0");
    }

    public async ValueTask DisposeAsync()
    {
        _launcher.Dispose();
        await _server.DisposeAsync();
        _key.Dispose();
    }

    [Fact]
    public async Task OnlyAStrictlyNewerVersionIsOffered()
    {
        await PublishAsync("1.0");
        Assert.Null(SelfUpdate.Offer); // Same version ("1.0" is 1.0.0)

        await PublishAsync("1.1.0");
        Assert.Equal("1.1.0", SelfUpdate.Offer?.Package.Version);
        Assert.Equal(FeedTrust.Signed, SelfUpdate.Offer?.Trust);
    }

    [Fact]
    public async Task AnOlderManifestThanSeenBefore_IsADowngrade_AndIsRefused()
    {
        await PublishAsync("2.0.0");
        Assert.NotNull(SelfUpdate.Offer);

        // Still newer than the running 1.0.0, but older than what this server already offered: a rollback.
        await PublishAsync("1.5.0");

        Assert.Null(SelfUpdate.Offer);
    }

    [Fact]
    public async Task ADevBuildWithoutANumericVersion_IsNeverOffered()
    {
        using var dev = new TestLauncher(_server.Url, [_key.PublicKey], launcherVersion: "1.0.0-dev");
        var selfUpdate = dev.Get<LauncherSelfUpdateService>();
        PublishPackage("9.0.0");

        await selfUpdate.RefreshAsync(TestToken);

        Assert.Null(selfUpdate.Offer);
        Assert.DoesNotContain(_server.Requests, r => r.Path == "/" + FeedLayout.ManifestPath);
    }

    [Fact]
    public async Task NotNow_HidesTheOffer()
    {
        await PublishAsync("1.1.0");

        SelfUpdate.Dismiss();
        await SelfUpdate.RefreshAsync(TestToken);

        Assert.Null(SelfUpdate.Offer);
    }

    [Fact]
    public async Task Update_StagesThePackageNextToTheExe_WritesTheMarker_AndHandsOff()
    {
        await PublishAsync("1.1.0", (TestLauncher.ExeName, "new exe"), ("lib/native.dll", "native"));
        var progress = new List<UpdateProgress>();

        var result = await SelfUpdate.UpdateAsync(new SyncProgress(progress), TestToken);

        Assert.Equal(SelfUpdateResult.Success, result);
        var staging = Path.Combine(_launcher.LauncherFolder, ".temp", "staging");
        Assert.Equal((staging, "1.1.0"), _launcher.SelfUpdater.HandedOff);
        Assert.Equal("new exe", await File.ReadAllTextAsync(Path.Combine(staging, TestLauncher.ExeName), TestToken));
        Assert.Equal("native", await File.ReadAllTextAsync(Path.Combine(staging, "lib", "native.dll"), TestToken));
        Assert.Contains(progress, p => p.Phase == UpdatePhase.UpdatingLauncher && p.BytesTotal > 0);

        // The next start (as 1.1.0) reports success.
        var report = UpdateResultMarker.Take(_launcher.DataFolder.UpdateResultFile, "1.1.0", NullLogger.Instance);
        Assert.Equal(new LauncherUpdateReport(true, "1.1.0", null), report);
    }

    [Fact]
    public async Task ACorruptedPackage_IsRejected_WithoutAHandOff()
    {
        var package = await PublishAsync("1.1.0", (TestLauncher.ExeName, "new exe"));
        // Every attempt gets wrong bytes of the right length: the hash check fails each time.
        for (var i = 0; i < 4; i++)
        {
            _server.CorruptNextRequest(FeedLayout.PackagePath(package.File));
        }

        var result = await SelfUpdate.UpdateAsync(null, TestToken);

        Assert.Equal(SelfUpdateResult.Failure(SelfUpdateError.DownloadFailed), result);
        AssertNoHandOff();
    }

    [Fact]
    public async Task APackageWithoutThisLaunchersExe_IsRejected()
    {
        await PublishAsync("1.1.0", ("SomethingElse.exe", "other"));

        var result = await SelfUpdate.UpdateAsync(null, TestToken);

        Assert.Equal(SelfUpdateResult.Failure(SelfUpdateError.PackageInvalid), result);
        AssertNoHandOff();
    }

    [Fact]
    public async Task AnUnsafePackage_IsRejected()
    {
        await PublishAsync("1.1.0", (TestLauncher.ExeName, "new exe"), ("../outside.txt", "x"));

        var result = await SelfUpdate.UpdateAsync(null, TestToken);

        Assert.Equal(SelfUpdateResult.Failure(SelfUpdateError.PackageInvalid), result);
        Assert.False(File.Exists(Path.Combine(_launcher.LauncherFolder, ".temp", "outside.txt")));
        AssertNoHandOff();
    }

    [Fact]
    public async Task APackageTheManifestNoLongerOffers_IsRejectedBeforeTheHandOff()
    {
        var offered = await PublishAsync("1.1.0", (TestLauncher.ExeName, "new exe"));
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.PauseNextRequest(FeedLayout.PackagePath(offered.File), arrived, release);

        var update = SelfUpdate.UpdateAsync(null, TestToken);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30), TestToken);
        PublishPackage("1.2.0", (TestLauncher.ExeName, "newer exe")); // Published while the download runs
        release.TrySetResult();
        var result = await update;

        Assert.Equal(SelfUpdateResult.Failure(SelfUpdateError.VerificationFailed), result);
        AssertNoHandOff();
    }

    [Fact]
    public async Task AFailedHandOff_RemovesTheMarker()
    {
        await PublishAsync("1.1.0", (TestLauncher.ExeName, "new exe"));
        _launcher.SelfUpdater.Succeeds = false;

        var result = await SelfUpdate.UpdateAsync(null, TestToken);

        Assert.Equal(SelfUpdateResult.Failure(SelfUpdateError.HandOffFailed), result);
        Assert.False(File.Exists(_launcher.DataFolder.UpdateResultFile));
    }

    [Fact]
    public void TheTempFolder_IsCleanedUpAtStartup()
    {
        var leftover = Path.Combine(_launcher.LauncherFolder, ".temp", "staging", "old.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        File.WriteAllText(leftover, "old");

        SelfUpdate.CleanUpTempFolder();

        Assert.False(Directory.Exists(Path.Combine(_launcher.LauncherFolder, ".temp")));
    }

    private void AssertNoHandOff()
    {
        Assert.Null(_launcher.SelfUpdater.HandedOff);
        Assert.False(File.Exists(_launcher.DataFolder.UpdateResultFile));
    }

    // Publishes a signed manifest with this launcher package and refreshes the offer.
    private async Task<PackageEntry> PublishAsync(string version, params (string Name, string Content)[] files)
    {
        var package = PublishPackage(version, files);
        await SelfUpdate.RefreshAsync(TestToken);
        return package;
    }

    private PackageEntry PublishPackage(string version, params (string Name, string Content)[] files)
    {
        var package = _server.AddPackage(PackageRole.Launcher, version, TestPackages.Zip(files.Length > 0 ? files : [(TestLauncher.ExeName, version)]));
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, [package]), _key);
        return package;
    }

    private sealed class SyncProgress(List<UpdateProgress> values) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value)
        {
            lock (values)
            {
                values.Add(value);
            }
        }
    }
}
