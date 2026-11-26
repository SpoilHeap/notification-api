using System.Net;

namespace NotificationApi.Web.Ingress;

internal static class IngressRequest
{
    /// <summary>Set by the middleware once a request is known to come through HA Ingress.</summary>
    internal const string TrustedItemKey = "NotificationApi.Ingress.Trusted";

    public static bool IsTrusted(HttpContext context) =>
        context.Items.TryGetValue(TrustedItemKey, out var value) && value is true;

    /// <summary>
    /// True when the remote address is the Ingress proxy. Kestrel listens dual-stack, so IPv4
    /// clients can appear as IPv4-mapped IPv6 (<c>::ffff:172.30.32.2</c>) and must be unwrapped.
    /// </summary>
    public static bool IsFromProxy(IPAddress? remote, IPAddress proxy)
    {
        if (remote is null)
        {
            return false;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        return remote.Equals(proxy);
    }

    /// <summary>The <c>&lt;base href&gt;</c> for a request: its PathBase with a trailing slash.</summary>
    public static string BaseHref(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var pathBase = context.Request.PathBase.Value ?? string.Empty;
        return pathBase.EndsWith('/') ? pathBase : pathBase + "/";
    }
}
