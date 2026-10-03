using System.Text.Json.Serialization;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Core.Storage;

// Source-generated (trim-safe) serialization for the files Core stores.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(FeedState))]
[JsonSerializable(typeof(Dictionary<string, HashCache.Entry>))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
