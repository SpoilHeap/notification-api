using NotificationApi.Application.Common;

namespace NotificationApi.Application.Notifications;

public static class NotificationErrors
{
    /// <summary>Web maps it to a 422 <c>ProblemDetails</c> titled <see cref="NoDeliveriesTitle"/> (AC08).</summary>
    public const string NoDeliveriesCode = "notification.no_deliveries";

    public const string NoDeliveriesTitle = "No deliveries";

    public static Error NoDeliveries(int resolvedContacts) => new(
        NoDeliveriesCode,
        resolvedContacts == 0
            ? "The recipients resolved to no contacts (for example, only empty groups)."
            : $"None of the {resolvedContacts} resolved contact(s) has a verified endpoint for the selected channels.");

    public static Error UnknownHandles(IEnumerable<string> handles) =>
        Error.Validation(new Dictionary<string, string[]> { [NotifyFields.To] = [.. handles.Select(h => $"Unknown handle: {h}")] });
}
