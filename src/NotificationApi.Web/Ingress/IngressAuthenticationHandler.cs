using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NotificationApi.Web.Authentication;

namespace NotificationApi.Web.Ingress;

/// <summary>
/// The <c>Ingress</c> scheme (spec §8.1, ADR 0003). A request is authenticated only when it comes from the
/// Ingress proxy address (or <c>DevFakeIngress</c> is on) and carries <c>X-Remote-User-Id</c>. The identity
/// headers are ignored from any other address. <see cref="IngressMiddleware"/> handles the PathBase separately,
/// because that has to happen before routing.
/// </summary>
internal sealed class IngressAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<IngressOptions> ingressOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var settings = ingressOptions.Value;
        if (!settings.DevFakeIngress
            && !IngressRequest.IsFromProxy(Context.Connection.RemoteIpAddress, IPAddress.Parse(settings.TrustedProxyAddress)))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var headers = Request.Headers;
        var userId = headers[IngressHeaders.UserId].ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Ingress request without {IngressHeaders.UserId}."));
        }

        var userName = FirstNonEmpty(headers[IngressHeaders.UserName].ToString(), userId);
        var displayName = FirstNonEmpty(headers[IngressHeaders.UserDisplayName].ToString(), userName);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userName),
                new Claim(NotificationApiClaimTypes.DisplayName, displayName),
            ],
            Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    /// <summary>403, not 401: the caller can't fix this by authenticating. The UI is reachable only through HA.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "text/plain; charset=utf-8";
        await Response.WriteAsync("The Notification API web UI is only available through Home Assistant (Ingress).", Context.RequestAborted);
    }

    private static string FirstNonEmpty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
