using NotificationApi.Application.Common;
using NotificationApi.Application.Events;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Tests.Contacts;

public sealed class ContactUseCaseTests
{
    private const string Number = "+353861234567";

    private readonly RecipientsHarness _h = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AC50_Creating_a_contact_stores_it_with_its_handle_and_name()
    {
        var result = await _h.CreateContact.HandleAsync(RecipientsHarness.Actor, new("alice", "  Alice  "), _ct);

        result.Value.ToString().ShouldBe("contact:alice");
        var alice = _h.Store.Contacts.ShouldHaveSingleItem();
        alice.Name.Value.ShouldBe("Alice");
        alice.CreatedAt.ShouldBe(_h.Time.GetUtcNow());
    }

    [Fact]
    public async Task A_prefixed_handle_is_accepted()
    {
        var result = await _h.CreateContact.HandleAsync(RecipientsHarness.Actor, new("contact:alice", "Alice"), _ct);

        result.Value.ToString().ShouldBe("contact:alice");
    }

    [Theory]
    [InlineData(null, "Alice", RecipientFields.Handle)]
    [InlineData("Alice", "Alice", RecipientFields.Handle)]
    [InlineData("al ice", "Alice", RecipientFields.Handle)]
    [InlineData("group:alice", "Alice", RecipientFields.Handle)]
    [InlineData("alice", "", RecipientFields.Name)]
    [InlineData("alice", null, RecipientFields.Name)]
    public async Task AC50_Invalid_input_is_rejected_with_a_field_error(string? handle, string? name, string field)
    {
        var result = await _h.CreateContact.HandleAsync(RecipientsHarness.Actor, new(handle, name), _ct);

        result.Error.Code.ShouldBe(Error.ValidationCode);
        result.Error.FieldErrors.Keys.ShouldBe([field]);
        _h.Store.Contacts.ShouldBeEmpty();
        _h.Audit.Entries.ShouldBeEmpty();
        _h.Journal.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Both_fields_are_reported_together()
    {
        var result = await _h.CreateContact.HandleAsync(RecipientsHarness.Actor, new("", new string('x', 101)), _ct);

        result.Error.FieldErrors.Keys.ShouldBe([RecipientFields.Handle, RecipientFields.Name], ignoreOrder: true);
        result.Error.FieldErrors[RecipientFields.Name].ShouldHaveSingleItem().ShouldContain("100");
    }

    [Fact]
    public async Task AC57_Contact_handles_are_unique_but_may_match_a_group_handle()
    {
        await _h.ContactAsync("alice", "Alice");
        await _h.GroupAsync("alice", "Alice's people");

        var duplicate = await _h.CreateContact.HandleAsync(RecipientsHarness.Actor, new("alice", "Other Alice"), _ct);

        duplicate.Error.FieldErrors[RecipientFields.Handle].ShouldHaveSingleItem().ShouldContain("already exists");
        _h.Store.Contacts.ShouldHaveSingleItem();
        _h.Store.Groups.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AC32_Creating_commits_once_then_queues_recipients_changed_with_the_new_revision()
    {
        await _h.ContactAsync("alice", "Alice");

        // EnsureInstance creates the instance on first use, then the change is committed, then the event is queued.
        _h.Journal.Steps.ShouldBe(["save", "save", "enqueue recipients.changed"]);
        var queued = _h.Journal.Queued.ShouldHaveSingleItem();
        queued.Data.ShouldBe(new RecipientsChangedData("2"));
        (await _h.RevisionAsync()).ShouldBe("2");
    }

    [Fact]
    public async Task AC32_Nothing_is_queued_when_the_commit_fails()
    {
        await _h.EnsureInstance.HandleAsync(_ct);
        _h.Journal.FailNextSave = true;

        await Should.ThrowAsync<InvalidOperationException>(() =>
            _h.CreateContact.HandleAsync(RecipientsHarness.Actor, new("alice", "Alice"), _ct));

        _h.Journal.Queued.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC43_Rename_bumps_the_revision_and_an_unchanged_name_does_not()
    {
        await _h.ContactAsync("alice", "Alice");

        (await _h.RenameContact.HandleAsync(RecipientsHarness.Actor, "alice", "Alice Smith", _ct)).Value.ShouldBeTrue();
        (await _h.RevisionAsync()).ShouldBe("3");
        (await _h.RenameContact.HandleAsync(RecipientsHarness.Actor, "alice", " Alice Smith ", _ct)).Value.ShouldBeFalse();

        (await _h.RevisionAsync()).ShouldBe("3");
        _h.Journal.Queued.Count.ShouldBe(2);
        _h.Store.Contacts.Single().Handle.ToString().ShouldBe("contact:alice");
        _h.Audit.Entries.Last().Summary.ShouldBe("Renamed from 'Alice' to 'Alice Smith'");
    }

    [Fact]
    public async Task Rename_validates_the_name_and_finds_the_contact()
    {
        await _h.ContactAsync("alice", "Alice");

        (await _h.RenameContact.HandleAsync(RecipientsHarness.Actor, "alice", " ", _ct)).Error.FieldErrors.Keys.ShouldBe([RecipientFields.Name]);
        (await _h.RenameContact.HandleAsync(RecipientsHarness.Actor, "bob", "Bob", _ct)).Error.Code.ShouldBe(RecipientErrors.ContactNotFoundCode);
        (await _h.RenameContact.HandleAsync(RecipientsHarness.Actor, "Not A Handle", "Bob", _ct)).Error.Code.ShouldBe(RecipientErrors.ContactNotFoundCode);
    }

    [Fact]
    public async Task AC52_An_sms_number_is_normalised_verified_and_does_not_bump_the_revision()
    {
        await _h.ContactAsync("alice", "Alice");
        var queued = _h.Journal.Queued.Count;

        var result = await _h.SetSms.HandleAsync(RecipientsHarness.Actor, "alice", "+353 86 123 4567", _ct);

        result.Value.Value.ShouldBe(Number);
        var sms = _h.Store.Contacts.Single().EndpointFor(ChannelType.Sms).ShouldNotBeNull();
        sms.Address.ShouldBe(Number);
        sms.VerifiedAt.ShouldBe(_h.Time.GetUtcNow());
        (await _h.RevisionAsync()).ShouldBe("2");
        _h.Journal.Queued.Count.ShouldBe(queued);
        _h.Journal.Steps.Last().ShouldBe("save");
    }

    [Theory]
    [InlineData("0861234567")]
    [InlineData("+35386")]
    [InlineData("")]
    public async Task AC52_An_invalid_sms_number_is_rejected_with_a_field_error(string number)
    {
        await _h.ContactAsync("alice", "Alice");

        var result = await _h.SetSms.HandleAsync(RecipientsHarness.Actor, "alice", number, _ct);

        result.Error.FieldErrors.Keys.ShouldBe([RecipientFields.PhoneNumber]);
        _h.Store.Contacts.Single().Endpoints.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC82_AC74_Sms_changes_are_audited_and_logged_with_masked_numbers_only()
    {
        await _h.ContactAsync("alice", "Alice", Number);
        await _h.SetSms.HandleAsync(RecipientsHarness.Actor, "alice", "+353879999999", _ct);
        await _h.SetSms.HandleAsync(RecipientsHarness.Actor, "alice", "+353879999999", _ct); // unchanged: no entry
        await _h.RemoveEndpoint.HandleAsync(RecipientsHarness.Actor, "alice", ChannelType.Sms, _ct);

        _h.Audit.Entries.Where(e => e.EntityType == AuditEntityType.Endpoint).Select(e => (e.Action, e.Summary)).ShouldBe(
        [
            (AuditAction.Create, "SMS number set to +3538****567"),
            (AuditAction.Update, "SMS number changed from +3538****567 to +3538****999"),
            (AuditAction.Delete, "SMS endpoint +3538****999 removed (and from routing)"),
        ]);
        _h.Audit.Entries.ShouldAllBe(e => !e.Summary.Contains("+35386") && !e.Summary.Contains("+35387"));
        _h.Logs.ShouldNotBeEmpty();
        _h.Logs.ShouldAllBe(l => !l.Contains("+35386") && !l.Contains("+35387"));
    }

    [Fact]
    public async Task Removing_a_missing_endpoint_changes_nothing()
    {
        await _h.ContactAsync("alice", "Alice");
        var saves = _h.Journal.Saves;

        (await _h.RemoveEndpoint.HandleAsync(RecipientsHarness.Actor, "alice", ChannelType.Sms, _ct)).Value.ShouldBeFalse();
        (await _h.RemoveEndpoint.HandleAsync(RecipientsHarness.Actor, "bob", ChannelType.Sms, _ct)).Error.Code.ShouldBe(RecipientErrors.ContactNotFoundCode);

        _h.Journal.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task AC53_Routing_is_set_for_every_priority_and_does_not_bump_the_revision()
    {
        await _h.ContactAsync("alice", "Alice", Number);

        var result = await _h.SetRouting.HandleAsync(RecipientsHarness.Actor, "alice", new Dictionary<Priority, IReadOnlyList<ChannelType>>
        {
            [Priority.Normal] = [ChannelType.Sms],
            [Priority.High] = [ChannelType.Sms],
        }, _ct);

        result.Value.ShouldBeTrue();
        var routing = _h.Store.Contacts.Single().Routing;
        routing.ChannelsFor(Priority.High).ShouldBe([ChannelType.Sms]);
        routing.ChannelsFor(Priority.Low).ShouldBeEmpty();
        (await _h.RevisionAsync()).ShouldBe("2");
        _h.Audit.Entries.Last().Summary.ShouldBe("Routing set to low: none; normal: SMS; high: SMS; critical: none");

        (await _h.SetRouting.HandleAsync(RecipientsHarness.Actor, "alice", new Dictionary<Priority, IReadOnlyList<ChannelType>>
        {
            [Priority.Normal] = [ChannelType.Sms],
            [Priority.High] = [ChannelType.Sms],
        }, _ct)).Value.ShouldBeFalse();
    }

    [Fact]
    public async Task AC53_Routing_to_a_channel_without_an_endpoint_is_rejected()
    {
        await _h.ContactAsync("alice", "Alice", Number);

        var result = await _h.SetRouting.HandleAsync(RecipientsHarness.Actor, "alice", new Dictionary<Priority, IReadOnlyList<ChannelType>>
        {
            [Priority.High] = [ChannelType.Telegram, ChannelType.Sms],
            [Priority.Low] = [ChannelType.Sms, ChannelType.Sms],
            [(Priority)42] = [ChannelType.Sms],
        }, _ct);

        result.Error.FieldErrors[RecipientFields.Routing].ShouldBe(
        [
            "high: Telegram isn't set up for this contact, so it can't be used.",
            "low: each channel can be chosen once.",
            "Unknown priority or channel.",
        ], ignoreOrder: true);
        _h.Store.Contacts.Single().Routing.ByPriority.ShouldBeEmpty();
        (await _h.SetRouting.HandleAsync(RecipientsHarness.Actor, "bob", new Dictionary<Priority, IReadOnlyList<ChannelType>>(), _ct))
            .Error.Code.ShouldBe(RecipientErrors.ContactNotFoundCode);
    }

    [Fact]
    public async Task AC56_Deleting_a_contact_removes_it_from_its_groups_and_bumps_the_revision()
    {
        await _h.ContactAsync("alice", "Alice", Number);
        await _h.ContactAsync("bob", "Bob");
        await _h.GroupAsync("family", "Family", "contact:alice", "contact:bob");
        await _h.GroupAsync("security", "Security", "contact:alice");
        var revision = await _h.RevisionAsync();

        var result = await _h.DeleteContact.HandleAsync(RecipientsHarness.Actor, "alice", _ct);

        result.Value.Select(g => g.Handle).ShouldBe(["group:family", "group:security"]);
        _h.Store.Contacts.ShouldHaveSingleItem().Handle.Name.ShouldBe("bob");
        _h.Store.Groups.Single(g => g.Handle.Name == "family").Members.Count.ShouldBe(1);
        _h.Store.Groups.Single(g => g.Handle.Name == "security").Members.ShouldBeEmpty();
        (await _h.RevisionAsync()).ShouldBe((int.Parse(revision, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        _h.Journal.Steps.TakeLast(2).ShouldBe(["save", "enqueue recipients.changed"]);
        _h.Audit.Entries.TakeLast(3).Select(e => (e.EntityType, e.EntityKey, e.Summary)).ShouldBe(
        [
            (AuditEntityType.Membership, "group:family", "Removed contact:alice (contact deleted)"),
            (AuditEntityType.Membership, "group:security", "Removed contact:alice (contact deleted)"),
            (AuditEntityType.Contact, "contact:alice", "Deleted contact:alice (Alice); removed from group:family, group:security"),
        ]);
    }

    [Fact]
    public async Task Deleting_an_unknown_contact_is_not_found()
    {
        (await _h.DeleteContact.HandleAsync(RecipientsHarness.Actor, "alice", _ct)).Error.Code.ShouldBe(RecipientErrors.ContactNotFoundCode);
        _h.Journal.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC82_Every_change_is_audited_with_the_ha_user()
    {
        await _h.ContactAsync("alice", "Alice", Number);
        await _h.RenameContact.HandleAsync(RecipientsHarness.Actor, "alice", "Alice Smith", _ct);
        await _h.SetRouting.HandleAsync(RecipientsHarness.Actor, "alice", new Dictionary<Priority, IReadOnlyList<ChannelType>> { [Priority.High] = [ChannelType.Sms] }, _ct);
        await _h.DeleteContact.HandleAsync(RecipientsHarness.Actor, "alice", _ct);

        _h.Audit.Entries.Select(e => (e.Action, e.EntityType)).ShouldBe(
        [
            (AuditAction.Create, AuditEntityType.Contact),
            (AuditAction.Create, AuditEntityType.Endpoint),
            (AuditAction.Update, AuditEntityType.Contact),
            (AuditAction.Update, AuditEntityType.Routing),
            (AuditAction.Delete, AuditEntityType.Contact),
        ]);
        _h.Audit.Entries.ShouldAllBe(e => e.HaUserId == "8f7e6d5c4b3a" && e.HaUserDisplayName == "Alice Admin" && e.EntityKey == "contact:alice");
    }

    [Fact]
    public async Task Lists_and_details_show_masked_numbers_only()
    {
        await _h.ContactAsync("bob", "Bob");
        await _h.ContactAsync("alice", "Alice", Number);
        await _h.GroupAsync("family", "Family", "contact:alice");

        var list = await _h.ListContacts.HandleAsync(_ct);
        var details = (await _h.GetContact.HandleAsync("alice", _ct)).Value;

        list.Select(c => c.Handle).ShouldBe(["contact:alice", "contact:bob"]);
        list[0].Endpoints.ShouldHaveSingleItem().MaskedAddress.ShouldBe("+3538****567");
        details.HandleName.ShouldBe("alice");
        details.Endpoints.ShouldHaveSingleItem().VerifiedAt.ShouldNotBeNull();
        details.Routing.Keys.ShouldBe(Enum.GetValues<Priority>(), ignoreOrder: true);
        details.MemberOf.ShouldHaveSingleItem().Handle.ShouldBe("group:family");
        (await _h.GetContact.HandleAsync("carol", _ct)).Error.Code.ShouldBe(RecipientErrors.ContactNotFoundCode);
    }

    [Fact]
    public async Task Use_cases_require_an_actor()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => _h.CreateContact.HandleAsync(null!, new("a", "A"), _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.CreateContact.HandleAsync(RecipientsHarness.Actor, null!, _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.RenameContact.HandleAsync(null!, "a", "A", _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.SetSms.HandleAsync(null!, "a", Number, _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.RemoveEndpoint.HandleAsync(null!, "a", ChannelType.Sms, _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.SetRouting.HandleAsync(null!, "a", new Dictionary<Priority, IReadOnlyList<ChannelType>>(), _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.SetRouting.HandleAsync(RecipientsHarness.Actor, "a", null!, _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.DeleteContact.HandleAsync(null!, "a", _ct));
    }
}
