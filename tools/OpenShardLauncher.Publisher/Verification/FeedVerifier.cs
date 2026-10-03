using System.Text;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Publisher.Verification;

public sealed record VerifyResult(IReadOnlyList<string> Problems, int FilesChecked, int PackagesChecked)
{
    public bool IsValid => Problems.Count == 0;
}

// Checks a feed folder the way a launcher would, plus everything it would only find out file by file: signatures,
// and that every blob and package referenced exists with the right size and SHA-256. With no trusted keys the feed
// must be unsigned.
public static class FeedVerifier
{
    public static VerifyResult Verify(string feed, IReadOnlyList<TrustedKey> trustedKeys)
    {
        var problems = new List<string>();
        var filesChecked = 0;
        var packagesChecked = 0;

        var fileList = ReadSigned(feed, FeedLayout.FileListPath, FeedLayout.FileListSignaturePath, trustedKeys, FileList.Parse, problems);
        if (fileList is not null)
        {
            foreach (var entry in fileList.Files)
            {
                if (!PathContainment.TryResolve(feed, entry.Name, out _))
                {
                    problems.Add($"'{entry.Name}' resolves outside the install folder.");
                }

                CheckContent(Path.Combine(feed, FeedLayout.BlobPath(entry.Sha256)), entry.Sha256, entry.Size, entry.Name, problems);
                filesChecked++;
            }
        }
        else if (!File.Exists(Path.Combine(feed, FeedLayout.FileListPath)))
        {
            problems.Add($"{FeedLayout.FileListPath} is missing.");
        }

        var manifest = ReadSigned(feed, FeedLayout.ManifestPath, FeedLayout.ManifestSignaturePath, trustedKeys, PackageManifest.Parse, problems);
        foreach (var package in manifest?.Packages ?? [])
        {
            CheckContent(Path.Combine(feed, FeedLayout.PackagePath(package.File)), package.Sha256, package.Size, package.File, problems);
            packagesChecked++;
        }

        return new VerifyResult(problems, filesChecked, packagesChecked);
    }

    private delegate T ParseFunc<out T>(ReadOnlySpan<byte> json);

    private static T? ReadSigned<T>(
        string feed, string path, string signaturePath, IReadOnlyList<TrustedKey> trustedKeys, ParseFunc<T> parse, List<string> problems)
        where T : class
    {
        var fullPath = Path.Combine(feed, path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(fullPath);
        var fullSignaturePath = Path.Combine(feed, signaturePath);
        var signature = File.Exists(fullSignaturePath) ? File.ReadAllText(fullSignaturePath, Encoding.UTF8) : null;
        var status = FeedSigning.Verify(bytes, signature, trustedKeys);

        if (trustedKeys.Count == 0 && status != SignatureStatus.Missing)
        {
            problems.Add($"{signaturePath} exists; pass --pub to check it.");
        }
        else if (trustedKeys.Count > 0 && status == SignatureStatus.Missing)
        {
            problems.Add($"{signaturePath} is missing (the feed is unsigned).");
        }
        else if (trustedKeys.Count > 0 && status == SignatureStatus.Invalid)
        {
            problems.Add($"{signaturePath} does not match {path} for any of the given keys.");
        }

        try
        {
            return parse(bytes);
        }
        catch (InvalidDataException e)
        {
            problems.Add(e.Message);
            return null;
        }
    }

    private static void CheckContent(string path, string sha256, long size, string label, List<string> problems)
    {
        if (!File.Exists(path))
        {
            problems.Add($"{label}: {Path.GetFileName(path)} is missing.");
        }
        else if (new FileInfo(path).Length != size)
        {
            problems.Add($"{label}: {Path.GetFileName(path)} has the wrong size.");
        }
        else if (Sha256Hex.OfFile(path) != sha256)
        {
            problems.Add($"{label}: {Path.GetFileName(path)} has the wrong SHA-256 (damaged or tampered).");
        }
    }
}
