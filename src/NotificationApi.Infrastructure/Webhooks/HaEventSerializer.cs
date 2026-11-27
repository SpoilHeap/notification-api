using System.Globalization;
using System.Text.Json;
using NotificationApi.Application.Events;

namespace NotificationApi.Infrastructure.Webhooks;

/// <summary>
/// Serialises the envelope <c>{"id", "type", "occurred_at", "data"}</c> with snake_case names (spec §4.4).
/// <c>occurred_at</c> is ISO 8601 UTC with a <c>Z</c>, to the second.
/// </summary>
internal static class HaEventSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static byte[] Serialize(HaEvent haEvent)
    {
        ArgumentNullException.ThrowIfNull(haEvent);
        var envelope = new Envelope(
            haEvent.Id,
            haEvent.Type,
            haEvent.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            haEvent.Data);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
    }

    // Data is typed as object so the runtime record's properties are written, with no type discriminator.
    private sealed record Envelope(string Id, string Type, string OccurredAt, object Data);
}
