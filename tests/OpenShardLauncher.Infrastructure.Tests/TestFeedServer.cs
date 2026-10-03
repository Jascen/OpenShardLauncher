using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Infrastructure.Tests;

// A real HTTP server on loopback serving a temp feed folder with StaticFiles (Range, 206, 416), like the bundled
// server. Tests can make the next request for a path misbehave: pause it until the test releases it, cut the body off
// at a point the test controls, or send wrong bytes.
internal sealed class TestFeedServer : IAsyncDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("osl-infra-feed-");
    // Per request path ("/files.json"); each one handles a single request and returns true when it answered it.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<HttpContext, Task<bool>>>> _misbehaviours = new(StringComparer.Ordinal);
    private WebApplication? _app;

    public string Root => _root.FullName;

    public Uri Url { get; private set; } = null!;

    // Every request: path and Range header ("" when none)
    public ConcurrentQueue<(string Path, string Range)> Requests { get; } = new();

    public int RequestsFor(string path) => Requests.Count(r => r.Path == "/" + path);

    public static async Task<TestFeedServer> StartAsync()
    {
        var server = new TestFeedServer();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";
            server.Requests.Enqueue((path, context.Request.Headers.Range.ToString()));
            if (server._misbehaviours.TryGetValue(path, out var queue) && queue.TryDequeue(out var misbehave) && await misbehave(context))
            {
                return;
            }

            await next(context);
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(server.Root, ExclusionFilters.None),
            ServeUnknownFileTypes = true,
        });

        await app.StartAsync();
        server._app = app;
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        server.Url = new Uri(address + "/");
        return server;
    }

    public void WriteFile(string relativePath, byte[] content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    public FileEntry AddBlob(string name, byte[] content)
    {
        var entry = new FileEntry(name, Shared.Files.Sha256Hex.Of(content), content.Length);
        WriteFile(FeedLayout.BlobPath(entry.Sha256), content);
        return entry;
    }

    // files.json, and files.sig when signers are given
    public void PublishFileList(FileList list, params IFeedSigner[] signers)
    {
        var bytes = list.ToJsonBytes();
        WriteFile(FeedLayout.FileListPath, bytes);
        if (signers.Length > 0)
        {
            WriteFile(FeedLayout.FileListSignaturePath, System.Text.Encoding.UTF8.GetBytes(FeedSigning.CreateSignatureFile(bytes, signers)));
        }
    }

    // packages/manifest.json, and manifest.sig when signers are given
    public void PublishManifest(PackageManifest manifest, params IFeedSigner[] signers)
    {
        var bytes = manifest.ToJsonBytes();
        WriteFile(FeedLayout.ManifestPath, bytes);
        if (signers.Length > 0)
        {
            WriteFile(FeedLayout.ManifestSignaturePath, System.Text.Encoding.UTF8.GetBytes(FeedSigning.CreateSignatureFile(bytes, signers)));
        }
    }

    // The next request for the path signals `arrived`, then waits until the test completes `release` and is served
    // normally. A client that gives up meanwhile just disconnects.
    public void PauseNextRequest(string relativePath, TaskCompletionSource arrived, TaskCompletionSource release) =>
        Misbehave(relativePath, async context =>
        {
            arrived.TrySetResult();
            try
            {
#pragma warning disable VSTHRD003 // Waiting for the test's signal is the point
                await release.Task.WaitAsync(context.RequestAborted);
#pragma warning restore VSTHRD003
                return false;
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        });

    // The next request sends the first `bytes` bytes of the file, waits until the test calls release, then drops the
    // connection.
    public void CutOffNextRequest(string relativePath, int bytes, TaskCompletionSource release) =>
        Misbehave(relativePath, async context =>
        {
            var content = await File.ReadAllBytesAsync(Path.Combine(Root, relativePath));
            context.Response.ContentLength = content.Length;
            await context.Response.Body.WriteAsync(content.AsMemory(0, bytes));
            await context.Response.Body.FlushAsync();
#pragma warning disable VSTHRD003 // Waiting for the test's signal is the point
            await release.Task;
#pragma warning restore VSTHRD003
            context.Abort();
            return true;
        });

    // The next request gets the file with every byte flipped (same length).
    public void CorruptNextRequest(string relativePath) =>
        Misbehave(relativePath, async context =>
        {
            var content = await File.ReadAllBytesAsync(Path.Combine(Root, relativePath));
            context.Response.ContentLength = content.Length;
            await context.Response.Body.WriteAsync(content.Select(b => (byte)~b).ToArray());
            return true;
        });

    private void Misbehave(string relativePath, Func<HttpContext, Task<bool>> misbehaviour) =>
        _misbehaviours.GetOrAdd("/" + relativePath, _ => new ConcurrentQueue<Func<HttpContext, Task<bool>>>()).Enqueue(misbehaviour);

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _root.Delete(recursive: true);
    }
}
