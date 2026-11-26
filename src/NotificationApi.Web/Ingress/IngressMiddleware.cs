using System.Net;
using Microsoft.Extensions.Options;

namespace NotificationApi.Web.Ingress;

/// <summary>
/// Marks requests from the Ingress proxy as trusted and applies <c>X-Ingress-Path</c> as the PathBase,
/// so links, assets and form posts resolve under <c>/api/hassio_ingress/&lt;token&gt;/</c>.
/// Requests from anywhere else are left untouched, and any Ingress headers they carry are ignored.
/// Must run before routing.
/// </summary>
internal sealed class IngressMiddleware(RequestDelegate next, IOptions<IngressOptions> options)
{
    private readonly IPAddress _proxy = IPAddress.Parse(options.Value.TrustedProxyAddress);
    private readonly bool _devFake = options.Value.DevFakeIngress;

    public Task InvokeAsync(HttpContext context)
    {
        if (_devFake)
        {
            AddFakeHeaders(context.Request.Headers);
        }
        else if (!IngressRequest.IsFromProxy(context.Connection.RemoteIpAddress, _proxy))
        {
            return next(context);
        }

        context.Items[IngressRequest.TrustedItemKey] = true;

        var ingressPath = context.Request.Headers[IngressHeaders.IngressPath].ToString().TrimEnd('/');
        if (ingressPath.Length > 0)
        {
            context.Request.PathBase = new PathString(ingressPath);
        }

        return next(context);
    }

    private static void AddFakeHeaders(IHeaderDictionary headers)
    {
        headers.TryAdd(IngressHeaders.UserId, "dev-user-id");
        headers.TryAdd(IngressHeaders.UserName, "dev");
        headers.TryAdd(IngressHeaders.UserDisplayName, "Developer");
    }
}
