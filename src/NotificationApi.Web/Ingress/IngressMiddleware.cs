using Microsoft.Extensions.Options;

namespace NotificationApi.Web.Ingress;

/// <summary>
/// Applies <c>X-Ingress-Path</c> from the Ingress proxy as the PathBase, so links, assets and form posts resolve
/// under <c>/api/hassio_ingress/&lt;token&gt;/</c>. Requests from anywhere else are left untouched. Must run before
/// routing. Authentication is separate: see <see cref="IngressAuthenticationHandler"/>.
/// </summary>
internal sealed class IngressMiddleware(RequestDelegate next, IOptions<IngressOptions> options)
{
    private readonly System.Net.IPAddress _proxy = System.Net.IPAddress.Parse(options.Value.TrustedProxyAddress);
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
