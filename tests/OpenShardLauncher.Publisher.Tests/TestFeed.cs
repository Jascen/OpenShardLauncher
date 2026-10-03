using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Publisher.Publishing;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Publisher.Tests;

// A temp folder with source/, packages/ and feed/ side by side (so the default state folder is <root>/.publisher-state),
// a signing key and a clock the test controls
internal sealed class TestFeed : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("osl-publisher-");

    public TestFeed()
    {
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Packages);
    }

    public string Root => _root.FullName;
    public string Source => Path.Combine(Root, "source");
    public string Packages => Path.Combine(Root, "packages");
    public string Out => Path.Combine(Root, "feed");
    public string State => Path.Combine(Root, FeedPublisher.DefaultStateFolderName);

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    public P256SigningKey Key { get; } = P256SigningKey.Generate();

    public PublishOptions Options => new() { Source = Source, Packages = Packages, Out = Out };

    public PublishSummary Publish(PublishOptions? options = null, IReadOnlyList<IFeedSigner>? signers = null) =>
        new FeedPublisher(Clock, NullLogger<FeedPublisher>.Instance).Publish(options ?? Options, signers ?? [Key]);

    public void WriteSource(string name, string content)
    {
        var path = Path.Combine(Source, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void DeleteSource(string name) => File.Delete(Path.Combine(Source, name));

    public FileList ReadFileList() => FileList.Parse(File.ReadAllBytes(Path.Combine(Out, FeedLayout.FileListPath)));

    public string BlobPath(string sha256) => Path.Combine(Out, FeedLayout.BlobPath(sha256));

    public string BlobOf(string name) => BlobPath(ReadFileList().Files.Single(f => f.Name == name).Sha256);

    public SortedSet<string> BlobHashes() =>
        Directory.Exists(Path.Combine(Out, FeedLayout.BlobsFolder))
            ? new(Directory.EnumerateFiles(Path.Combine(Out, FeedLayout.BlobsFolder), "*", SearchOption.AllDirectories).Select(Path.GetFileName)!, StringComparer.Ordinal)
            : new(StringComparer.Ordinal);

    // Every file in the feed with its bytes, to prove a refused publish changed nothing
    public SortedDictionary<string, string> Snapshot() =>
        new(Directory.EnumerateFiles(Out, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(Out, p), p => Convert.ToBase64String(File.ReadAllBytes(p))), StringComparer.Ordinal);

    public void Dispose()
    {
        Key.Dispose();
        _root.Delete(recursive: true);
    }
}

internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
