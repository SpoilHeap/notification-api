using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Domain.Tests.ApiClients;

public sealed class ApiTokenTests
{
    [Fact]
    public void Generated_tokens_are_256_bit_base64url_and_unique()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => ApiToken.Generate().Value).ToList();

        foreach (var t in tokens)
        {
            t.Length.ShouldBe(ApiToken.EncodedLength);
            ApiToken.TryParse(t, out var parsed).ShouldBeTrue();
        }

        tokens.ShouldAllBe(t => !t.Contains('=') && !t.Contains('+') && !t.Contains('/'));
        tokens.Distinct().Count().ShouldBe(tokens.Count);
    }

    [Fact]
    public void A_parsed_token_keeps_its_value()
    {
        var value = ApiToken.Generate().Value;

        ApiToken.TryParse(value, out var token).ShouldBeTrue();
        token.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 44 characters
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // padded
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")] // not base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // base64, not base64url
    public void Malformed_tokens_are_rejected(string? value)
    {
        ApiToken.TryParse(value, out var token).ShouldBeFalse();
        token.ShouldBeNull();
    }

    [Fact]
    public void ToString_never_reveals_the_token()
    {
        var token = ApiToken.Generate();

        token.ToString().ShouldNotContain(token.Value);
        $"{token}".ShouldBe("ApiToken(redacted)");
    }

    [Fact]
    public void The_hash_matches_only_the_same_token()
    {
        var token = ApiToken.Generate();
        var hash = token.ComputeHash();

        hash.Matches(token).ShouldBeTrue();
        hash.Matches(ApiToken.Generate()).ShouldBeFalse();
    }
}
