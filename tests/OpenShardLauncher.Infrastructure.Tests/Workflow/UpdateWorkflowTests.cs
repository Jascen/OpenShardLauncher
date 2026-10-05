using System.Text;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Infrastructure.Tests.Workflow;

// Check / Download / Retry end to end: the real workflow and HTTP stack against the in-process feed server and a real
// temp install folder. Requests the tests interrupt are paused on the server until the test releases them, so nothing
// depends on timing.
public sealed class UpdateWorkflowTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly P256SigningKey _key = P256SigningKey.Generate();
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TestFeedServer _server = null!;
    private TestLauncher _launcher = null!;
    private InstallSession _session = null!;
    private long _version = 1_700_000_000;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server = await TestFeedServer.StartAsync();
        _launcher = new TestLauncher(_server.Url, [_key.PublicKey], configure: o => o with
        {
            KeepLocalPatterns = ["*.cfg"],
            Client = new ClientOptions { TazUOProfiles = [new TazUOProfile { Id = "shard", Name = "Shard", ClientVersion = "7.0.15.1" }] },
        });
        _session = _launcher.OpenSession();
    }

    public async ValueTask DisposeAsync()
    {
        _release.TrySetResult(); // Lets any paused request finish, so the server can stop
        _session.Dispose();
        _launcher.Dispose();
        await _server.DisposeAsync();
        _key.Dispose();
    }

    [Fact]
    public async Task Check_StopsAtTheFirstDifference()
    {
        Publish([.. Enumerable.Range(0, 100).Select(i => ($"Data/file{i}.mul", $"content {i}"))]);
        var progress = new Recorder<UpdateProgress>();

        var outcome = await _launcher.Workflow.CheckAsync(_session, progress, TestToken);

        Assert.Equal(UpdateResult.UpdatesReady, outcome.Result);
        var comparing = progress.Values.Last(p => p.Phase == UpdatePhase.Comparing);
        Assert.Equal(100, comparing.FilesTotal);
        Assert.InRange(comparing.FilesDone, 1, 99);
        Assert.DoesNotContain(_server.Requests, r => r.Path.StartsWith("/blobs/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Download_FixesEverything_ThenACheckIsVerified()
    {
        WriteLocal("same.mul", "same");
        WriteLocal("changed.mul", "old content");
        var files = Publish(("same.mul", "same"), ("changed.mul", "new content"), ("Data/missing.mul", "missing"), ("Data/copy.mul", "missing"));

        var check = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);
        var download = await _launcher.Workflow.DownloadAsync(_session, cancellationToken: TestToken);
        var recheck = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.UpdatesReady, check.Result);
        Assert.Equal(UpdateResult.Finished, download.Result);
        Assert.Empty(download.FailedFiles);
        Assert.Empty(download.PackageWarnings); // No manifest published: no packages, no warning
        Assert.Equal("new content", ReadLocal("changed.mul"));
        Assert.Equal("missing", ReadLocal("Data/missing.mul"));
        Assert.Equal("missing", ReadLocal("Data/copy.mul"));
        Assert.Equal(1, _server.RequestsFor(FeedLayout.BlobPath(files[2].Sha256))); // Same content: downloaded once
        Assert.Equal(0, _server.RequestsFor(FeedLayout.BlobPath(files[0].Sha256)));
        Assert.Equal(UpdateResult.Finished, recheck.Result);
        Assert.Equal(recheck, _session.LastOutcome);
    }

    [Fact]
    public async Task UntrustedFileList_FailsWithoutComparingOrDownloading()
    {
        var entry = _server.AddBlob("art.mul", Bytes("art"));
        _server.PublishFileList(new FileList(++_version, [entry], [])); // No signature
        var progress = new Recorder<UpdateProgress>();

        var outcome = await _launcher.Workflow.DownloadAsync(_session, progress, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Equal(UpdateError.FeedUntrusted, outcome.Error);
        Assert.DoesNotContain(progress.Values, p => p.Phase != UpdatePhase.FetchingFileList);
        Assert.Equal(0, _server.RequestsFor(FeedLayout.BlobPath(entry.Sha256)));
    }

    [Fact]
    public async Task CancelDuringCheck_IsCancelled()
    {
        Publish(("art.mul", "art"));
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.PauseNextRequest(FeedLayout.FileListPath, arrived, _release);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestToken);

        var check = _launcher.Workflow.CheckAsync(_session, cancellationToken: cancel.Token);
        await arrived.Task.WaitAsync(Patience, TestToken);
        await cancel.CancelAsync();
        var outcome = await check;

        Assert.Equal(UpdateResult.Cancelled, outcome.Result);
        Assert.Equal(UpdateResult.Cancelled, _session.LastOutcome?.Result);
    }

    [Fact]
    public async Task CancelDuringDownload_LeavesTheUpdatesPending()
    {
        var files = Publish(("art.mul", "art"));
        Assert.Equal(UpdateResult.UpdatesReady, (await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken)).Result);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.PauseNextRequest(FeedLayout.BlobPath(files[0].Sha256), arrived, _release);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestToken);

        var download = _launcher.Workflow.DownloadAsync(_session, cancellationToken: cancel.Token);
        await arrived.Task.WaitAsync(Patience, TestToken);
        await cancel.CancelAsync();
        var outcome = await download;
        var recheck = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Cancelled, outcome.Result);
        Assert.Equal(UpdateResult.UpdatesReady, recheck.Result);
    }

    [Fact]
    public async Task IgnoreList_KeepLocal_AndReservedNames_AreRespected()
    {
        WriteLocal(InstallFolder.IgnoreFileName, "Music/");
        WriteLocal("user.cfg", "the player's settings");
        var files = Publish(
            ("Music/theme.mp3", "music"),
            ("user.cfg", "default settings"),
            ("new.cfg", "default new settings"),
            (InstallFolder.IgnoreFileName, "evil"),
            (InstallFolder.CacheFolderName + "/hashes.json", "evil cache"),
            ("art.mul", "art"));

        var outcome = await _launcher.Workflow.DownloadAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, outcome.Result);
        Assert.Equal(["Music/"], outcome.IgnoredItems);
        Assert.False(File.Exists(LocalPath("Music/theme.mp3")));
        Assert.Equal("the player's settings", ReadLocal("user.cfg")); // Keep-local: never replaced
        Assert.Equal("default new settings", ReadLocal("new.cfg")); // ... but downloaded when missing
        Assert.Equal("Music/", ReadLocal(InstallFolder.IgnoreFileName));
        Assert.Equal("art", ReadLocal("art.mul"));
        Assert.All(files.Where(f => f.Name is InstallFolder.IgnoreFileName || f.Name.StartsWith(InstallFolder.CacheFolderName, StringComparison.Ordinal)),
            f => Assert.Equal(0, _server.RequestsFor(FeedLayout.BlobPath(f.Sha256))));
    }

    [Fact]
    public async Task RemovedFiles_AreDeleted_UnlessKeptLocalOrIgnored()
    {
        WriteLocal(InstallFolder.IgnoreFileName, "Music/");
        WriteLocal("old.dll", "old");
        WriteLocal("old.cfg", "the player's settings");
        WriteLocal("Music/old.mp3", "the player's music");
        Publish([], [new RemovedEntry("old.dll", _version), new RemovedEntry("old.cfg", _version), new RemovedEntry("Music/old.mp3", _version)]);

        var check = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);
        var download = await _launcher.Workflow.DownloadAsync(_session, cancellationToken: TestToken);
        var recheck = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.UpdatesReady, check.Result); // A removal-only publish still offers Download
        Assert.Equal(0, check.PendingDownloads);
        Assert.Equal(1, check.PendingRemovals);
        Assert.Equal(UpdateResult.Finished, download.Result);
        Assert.False(File.Exists(LocalPath("old.dll")));
        Assert.True(File.Exists(LocalPath("old.cfg")));
        Assert.True(File.Exists(LocalPath("Music/old.mp3")));
        Assert.Equal(UpdateResult.Finished, recheck.Result);
    }

    [Fact]
    public async Task LockedFile_IsReportedAndNotRetried_AndRetryCompletesItWithoutDownloadingAgain()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows locks files that are open in another program.");
        WriteLocal("client.exe", "old");
        var files = Publish(("client.exe", "new client"), ("art.mul", "art"));

        UpdateOutcome locked;
        using (new FileStream(LocalPath("client.exe"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            locked = await _launcher.Workflow.DownloadAsync(_session, cancellationToken: TestToken);
        }

        var retry = await _launcher.Workflow.RetryAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, locked.Result);
        Assert.Equal([new FailedFile("client.exe", Locked: true)], locked.FailedFiles);
        Assert.Equal("art", ReadLocal("art.mul")); // The other files still updated
        Assert.Equal(UpdateResult.Finished, retry.Result);
        Assert.Empty(retry.FailedFiles);
        Assert.Equal("new client", ReadLocal("client.exe"));
        Assert.Equal(1, _server.RequestsFor(FeedLayout.BlobPath(files[0].Sha256)));
    }

    [Fact]
    public async Task BlobStillUploading_IsFeedUpdating_AndRetryResumesFromTheKeptPart()
    {
        var content = new byte[300_000];
        new Random(42).NextBytes(content);
        var entry = _server.AddBlob("Data/big.mul", content);
        _server.PublishFileList(new FileList(++_version, [entry], []), _key);
        var blob = FeedLayout.BlobPath(entry.Sha256);
        var part = Path.Combine(_launcher.InstallFolder.DownloadsFolder, entry.Sha256 + ".part");
        Directory.CreateDirectory(_launcher.InstallFolder.DownloadsFolder);
        await File.WriteAllBytesAsync(part, content[..100_000], TestToken); // From an earlier, interrupted run
        _server.WriteFile(blob, content[..150_000]); // The server's copy is still being uploaded

        var uploading = await _launcher.Workflow.DownloadAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Failed, uploading.Result);
        Assert.Equal(UpdateError.FeedUpdating, uploading.Error);
        Assert.Equal("Data/big.mul", uploading.ErrorFile);
        Assert.Equal(100_000, new FileInfo(part).Length);

        _server.WriteFile(blob, content); // The upload finishes
        var retry = await _launcher.Workflow.RetryAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, retry.Result);
        Assert.Equal(content, await File.ReadAllBytesAsync(LocalPath("Data/big.mul"), TestToken));
        Assert.Equal("bytes=100000-", _server.Requests.Last(r => r.Path == "/" + blob).Range);
    }

    [Fact]
    public async Task UntrustedManifest_Warns_ButGameFilesStillUpdate()
    {
        Publish(("art.mul", "art"));
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, [ClientPackage("2.0.0")])); // No signature

        var outcome = await _launcher.Workflow.DownloadAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, outcome.Result);
        Assert.Equal([PackageWarning.ManifestUntrusted], outcome.PackageWarnings);
        Assert.Equal("art", ReadLocal("art.mul"));
    }

    [Fact]
    public async Task Client_MissingIsOffered_AndAnOlderManifestIsRefused()
    {
        Publish();
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, [ClientPackage("2.0.0")]), _key);

        var missing = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, [ClientPackage("1.0.0")]), _key);
        var rollback = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.PackagesReady, missing.Result);
        Assert.Equal(UpdateResult.Finished, rollback.Result);
        Assert.Equal([PackageWarning.ManifestUntrusted], rollback.PackageWarnings);
    }

    [Fact]
    public async Task Client_InstalledAtTheOfferedVersion_IsUpToDate()
    {
        Publish();
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, [ClientPackage("2.0.0")]), _key);
        _launcher.Game.Installed = true;
        _launcher.FeedState.SetInstalledVersion(PackageRole.Client, "2.0.0");

        var outcome = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, outcome.Result);
        Assert.Empty(outcome.PackageWarnings);
    }

    [Fact]
    public async Task Client_IsInstalledByTheDownload_WithItsVersionAndTazUOProfiles()
    {
        Publish(("art.mul", "art"));
        var package = _server.AddPackage(PackageRole.Client, "2.0.0", TestPackages.Zip(("TazUOLauncher.exe", "tazuo"), ("lib/x.dll", "x")));
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, [package]), _key);
        WriteLocal("TazUO/Profiles/mine.json", "kept"); // A player's own profile isn't in the zip
        var progress = new Recorder<UpdateProgress>();

        var outcome = await _launcher.Workflow.DownloadAsync(_session, progress, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, outcome.Result);
        Assert.Empty(outcome.PackageWarnings);
        Assert.Equal("tazuo", ReadLocal("TazUO/TazUOLauncher.exe"));
        Assert.Equal("x", ReadLocal("TazUO/lib/x.dll"));
        Assert.Equal("kept", ReadLocal("TazUO/Profiles/mine.json"));
        Assert.Equal("2.0.0", _launcher.FeedState.GetInstalledVersion(PackageRole.Client));
        Assert.Contains("\"ultimaonlinedirectory\"", ReadLocal("TazUO/Profiles/Settings/shard.json"));
        Assert.Contains("\"Shard\"", ReadLocal("TazUO/Profiles/shard.json"));
        Assert.Contains(progress.Values, p => p.Phase == UpdatePhase.InstallingClient && p.BytesTotal == package.Size);
        Assert.False(Directory.Exists(_launcher.InstallFolder.PackagesFolder) && Directory.EnumerateFileSystemEntries(_launcher.InstallFolder.PackagesFolder).Any());
    }

    [Fact]
    public async Task Client_UnsafeZip_IsRefused_WithAWarning_AndGameFilesStillUpdate()
    {
        Publish(("art.mul", "art"));
        var package = _server.AddPackage(PackageRole.Client, "2.0.0", TestPackages.Zip(("TazUOLauncher.exe", "tazuo"), ("../../escaped.txt", "x")));
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, [package]), _key);

        var outcome = await _launcher.Workflow.DownloadAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, outcome.Result);
        Assert.Equal([PackageWarning.ClientInstallFailed], outcome.PackageWarnings);
        Assert.Equal("art", ReadLocal("art.mul"));
        Assert.False(File.Exists(LocalPath("TazUO/TazUOLauncher.exe")));
        Assert.Null(_launcher.FeedState.GetInstalledVersion(PackageRole.Client));
    }

    [Fact]
    public async Task Client_InTheGameFiles_WithoutAPackage_IsUpToDate()
    {
        Publish(("ClassicUO/ClassicUO.exe", "cuo"));
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, []), _key);
        WriteLocal("ClassicUO/ClassicUO.exe", "cuo");
        _launcher.Game.Installed = true;

        var outcome = await _launcher.Workflow.CheckAsync(_session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, outcome.Result);
        Assert.Empty(outcome.PackageWarnings);
    }

    [Fact]
    public async Task Client_WithoutTazUOProfiles_GetsNoProfileFiles()
    {
        using var launcher = new TestLauncher(_server.Url, [_key.PublicKey], configure: o => o with
        {
            Client = new ClientOptions { InstallFolder = "ClassicUO", ExecutableName = "ClassicUO", Arguments = ["-uopath", "{GameFolder}"] },
        });
        using var session = launcher.OpenSession();
        Publish(("art.mul", "art"));
        var package = _server.AddPackage(PackageRole.Client, "1.0.0", TestPackages.Zip(("ClassicUO.exe", "cuo")));
        _server.PublishManifest(new PackageManifest(DateTimeOffset.UtcNow, [package]), _key);

        var outcome = await launcher.Workflow.DownloadAsync(session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Finished, outcome.Result);
        Assert.Empty(outcome.PackageWarnings);
        Assert.Equal("cuo", await File.ReadAllTextAsync(Path.Combine(launcher.InstallFolder.Root, "ClassicUO", "ClassicUO.exe"), TestToken));
        Assert.False(Directory.Exists(Path.Combine(launcher.InstallFolder.Root, "ClassicUO", "Profiles")));
    }

    // Over plain http to another machine is covered by FeedVerifierTests: the test server only listens on loopback.
    [Fact]
    public async Task UnsignedFeed_FromAnOverriddenServer_IsRefusedWithTheReason()
    {
        using var launcher = new TestLauncher(new Uri("http://127.0.0.1:1/"), [], allowUnsignedFeed: true);
        launcher.Endpoint.SetOverride(_server.Url.AbsoluteUri);
        _server.PublishFileList(new FileList(++_version, [], [])); // No signature
        using var session = launcher.OpenSession();

        var outcome = await launcher.Workflow.CheckAsync(session, cancellationToken: TestToken);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Equal(UpdateError.UnsignedFeedNotDefaultServer, outcome.Error);
    }

    [Fact]
    public async Task DisposingTheSessionMidRun_StopsProgressCallbacks()
    {
        var files = Publish(("art.mul", "art"));
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.PauseNextRequest(FeedLayout.BlobPath(files[0].Sha256), arrived, _release);
        var progress = new Recorder<UpdateProgress>();
        var fileProgress = new Recorder<FileProgress>();

        var download = _launcher.Workflow.DownloadAsync(_session, progress, fileProgress, TestToken);
        await arrived.Task.WaitAsync(Patience, TestToken);
        _session.Dispose();
        var reported = progress.Values.Count + fileProgress.Values.Count;
        _release.TrySetResult();
        var outcome = await download;

        Assert.Equal(UpdateResult.Cancelled, outcome.Result);
        Assert.Equal(reported, progress.Values.Count + fileProgress.Values.Count);
        Assert.Null(_session.LastOutcome);
        Assert.False(File.Exists(LocalPath("art.mul")));
    }

    private FileEntry[] Publish(params (string Name, string Content)[] files) => Publish(files, []);

    private FileEntry[] Publish((string Name, string Content)[] files, RemovedEntry[] removed)
    {
        var entries = files.Select(f => _server.AddBlob(f.Name, Bytes(f.Content))).ToArray();
        _server.PublishFileList(new FileList(++_version, entries, removed), _key);
        return entries;
    }

    private static PackageEntry ClientPackage(string version)
    {
        var file = PackageFileName.Format(PackageRole.Client, version, TestLauncher.Platform);
        return new PackageEntry(PackageRole.Client, version, TestLauncher.Platform, file, new string('a', 64), 1);
    }

    private string LocalPath(string name) => Path.Combine(_launcher.InstallFolder.Root, name);

    private void WriteLocal(string name, string content)
    {
        var path = LocalPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string ReadLocal(string name) => File.ReadAllText(LocalPath(name));

    private static byte[] Bytes(string content) => Encoding.UTF8.GetBytes(content);

    // Records reports on the calling thread, unlike Progress<T>, so the test sees them in order and straight away.
    private sealed class Recorder<T> : IProgress<T>
    {
        private readonly Lock _lock = new();
        private readonly List<T> _values = [];

        public IReadOnlyList<T> Values
        {
            get
            {
                lock (_lock)
                {
                    return [.. _values];
                }
            }
        }

        public void Report(T value)
        {
            lock (_lock)
            {
                _values.Add(value);
            }
        }
    }
}
