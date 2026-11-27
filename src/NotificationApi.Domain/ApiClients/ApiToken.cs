using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace NotificationApi.Domain.ApiClients;

/// <summary>
/// A plaintext API bearer token: 256 random bits, base64url-encoded without padding (43 characters).
/// <see cref="ToString"/> is redacted so the token can't leak into logs by accident (engineering_standards.md §5).
/// </summary>
public sealed class ApiToken
{
    public const int SizeInBytes = 32;

    /// <summary>Length of the encoded token: 32 bytes as unpadded base64url.</summary>
    public const int EncodedLength = 43;

    private ApiToken(string value) => Value = value;

    /// <summary>The plaintext token. Only for the discovery message, the bearer header and the manual-setup page.</summary>
    public string Value { get; }

    public static ApiToken Generate() => new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SizeInBytes)));

    /// <summary>Accepts only a well-formed token, so malformed input is rejected before any hashing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out ApiToken? token)
    {
        token = null;
        if (value is null || value.Length != EncodedLength || !Base64Url.IsValid(value, out var decodedLength) || decodedLength != SizeInBytes)
        {
            return false;
        }

        token = new ApiToken(value);
        return true;
    }

    /// <summary>SHA-256 of the token's UTF-8 text. This is what gets stored and compared.</summary>
    public ApiTokenHash ComputeHash() => ApiTokenHash.Of(this);

    public override string ToString() => "ApiToken(redacted)";
}
