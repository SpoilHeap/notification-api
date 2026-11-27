using NotificationApi.Application.Webhooks;

namespace NotificationApi.Application.Tests.Webhooks;

public sealed class WebhookSecretTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(WebhookSecret.MaxBytes)]
    public void Decodes_standard_base64_of_at_least_32_bytes(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 7)).ToArray();

        WebhookSecret.TryDecode(Convert.ToBase64String(bytes), out var key).ShouldBeTrue();

        key.ShouldBe(bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHg==")] // 31 bytes
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")] // missing padding
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwd Hh8=")] // whitespace
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwd-h8=")] // base64url alphabet
    [InlineData("this is not base64 at all, not even close......")]
    public void Rejects_anything_else(string? value) =>
        WebhookSecret.TryDecode(value, out _).ShouldBeFalse();

    [Fact]
    public void Rejects_secrets_over_the_size_limit() =>
        WebhookSecret.TryDecode(Convert.ToBase64String(new byte[WebhookSecret.MaxBytes + 1]), out _).ShouldBeFalse();
}
