namespace NotificationApi.Web.Ingress;

/// <summary>Headers added by the HA Supervisor's Ingress proxy (verified in the spike, spec §13).</summary>
public static class IngressHeaders
{
    public const string IngressPath = "X-Ingress-Path";
    public const string UserId = "X-Remote-User-Id";
    public const string UserName = "X-Remote-User-Name";
    public const string UserDisplayName = "X-Remote-User-Display-Name";
}
