using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Application.Tests.Groups;

public sealed class GroupUseCaseTests
{
    private readonly RecipientsHarness _h = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AC54_A_group_holds_contacts_and_other_groups()
    {
        await _h.ContactAsync("alice", "Alice");
        await _h.GroupAsync("kids", "Kids");
        await _h.GroupAsync("family", "Family", "contact:alice", "group:kids");

        var details = (await _h.GetGroup.HandleAsync("family", _ct)).Value;

        details.Members.Select(m => (m.Handle, m.Type)).ShouldBe([("contact:alice", "contact"), ("group:kids", "group")]);
        details.Candidates.ShouldBeEmpty();
        (await _h.GetGroup.HandleAsync("kids", _ct)).Value.MemberOf.ShouldHaveSingleItem().Handle.ShouldBe("group:family");
        var list = await _h.ListGroups.HandleAsync(_ct);
        list.Select(g => (g.Handle, g.ContactCount, g.GroupCount)).ShouldBe([("group:family", 1, 1), ("group:kids", 0, 0)]);
    }

    [Fact]
    public async Task The_picker_offers_everything_except_members_and_the_group_itself()
    {
        await _h.ContactAsync("alice", "Alice");
        await _h.ContactAsync("bob", "Bob");
        await _h.GroupAsync("kids", "Kids");
        await _h.GroupAsync("family", "Family", "contact:alice");

        (await _h.GetGroup.HandleAsync("family", _ct)).Value.Candidates.Select(c => c.Handle).ShouldBe(["contact:bob", "group:kids"]);
    }

