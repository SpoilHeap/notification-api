using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;

namespace NotificationApi.Application.Groups;

/// <summary>One group for its edit and delete pages: members, candidates for the member picker, and containing groups.</summary>
public sealed class GetGroup(IGroupRepository groups, IContactRepository contacts)
{
    public async Task<Result<GroupDetails>> HandleAsync(string groupName, CancellationToken cancellationToken)
    {
        var all = await groups.ListAsync(cancellationToken);
        var found = all.Find(groupName);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var group = found.Value;
        var allContacts = await contacts.ListAsync(cancellationToken);

        var members = new List<MemberView>();
        var candidates = new List<RecipientRef>();
        foreach (var contact in allContacts.OrderBy(c => c.Handle.ToString(), StringComparer.Ordinal))
        {
            var view = new RecipientRef(contact.Handle.ToString(), contact.Name.Value);
            if (group.Contains(contact.Id))
            {
                members.Add(new MemberView(view.Handle, view.Name, RecipientSummary.ContactType));
            }
            else
            {
                candidates.Add(view);
            }
        }

        foreach (var other in all.Where(g => g != group).OrderBy(g => g.Handle.ToString(), StringComparer.Ordinal))
        {
            var view = new RecipientRef(other.Handle.ToString(), other.Name.Value);
            if (group.Contains(other.Id))
            {
                members.Add(new MemberView(view.Handle, view.Name, RecipientSummary.GroupType));
            }
            else
            {
                candidates.Add(view);
            }
        }

        var memberOf = all
            .Where(g => g.Contains(group.Id))
            .OrderBy(g => g.Handle.ToString(), StringComparer.Ordinal)
            .Select(g => new RecipientRef(g.Handle.ToString(), g.Name.Value))
            .ToList();

        return new GroupDetails(group.Handle.ToString(), group.Handle.Name, group.Name.Value, members, candidates, memberOf);
    }
}
