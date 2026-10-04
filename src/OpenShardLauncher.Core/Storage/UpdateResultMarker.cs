using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Packages;

namespace OpenShardLauncher.Core.Storage;

// What the launcher reports at startup about a self-update it handed off: updated, or failed and why.
public sealed record LauncherUpdateReport(bool Succeeded, string ExpectedVersion, string? Error);

// update-result.json in the launcher data folder. The running launcher writes it just before handing off, with the
// version it expects to restart as. The applier (the *new* version's exe) adds an error when it gives up. The next
// start compares its own version with the expected one, reports the result and deletes the file. A launcher that
// simply never came back (the applier timed out, or crashed) shows up as a version mismatch.
//
// The old launcher writes it and the new one reads and changes it, so the property names are a stable contract
// (docs/self-update.md). Unknown properties are kept.
public static class UpdateResultMarker
{
    public const string ExpectedVersionProperty = "expectedVersion";
    public const string PreviousVersionProperty = "previousVersion";
    public const string ErrorProperty = "error";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void WriteExpected(string path, string expectedVersion, string? previousVersion)
    {
        var marker = new JsonObject
        {
            [ExpectedVersionProperty] = expectedVersion,
            [PreviousVersionProperty] = previousVersion,
            [ErrorProperty] = null,
        };
        AtomicFile.WriteAllText(path, marker.ToJsonString(Indented));
    }

    // Used by the applier. A missing or unreadable marker is replaced by one holding only the error.
    public static void RecordError(string path, string error)
    {
        JsonObject marker;
        try
        {
            marker = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            marker = [];
        }

        marker[ErrorProperty] = error;
        AtomicFile.WriteAllText(path, marker.ToJsonString(Indented));
    }

    // The result of the last hand-off, or null when there was none. The marker is deleted, so it is reported once.
    // currentVersion is this launcher's version as text (LauncherVersion.Current).
    public static LauncherUpdateReport? Take(string path, string currentVersion, ILogger logger)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        LauncherUpdateReport? report = null;
        try
        {
            var marker = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var expected = Text(marker, ExpectedVersionProperty);
            var error = Text(marker, ErrorProperty);
            if (expected is null)
            {
                logger.LogWarning("The self-update marker {Path} has no expected version; ignoring it", path);
            }
            else if (error is null && SameVersion(expected, currentVersion))
            {
                logger.LogInformation("Updated the launcher to {Version}", expected);
                report = new LauncherUpdateReport(true, expected, null);
            }
            else
            {
                error ??= $"The launcher is still version {currentVersion}.";
                logger.LogError("The launcher update to {Version} failed: {Error}", expected, error);
                report = new LauncherUpdateReport(false, expected, error);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(e, "Could not read the self-update marker {Path}", path);
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not delete the self-update marker {Path}", path);
        }

        return report;
    }

    private static string? Text(JsonObject? marker, string name) =>
        marker?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    // "1.2" and "1.2.0" are the same version.
    private static bool SameVersion(string a, string b) =>
        Version.TryParse(a, out var x) && Version.TryParse(b, out var y)
            ? PackageVersions.Normalize(x) == PackageVersions.Normalize(y)
            : string.Equals(a, b, StringComparison.Ordinal);
}
