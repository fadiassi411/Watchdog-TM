using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Watchdog.Licensing;

public sealed record LicensePayload(
    int FormatVersion,
    Guid LicenseId,
    string CustomerName,
    string InstallationId,
    string Edition,
    int MaximumMeters,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

public sealed record LicenseDocument(string Payload, string Signature)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static LicenseDocument Create(LicensePayload payload, string privateKeyPem)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var signer = System.Security.Cryptography.ECDsa.Create();
        signer.ImportFromPem(privateKeyPem);
        var signature = signer.SignData(
            payloadBytes,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);
        return new(ToBase64Url(payloadBytes), ToBase64Url(signature));
    }

    public static LicenseDocument Parse(string json) =>
        JsonSerializer.Deserialize<LicenseDocument>(json, JsonOptions)
        ?? throw new InvalidDataException("The license file is empty or invalid.");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public bool TryVerify(string publicKeyPem, out LicensePayload? payload, out string error)
    {
        payload = null;
        error = "";
        try
        {
            if (string.IsNullOrWhiteSpace(Payload) || string.IsNullOrWhiteSpace(Signature))
            {
                error = "The license payload or signature is missing.";
                return false;
            }
            var payloadBytes = FromBase64Url(Payload);
            var signatureBytes = FromBase64Url(Signature);
            using var verifier = System.Security.Cryptography.ECDsa.Create();
            verifier.ImportFromPem(publicKeyPem);
            if (!verifier.VerifyData(
                    payloadBytes,
                    signatureBytes,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence))
            {
                error = "The digital signature is invalid.";
                return false;
            }

            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
            if (payload is null || payload.FormatVersion != 1)
            {
                payload = null;
                error = "The license format is not supported.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            error = "The license file is malformed or cannot be verified.";
            return false;
        }
    }

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
