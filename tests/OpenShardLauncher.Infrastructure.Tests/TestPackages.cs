using System.IO.Compression;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Infrastructure.Tests;

// Package zips for the feed server: built in memory and written to packages/ under their manifest name.
internal static class TestPackages
{
    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    // packages/{role}-{version}.{rid}.zip on the server, and its manifest entry.
    public static PackageEntry AddPackage(this TestFeedServer server, string role, string version, byte[] zip)
    {
        var file = PackageFileName.Format(role, version, TestLauncher.Platform);
        server.WriteFile(FeedLayout.PackagePath(file), zip);
        return new PackageEntry(role, version, TestLauncher.Platform, file, Sha256Hex.Of(zip), zip.Length);
    }
}
