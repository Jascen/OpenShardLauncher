using System.IO.Compression;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Packages;

// Caps for one package zip. The defaults are far above any real launcher or TazUO package.
public sealed record SafeZipLimits
{
    public static SafeZipLimits Default { get; } = new();

    public int MaxEntries { get; init; } = 20_000;

    public long MaxTotalBytes { get; init; } = 4L * 1024 * 1024 * 1024;
}

// Extracts every package zip (TazUO, launcher). The signed manifest pins the zip's hash, so these checks are a
// backstop against a publishing mistake rather than the security control:
// - every entry must resolve strictly inside the target (zip-slip)
// - no symlink entries (a link could point anywhere, and later entries would write through it)
// - at most MaxEntries entries and MaxTotalBytes uncompressed. The sizes in the zip's headers are checked first, and
//   the bytes actually written are counted too, so a header that lies doesn't get past the cap.
// All entries are checked before the first file is written. A refused zip throws InvalidDataException. Unix permissions stored in the zip are kept, so
// executables stay executable.
public static class SafeZip
{
    // Unix file type bits in the high 16 bits of ExternalAttributes (zips made on Unix), and the Windows reparse-point
    // attribute in the low bits.
    private const int UnixTypeMask = 0xF000;
    private const int UnixSymlink = 0xA000;
    private const int WindowsReparsePoint = 0x400;

    public static void ExtractToDirectory(string zipPath, string targetFolder, SafeZipLimits? limits = null)
    {
        limits ??= SafeZipLimits.Default;
        var root = Path.GetFullPath(targetFolder);
        using var zip = ZipFile.OpenRead(zipPath);

        var entries = Check(zip, root, limits);
        Directory.CreateDirectory(root);
        long written = 0;
        foreach (var (entry, path) in entries)
        {
            if (IsDirectory(entry))
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            written = Copy(entry, path, written, limits.MaxTotalBytes);
            SetUnixMode(entry, path);
        }
    }

    private static List<(ZipArchiveEntry Entry, string Path)> Check(ZipArchive zip, string root, SafeZipLimits limits)
    {
        if (zip.Entries.Count > limits.MaxEntries)
        {
            throw new InvalidDataException($"The package has {zip.Entries.Count} entries, more than the {limits.MaxEntries} allowed.");
        }

        var entries = new List<(ZipArchiveEntry, string)>(zip.Entries.Count);
        long declared = 0;
        foreach (var entry in zip.Entries)
        {
            if (IsLink(entry))
            {
                throw new InvalidDataException($"The package entry '{entry.FullName}' is a link.");
            }

            var name = entry.FullName.TrimEnd('/', '\\');
            if (!PathContainment.TryResolve(root, name, out var path))
            {
                throw new InvalidDataException($"The package entry '{entry.FullName}' resolves outside the target folder.");
            }

            declared += entry.Length;
            if (declared > limits.MaxTotalBytes)
            {
                throw new InvalidDataException($"The package unpacks to more than the {limits.MaxTotalBytes} bytes allowed.");
            }

            entries.Add((entry, path));
        }

        return entries;
    }

    // Counts what is really decompressed: no more than the entry says, and no more than the cap in total.
    private static long Copy(ZipArchiveEntry entry, string path, long written, long maxTotalBytes)
    {
        using var source = entry.Open();
        using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long entryBytes = 0;
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            entryBytes += read;
            written += read;
            if (entryBytes > entry.Length || written > maxTotalBytes)
            {
                throw new InvalidDataException($"The package entry '{entry.FullName}' unpacks to more bytes than its header says.");
            }

            target.Write(buffer, 0, read);
        }

        return written;
    }

    private static bool IsDirectory(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

    private static bool IsLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & UnixTypeMask) == UnixSymlink
        || (entry.ExternalAttributes & WindowsReparsePoint) != 0;

    private static void SetUnixMode(ZipArchiveEntry entry, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = (entry.ExternalAttributes >> 16) & 0x1FF;
        if (mode != 0)
        {
            File.SetUnixFileMode(path, (UnixFileMode)mode);
        }
    }
}
