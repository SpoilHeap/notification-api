namespace NotificationApi.Domain.Recipients;

/// <summary>One member of a <see cref="Group"/>: exactly one of a contact or another group.</summary>
public sealed class GroupMember
{
    private GroupMember(ContactId? memberContactId, GroupId? memberGroupId)
    {
        MemberContactId = memberContactId;
        MemberGroupId = memberGroupId;
    }

    public ContactId? MemberContactId { get; private set; }

    public GroupId? MemberGroupId { get; private set; }

    public bool IsContact => MemberContactId is not null;

    internal static GroupMember ForContact(ContactId id) => new(id, null);

    internal static GroupMember ForGroup(GroupId id) => new(null, id);
}
