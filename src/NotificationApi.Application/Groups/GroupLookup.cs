using NotificationApi.Application.Common;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Recipients;

namespace NotificationApi.Application.Groups;

internal static class GroupLookup
{
    /// <summary>The group named in a URL, from <paramref name="all"/>, or <see cref="RecipientErrors.GroupNotFound"/>.</summary>
    public static Result<Group> Find(this IReadOnlyList<Group> all, string? name)
    {
        var handle = RecipientValidator.ParseExisting(HandleType.Group, name);
        var group = handle is null ? null : all.SingleOrDefault(g => g.Handle == handle);
        return group is null ? RecipientErrors.GroupNotFound(name ?? string.Empty) : group;
    }

    public static async Task<Result<Group>> FindAsync(this IGroupRepository groups, string? name, CancellationToken cancellationToken)
    {
        var handle = RecipientValidator.ParseExisting(HandleType.Group, name);
        var group = handle is null ? null : await groups.FindByHandleAsync(handle, cancellationToken);
        return group is null ? RecipientErrors.GroupNotFound(name ?? string.Empty) : group;
    }
}
