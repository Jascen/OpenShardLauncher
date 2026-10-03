using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(logger => logger.WriteTo.Console());
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
