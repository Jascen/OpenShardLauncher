using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenShardLauncher.Server.Tests;

// The real server over a temp feed folder
public sealed class FeedServerFactory : WebApplicationFactory<Program>
{
    private readonly DirectoryInfo _feed = Directory.CreateTempSubdirectory("osl-server-feed-");

    public string FeedDirectory => _feed.FullName;

    public void WriteFeedFile(string relativePath, string content)
    {
        var path = Path.Combine(FeedDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Server:FeedDirectory", FeedDirectory);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _feed.Delete(recursive: true);
        }
    }
}
