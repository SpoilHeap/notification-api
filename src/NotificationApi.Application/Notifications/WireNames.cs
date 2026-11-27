using System.Diagnostics.CodeAnalysis;
using NotificationApi.Domain.Notifications;

namespace NotificationApi.Application.Notifications;

/// <summary>Priority and channel names as the API spells them: exact lower case (spec §4.1 "Case").</summary>
public static class WireNames
{
    private static readonly Dictionary<string, Priority> Priorities = new(StringComparer.Ordinal)
    {
        ["low"] = Priority.Low,
        ["normal"] = Priority.Normal,
        ["high"] = Priority.High,
        ["critical"] = Priority.Critical,
    };

    private static readonly Dictionary<string, ChannelType> Channels = new(StringComparer.Ordinal)
    {
        ["telegram"] = ChannelType.Telegram,
        ["sms"] = ChannelType.Sms,
    };

    public static IEnumerable<string> PriorityNames => Priorities.Keys;

    public static IEnumerable<string> ChannelNames => Channels.Keys;

    public static bool TryParsePriority([NotNullWhen(true)] string? value, out Priority priority) =>
        Priorities.TryGetValue(value ?? string.Empty, out priority);

    public static bool TryParseChannel([NotNullWhen(true)] string? value, out ChannelType channel) =>
        Channels.TryGetValue(value ?? string.Empty, out channel);

    public static string Of(ChannelType channel) => Channels.Single(c => c.Value == channel).Key;

    public static string Of(Priority priority) => Priorities.Single(p => p.Value == priority).Key;
}