    [Fact]
    public async Task AC57_Group_handles_are_unique_but_may_match_a_contact_handle()
    {
        await _h.GroupAsync("family", "Family");
        await _h.ContactAsync("family", "The family phone");

        var duplicate = await _h.CreateGroup.HandleAsync(RecipientsHarness.Actor, new("family", "Family 2"), _ct);

        duplicate.Error.FieldErrors[RecipientFields.Handle].ShouldHaveSingleItem().ShouldContain("group:family already exists");
        (await _h.CreateGroup.HandleAsync(RecipientsHarness.Actor, new("Fam!", ""), _ct)).Error.FieldErrors.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AC32_Create_rename_and_delete_bump_the_revision_and_membership_does_not()
    {
        await _h.ContactAsync("alice", "Alice");                           // 2
        await _h.GroupAsync("family", "Family");                           // 3
        await _h.GroupAsync("kids", "Kids");                               // 4
        await _h.AddMember.HandleAsync(RecipientsHarness.Actor, "family", "contact:alice", _ct);
        await _h.AddMember.HandleAsync(RecipientsHarness.Actor, "family", "group:kids", _ct);
        await _h.RemoveMember.HandleAsync(RecipientsHarness.Actor, "family", "group:kids", _ct);
        (await _h.RevisionAsync()).ShouldBe("4");

        await _h.RenameGroup.HandleAsync(RecipientsHarness.Actor, "family", "The Family", _ct); // 5
        await _h.RenameGroup.HandleAsync(RecipientsHarness.Actor, "family", "The Family", _ct); // unchanged
        await _h.DeleteGroup.HandleAsync(RecipientsHarness.Actor, "kids", _ct);                 // 6

        (await _h.RevisionAsync()).ShouldBe("6");
        _h.Journal.Queued.Select(e => ((NotificationApi.Application.Events.RecipientsChangedData)e.Data).Revision).ShouldBe(["2", "3", "4", "5", "6"]);
    }

    [Fact]
    public async Task AC55_Self_membership_is_rejected_with_a_clear_message()
    {
        await _h.GroupAsync("family", "Family");

        var result = await _h.AddMember.HandleAsync(RecipientsHarness.Actor, "family", "group:family", _ct);

        result.Error.FieldErrors[RecipientFields.Member].ShouldBe(["A group can't be a member of itself."]);
    }

    [Fact]
    public async Task AC55_A_direct_cycle_is_rejected_with_the_path()
    {
        await _h.GroupAsync("kids", "Kids");
        await _h.GroupAsync("family", "Family", "group:kids");

        var result = await _h.AddMember.HandleAsync(RecipientsHarness.Actor, "kids", "group:family", _ct);

        result.Error.FieldErrors[RecipientFields.Member].ShouldBe(
            ["group:family already contains group:kids (group:family → group:kids), so adding it would create a loop."]);
        _h.Store.Groups.Single(g => g.Handle.Name == "kids").Members.ShouldBeEmpty();
    }

    [Fact]
    public async Task AC55_An_indirect_cycle_is_rejected_with_the_path()
    {
        await _h.GroupAsync("teens", "Teens");
        await _h.GroupAsync("kids", "Kids", "group:teens");
        await _h.GroupAsync("family", "Family", "group:kids");
        var saves = _h.Journal.Saves;
        var audited = _h.Audit.Entries.Count;

        var result = await _h.AddMember.HandleAsync(RecipientsHarness.Actor, "teens", "group:family", _ct);

        result.Error.FieldErrors[RecipientFields.Member].ShouldBe(
            ["group:family already contains group:teens (group:family → group:kids → group:teens), so adding it would create a loop."]);
        _h.Journal.Saves.ShouldBe(saves);
        _h.Audit.Entries.Count.ShouldBe(audited);
    }

    [Theory]
    [InlineData("contact:alice", "contact:alice is already a member of group:family.")]
    [InlineData("contact:nobody", "There is no contact:nobody.")]
    [InlineData("group:nobody", "There is no group:nobody.")]
    [InlineData("", "Choose a contact or group to add.")]
    [InlineData(null, "Choose a contact or group to add.")]
    [InlineData("alice", "Choose a contact or group to add.")]
    public async Task Bad_members_are_rejected_with_a_field_error(string? member, string message)
    {
        await _h.ContactAsync("alice", "Alice");
        await _h.GroupAsync("family", "Family", "contact:alice");

        var result = await _h.AddMember.HandleAsync(RecipientsHarness.Actor, "family", member, _ct);

        result.Error.FieldErrors[RecipientFields.Member].ShouldBe([message]);
    }

    [Fact]
    public async Task A_group_already_in_the_group_is_rejected()
    {
        await _h.GroupAsync("kids", "Kids");
        await _h.GroupAsync("family", "Family", "group:kids");

        (await _h.AddMember.HandleAsync(RecipientsHarness.Actor, "family", "group:kids", _ct))
            .Error.FieldErrors[RecipientFields.Member].ShouldBe(["group:kids is already a member of group:family."]);
    }

    [Fact]
    public async Task AC82_Membership_changes_are_audited_against_the_group()
    {
        await _h.ContactAsync("alice", "Alice");
        await _h.GroupAsync("family", "Family", "contact:alice");

        (await _h.RemoveMember.HandleAsync(RecipientsHarness.Actor, "family", "contact:alice", _ct)).Value.ShouldBeTrue();
        (await _h.RemoveMember.HandleAsync(RecipientsHarness.Actor, "family", "contact:alice", _ct)).Value.ShouldBeFalse();
        (await _h.RemoveMember.HandleAsync(RecipientsHarness.Actor, "family", "group:nobody", _ct)).Value.ShouldBeFalse();

        _h.Audit.Entries.Where(e => e.EntityType == AuditEntityType.Membership).Select(e => (e.Action, e.EntityKey, e.Summary)).ShouldBe(
        [
            (AuditAction.Create, "group:family", "Added contact:alice"),
            (AuditAction.Delete, "group:family", "Removed contact:alice"),
        ]);
    }

    [Fact]
    public async Task Removing_needs_a_member_and_an_existing_group()
    {
        await _h.GroupAsync("family", "Family");

        (await _h.RemoveMember.HandleAsync(RecipientsHarness.Actor, "family", "nonsense", _ct)).Error.FieldErrors.Keys.ShouldBe([RecipientFields.Member]);
        (await _h.RemoveMember.HandleAsync(RecipientsHarness.Actor, "nope", "contact:alice", _ct)).Error.Code.ShouldBe(RecipientErrors.GroupNotFoundCode);
        (await _h.AddMember.HandleAsync(RecipientsHarness.Actor, "nope", "contact:alice", _ct)).Error.Code.ShouldBe(RecipientErrors.GroupNotFoundCode);
        (await _h.RenameGroup.HandleAsync(RecipientsHarness.Actor, "nope", "Nope", _ct)).Error.Code.ShouldBe(RecipientErrors.GroupNotFoundCode);
        (await _h.RenameGroup.HandleAsync(RecipientsHarness.Actor, "family", "", _ct)).Error.Code.ShouldBe(Error.ValidationCode);
        (await _h.DeleteGroup.HandleAsync(RecipientsHarness.Actor, "nope", _ct)).Error.Code.ShouldBe(RecipientErrors.GroupNotFoundCode);
        (await _h.GetGroup.HandleAsync("NOPE", _ct)).Error.Code.ShouldBe(RecipientErrors.GroupNotFoundCode);
    }

    [Fact]
    public async Task AC56_Deleting_a_group_removes_it_from_its_parents_but_keeps_its_members()
    {
        await _h.ContactAsync("alice", "Alice");
        await _h.GroupAsync("kids", "Kids", "contact:alice");
        await _h.GroupAsync("family", "Family", "group:kids");
        await _h.GroupAsync("school", "School", "group:kids");

        var result = await _h.DeleteGroup.HandleAsync(RecipientsHarness.Actor, "kids", _ct);

        result.Value.Select(g => g.Handle).ShouldBe(["group:family", "group:school"]);
        _h.Store.Groups.Select(g => g.Handle.Name).ShouldBe(["family", "school"], ignoreOrder: true);
        _h.Store.Groups.ShouldAllBe(g => g.Members.Count == 0);
        _h.Store.Contacts.ShouldHaveSingleItem();
        _h.Audit.Entries.Last().Summary.ShouldBe("Deleted group:kids (Kids); removed from group:family, group:school");
    }

    [Fact]
    public async Task Use_cases_require_an_actor()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => _h.CreateGroup.HandleAsync(null!, new("a", "A"), _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.CreateGroup.HandleAsync(RecipientsHarness.Actor, null!, _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.RenameGroup.HandleAsync(null!, "a", "A", _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.AddMember.HandleAsync(null!, "a", "contact:a", _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.RemoveMember.HandleAsync(null!, "a", "contact:a", _ct));
        await Should.ThrowAsync<ArgumentNullException>(() => _h.DeleteGroup.HandleAsync(null!, "a", _ct));
    }
}
