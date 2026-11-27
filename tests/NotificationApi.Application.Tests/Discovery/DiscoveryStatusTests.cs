using Microsoft.Extensions.Options;
using NotificationApi.Application.Common;
using NotificationApi.Application.Discovery;

namespace NotificationApi.Application.Tests.Discovery;

public sealed class DiscoveryStatusTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Store_starts_pending_and_keeps_the_latest_status()
    {
        var store = new DiscoveryStatusStore();
        store.Current.ShouldBe(DiscoveryStatus.Pending);

        store.Set(DiscoveryStatus.Published("uuid", 1, At));

        store.Current.ShouldBe(new DiscoveryStatus(DiscoveryState.Published, "uuid", null, 1, At));
        Should.Throw<ArgumentNullException>(() => store.Set(null!));
    }

    [Fact]
    public void Factories_set_the_state()
    {
        DiscoveryStatus.Skipped("why", At).ShouldBe(new DiscoveryStatus(DiscoveryState.Skipped, null, "why", 0, At));
        DiscoveryStatus.Retrying("err", 2, At).ShouldBe(new DiscoveryStatus(DiscoveryState.Pending, null, "err", 2, At));
        DiscoveryStatus.Failed("err", 3, At).ShouldBe(new DiscoveryStatus(DiscoveryState.Failed, null, "err", 3, At));
    }

    [Fact]
    public void Integration_details_combine_the_status_and_the_manual_setup_values()
    {
        var store = new DiscoveryStatusStore();
        store.Set(DiscoveryStatus.Skipped("why", At));
        var options = Options.Create(new DiscoveryOptions { Host = "addon-host", Port = 8080 });

        var details = new GetIntegrationDetails(store, options).Handle();

        details.ShouldBe(new IntegrationDetails(store.Current, "addon-host", 8080));
    }

    public static TheoryData<Error, bool> Errors => new()
    {
        { DiscoveryErrors.Unreachable("x"), true },
        { DiscoveryErrors.Timeout(), true },
        { DiscoveryErrors.ServerError(503, "x"), true },
        { DiscoveryErrors.InvalidResponse("x"), true },
        { DiscoveryErrors.Rejected(403, "x"), false },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Only_rejections_are_permanent(Error error, bool transient) =>
        DiscoveryErrors.IsTransient(error).ShouldBe(transient);

    [Fact]
    public void IsTransient_requires_an_error() =>
        Should.Throw<ArgumentNullException>(() => DiscoveryErrors.IsTransient(null!));
}
