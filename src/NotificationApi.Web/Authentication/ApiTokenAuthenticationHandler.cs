using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NotificationApi.Application.ApiAccess;

namespace NotificationApi.Web.Authentication;

/// <summary><c>Authorization: Bearer &lt;token&gt;</c>, verified against the stored SHA-256 hash (spec §8.3).</summary>
internal sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    VerifyApiToken verifyApiToken)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string Bearer = "Bearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, Bearer, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await verifyApiToken.HandleAsync(header.Parameter, Context.RequestAborted);
        if (result.IsFailure)
        {
            return AuthenticateResult.Fail(result.Error.Message);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, result.Value.Id.ToString()),
                new Claim(ClaimTypes.Name, result.Value.Name),
            ],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var authenticated = await HandleAuthenticateOnceSafeAsync();
        var invalidToken = authenticated.Failure is not null;

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = invalidToken ? $"{Bearer} error=\"invalid_token\"" : Bearer;
        await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                detail: invalidToken ? "The bearer token is invalid." : "A bearer token is required.")
            .ExecuteAsync(Context);
    }
}
