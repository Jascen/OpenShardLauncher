using OpenShardLauncher.Server.Hosting;

// A plain static host for a feed folder produced by the Publisher. It builds, hashes and signs nothing; any static
// host or CDN can serve the same folder (see README.md).
// The content root is the exe folder, so appsettings.json is found however the server is started (e.g. as a service).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.AddOpenShardLauncherServer();

var app = builder.Build();
app.UseOpenShardLauncherFeed();
app.MapHealthChecks("/health");

app.Run();
