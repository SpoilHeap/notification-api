using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using NotificationApi.Web.Ingress;

namespace NotificationApi.Web.Authentication;

internal static class AuthenticationExtensions
{
    public const string ApiPathPrefix = "/api";

    public static IServiceCollection AddNotificationApiAuth(this IServiceCollection services)
    {
        services.AddAuthentication(AuthSchemes.ByPath)
            .AddPolicyScheme(AuthSchemes.ByPath, displayName: null, o =>
                o.ForwardDefaultSelector = context => context.Request.Path.StartsWithSegments(ApiPathPrefix)
                    ? AuthSchemes.ApiToken
                    : AuthSchemes.Ingress)
            .AddScheme<AuthenticationSchemeOptions, IngressAuthenticationHandler>(AuthSchemes.Ingress, displayName: null, configureOptions: null)
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(AuthSchemes.ApiToken, displayName: null, configureOptions: null);

        services.AddAuthorizationBuilder()
            // Deny anything that doesn't opt into a policy (or AllowAnonymous) explicitly (engineering_standards.md §6).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAssertion(_ => false).Build())
            .AddPolicy(AuthPolicies.Ingress, p => p.AddAuthenticationSchemes(AuthSchemes.Ingress).RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.ApiToken, p => p.AddAuthenticationSchemes(AuthSchemes.ApiToken).RequireAuthenticatedUser());

        return services;
    }
}
