using NotificationApi.Domain.Instances;

namespace NotificationApi.Domain.Tests.Instances;

public sealed class InstanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_instance_has_a_random_id_and_revision_1()
    {
        var instance = Instance.Create(Now);

        instance.Id.Value.Version.ShouldBe(4);
        instance.RecipientsRevision.ShouldBe("1");
        instance.RecipientsRevisionNumber.ShouldBe(Instance.InitialRecipientsRevision);
        instance.CreatedAt.ShouldBe(Now);
        Instance.Create(Now).Id.ShouldNotBe(instance.Id);
    }

    [Fact]
    public void Bumping_the_revision_returns_the_next_value()
    {
        var instance = Instance.Create(Now);

        instance.BumpRecipientsRevision().ShouldBe("2");
        instance.BumpRecipientsRevision().ShouldBe("3");
        instance.RecipientsRevision.ShouldBe("3");
    }
}

public sealed class InstanceIdTests
{
    [Fact]
    public void New_ids_are_lower_case_version_4_uuids()
    {
        var id = InstanceId.New();

        id.ToString().ShouldMatch("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
        InstanceId.New().ShouldNotBe(id);
    }

    [Fact]
    public void Round_trips_through_its_text()
    {
        var id = InstanceId.New();

        InstanceId.Parse(id.ToString()).ShouldBe(id);
        InstanceId.TryParse(id.ToString().ToUpperInvariant(), out var upper).ShouldBeTrue();
        upper.ShouldBe(id);
        InstanceId.From(id.Value).ShouldBe(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("8d3f0c3e5b1a4c2e9f001a2b3c4d5e6f")] // no hyphens
    [InlineData("0190f3a8-c1d2-7a4b-9e5f-60718293a4b5")] // version 7
    [InlineData("8d3f0c3e-5b1a-4c2e-7f00-1a2b3c4d5e6f")] // NCS variant
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Rejects_anything_but_a_version_4_uuid(string? value)
    {
        InstanceId.TryParse(value, out _).ShouldBeFalse();
        if (value is not null)
        {
            Should.Throw<FormatException>(() => InstanceId.Parse(value));
        }
    }

    [Fact]
    public void From_rejects_other_uuid_versions() =>
        Should.Throw<ArgumentException>(() => InstanceId.From(Guid.CreateVersion7()));
}
