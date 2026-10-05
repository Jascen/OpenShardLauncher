using System.Text.Json;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Shared.Feed;

// The two things a feed carries besides game files. Role strings are a stable contract.
public static class PackageRole
{
    public const string Launcher = "launcher"; // The launcher itself (self-update)
    public const string Client = "client"; // The game client the launcher starts (any zip: TazUO launcher, ClassicUO, ...)
}

// One downloadable package. File is the zip's name inside packages/, Sha256 is lowercase hex.
public sealed record PackageEntry(string Role, string Version, string Rid, string File, string Sha256, long Size);

// packages/manifest.json. It lists only the newest version of each role and platform.
// Property names are a stable contract; add fields only.
public sealed record PackageManifest(DateTimeOffset Generated, IReadOnlyList<PackageEntry> Packages)
{
    public PackageEntry? Find(string role, string rid) =>
        Packages.FirstOrDefault(p => p.Role == role && p.Rid == rid);

    // The exact bytes that are signed and written to manifest.json
    public byte[] ToJsonBytes() => JsonSerializer.SerializeToUtf8Bytes(this, FeedJsonContext.Default.PackageManifest);

    // Only trust the result after the bytes' signature has been checked (or AllowUnsignedFeed applied)
    public static PackageManifest Parse(ReadOnlySpan<byte> json) => Parse(json, onInvalidEntry: null);

    // Like Parse, but an entry whose file name doesn't match its role, version and platform, or with an invalid hash or
    // size, is left out and described to onInvalidEntry instead of failing the whole manifest. The launcher uses this so
    // one bad entry (a stale package, or a role this launcher doesn't know) doesn't hide every other package. The
    // document itself must still be valid.
    public static PackageManifest Parse(ReadOnlySpan<byte> json, Action<string>? onInvalidEntry)
    {
        PackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(json, FeedJsonContext.Default.PackageManifest);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{FeedLayout.ManifestFileName} is not valid: {e.Message}", e);
        }

        if (manifest is null)
        {
            throw new InvalidDataException($"{FeedLayout.ManifestFileName} is empty.");
        }

        var packages = new List<PackageEntry>(manifest.Packages.Count);
        foreach (var package in manifest.Packages)
        {
            if (IsValid(package))
            {
                packages.Add(package);
                continue;
            }

            var problem = $"has an invalid entry '{package?.File}'";
            if (onInvalidEntry is null)
            {
                throw new InvalidDataException($"{FeedLayout.ManifestFileName} {problem}.");
            }

            onInvalidEntry(problem);
        }

        return packages.Count == manifest.Packages.Count ? manifest : manifest with { Packages = packages };
    }

    // A null element isn't caught by the serializer's nullable checks
    private static bool IsValid(PackageEntry? package) =>
        package is not null
        && PackageFileName.TryParse(package.File, out var role, out var version, out var rid)
        && role == package.Role && rid == package.Rid && version.ToString() == package.Version
        && Sha256Hex.IsValid(package.Sha256) && package.Size >= 0;
}
