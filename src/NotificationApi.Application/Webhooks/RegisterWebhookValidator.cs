using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Application.Webhooks;

/// <summary>Hand-written validator for <see cref="RegisterWebhookCommand"/>. Keys are the JSON field names.</summary>
public static class RegisterWebhookValidator
{
    public const string UrlField = "url";
    public const string SecretField = "secret";

    /// <summary>Empty when the command is valid.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(RegisterWebhookCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.Url))
        {
            errors[UrlField] = ["The webhook URL is required."];
        }
        else if (!Uri.TryCreate(command.Url, UriKind.Absolute, out var url) || !ApiClient.IsValidWebhookUrl(url))
        {
            errors[UrlField] = [$"The webhook URL must be an absolute http or https URL of at most {ApiClient.MaxWebhookUrlLength} characters."];
        }

        if (string.IsNullOrEmpty(command.Secret))
        {
            errors[SecretField] = ["The webhook secret is required."];
        }
        else if (!WebhookSecret.TryDecode(command.Secret, out _))
        {
            errors[SecretField] = [$"The webhook secret must be standard base64 of {WebhookSecret.MinBytes} to {WebhookSecret.MaxBytes} bytes."];
        }

        return errors;
    }
}
