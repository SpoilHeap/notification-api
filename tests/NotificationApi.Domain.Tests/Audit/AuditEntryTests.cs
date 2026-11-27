using NotificationApi.Domain.Audit;

namespace NotificationApi.Domain.Tests.Audit;

public sealed class AuditEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly AuditActor Actor = new("8f7e6d5c4b3a", "Alice Admin");

    [Fact]
    public void AC82_Records_who_did_what_and_when()
    {
        var entry = AuditEntry.Record(Actor, AuditAction.Create, AuditEntityType.Contact, "contact:bob", "Created contact:bob (Bob)", Now);

        entry.HaUserId.ShouldBe("8f7e6d5c4b3a");
        entry.HaUserDisplayName.ShouldBe("Alice Admin");
        entry.Action.ShouldBe(AuditAction.Create);
        entry.EntityType.ShouldBe(AuditEntityType.Contact);
        entry.EntityKey.ShouldBe("contact:bob");
        entry.Summary.ShouldBe("Created contact:bob (Bob)");
        entry.OccurredAt.ShouldBe(Now);
    }

    [Fact]
    public void AC82_A_full_phone_number_never_reaches_the_summary() =>
        AuditEntry.Record(Actor, AuditAction.Update, AuditEntityType.Endpoint, "contact:bob", "SMS number set to +353861234567", Now)
            .Summary.ShouldBe("SMS number set to +3538****567");

    [Fact]
    public void Long_values_are_truncated()
    {
        var entry = AuditEntry.Record(Actor, AuditAction.Update, AuditEntityType.Group, new string('k', 300), new string('s', 600), Now);

        entry.EntityKey.Length.ShouldBe(AuditEntry.MaxEntityKeyLength);
        entry.Summary.Length.ShouldBe(AuditEntry.MaxSummaryLength);
        entry.Summary.ShouldEndWith("…");
    }

    [Fact]
    public void The_actor_falls_back_to_the_user_id_and_is_bounded()
    {
        new AuditActor("id-1", " ").DisplayName.ShouldBe("id-1");
        new AuditActor(" id-1 ", " Alice ").ShouldBe(new AuditActor("id-1", "Alice"));
        new AuditActor(new string('x', 300), "A").HaUserId.Length.ShouldBe(AuditActor.MaxLength);
        Should.Throw<ArgumentException>(() => new AuditActor(" ", "Alice"));
    }

    [Fact]
    public void Requires_a_key_and_a_summary()
    {
        Should.Throw<ArgumentException>(() => AuditEntry.Record(Actor, AuditAction.Create, AuditEntityType.Contact, " ", "x", Now));
        Should.Throw<ArgumentException>(() => AuditEntry.Record(Actor, AuditAction.Create, AuditEntityType.Contact, "k", "", Now));
        Should.Throw<ArgumentNullException>(() => AuditEntry.Record(null!, AuditAction.Create, AuditEntityType.Contact, "k", "x", Now));
    }
}
