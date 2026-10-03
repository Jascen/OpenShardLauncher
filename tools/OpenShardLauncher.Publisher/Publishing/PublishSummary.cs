namespace OpenShardLauncher.Publisher.Publishing;

// What a publish did, for the operator to check before uploading. NewFiles is listed in full so an accidental
// Accounts.xml is noticed.
public sealed record PublishSummary
{
    public required long Version { get; init; }
    public required bool Signed { get; init; }
    public required int FileCount { get; init; }
    public required int HashedCount { get; init; }
    public required IReadOnlyList<string> NewFiles { get; init; }
    public required IReadOnlyList<string> ChangedFiles { get; init; }
    public required IReadOnlyList<string> RemovedFiles { get; init; }
    public required int BlobsAdded { get; init; }
    public required int BlobsPruned { get; init; }
    public required IReadOnlyList<string> PackagesAdded { get; init; }
    public required int PackagesPruned { get; init; }
    public required IReadOnlyList<string> IgnoredPackages { get; init; }
    public required IReadOnlyList<string> SupersededPackages { get; init; }

    // New blobs and zips plus the list, manifest and signatures
    public required long BytesToUpload { get; init; }
}
