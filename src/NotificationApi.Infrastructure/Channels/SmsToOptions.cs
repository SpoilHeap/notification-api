namespace NotificationApi.Infrastructure.Channels;

/// <summary>SMS.to connection settings (config section <c>SmsTo</c>). The API key and sender ID are on the Settings page.</summary>
public sealed class SmsToOptions
{
    public const string SectionName = "SmsTo";

    /// <summary>The REST API (developers.sms.to): <c>POST {BaseUrl}sms/send</c>.</summary>
    public Uri BaseUrl { get; set; } = new("https://api.sms.to/");

    /// <summary>One attempt per delivery attempt, with this timeout and no HTTP-level retry (engineering_standards.md §2.4).</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
