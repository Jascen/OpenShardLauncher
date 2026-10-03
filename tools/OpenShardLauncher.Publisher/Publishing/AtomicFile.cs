using System.Buffers;
using System.Security.Cryptography;

namespace OpenShardLauncher.Publisher.Publishing;

// Every feed file is written to a temp file next to it and then moved into place, so readers (the web server, an
// upload tool) see either the old file or the new one, never half of one. Temp names start with "." and end in
// ".tmp"; pruning removes any left behind by a crash.
internal static class AtomicFile
{
    public const string TempSuffix = ".tmp";

    private const int BufferSize = 1 << 16;

    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        var temp = TempPathFor(path);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    // Copies and hashes in one pass. The copy only replaces destination when it has the expected SHA-256, which
    // catches a source file that changed after it was hashed.
    public static void CopyVerified(string source, string destination, string expectedSha256)
    {
        var temp = TempPathFor(destination);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }

                output.Flush(flushToDisk: true);
                if (Convert.ToHexStringLower(hash.GetHashAndReset()) != expectedSha256)
                {
                    throw new PublishException($"{source} changed while publishing. Run the publish again.");
                }
            }

            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            File.Delete(temp);
        }
    }

    public static bool IsTempFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith('.') && name.EndsWith(TempSuffix, StringComparison.Ordinal);
    }

    private static string TempPathFor(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}{TempSuffix}");
    }
}
