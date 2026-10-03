using System.Text;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Publisher.Publishing;

public sealed record PublishPaths(string Source, string Packages, string Out, string State);

// Builds and signs the whole feed in --out from --source and --packages (REWRITE_PLAN section 4, Publisher).
// Everything that could refuse the publish is checked before the first write, so a refused publish leaves the
// feed unchanged. Write order: blobs, package zips, manifest.sig, manifest.json, files.sig, files.json, then
// pruning, each file written atomically, so a launcher never sees a list that references a missing blob.
public sealed partial class FeedPublisher(TimeProvider time, ILogger<FeedPublisher> logger)
{
    public const string DefaultStateFolderName = ".publisher-state";

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // Checks the folders without touching anything, so the CLI can refuse before asking for a key password
    public static PublishPaths ResolvePaths(PublishOptions options)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Source));
        var packages = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Packages));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Out));
        var state = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            options.State ?? Path.Combine(Path.GetDirectoryName(output) ?? output, DefaultStateFolderName)));

        if (!Directory.Exists(source))
        {
            throw new PublishException($"--source {source} does not exist.");
        }

        if (!Directory.Exists(packages))
        {
            throw new PublishException($"--packages {packages} does not exist.");
        }

        if (IsSameOrInside(output, source) || IsSameOrInside(source, output))
        {
            throw new PublishException("--out and --source must not be inside each other.");
        }

        if (IsSameOrInside(state, source) || IsSameOrInside(state, output))
        {
            throw new PublishException($"The state folder {state} must not be inside --source or --out. Pass --state <dir>.");
        }

        if (IsSameOrInside(packages, output))
        {
            throw new PublishException("--packages must not be inside --out.");
        }

        foreach (var keyFile in options.KeyFiles)
        {
            if (IsSameOrInside(Path.GetFullPath(keyFile), source))
            {
                throw new PublishException($"The key {keyFile} is inside --source. Keep private keys away from game files.");
            }
        }

        return new PublishPaths(source, packages, output, state);
    }

    public PublishSummary Publish(PublishOptions options, IReadOnlyList<IFeedSigner> signers)
    {
        var paths = ResolvePaths(options);
        if (options.Unsigned && signers.Count > 0)
        {
            throw new PublishException("--unsigned can't be combined with --key.");
        }

        if (!options.Unsigned && signers.Count == 0)
        {
            throw new PublishException("Pass at least one --key, or --unsigned for launchers built with AllowUnsignedFeed.");
        }

        // The previous version comes from the feed itself, not from saved state
        var previous = ReadPrevious(paths.Out, FeedLayout.FileListPath, FileList.Parse);
        var previousManifest = ReadPrevious(paths.Out, FeedLayout.ManifestPath, PackageManifest.Parse);

        var sources = SourceScanner.Scan(paths.Source);
        if (sources.Count == 0)
        {
            throw new PublishException($"--source {paths.Source} has no files to publish.");
        }

        var (entries, rehashed) = Hash(sources, paths.State);
        var now = time.GetUtcNow();
        var version = NextVersion(previous, now);
        var fileList = new FileList(version, entries, RemovedList.Next(previous, entries, version, options.ForgetRemovedBefore));

        var blobCopies = PlanBlobs(paths.Out, sources, entries, rehashed);
        var packages = BuildManifest(paths.Packages, now);
        var packageCopies = PlanPackages(paths.Out, packages);

        // Nothing has been written yet. From here on, write in the order that keeps the feed consistent.
        long bytesToUpload = 0;
        foreach (var copy in blobCopies)
        {
            AtomicFile.CopyVerified(copy.Source, copy.Destination, copy.Sha256);
            bytesToUpload += copy.Size;
        }

        foreach (var copy in packageCopies)
        {
            AtomicFile.CopyVerified(copy.Source, copy.Destination, copy.Sha256);
            bytesToUpload += copy.Size;
        }

        var manifestBytes = packages.Manifest.ToJsonBytes();
        bytesToUpload += WriteSignature(Path.Combine(paths.Out, FeedLayout.ManifestSignaturePath), manifestBytes, signers);
        AtomicFile.WriteAllBytes(Path.Combine(paths.Out, FeedLayout.ManifestPath), manifestBytes);

        var fileListBytes = fileList.ToJsonBytes();
        bytesToUpload += WriteSignature(Path.Combine(paths.Out, FeedLayout.FileListSignaturePath), fileListBytes, signers);
        AtomicFile.WriteAllBytes(Path.Combine(paths.Out, FeedLayout.FileListPath), fileListBytes);
        bytesToUpload += manifestBytes.Length + fileListBytes.Length;

        var blobsPruned = PruneBlobs(paths.Out, fileList, previous);
        var packagesPruned = PrunePackages(paths.Out, packages.Manifest, previousManifest);

        var previousHashes = (previous?.Files ?? []).ToDictionary(f => f.Name, f => f.Sha256, StringComparer.OrdinalIgnoreCase);
        return new PublishSummary
        {
            Version = version,
            Signed = !options.Unsigned,
            FileCount = entries.Count,
            HashedCount = rehashed.Count,
            NewFiles = entries.Where(e => !previousHashes.ContainsKey(e.Name)).Select(e => e.Name).ToList(),
            ChangedFiles = entries.Where(e => previousHashes.TryGetValue(e.Name, out var sha) && sha != e.Sha256).Select(e => e.Name).ToList(),
            RemovedFiles = fileList.Removed.Where(r => r.RemovedIn == version).Select(r => r.Name).ToList(),
            BlobsAdded = blobCopies.Count,
            BlobsPruned = blobsPruned,
            PackagesAdded = packageCopies.Select(c => Path.GetFileName(c.Destination)).ToList(),
            PackagesPruned = packagesPruned,
            IgnoredPackages = packages.Ignored,
            SupersededPackages = packages.Superseded,
            BytesToUpload = bytesToUpload,
        };
    }

    private static T? ReadPrevious<T>(string feed, string relativePath, ParseFunc<T> parse)
        where T : class
    {
        var path = Path.Combine(feed, relativePath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return parse(File.ReadAllBytes(path));
        }
        catch (InvalidDataException e)
        {
            throw new PublishException($"The existing {path} can't be read, so the previous version is unknown: {e.Message}");
        }
    }

    private delegate T ParseFunc<out T>(ReadOnlySpan<byte> json);

    // Hashes only files whose size or modified time changed since the last publish from this state folder
    private (List<FileEntry> Entries, HashSet<string> Rehashed) Hash(IReadOnlyList<SourceFile> sources, string stateDirectory)
    {
        var cache = HashCache.Load(stateDirectory);
        var hashes = new string?[sources.Count];
        var misses = new List<int>();
        for (var i = 0; i < sources.Count; i++)
        {
            hashes[i] = cache.Find(sources[i].FullPath, sources[i].Size, sources[i].LastWriteUtc);
            if (hashes[i] is null)
            {
                misses.Add(i);
            }
        }

        LogHashing(logger, misses.Count, sources.Count);
        Parallel.ForEach(misses, i =>
        {
            var sha = Sha256Hex.OfFile(sources[i].FullPath);
            hashes[i] = sha;
            cache.Add(sources[i].FullPath, sources[i].Size, sources[i].LastWriteUtc, sha);
        });
        cache.Save(stateDirectory);

        var entries = sources.Select((s, i) => new FileEntry(s.Name, hashes[i]!, s.Size)).ToList();
        var rehashed = misses.Select(i => sources[i].Name).ToHashSet(StringComparer.Ordinal);
        return (entries, rehashed);
    }

    private long NextVersion(FileList? previous, DateTimeOffset now)
    {
        var version = now.ToUnixTimeSeconds();
        if (previous is not null && previous.Version >= version)
        {
            LogClockBehind(logger, previous.Version, version);
            version = previous.Version + 1;
        }

        return version;
    }

    // Collision guard: an existing blob must have the same size and, for files hashed in this run, the same bytes.
    // (Files taken from the hash cache were compared when they were first published.)
    private static List<PlannedCopy> PlanBlobs(
        string feed, IReadOnlyList<SourceFile> sources, IReadOnlyList<FileEntry> entries, HashSet<string> rehashed)
    {
        var copies = new List<PlannedCopy>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var source = sources[i];
            var destination = Path.Combine(feed, FeedLayout.BlobPath(entry.Sha256));
            var isNew = seen.Add(entry.Sha256);
            if (!File.Exists(destination))
            {
                if (isNew)
                {
                    copies.Add(new PlannedCopy(source.FullPath, destination, entry.Sha256, entry.Size));
                }

                continue;
            }

            if (new FileInfo(destination).Length != entry.Size
                || (rehashed.Contains(entry.Name) && !SameBytes(destination, source.FullPath)))
            {
                throw new PublishException(
                    $"{FeedLayout.BlobPath(entry.Sha256)} already exists with different content than {entry.Name}. " +
                    "The feed was not changed. The feed folder may be damaged; run verify or delete that blob.");
            }
        }

        return copies;
    }

    private static ManifestBuildResult BuildManifest(string packagesDirectory, DateTimeOffset now)
    {
        try
        {
            return ManifestBuilder.Build(packagesDirectory, now);
        }
        catch (InvalidDataException e)
        {
            throw new PublishException(e.Message);
        }
    }

    // A package name is published once: the same name with different bytes would reach some players and not others
    private static List<PlannedCopy> PlanPackages(string feed, ManifestBuildResult packages)
    {
        var copies = new List<PlannedCopy>();
        foreach (var package in packages.Manifest.Packages)
        {
            var destination = Path.Combine(feed, FeedLayout.PackagePath(package.File));
            if (!File.Exists(destination))
            {
                copies.Add(new PlannedCopy(packages.SourcePaths[package.File], destination, package.Sha256, package.Size));
            }
            else if (Sha256Hex.OfFile(destination) != package.Sha256)
            {
                throw new PublishException(
                    $"{FeedLayout.PackagePath(package.File)} was already published with different content. " +
                    "Give the new build a higher version. The feed was not changed.");
            }
        }

        return copies;
    }

    // Unsigned mode deletes a stale signature; launchers would otherwise reject the new file as wrongly signed
    private static long WriteSignature(string path, byte[] signedBytes, IReadOnlyList<IFeedSigner> signers)
    {
        if (signers.Count == 0)
        {
            File.Delete(path);
            return 0;
        }

        var bytes = Encoding.UTF8.GetBytes(FeedSigning.CreateSignatureFile(signedBytes, signers));
        AtomicFile.WriteAllBytes(path, bytes);
        return bytes.Length;
    }

    // Keeps blobs of the new and the previous list, so a launcher still downloading from the previous list can finish
    private static int PruneBlobs(string feed, FileList current, FileList? previous)
    {
        var blobs = Path.Combine(feed, FeedLayout.BlobsFolder);
        if (!Directory.Exists(blobs))
        {
            return 0;
        }

        var keep = current.Files.Concat(previous?.Files ?? []).Select(f => f.Sha256).ToHashSet(StringComparer.Ordinal);
        var pruned = 0;
        foreach (var path in Directory.EnumerateFiles(blobs, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            var kept = keep.Contains(name) && Path.GetFileName(Path.GetDirectoryName(path)) == name[..2];
            if (!kept)
            {
                File.Delete(path);
                pruned += AtomicFile.IsTempFile(path) ? 0 : 1;
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(blobs, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        return pruned;
    }

    private static int PrunePackages(string feed, PackageManifest current, PackageManifest? previous)
    {
        var packages = Path.Combine(feed, FeedLayout.PackagesFolder);
        if (!Directory.Exists(packages))
        {
            return 0;
        }

        var keep = current.Packages.Concat(previous?.Packages ?? []).Select(p => p.File).ToHashSet(StringComparer.Ordinal);
        var pruned = 0;
        foreach (var path in Directory.EnumerateFiles(packages))
        {
            var name = Path.GetFileName(path);
            if (AtomicFile.IsTempFile(path))
            {
                File.Delete(path);
            }
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !keep.Contains(name))
            {
                File.Delete(path);
                pruned++;
            }
        }

        return pruned;
    }

    private static bool SameBytes(string first, string second)
    {
        const int size = 1 << 16;
        using var a = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read, size, FileOptions.SequentialScan);
        using var b = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read, size, FileOptions.SequentialScan);
        if (a.Length != b.Length)
        {
            return false;
        }

        var bufferA = new byte[size];
        var bufferB = new byte[size];
        int read;
        while ((read = a.ReadAtLeast(bufferA, size, throwOnEndOfStream: false)) > 0)
        {
            if (b.ReadAtLeast(bufferB.AsSpan(0, read), read, throwOnEndOfStream: false) != read
                || !bufferA.AsSpan(0, read).SequenceEqual(bufferB.AsSpan(0, read)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        folder = Path.TrimEndingDirectorySeparator(folder);
        return path.Equals(folder, PathComparison)
            || path.StartsWith(folder + Path.DirectorySeparatorChar, PathComparison);
    }

    private sealed record PlannedCopy(string Source, string Destination, string Sha256, long Size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hashing {Changed} of {Total} source files (the rest are unchanged since the last publish)")]
    private static partial void LogHashing(ILogger logger, int changed, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The existing feed has version {Existing}, which is not older than the clock ({Now}). Using {Existing} + 1. Check this PC's clock if you didn't just publish.")]
    private static partial void LogClockBehind(ILogger logger, long existing, long now);
}
