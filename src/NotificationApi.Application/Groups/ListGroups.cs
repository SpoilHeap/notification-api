using NotificationApi.Application.Recipients;

namespace NotificationApi.Application.Groups;

/// <summary>The Groups list, sorted by handle.</summary>
public sealed class ListGroups(IGroupRepository groups)
{
    public async Task<IReadOnlyList<GroupSummary>> HandleAsync(CancellationToken cancellationToken) =>
    [
        .. (await groups.ListAsync(cancellationToken))
            .OrderBy(g => g.Handle.ToString(), StringComparer.Ordinal)
            .Select(g => new GroupSummary(
                g.Handle.ToString(),
                g.Handle.Name,
                g.Name.Value,
                g.Members.Count(m => m.IsContact),
                g.Members.Count(m => !m.IsContact))),
    ];
}
