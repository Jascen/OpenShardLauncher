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
    public static PackageManifest Parse(ReadOnlySpan<byte> json)
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

        foreach (var package in manifest.Packages)
        {
            var named = PackageFileName.TryParse(package.File, out var role, out var version, out var rid)
                && role == package.Role && rid == package.Rid && version.ToString() == package.Version;
            if (!named || !Sha256Hex.IsValid(package.Sha256) || package.Size < 0)
            {
                throw new InvalidDataException($"{FeedLayout.ManifestFileName} has an invalid entry '{package.File}'.");
            }
        }

        return manifest;
    }
}
