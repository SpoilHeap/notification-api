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
}
