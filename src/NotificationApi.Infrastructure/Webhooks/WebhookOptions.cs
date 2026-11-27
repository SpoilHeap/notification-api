namespace NotificationApi.Infrastructure.Webhooks;

/// <summary>HA webhook settings (config section <c>Webhook</c>).</summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    /// <summary>One attempt per event, with this timeout and no retry (spec §4.4, AC34).</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
