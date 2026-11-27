namespace NotificationApi.Domain.Recipients;

/// <summary>Which groups contain which groups. Used to reject membership cycles (AC55).</summary>
public sealed class GroupGraph
{
    private readonly Dictionary<GroupId, GroupId[]> _children;

    /// <param name="groups">Every group, in its current state.</param>
    public GroupGraph(IEnumerable<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        _children = groups.ToDictionary(
            g => g.Id,
            g => g.Members.Where(m => m.MemberGroupId is not null).Select(m => m.MemberGroupId!.Value).ToArray());
    }

    /// <summary>
    /// The chain of groups from <paramref name="from"/> down to <paramref name="to"/> (both included), or null when
    /// <paramref name="from"/> doesn't contain <paramref name="to"/> at any depth.
    /// </summary>
    public IReadOnlyList<GroupId>? PathBetween(GroupId from, GroupId to)
    {
        var visited = new HashSet<GroupId>();
        var path = new List<GroupId>();
        return Search(from) ? path : null;

        bool Search(GroupId current)
        {
            if (!visited.Add(current))
            {
                return false;
            }

            path.Add(current);
            if (current == to)
            {
                return true;
            }

            foreach (var child in _children.GetValueOrDefault(current, []))
            {
                if (Search(child))
                {
                    return true;
                }
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }
    }

    public bool Contains(GroupId ancestor, GroupId descendant) =>
        ancestor != descendant && PathBetween(ancestor, descendant) is not null;
}
