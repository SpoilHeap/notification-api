using NotificationApi.Web.Ingress;

namespace NotificationApi.Web.Components;

/// <summary>
/// Post/Redirect/Get for the UI's forms (spec §13: a browser refresh under Ingress must not re-post). The answer is a
/// 303 with a <em>path-absolute</em> <c>Location</c> under the Ingress prefix and no scheme or host, so the browser stays
/// on Home Assistant's origin. (<c>NavigationManager.NavigateTo</c> would send an absolute URL built from the
/// add-on's own host, which the browser can't reach.)
/// </summary>
internal static class PostRedirectGet
{
    /// <param name="context">The form post.</param>
    /// <param name="relativePath">A path relative to the app root, e.g. <c>contacts/alice?saved=name</c>.</param>
    public static void SeeOther(HttpContext context, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        context.Response.StatusCode = StatusCodes.Status303SeeOther;
        context.Response.Headers.Location = IngressRequest.BaseHref(context) + relativePath.TrimStart('/');
    }
}
