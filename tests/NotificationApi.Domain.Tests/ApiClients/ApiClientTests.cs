using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Domain.Tests.ApiClients;

public sealed class ApiClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_stores_the_hash_and_the_protected_token_but_not_the_plaintext()
    {
        var token = ApiToken.Generate();

        var client = ApiClient.Create("home_assistant", token, "protected-value", Now);

        client.Id.Value.ShouldNotBe(Guid.Empty);
        client.Name.ShouldBe("home_assistant");
        client.ProtectedToken.ShouldBe("protected-value");
        client.TokenHash.Matches(token).ShouldBeTrue();
        client.CreatedAt.ShouldBe(Now);
        client.LastUsedAt.ShouldBeNull();
        client.WebhookUrl.ShouldBeNull();
        client.ProtectedWebhookSecret.ShouldBeNull();
    }

    [Fact]
    public void Accepts_only_its_own_token()
    {
        var token = ApiToken.Generate();
        var client = ApiClient.Create("home_assistant", token, "protected", Now);

        client.Accepts(token).ShouldBeTrue();
        client.Accepts(ApiToken.Generate()).ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => client.Accepts(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_requires_a_name(string name) =>
        Should.Throw<ArgumentException>(() => ApiClient.Create(name, ApiToken.Generate(), "protected", Now));

    [Fact]
    public void Create_rejects_long_names() =>
        Should.Throw<ArgumentException>(() => ApiClient.Create(new string('a', ApiClient.MaxNameLength + 1), ApiToken.Generate(), "protected", Now));

    [Fact]
    public void Create_requires_a_token_and_its_protected_form()
    {
        Should.Throw<ArgumentNullException>(() => ApiClient.Create("ha", null!, "protected", Now));
        Should.Throw<ArgumentException>(() => ApiClient.Create("ha", ApiToken.Generate(), " ", Now));
    }

    [Fact]
    public void RecordUse_refreshes_last_used_at_most_once_per_resolution()
    {
        var client = ApiClient.Create("home_assistant", ApiToken.Generate(), "protected", Now);

        client.RecordUse(Now).ShouldBeTrue();
        client.RecordUse(Now + ApiClient.LastUsedResolution - TimeSpan.FromSeconds(1)).ShouldBeFalse();
        client.LastUsedAt.ShouldBe(Now);

        client.RecordUse(Now + ApiClient.LastUsedResolution).ShouldBeTrue();
        client.LastUsedAt.ShouldBe(Now + ApiClient.LastUsedResolution);
    }

    [Fact]
    public void Ids_are_unique_and_print_as_guids()
    {
        var a = ApiClientId.New();
        var b = ApiClientId.New();

        a.ShouldNotBe(b);
        a.ToString().ShouldBe(a.Value.ToString());
    }

    [Fact]
    public void RegisterWebhook_sets_the_url_secret_and_time_and_replaces_an_earlier_one()
    {
        var client = ApiClient.Create("home_assistant", ApiToken.Generate(), "protected", Now);
        client.HasWebhook.ShouldBeFalse();

        client.RegisterWebhook(new Uri("http://homeassistant:8123/api/webhook/first"), "secret-1", Now);
        client.RegisterWebhook(new Uri("https://ha.example/api/webhook/second"), "secret-2", Now.AddMinutes(1));

        client.HasWebhook.ShouldBeTrue();
        client.WebhookUrl.ShouldBe(new Uri("https://ha.example/api/webhook/second"));
        client.ProtectedWebhookSecret.ShouldBe("secret-2");
        client.WebhookRegisteredAt.ShouldBe(Now.AddMinutes(1));
    }

    [Theory]
    [InlineData("ftp://homeassistant/api/webhook/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("mailto:someone@example.com")]
    public void RegisterWebhook_rejects_non_http_urls(string url)
    {
        var client = ApiClient.Create("home_assistant", ApiToken.Generate(), "protected", Now);

        Should.Throw<ArgumentException>(() => client.RegisterWebhook(new Uri(url), "secret", Now));
        client.HasWebhook.ShouldBeFalse();
    }

    [Fact]
    public void RegisterWebhook_requires_a_url_and_a_protected_secret()
    {
        var client = ApiClient.Create("home_assistant", ApiToken.Generate(), "protected", Now);

        Should.Throw<ArgumentNullException>(() => client.RegisterWebhook(null!, "secret", Now));
        Should.Throw<ArgumentException>(() => client.RegisterWebhook(new Uri("http://ha/"), " ", Now));
        Should.Throw<ArgumentException>(() => client.RegisterWebhook(new Uri("/relative", UriKind.Relative), "secret", Now));
    }

    [Fact]
    public void IsValidWebhookUrl_accepts_only_absolute_http_urls_within_the_length_limit()
    {
        ApiClient.IsValidWebhookUrl(new Uri("http://homeassistant:8123/api/webhook/x")).ShouldBeTrue();
        ApiClient.IsValidWebhookUrl(new Uri("https://ha.example/api/webhook/x")).ShouldBeTrue();
        ApiClient.IsValidWebhookUrl(null).ShouldBeFalse();
        ApiClient.IsValidWebhookUrl(new Uri("/api/webhook/x", UriKind.Relative)).ShouldBeFalse();
        ApiClient.IsValidWebhookUrl(new Uri("http://ha/" + new string('a', ApiClient.MaxWebhookUrlLength))).ShouldBeFalse();
    }

    [Fact]
    public void RemoveWebhook_clears_the_registration_and_reports_whether_anything_changed()
    {
        var client = ApiClient.Create("home_assistant", ApiToken.Generate(), "protected", Now);
        client.RemoveWebhook().ShouldBeFalse();
        client.RegisterWebhook(new Uri("http://homeassistant:8123/api/webhook/x"), "secret", Now);

        client.RemoveWebhook().ShouldBeTrue();

        client.HasWebhook.ShouldBeFalse();
        client.WebhookUrl.ShouldBeNull();
        client.ProtectedWebhookSecret.ShouldBeNull();
        client.WebhookRegisteredAt.ShouldBeNull();
        client.RemoveWebhook().ShouldBeFalse();
    }
}
