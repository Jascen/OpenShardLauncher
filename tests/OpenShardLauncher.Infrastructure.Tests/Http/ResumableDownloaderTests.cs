using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Infrastructure.Tests.Http;

// Blob downloads through IUpdateServer, against the in-process server and a real temp install folder.
public sealed class ResumableDownloaderTests : IAsyncLifetime
{
    private static readonly byte[] Content = RandomContent(300_000);

    private TestFeedServer _server = null!;
    private TestLauncher _launcher = null!;
    private FileEntry _file = null!;

    private string BlobPath => FeedLayout.BlobPath(_file.Sha256);

    private string Destination => _launcher.InstallFolder.PathFor(_file.Name);

    public async ValueTask InitializeAsync()
    {
        _server = await TestFeedServer.StartAsync();
        _launcher = new TestLauncher(_server.Url, keys: []);
        _file = _server.AddBlob("Data/art.mul", Content);
    }

    public async ValueTask DisposeAsync()
    {
        _launcher.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Interrupted_ResumesWhereItStopped()
    {
        var cutOff = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.CutOffNextRequest(BlobPath, 100_000, cutOff);
        var progress = new SyncProgress(bytes =>
        {
            if (bytes >= 100_000)
            {
                cutOff.TrySetResult(); // Drop the connection once the client has the first 100,000 bytes
            }
        });

        await DownloadAsync(progress);

        Assert.Equal(Content, await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken));
        Assert.Equal(["", "bytes=100000-"], _server.Requests.Select(r => r.Range));
        Assert.Empty(Directory.EnumerateFiles(_launcher.InstallFolder.DownloadsFolder));
    }

    [Fact]
    public async Task HashMismatch_IsDownloadedAgainFromTheStart()
    {
        _server.CorruptNextRequest(BlobPath);

        await DownloadAsync();

        Assert.Equal(Content, await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken));
        Assert.Equal(["", ""], _server.Requests.Select(r => r.Range));
    }

    [Fact]
    public async Task HashMismatch_Twice_GivesUpWithoutDownloadingAgain()
    {
        _server.CorruptNextRequest(BlobPath);
        _server.CorruptNextRequest(BlobPath);

        var e = await Assert.ThrowsAsync<UpdateException>(() => DownloadAsync());

        Assert.Equal(UpdateError.FileFailed, e.Error);
        Assert.Equal(_file.Name, e.FileName);
        Assert.Equal(2, _server.RequestsFor(BlobPath));
        Assert.False(File.Exists(Destination));
    }

    // The server's copy is shorter than the list says (being uploaded) while the launcher has `part` bytes from
    // before: a full response (no part), a 206 (part shorter than the server's copy) and a 416 (part longer).
    [Theory]
    [InlineData(0, 150_000)]
    [InlineData(100_000, 150_000)]
    [InlineData(100_000, 50_000)]
    public async Task UploadInProgress_KeepsThePartAndResumesOnceComplete(int part, int uploaded)
    {
        var partPath = Path.Combine(_launcher.InstallFolder.DownloadsFolder, _file.Sha256 + ".part");
        Directory.CreateDirectory(_launcher.InstallFolder.DownloadsFolder);
        await File.WriteAllBytesAsync(partPath, Content[..part], TestContext.Current.CancellationToken);
        _server.WriteFile(BlobPath, Content[..uploaded]);

        var e = await Assert.ThrowsAsync<UpdateException>(() => DownloadAsync());

        Assert.Equal(UpdateError.FeedUpdating, e.Error);
        Assert.Equal(1, _server.RequestsFor(BlobPath)); // Not retried
        Assert.Equal(part, new FileInfo(partPath).Length); // Nothing of the short copy was stored, nothing kept was lost

        _server.WriteFile(BlobPath, Content); // The upload finishes
        await DownloadAsync();

        Assert.Equal(Content, await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken));
        Assert.Equal(part == 0 ? "" : $"bytes={part}-", _server.Requests.Last().Range);
    }

    [Fact]
    public async Task MissingBlob_IsFeedUpdatingAndNotRetried()
    {
        File.Delete(Path.Combine(_server.Root, BlobPath));

        var e = await Assert.ThrowsAsync<UpdateException>(() => DownloadAsync());

        Assert.Equal(UpdateError.FeedUpdating, e.Error);
        Assert.Equal(1, _server.RequestsFor(BlobPath));
    }

    [Fact]
    public async Task LockedFile_IsNotRetried_AndARetryLaterNeedsNoDownload()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows locks files that are open in another program.");
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await File.WriteAllTextAsync(Destination, "old", TestContext.Current.CancellationToken);

        using (new FileStream(Destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var e = await Assert.ThrowsAsync<UpdateException>(() => DownloadAsync());

            Assert.Equal(UpdateError.FileLocked, e.Error);
            Assert.Equal(1, _server.RequestsFor(BlobPath));
        }

        await DownloadAsync();

        Assert.Equal(Content, await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken));
        Assert.Equal(1, _server.RequestsFor(BlobPath)); // The complete part was checked and moved, not downloaded again
    }

    private Task DownloadAsync(IProgress<long>? progress = null) =>
        _launcher.Server.DownloadBlobAsync(_file, _launcher.InstallFolder, progress, TestContext.Current.CancellationToken);

    private static byte[] RandomContent(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    // Reports on the calling thread, unlike Progress<T>, so the test sees each value before the download reads on.
    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
