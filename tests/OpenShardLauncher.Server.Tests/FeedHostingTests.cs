using System.Net;
using OpenShardLauncher.Server.Feed;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Server.Tests;

public sealed class FeedHostingTests(FeedServerFactory factory) : IClassFixture<FeedServerFactory>
{
    [Fact]
    public async Task ServesTheFeedLayout_WithTheRightHeaders_AndNothingElse()
    {
        const string Art = "contents of art.mul";
        var sha = Sha256Hex.Of(System.Text.Encoding.UTF8.GetBytes(Art));
        factory.WriteFeedFile(FeedLayout.BlobPath(sha), Art);
        factory.WriteFeedFile(FeedLayout.FileListPath, $$"""{ "version": 1, "files": [ { "name": "art.mul", "sha256": "{{sha}}", "size": {{Art.Length}} } ], "removed": [] }""");
        factory.WriteFeedFile("art.mul", Art); // In the feed folder but outside the layout
        factory.WriteFeedFile(".publisher-state/hash-cache.json", "{}");
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();

        using var blob = await client.GetAsync(new Uri("/" + FeedLayout.BlobPath(sha), UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, blob.StatusCode);
        Assert.Equal(Art, await blob.Content.ReadAsStringAsync(ct));
        Assert.Equal("application/octet-stream", blob.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FeedStaticFiles.BlobCacheControl, blob.Headers.CacheControl?.ToString());

        using var list = await client.GetAsync(new Uri("/files.json", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal("application/json", list.Content.Headers.ContentType?.MediaType);
        Assert.True(list.Headers.CacheControl?.NoCache);

        foreach (var outside in new[] { "/art.mul", "/.publisher-state/hash-cache.json", "/blobs/", "/FILES.JSON" })
        {
            using var response = await client.GetAsync(new Uri(outside, UriKind.Relative), ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
