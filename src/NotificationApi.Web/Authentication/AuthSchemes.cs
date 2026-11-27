namespace NotificationApi.Web.Authentication;

/// <summary>Authentication schemes and authorisation policies (spec §8, engineering_standards.md §2.5).</summary>
public static class AuthSchemes
{
    /// <summary>Web UI: requests from the HA Ingress proxy, identified by the <c>X-Remote-User-*</c> headers.</summary>
    public const string Ingress = "Ingress";

    /// <summary><c>/api/v1</c>: <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
    public const string ApiToken = "ApiToken";

    /// <summary>
    /// Default scheme. Forwards to <see cref="ApiToken"/> under <c>/api</c> and to <see cref="Ingress"/> elsewhere,
    /// so <c>HttpContext.User</c> and challenges match the kind of caller.
    /// </summary>
    public const string ByPath = "ByPath";
}

public static class AuthPolicies
{
    public const string Ingress = AuthSchemes.Ingress;

    public const string ApiToken = AuthSchemes.ApiToken;
}

/// <summary>Claim types beyond the standard <c>ClaimTypes</c>.</summary>
public static class NotificationApiClaimTypes
{
    /// <summary>The HA user's display name (<c>X-Remote-User-Display-Name</c>).</summary>
    public const string DisplayName = "notification_api/display_name";
}
