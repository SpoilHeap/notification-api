using NotificationApi.Application.Webhooks;

namespace NotificationApi.Application.Tests.Webhooks;

public sealed class RegisterWebhookValidatorTests
{
    private const string Secret = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Theory]
    [InlineData("http://homeassistant:8123/api/webhook/abc")]
    [InlineData("https://ha.example.com/api/webhook/abc")]
    [InlineData("http://172.30.32.1:8123/api/webhook/abc")]
    public void Accepts_absolute_http_urls_with_a_valid_secret(string url) =>
        RegisterWebhookValidator.Validate(new RegisterWebhookCommand(url, Secret)).ShouldBeEmpty();

    [Theory]
    [InlineData("/api/webhook/abc")]
    [InlineData("api/webhook/abc")]
    [InlineData("ftp://homeassistant/api/webhook/abc")]
    [InlineData("file:///etc/passwd")]
    [InlineData("homeassistant:8123/api/webhook/abc")]
    public void AC36_Rejects_relative_and_non_http_urls(string url)
    {
        var errors = RegisterWebhookValidator.Validate(new RegisterWebhookCommand(url, Secret));

        errors.Keys.ShouldBe(["url"]);
        errors["url"].ShouldHaveSingleItem().ShouldContain("absolute http or https URL");
    }

    [Fact]
    public void AC36_Rejects_a_secret_shorter_than_32_bytes()
    {
        var errors = RegisterWebhookValidator.Validate(
            new RegisterWebhookCommand("http://homeassistant:8123/api/webhook/abc", Convert.ToBase64String(new byte[31])));

        errors.Keys.ShouldBe(["secret"]);
        errors["secret"].ShouldHaveSingleItem().ShouldContain("base64");
    }

    [Fact]
    public void Missing_fields_are_both_reported()
    {
        var errors = RegisterWebhookValidator.Validate(new RegisterWebhookCommand(null, null));

        errors.Keys.ShouldBe(["url", "secret"], ignoreOrder: true);
        errors["url"].ShouldBe(["The webhook URL is required."]);
        errors["secret"].ShouldBe(["The webhook secret is required."]);
    }

    [Fact]
    public void Rejects_overlong_urls() =>
        RegisterWebhookValidator.Validate(new RegisterWebhookCommand("http://ha/" + new string('a', 2048), Secret)).Keys.ShouldBe(["url"]);

    [Fact]
    public void The_command_never_prints_its_secret()
    {
        var command = new RegisterWebhookCommand("http://ha/", Secret);

        command.ToString().ShouldNotContain(Secret);
        command.ToString().ShouldContain("redacted");
    }
}
