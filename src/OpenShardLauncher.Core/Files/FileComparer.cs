using System.Security.Cryptography;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Core.Files;

// Whether one local file matches its feed entry: size first, then SHA-256 (from the hash cache when the file's size
// and modified time haven't changed since it was hashed). Safe to call in parallel.
public sealed class FileComparer(InstallFolder folder, HashCache hashCache)
{
    public async Task<bool> IsUpToDateAsync(FileToCompare file, CancellationToken cancellationToken)
    {
        if (!folder.TryGetPathFor(file.Name, out var path))
        {
            throw new ArgumentException($"'{file.Name}' is outside the install folder or reserved.", nameof(file));
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return false;
        }

        if (file.KeepLocal)
        {
            return true;
        }

        if (info.Length != file.Entry.Size)
        {
            return false;
        }

        var hash = await GetHashAsync(file.Name, info, cancellationToken).ConfigureAwait(false);
        return string.Equals(hash, file.Entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GetHashAsync(string name, FileInfo info, CancellationToken cancellationToken)
    {
        var modified = info.LastWriteTimeUtc;
        if (hashCache.TryGet(name, info.Length, modified, out var cached))
        {
            return cached;
        }

        var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            hashCache.Set(name, info.Length, modified, hash);
            return hash;
        }
    }
}
