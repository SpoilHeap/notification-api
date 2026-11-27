using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Recipients;

public sealed class GroupTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static Group NewGroup(string name) => Group.Create(Handle.Group(name), DisplayName.Create(name), Now);

    private static Contact NewContact(string name) => Contact.Create(Handle.Contact(name), DisplayName.Create(name), Now);

    [Fact]
    public void A_group_needs_a_group_handle() =>
        Should.Throw<ArgumentException>(() => Group.Create(Handle.Contact("family"), DisplayName.Create("Family"), Now));

    [Fact]
    public void AC51_The_handle_cannot_be_changed_but_the_name_can()
    {
        var family = NewGroup("family");

        typeof(Group).GetProperty(nameof(Group.Handle))!.SetMethod.ShouldBeNull();
        family.Rename(DisplayName.Create("The Family")).ShouldBeTrue();
        family.Rename(DisplayName.Create("The Family")).ShouldBeFalse();

        family.Name.Value.ShouldBe("The Family");
        family.Handle.ToString().ShouldBe("group:family");
        family.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void AC54_A_group_holds_contacts_and_other_groups()
    {
        var family = NewGroup("family");
        var kids = NewGroup("kids");
        var alice = NewContact("alice");

        family.AddMember(alice);
        family.AddMember(kids, new GroupGraph([family, kids]));

        family.Members.Count.ShouldBe(2);
        family.Contains(alice.Id).ShouldBeTrue();
        family.Contains(kids.Id).ShouldBeTrue();
        family.Members.Count(m => m.IsContact).ShouldBe(1);
        family.Members.Single(m => !m.IsContact).MemberGroupId.ShouldBe(kids.Id);
    }

    [Fact]
    public void A_contact_can_be_in_many_groups_but_only_once_in_each()
    {
        var family = NewGroup("family");
        var security = NewGroup("security");
        var alice = NewContact("alice");
        family.AddMember(alice);
        security.AddMember(alice);

        family.ProblemAdding(alice).ShouldBe(MembershipProblem.AlreadyMember);
        Should.Throw<InvalidOperationException>(() => family.AddMember(alice));
        family.Members.Count.ShouldBe(1);
        security.Contains(alice.Id).ShouldBeTrue();
    }

    [Fact]
    public void A_group_can_be_a_member_only_once()
    {
        var family = NewGroup("family");
        var kids = NewGroup("kids");
        family.AddMember(kids, new GroupGraph([family, kids]));

        family.ProblemAdding(kids, new GroupGraph([family, kids])).ShouldBe(MembershipProblem.AlreadyMember);
    }

    [Fact]
    public void AC55_A_group_cannot_contain_itself()
    {
        var family = NewGroup("family");

        family.ProblemAdding(family, new GroupGraph([family])).ShouldBe(MembershipProblem.Self);
        Should.Throw<InvalidOperationException>(() => family.AddMember(family, new GroupGraph([family])));
        family.Members.ShouldBeEmpty();
    }

    [Fact]
    public void AC55_A_direct_cycle_is_rejected()
    {
        var family = NewGroup("family");
        var kids = NewGroup("kids");
        family.AddMember(kids, new GroupGraph([family, kids]));

        var graph = new GroupGraph([family, kids]);

        kids.ProblemAdding(family, graph).ShouldBe(MembershipProblem.Cycle);
        Should.Throw<InvalidOperationException>(() => kids.AddMember(family, graph));
        graph.PathBetween(family.Id, kids.Id).ShouldBe([family.Id, kids.Id]);
    }

    [Fact]
    public void AC55_An_indirect_cycle_is_rejected()
    {
        var family = NewGroup("family");
        var kids = NewGroup("kids");
        var teens = NewGroup("teens");
        family.AddMember(kids, new GroupGraph([family, kids, teens]));
        kids.AddMember(teens, new GroupGraph([family, kids, teens]));

        var graph = new GroupGraph([family, kids, teens]);

        teens.ProblemAdding(family, graph).ShouldBe(MembershipProblem.Cycle);
        graph.PathBetween(family.Id, teens.Id).ShouldBe([family.Id, kids.Id, teens.Id]);
    }

    [Fact]
    public void AC55_A_deep_cycle_is_rejected()
    {
        var groups = Enumerable.Range(0, 30).Select(i => NewGroup($"g{i}")).ToList();
        for (var i = 0; i < groups.Count - 1; i++)
        {
            groups[i].AddMember(groups[i + 1], new GroupGraph(groups));
        }

        var graph = new GroupGraph(groups);

        groups[^1].ProblemAdding(groups[0], graph).ShouldBe(MembershipProblem.Cycle);
        graph.PathBetween(groups[0].Id, groups[^1].Id)!.Count.ShouldBe(30);
        groups[^1].ProblemAdding(groups[5], graph).ShouldBe(MembershipProblem.Cycle);
    }

    [Fact]
    public void Diamonds_are_allowed_because_they_are_not_cycles()
    {
        // family → kids → alice and family → teens, then teens → kids: kids is reachable twice, but nothing loops.
        var family = NewGroup("family");
        var kids = NewGroup("kids");
        var teens = NewGroup("teens");
        var all = new[] { family, kids, teens };
        family.AddMember(kids, new GroupGraph(all));
        family.AddMember(teens, new GroupGraph(all));

        teens.ProblemAdding(kids, new GroupGraph(all)).ShouldBeNull();
        teens.AddMember(kids, new GroupGraph(all));

        new GroupGraph(all).Contains(family.Id, kids.Id).ShouldBeTrue();
    }

    [Fact]
    public void Contains_is_false_for_unrelated_groups_and_for_a_group_and_itself()
    {
        var family = NewGroup("family");
        var friends = NewGroup("friends");
        var graph = new GroupGraph([family, friends]);

        graph.Contains(family.Id, friends.Id).ShouldBeFalse();
        graph.Contains(family.Id, family.Id).ShouldBeFalse();
        graph.PathBetween(family.Id, friends.Id).ShouldBeNull();
        graph.PathBetween(GroupId.New(), family.Id).ShouldBeNull(); // unknown group
    }

    [Fact]
    public void AC56_Removing_a_member_leaves_the_others_and_the_member_itself()
    {
        var family = NewGroup("family");
        var kids = NewGroup("kids");
        var alice = NewContact("alice");
        var bob = NewContact("bob");
        kids.AddMember(bob);
        family.AddMember(alice);
        family.AddMember(kids, new GroupGraph([family, kids]));

        family.RemoveMember(alice.Id).ShouldBeTrue();
        family.RemoveMember(alice.Id).ShouldBeFalse();
        family.RemoveMember(kids.Id).ShouldBeTrue();
        family.RemoveMember(kids.Id).ShouldBeFalse();

        family.Members.ShouldBeEmpty();
        kids.Contains(bob.Id).ShouldBeTrue("removing a group from its parent doesn't touch its own members");
    }
}
