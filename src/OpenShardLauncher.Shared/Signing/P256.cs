using System.Security.Cryptography;

namespace OpenShardLauncher.Shared.Signing;

// ECDSA over NIST P-256 with SHA-256. Signatures are the 64-byte IEEE P1363 form (r || s), public keys the
// SubjectPublicKeyInfo.
public sealed class P256SignatureAlgorithm : ISignatureAlgorithm
{
    public const string AlgTag = "p256";

    public string Tag => AlgTag;

    public bool IsSupported => true;

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            return P256SigningKey.IsP256(key) && key.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

// A P-256 private key. Stored as password-encrypted PKCS#8 PEM; it never leaves the operator's machine.
public sealed class P256SigningKey : IFeedSigner, IDisposable
{
    private const string EncryptedPemLabel = "ENCRYPTED PRIVATE KEY";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    private readonly ECDsa _key;

    private P256SigningKey(ECDsa key)
    {
        _key = key;
        PublicKey = new TrustedKey(P256SignatureAlgorithm.AlgTag, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    public string Alg => P256SignatureAlgorithm.AlgTag;

    public TrustedKey PublicKey { get; }

    public static P256SigningKey Generate() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public static bool IsEncryptedPem(string pem) => pem.Contains(EncryptedPemLabel, StringComparison.Ordinal);

    // password is only used for an encrypted PEM
    public static P256SigningKey ImportPem(string pem, string? password)
    {
        var key = ECDsa.Create();
        try
        {
            if (IsEncryptedPem(pem))
            {
                key.ImportFromEncryptedPem(pem, password ?? string.Empty);
            }
            else
            {
                key.ImportFromPem(pem);
            }

            if (!IsP256(key))
            {
                throw new CryptographicException("The key is not a P-256 key.");
            }

            return new P256SigningKey(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public string ExportEncryptedPem(string password) =>
        _key.ExportEncryptedPkcs8PrivateKeyPem(password,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));

    public byte[] Sign(ReadOnlySpan<byte> data) => _key.SignData(data, HashAlgorithmName.SHA256);

    public void Dispose() => _key.Dispose();

    // Platforms fill in the curve's OID value or only its friendly name, so accept either spelling
    internal static bool IsP256(ECDsa key) =>
        key.ExportParameters(false).Curve.Oid is { } oid
        && (oid.Value == P256Oid || oid.FriendlyName is "nistP256" or "ECDSA_P256" or "secp256r1");
}
