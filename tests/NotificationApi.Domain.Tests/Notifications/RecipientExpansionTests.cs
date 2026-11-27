using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Notifications;

/// <summary>Recipient expansion (spec §5 step 1, engineering_standards.md §4.2): depth-first, duplicates kept, via = the top-level handle.</summary>
public sealed class RecipientExpansionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 16, 0, 0, TimeSpan.Zero);

    private readonly Contact _alice = Contact.Create(Handle.Contact("alice"), DisplayName.Create("Alice"), Now);
    private readonly Contact _bob = Contact.Create(Handle.Contact("bob"), DisplayName.Create("Bob"), Now);

    [Fact]
    public void A_single_contact_is_reached_via_itself()
    {
        var result = Expand([Handle.Contact("alice")], []);

        result.UnknownHandles.ShouldBeEmpty();
        Describe(result).ShouldBe(["alice via contact:alice"]);
    }

    [Fact]
    public void A_flat_group_yields_its_contacts_in_member_order()
    {
        var family = Group("family", _bob, _alice);

        Describe(Expand([family.Handle], [family])).ShouldBe(["bob via group:family", "alice via group:family"]);
    }

    [Fact]
    public void AC03_A_contact_in_two_groups_is_reached_twice_with_each_via()
    {
        var family = Group("family", _alice);
        var security = Group("security", _alice);

        Describe(Expand([family.Handle, security.Handle], [family, security]))
            .ShouldBe(["alice via group:family", "alice via group:security"]);
    }

    [Fact]
    public void AC04_A_group_and_the_contact_itself_are_not_deduplicated()
    {
        var family = Group("family", _alice);

        Describe(Expand([family.Handle, _alice.Handle], [family]))
            .ShouldBe(["alice via group:family", "alice via contact:alice"]);
    }

    [Fact]
    public void AC05_Nested_groups_expand_depth_first_and_keep_the_top_level_via()
    {
        var kids = Group("kids", _alice, _bob);
        var family = Group("family");
        family.AddMember(kids, new GroupGraph([kids, family]));
        family.AddMember(_alice);

        Describe(Expand([family.Handle], [kids, family]))
            .ShouldBe(["alice via group:family", "bob via group:family", "alice via group:family"]);
    }

    [Fact]
    public void An_empty_group_yields_nobody()
    {
        var empty = Group("empty");

        var result = Expand([empty.Handle], [empty]);

        result.Recipients.ShouldBeEmpty();
        result.UnknownHandles.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_handles_are_reported_in_request_order_including_a_contact_named_like_a_group()
    {
        var family = Group("family", _alice);

        var result = Expand([Handle.Contact("zed"), family.Handle, Handle.Group("alice"), Handle.Contact("family")], [family]);

        result.UnknownHandles.Select(h => h.ToString()).ShouldBe(["contact:zed", "group:alice", "contact:family"]);
        Describe(result).ShouldBe(["alice via group:family"]);
    }

    [Fact]
    public void A_cycle_in_stored_data_does_not_loop_forever()
    {
        // The domain refuses cycles (AC55); this only proves expansion is safe against bad data.
        var a = Group("a", _alice);
        var b = Group("b", _bob);
        a.AddMember(b, new GroupGraph([a, b]));
        ForceMember(b, a); // b contains a, a contains b

        Describe(Expand([a.Handle], [a, b])).ShouldBe(["alice via group:a", "bob via group:a"]);
    }

    private static void ForceMember(Group group, Group member)
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic;
        var members = (List<GroupMember>)typeof(Group).GetField("_members", Private | System.Reflection.BindingFlags.Instance)!.GetValue(group)!;
        members.Add((GroupMember)typeof(GroupMember).GetMethod("ForGroup", Private | System.Reflection.BindingFlags.Static)!.Invoke(null, [member.Id])!);
    }

    private ExpansionResult Expand(IEnumerable<Handle> to, IEnumerable<Group> groups) =>
        RecipientExpansion.Expand(to, [_alice, _bob], groups);

    private static Group Group(string name, params Contact[] members)
    {
        var group = Domain.Recipients.Group.Create(Handle.Group(name), DisplayName.Create(name), Now);
        foreach (var member in members)
        {
            group.AddMember(member);
        }

        return group;
    }

    private static List<string> Describe(ExpansionResult result) =>
        [.. result.Recipients.Select(r => $"{r.Contact.Handle.Name} via {r.Via}")];
}
