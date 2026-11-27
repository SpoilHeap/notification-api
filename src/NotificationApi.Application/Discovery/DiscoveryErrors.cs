using NotificationApi.Application.Common;

namespace NotificationApi.Application.Discovery;

/// <summary>Failures from <see cref="IDiscoveryPublisher"/>. All but <see cref="Rejected"/> are worth retrying.</summary>
public static class DiscoveryErrors
{
    public const string UnreachableCode = "discovery.unreachable";
    public const string TimeoutCode = "discovery.timeout";
    public const string ServerErrorCode = "discovery.server_error";
    public const string RejectedCode = "discovery.rejected";
    public const string InvalidResponseCode = "discovery.invalid_response";

    public static Error Unreachable(string detail) => new(UnreachableCode, $"The Supervisor is unreachable: {detail}");

    public static Error Timeout() => new(TimeoutCode, "The Supervisor did not answer in time.");

    public static Error ServerError(int status, string body) => new(ServerErrorCode, $"The Supervisor returned HTTP {status}: {body}");

    /// <summary>A 4xx answer, e.g. 401/403 when <c>hassio_api</c> or <c>discovery</c> is missing from config.yaml. Not retried.</summary>
    public static Error Rejected(int status, string body) => new(RejectedCode, $"The Supervisor rejected the discovery message with HTTP {status}: {body}");

    public static Error InvalidResponse(string detail) => new(InvalidResponseCode, $"The Supervisor's answer could not be read: {detail}");

    public static bool IsTransient(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code != RejectedCode;
    }
}
