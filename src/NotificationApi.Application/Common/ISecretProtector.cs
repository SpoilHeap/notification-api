namespace NotificationApi.Application.Common;

/// <summary>
/// Encrypts secrets at rest (API token, provider credentials). Implemented with ASP.NET Data Protection,
/// keys in <c>/data/keys</c> (engineering_standards.md §2.4, ADR 0012).
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Fails with <see cref="SecretErrors.Unreadable"/> if the value can't be decrypted (e.g. lost or replaced keys).</summary>
    Result<string> Unprotect(string protectedValue);
}

public static class SecretErrors
{
    public static readonly Error Unreadable = new(
        "secret.unreadable",
        "A stored secret could not be decrypted. The Data Protection keys in /data/keys may be missing or replaced.");
}
