using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Core.Tests.Storage;

public sealed class UpdateResultMarkerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private string Marker => _temp.Combine(LauncherDataFolder.UpdateResultFileName);

    [Fact]
    public void Starting_as_the_expected_version_reports_success_once()
    {
        UpdateResultMarker.WriteExpected(Marker, "1.2.0", "1.1.0");

        var report = UpdateResultMarker.Take(Marker, "1.2", NullLogger.Instance);

        Assert.Equal(new LauncherUpdateReport(true, "1.2.0", null), report);
        Assert.Null(UpdateResultMarker.Take(Marker, "1.2", NullLogger.Instance));
    }

    [Fact]
    public void Starting_as_another_version_reports_failure()
    {
        // The applier never got to run (or timed out without recording anything): the old launcher starts again.
        UpdateResultMarker.WriteExpected(Marker, "1.2.0", "1.1.0");

        var report = UpdateResultMarker.Take(Marker, "1.1.0", NullLogger.Instance);

        Assert.NotNull(report);
        Assert.False(report.Succeeded);
        Assert.Equal("1.2.0", report.ExpectedVersion);
        Assert.Contains("1.1.0", report.Error);
    }

    [Fact]
    public void An_error_the_applier_recorded_is_the_reason()
    {
        UpdateResultMarker.WriteExpected(Marker, "1.2.0", "1.1.0");
        UpdateResultMarker.RecordError(Marker, "The old launcher didn't exit within 10 seconds.");

        var report = UpdateResultMarker.Take(Marker, "1.1.0", NullLogger.Instance);

        Assert.Equal(new LauncherUpdateReport(false, "1.2.0", "The old launcher didn't exit within 10 seconds."), report);
    }

    [Fact]
    public void Recording_an_error_keeps_the_other_properties()
    {
        // A newer launcher may add properties; an older applier must keep them.
        File.WriteAllText(Marker, """{ "expectedVersion": "2.0.0", "somethingNew": 5 }""");

        UpdateResultMarker.RecordError(Marker, "boom");

        var marker = JsonNode.Parse(File.ReadAllText(Marker))!.AsObject();
        Assert.Equal("2.0.0", (string?)marker["expectedVersion"]);
        Assert.Equal(5, (int?)marker["somethingNew"]);
        Assert.Equal("boom", (string?)marker["error"]);
    }

    [Fact]
    public void No_marker_reports_nothing() => Assert.Null(UpdateResultMarker.Take(Marker, "1.0.0", NullLogger.Instance));

    public void Dispose() => _temp.Dispose();
}
