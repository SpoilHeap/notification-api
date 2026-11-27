using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Notifications;

/// <param name="Contact">The contact reached.</param>
/// <param name="Via">The top-level handle from <c>to</c> that reached it.</param>
public sealed record ExpandedRecipient(Contact Contact, Handle Via);

/// <param name="Recipients">Every contact reached, in order and with duplicates.</param>
/// <param name="UnknownHandles">Handles in <c>to</c> that name no contact or group, in request order.</param>
public sealed record ExpansionResult(IReadOnlyList<ExpandedRecipient> Recipients, IReadOnlyList<Handle> UnknownHandles);

/// <summary>
/// Expands <c>to</c> handles into contacts (spec §5 step 1): in request order, groups depth-first in member order,
/// keeping duplicates (spec §2: no deduplication; AC03–AC05). Pure: works on the contacts and groups it is given.
/// </summary>
public static class RecipientExpansion
{
    public static ExpansionResult Expand(IEnumerable<Handle> to, IEnumerable<Contact> contacts, IEnumerable<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(groups);

        var contactList = contacts.ToList();
        var groupList = groups.ToList();
        var contactsByHandle = contactList.ToDictionary(c => c.Handle);
        var contactsById = contactList.ToDictionary(c => c.Id);
        var groupsByHandle = groupList.ToDictionary(g => g.Handle);
        var groupsById = groupList.ToDictionary(g => g.Id);

        var recipients = new List<ExpandedRecipient>();
        var unknown = new List<Handle>();
        foreach (var handle in to)
        {
            ArgumentNullException.ThrowIfNull(handle);
            if (handle.Type == HandleType.Contact && contactsByHandle.TryGetValue(handle, out var contact))
            {
                recipients.Add(new ExpandedRecipient(contact, handle));
            }
            else if (handle.Type == HandleType.Group && groupsByHandle.TryGetValue(handle, out var group))
            {
                AddMembers(group, handle, []);
            }
            else
            {
                unknown.Add(handle);
            }
        }

        return new ExpansionResult(recipients, unknown);

        void AddMembers(Group group, Handle via, HashSet<GroupId> path)
        {
            // Cycles are rejected when members are added (AC55); the path check only guards against bad data.
            if (!path.Add(group.Id))
            {
                return;
            }

            foreach (var member in group.Members)
            {
                if (member.MemberContactId is { } contactId && contactsById.TryGetValue(contactId, out var memberContact))
                {
                    recipients.Add(new ExpandedRecipient(memberContact, via));
                }
                else if (member.MemberGroupId is { } groupId && groupsById.TryGetValue(groupId, out var child))
                {
                    AddMembers(child, via, path);
                }
            }

            path.Remove(group.Id);
        }
    }
}
