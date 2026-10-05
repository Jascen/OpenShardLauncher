using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Server.Tests;

public sealed class LatestPackageTests
{
    [Fact]
    public async Task RedirectsToTheNewestPackage_AndTheTargetDownloads()
    {
        using var factory = new FeedServerFactory();
        const string Zip = "zip bytes";
        var sha = Sha256Hex.Of(System.Text.Encoding.UTF8.GetBytes(Zip));
        factory.WriteFeedFile(FeedLayout.PackagePath("launcher-1.2.0.win-x64.zip"), Zip);
        factory.WriteFeedFile(FeedLayout.ManifestPath, $$"""
            { "generated": "2026-10-03T13:51:55+00:00", "packages": [
              { "role": "launcher", "version": "1.2.0", "rid": "win-x64", "file": "launcher-1.2.0.win-x64.zip", "sha256": "{{sha}}", "size": {{Zip.Length}} } ] }
            """);
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var redirect = await client.GetAsync(new Uri("/latest/launcher/win-x64", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/packages/launcher-1.2.0.win-x64.zip", redirect.Headers.Location?.OriginalString);
        Assert.True(redirect.Headers.CacheControl?.NoCache);

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/latest/launcher/win-x64"), ct);
        Assert.Equal(HttpStatusCode.Redirect, head.StatusCode);

        using var zip = await client.GetAsync(redirect.Headers.Location, ct);
        Assert.Equal(Zip, await zip.Content.ReadAsStringAsync(ct));

        foreach (var missing in new[] { "/latest/launcher/linux-x64", "/latest/client/win-x64", "/latest/tazuo/win-x64", "/latest/other/win-x64", "/latest/launcher" })
        {
            using var response = await client.GetAsync(new Uri(missing, UriKind.Relative), ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    public async Task NotFound_WithoutAReadableManifest(string? manifest)
    {
        using var factory = new FeedServerFactory();
        if (manifest is not null)
        {
            factory.WriteFeedFile(FeedLayout.ManifestPath, manifest);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(new Uri("/latest/launcher/win-x64", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
