namespace NotificationApi.Application.Recipients;

/// <summary>One entry of <c>GET /recipients</c>. No PII: no phone numbers, chat ids or routing (AC40).</summary>
/// <param name="Handle">E.g. <c>group:family</c>.</param>
/// <param name="Name">The display name.</param>
/// <param name="Type"><c>contact</c> or <c>group</c>.</param>
public sealed record RecipientSummary(string Handle, string Name, string Type)
{
    public const string ContactType = "contact";
    public const string GroupType = "group";
}

/// <param name="Revision">Changes whenever any contact or group is created, renamed or deleted (spec §4.2).</param>
/// <param name="Recipients">Every contact and group, contacts first, each sorted by handle.</param>
public sealed record RecipientList(string Revision, IReadOnlyList<RecipientSummary> Recipients);

/// <summary>A contact or group as the UI lists it: its handle and display name.</summary>
public sealed record RecipientRef(string Handle, string Name);
