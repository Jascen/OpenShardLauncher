using System.Text.Json;
using System.Text.Json.Serialization;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Shared.Feed;

// Source-generated (trim-safe) JSON for the feed formats. camelCase property names; missing properties and nulls
// where the type doesn't allow them are errors rather than silent defaults.
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(FileList))]
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(TrustedKey))]
internal sealed partial class FeedJsonContext : JsonSerializerContext;
