namespace NotificationApi.Domain.Recipients;

/// <summary>
/// A named set of contacts and other groups. Nesting is allowed (spec §2), cycles are not (AC55), and a member can
/// appear only once in the same group. The <see cref="Handle"/> is fixed at creation (AC51).
/// </summary>
public sealed class Group
{
    private readonly List<GroupMember> _members = [];

    private Group(GroupId id, Handle handle, DisplayName name, DateTimeOffset createdAt)
    {
        Id = id;
        Handle = handle;
        Name = name;
        CreatedAt = createdAt;
    }

    public GroupId Id { get; private set; }

    /// <summary><c>group:&lt;name&gt;</c>. Get-only: it never changes (AC51).</summary>
    public Handle Handle { get; }

    public DisplayName Name { get; private set; }

    public IReadOnlyCollection<GroupMember> Members => _members;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Group Create(Handle handle, DisplayName name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(name);
        if (handle.Type != HandleType.Group)
        {
            throw new ArgumentException("A group needs a group: handle.", nameof(handle));
        }

        return new Group(GroupId.New(), handle, name, now);
    }

    /// <summary>Returns false when the name is unchanged.</summary>
    public bool Rename(DisplayName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == Name)
        {
            return false;
        }

        Name = name;
        return true;
    }

    public bool Contains(ContactId contactId) => _members.Any(m => m.MemberContactId == contactId);

    public bool Contains(GroupId groupId) => _members.Any(m => m.MemberGroupId == groupId);

    /// <summary>Why <paramref name="contact"/> can't be added, or null if it can.</summary>
    public MembershipProblem? ProblemAdding(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return Contains(contact.Id) ? MembershipProblem.AlreadyMember : null;
    }

    /// <summary>Why <paramref name="group"/> can't be added, or null if it can.</summary>
    /// <param name="group">The candidate member.</param>
    /// <param name="graph">Every group's current members, to find indirect cycles.</param>
    public MembershipProblem? ProblemAdding(Group group, GroupGraph graph)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(graph);
        if (group.Id == Id)
        {
            return MembershipProblem.Self;
        }

        if (Contains(group.Id))
        {
            return MembershipProblem.AlreadyMember;
        }

        // Adding the candidate makes this group contain it, so it must not already contain this group.
        return graph.Contains(group.Id, Id) ? MembershipProblem.Cycle : null;
    }

    public void AddMember(Contact contact)
    {
        if (ProblemAdding(contact) is { } problem)
        {
            throw new InvalidOperationException($"{contact.Handle} can't be added to {Handle}: {problem}.");
        }

        _members.Add(GroupMember.ForContact(contact.Id));
    }

    public void AddMember(Group group, GroupGraph graph)
    {
        if (ProblemAdding(group, graph) is { } problem)
        {
            throw new InvalidOperationException($"{group.Handle} can't be added to {Handle}: {problem}.");
        }

        _members.Add(GroupMember.ForGroup(group.Id));
    }

    /// <summary>Returns false when the contact wasn't a member.</summary>
    public bool RemoveMember(ContactId contactId) => _members.RemoveAll(m => m.MemberContactId == contactId) > 0;

    /// <summary>Returns false when the group wasn't a member. Its own members are untouched.</summary>
    public bool RemoveMember(GroupId groupId) => _members.RemoveAll(m => m.MemberGroupId == groupId) > 0;
}
