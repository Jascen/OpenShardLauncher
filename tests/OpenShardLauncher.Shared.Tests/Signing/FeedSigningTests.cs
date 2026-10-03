using System.Text;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Shared.Tests.Signing;

public sealed class FeedSigningTests : IDisposable
{
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("""{ "version": 1 }""");

    private readonly P256SigningKey _key = P256SigningKey.Generate();
    private readonly P256SigningKey _otherKey = P256SigningKey.Generate();

    public void Dispose()
    {
        _key.Dispose();
        _otherKey.Dispose();
    }

    [Fact]
    public void Verify_NoSignatureFile_IsMissing()
    {
        Assert.Equal(SignatureStatus.Missing, FeedSigning.Verify(Data, null, [_key.PublicKey]));
    }

    [Fact]
    public void Verify_SignedByTrustedKey_IsValid()
    {
        var signatures = FeedSigning.CreateSignatureFile(Data, [_key]);

        Assert.StartsWith("p256:", signatures, StringComparison.Ordinal);
        Assert.Equal(SignatureStatus.Valid, FeedSigning.Verify(Data, signatures, [_key.PublicKey]));
    }

    [Fact]
    public void Verify_ChangedData_IsInvalid()
    {
        var signatures = FeedSigning.CreateSignatureFile(Data, [_key]);

        Assert.Equal(SignatureStatus.Invalid, FeedSigning.Verify([.. Data, (byte)' '], signatures, [_key.PublicKey]));
    }

    [Fact]
    public void Verify_SignedByUntrustedKey_IsInvalid()
    {
        var signatures = FeedSigning.CreateSignatureFile(Data, [_otherKey]);

        Assert.Equal(SignatureStatus.Invalid, FeedSigning.Verify(Data, signatures, [_key.PublicKey]));
    }

    [Fact]
    public void Verify_AnyOfSeveralLinesVerifying_IsValid()
    {
        // Key rotation: the feed is signed with the old and new key, the launcher trusts only one of them
        var signatures = FeedSigning.CreateSignatureFile(Data, [_otherKey, _key]);

        Assert.Equal(SignatureStatus.Valid, FeedSigning.Verify(Data, signatures, [_key.PublicKey]));
    }

    [Fact]
    public void Verify_UnknownAlgorithmLine_IsSkipped()
    {
        var signatures = "mldsa65:" + Convert.ToBase64String(new byte[64]) + "\n" + FeedSigning.CreateSignatureFile(Data, [_key]);

        Assert.Equal(SignatureStatus.Valid, FeedSigning.Verify(Data, signatures, [_key.PublicKey]));
    }

    [Fact]
    public void Verify_OnlyUnknownAlgorithms_IsInvalid()
    {
        var signatures = "mldsa65:" + Convert.ToBase64String(new byte[64]) + "\n";

        Assert.Equal(SignatureStatus.Invalid, FeedSigning.Verify(Data, signatures, [_key.PublicKey]));
    }

    [Fact]
    public void Verify_UntaggedLine_MakesTheFileInvalid()
    {
        // The signature itself is good; only the tag is missing
        var untagged = Convert.ToBase64String(_key.Sign(Data));

        Assert.Equal(SignatureStatus.Invalid, FeedSigning.Verify(Data, untagged, [_key.PublicKey]));
        Assert.Equal(SignatureStatus.Invalid,
            FeedSigning.Verify(Data, FeedSigning.CreateSignatureFile(Data, [_key]) + untagged, [_key.PublicKey]));
    }

    [Fact]
    public void Verify_EmptySignatureFile_IsInvalid()
    {
        Assert.Equal(SignatureStatus.Invalid, FeedSigning.Verify(Data, "\n", [_key.PublicKey]));
    }

    [Fact]
    public void Verify_TrustedKeyOfAnotherAlgorithm_IsNotUsed()
    {
        var signatures = FeedSigning.CreateSignatureFile(Data, [_key]);

        Assert.Equal(SignatureStatus.Invalid, FeedSigning.Verify(Data, signatures, [_key.PublicKey with { Alg = "mldsa65" }]));
    }

    [Fact]
    public void Verify_MalformedTrustedKey_DoesNotStopTheOthers()
    {
        var signatures = FeedSigning.CreateSignatureFile(Data, [_key]);

        Assert.Equal(SignatureStatus.Valid,
            FeedSigning.Verify(Data, signatures, [new TrustedKey("p256", "not base64!"), _key.PublicKey]));
    }

    [Fact]
    public void EncryptedPem_RoundTrips_AndNeedsThePassword()
    {
        var pem = _key.ExportEncryptedPem("correct horse");

        using var imported = P256SigningKey.ImportPem(pem, "correct horse");
        Assert.Equal(_key.PublicKey, imported.PublicKey);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => P256SigningKey.ImportPem(pem, "wrong"));
    }

    [Fact]
    public void TrustedKey_UsesLauncherJsonFormat()
    {
        var json = _key.PublicKey.ToJson();

        Assert.StartsWith("""{"alg":"p256","key":""", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\u", json, StringComparison.Ordinal);
        Assert.Equal(_key.PublicKey, TrustedKey.Parse(json));
    }
}
