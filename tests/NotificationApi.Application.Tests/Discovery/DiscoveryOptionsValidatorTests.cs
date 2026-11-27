using NotificationApi.Application.Discovery;

namespace NotificationApi.Application.Tests.Discovery;

public sealed class DiscoveryOptionsValidatorTests
{
    private static readonly DiscoveryOptionsValidator Validator = new();

    private static DiscoveryOptions Valid() => new() { Host = "a0d7b954-notification-api" };

    [Fact]
    public void Defaults_with_a_host_are_valid() =>
        Validator.Validate(null, Valid()).Succeeded.ShouldBeTrue();

    [Fact]
    public void An_ip_address_is_a_valid_host() =>
        Validator.Validate(null, new DiscoveryOptions { Host = "172.30.33.5" }).Succeeded.ShouldBeTrue();

    public static TheoryData<string, Action<DiscoveryOptions>> Invalid => new()
    {
        { "Discovery:Host", o => o.Host = "" },
        { "Discovery:Host", o => o.Host = "not a host" },
        { "Discovery:Port", o => o.Port = 0 },
        { "Discovery:Port", o => o.Port = 65536 },
        { "Discovery:MaxAttempts", o => o.MaxAttempts = 0 },
        { "Discovery:MaxAttempts", o => o.MaxAttempts = 21 },
        { "Discovery:InitialRetryDelay", o => o.InitialRetryDelay = TimeSpan.Zero },
        { "Discovery:InitialRetryDelay", o => o.InitialRetryDelay = TimeSpan.FromMinutes(5) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_settings_name_the_field(string field, Action<DiscoveryOptions> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var options = Valid();
        change(options);

        var result = Validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(field);
    }

    [Fact]
    public void Null_options_throw() =>
        Should.Throw<ArgumentNullException>(() => Validator.Validate(null, null!));
}
