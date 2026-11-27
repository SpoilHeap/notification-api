using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using NotificationApi.Application.Common;

namespace NotificationApi.Infrastructure.Security;

/// <summary><see cref="ISecretProtector"/> over ASP.NET Data Protection, with keys in <c>/data/keys</c> (ADR 0012).</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    /// <summary>Changing the purpose makes every stored secret unreadable, so it is versioned and never edited.</summary>
    public const string Purpose = "NotificationApi.Secrets.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    public Result<string> Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return SecretErrors.Unreadable;
        }
    }
}
