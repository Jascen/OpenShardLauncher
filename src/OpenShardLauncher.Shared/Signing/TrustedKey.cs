using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Shared.Signing;

// A public key the launcher trusts, as written in launcher.json: { "alg": "p256", "key": "<base64>" }.
// For p256 the key is the base64 SubjectPublicKeyInfo.
public sealed record TrustedKey(string Alg, string Key)
{
    // Base64 '+' and '/' unescaped, so the line can be pasted into launcher.json as is
    private static readonly JsonSerializerOptions PasteableJson = new(FeedJsonContext.Default.Options)
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, (JsonTypeInfo<TrustedKey>)PasteableJson.GetTypeInfo(typeof(TrustedKey)));

    public static TrustedKey Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, FeedJsonContext.Default.TrustedKey)
                ?? throw new InvalidDataException("The public key is empty.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Not a public key in launcher.json format: {e.Message}", e);
        }
    }
}
