using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Client.Composition;

// LauncherOptions come only from the embedded launcher.json, never from environment variables, arguments or an
// appsettings.json, so nobody can point a release build at another server or key. A developer's launcher.local.json next
// to the exe is layered on top in Debug builds only (see LauncherHost): its objects merge into launcher.json's, and any
// other value (including a list such as TrustedPublicKeys) replaces the one there.
public static class LauncherOptionsLoader
{
    public const string EmbeddedResourceName = "launcher.json";
    public const string LocalFileName = "launcher.local.json";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static LauncherOptions Load(string launcherFolder, bool includeLocalFile)
    {
        using var embedded = typeof(LauncherOptionsLoader).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException("The embedded launcher.json is missing.");
        var json = Parse(embedded, EmbeddedResourceName);

        var localPath = Path.Combine(Path.GetFullPath(launcherFolder), LocalFileName);
        if (includeLocalFile && File.Exists(localPath))
        {
            using var local = File.OpenRead(localPath);
            Merge(json, Parse(local, localPath));
        }

        return json.Deserialize(LauncherOptionsJsonContext.Default.LauncherOptions)
            ?? throw new InvalidDataException("launcher.json is empty.");
    }

    private static JsonObject Parse(Stream stream, string name) =>
        JsonNode.Parse(stream, documentOptions: DocumentOptions) as JsonObject
            ?? throw new InvalidDataException($"{name} must be a JSON object.");

    private static void Merge(JsonObject target, JsonObject overrides)
    {
        foreach (var (name, value) in overrides.ToList())
        {
            if (value is JsonObject child && target[name] is JsonObject existing)
            {
                Merge(existing, child);
            }
            else
            {
                overrides.Remove(name);
                target[name] = value;
            }
        }
    }
}

// Source-generated (trim-safe). Case-insensitive, so trusted keys can be pasted as "publisher keygen" prints them
// ({ "alg": ..., "key": ... }).
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(LauncherOptions))]
internal sealed partial class LauncherOptionsJsonContext : JsonSerializerContext;
