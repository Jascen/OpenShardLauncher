using System.IO.Compression;
using OpenShardLauncher.Core.Packages;

namespace OpenShardLauncher.Core.Tests.Packages;

public sealed class SafeZipTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private string Target => _temp.Combine("target");

    [Fact]
    public void A_normal_zip_is_extracted_with_its_folders()
    {
        var zip = Zip(("TazUOLauncher.exe", 10), ("Data/a.bin", 20), ("Data/Sub/", 0));

        SafeZip.ExtractToDirectory(zip, Target);

        Assert.Equal(10, new FileInfo(Path.Combine(Target, "TazUOLauncher.exe")).Length);
        Assert.Equal(20, new FileInfo(Path.Combine(Target, "Data", "a.bin")).Length);
        Assert.True(Directory.Exists(Path.Combine(Target, "Data", "Sub")));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("Data/../../evil.txt")]
    [InlineData("/etc/evil.txt")]
    public void An_entry_outside_the_target_is_refused_before_anything_is_written(string name)
    {
        var zip = Zip(("first.txt", 5), (name, 5));

        Assert.Throws<InvalidDataException>(() => SafeZip.ExtractToDirectory(zip, Target));

        Assert.False(Directory.Exists(Target) && Directory.EnumerateFileSystemEntries(Target).Any());
        Assert.False(File.Exists(_temp.Combine("evil.txt")));
    }

    [Fact]
    public void A_symlink_entry_is_refused()
    {
        var zip = _temp.Combine("link.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var link = archive.CreateEntry("link");
            link.ExternalAttributes = unchecked((int)(0xA1FFu << 16)); // lrwxrwxrwx
            using var writer = new StreamWriter(link.Open());
            writer.Write("/etc/passwd");
        }

        Assert.Throws<InvalidDataException>(() => SafeZip.ExtractToDirectory(zip, Target));
        Assert.False(File.Exists(Path.Combine(Target, "link")));
    }

    [Fact]
    public void More_bytes_than_the_cap_are_refused()
    {
        var zip = Zip(("a.bin", 600), ("b.bin", 600));

        Assert.Throws<InvalidDataException>(() =>
            SafeZip.ExtractToDirectory(zip, Target, new SafeZipLimits { MaxTotalBytes = 1000 }));
        Assert.False(File.Exists(Path.Combine(Target, "a.bin")));
    }

    [Fact]
    public void More_entries_than_the_cap_are_refused()
    {
        var zip = Zip(("a", 1), ("b", 1), ("c", 1));

        Assert.Throws<InvalidDataException>(() =>
            SafeZip.ExtractToDirectory(zip, Target, new SafeZipLimits { MaxEntries = 2 }));
    }

    public void Dispose() => _temp.Dispose();

    // Entries ending in '/' are folders; the others hold `size` bytes.
    private string Zip(params (string Name, int Size)[] entries)
    {
        var path = _temp.Combine($"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, size) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (!name.EndsWith('/'))
            {
                using var stream = entry.Open();
                stream.Write(new byte[size]);
            }
        }

        return path;
    }
}
