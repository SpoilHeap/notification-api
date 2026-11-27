using NotificationApi.Application.Recipients;

namespace NotificationApi.Application.Groups;

/// <summary>A row of the Groups list.</summary>
public sealed record GroupSummary(string Handle, string HandleName, string Name, int ContactCount, int GroupCount);

/// <summary>A member as the group page shows it.</summary>
/// <param name="Handle">E.g. <c>contact:alice</c>.</param>
/// <param name="Name">The display name.</param>
/// <param name="Type"><c>contact</c> or <c>group</c>.</param>
public sealed record MemberView(string Handle, string Name, string Type);

/// <summary>The group edit page.</summary>
/// <param name="Handle">E.g. <c>group:family</c>.</param>
/// <param name="HandleName">E.g. <c>family</c>, used in the page URL.</param>
/// <param name="Name">The display name.</param>
/// <param name="Members">Contacts first, then groups, each by handle.</param>
/// <param name="Candidates">Contacts and groups that aren't members yet (a group that would form a cycle is still listed; adding it is rejected with a clear message).</param>
/// <param name="MemberOf">The groups that contain this group directly (listed before deleting, AC56).</param>
public sealed record GroupDetails(
    string Handle,
    string HandleName,
    string Name,
    IReadOnlyList<MemberView> Members,
    IReadOnlyList<RecipientRef> Candidates,
    IReadOnlyList<RecipientRef> MemberOf);
