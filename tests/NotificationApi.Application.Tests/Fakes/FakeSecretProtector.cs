using NotificationApi.Application.Common;

namespace NotificationApi.Application.Tests.Fakes;

/// <summary>Reversible, obviously-not-plaintext "encryption". <see cref="Broken"/> simulates lost keys.</summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    private const string Prefix = "protected:";

    public bool Broken { get; set; }

    /// <summary>If set, Unprotect returns this instead of the real value (simulates a tampered row).</summary>
    public string? Override { get; set; }

    public string Protect(string plaintext) => Prefix + new string(plaintext.Reverse().ToArray());

    public Result<string> Unprotect(string protectedValue)
    {
        if (Broken || !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return SecretErrors.Unreadable;
        }

        return Override ?? new string(protectedValue[Prefix.Length..].Reverse().ToArray());
    }
}
