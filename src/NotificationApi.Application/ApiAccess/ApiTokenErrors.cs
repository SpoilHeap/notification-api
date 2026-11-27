using NotificationApi.Application.Common;

namespace NotificationApi.Application.ApiAccess;

public static class ApiTokenErrors
{
    public static readonly Error Invalid = new("api_token.invalid", "The API token is missing, malformed or not recognised.");

    public static readonly Error Unrecoverable = new(
        "api_token.unrecoverable",
        "The stored API token can't be decrypted, so it can't be re-published or shown. The integration keeps working with the token it already has. Rotate the token to recover.");
}
