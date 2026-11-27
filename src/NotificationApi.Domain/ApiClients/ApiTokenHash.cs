using System.Security.Cryptography;
using System.Text;

namespace NotificationApi.Domain.ApiClients;

/// <summary>SHA-256 hash of an <see cref="ApiToken"/>. Compared in constant time (engineering_standards.md §6).</summary>
public sealed class ApiTokenHash
{
    public const int SizeInBytes = SHA256.HashSizeInBytes;

    private readonly byte[] _bytes;

    private ApiTokenHash(byte[] bytes) => _bytes = bytes;

    public static ApiTokenHash Of(ApiToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ApiTokenHash(SHA256.HashData(Encoding.UTF8.GetBytes(token.Value)));
    }

    /// <summary>Rehydrates a stored hash.</summary>
    public static ApiTokenHash FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != SizeInBytes)
        {
            throw new ArgumentException($"An API token hash is {SizeInBytes} bytes.", nameof(bytes));
        }

        return new ApiTokenHash((byte[])bytes.Clone());
    }

    /// <summary>True if <paramref name="token"/> hashes to this value. Constant time in the hash length.</summary>
    public bool Matches(ApiToken token) => CryptographicOperations.FixedTimeEquals(_bytes, Of(token)._bytes);

    public byte[] ToArray() => (byte[])_bytes.Clone();
}
