using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NotificationApi.Web.Authentication;
using NotificationApi.Web.Ingress;

namespace NotificationApi.Web.Tests.Authentication;

/// <summary>The claims the Ingress scheme builds from the <c>X-Remote-User-*</c> headers (used for the AuditLog, spec §8.2).</summary>
public sealed class IngressAuthenticationHandlerTests
{
    [Fact]
    public async Task Builds_the_principal_from_the_identity_headers()
    {
        var result = await AuthenticateAsync("172.30.32.2", ("X-Remote-User-Id", "abc"), ("X-Remote-User-Name", "alice"), ("X-Remote-User-Display-Name", "Alice Smith"));

        result.Succeeded.ShouldBeTrue();
        var user = result.Principal!;
        user.Identity!.AuthenticationType.ShouldBe(AuthSchemes.Ingress);
        user.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe("abc");
        user.Identity.Name.ShouldBe("alice");
        user.FindFirstValue(NotificationApiClaimTypes.DisplayName).ShouldBe("Alice Smith");
    }

    [Fact]
    public async Task Missing_name_headers_fall_back_to_the_user_id()
    {
        var result = await AuthenticateAsync("172.30.32.2", ("X-Remote-User-Id", "abc"));

        result.Principal!.Identity!.Name.ShouldBe("abc");
        result.Principal.FindFirstValue(NotificationApiClaimTypes.DisplayName).ShouldBe("abc");
    }

    [Fact]
    public async Task Headers_from_another_address_are_ignored()
    {
        var result = await AuthenticateAsync("192.168.1.50", ("X-Remote-User-Id", "abc"));

        result.None.ShouldBeTrue();
    }

    [Fact]
    public async Task Proxy_request_without_a_user_fails()
    {
        var result = await AuthenticateAsync("172.30.32.2");

        result.Failure.ShouldNotBeNull().Message.ShouldContain("X-Remote-User-Id");
    }

    [Fact]
    public async Task Dev_fake_ingress_trusts_any_address()
    {
        var result = await AuthenticateAsync("192.168.1.50", new IngressOptions { DevFakeIngress = true }, ("X-Remote-User-Id", "dev-user-id"));

        result.Succeeded.ShouldBeTrue();
    }

    private static Task<AuthenticateResult> AuthenticateAsync(string remoteIp, params (string Name, string Value)[] headers) =>
        AuthenticateAsync(remoteIp, new IngressOptions(), headers);

    private static async Task<AuthenticateResult> AuthenticateAsync(string remoteIp, IngressOptions options, params (string Name, string Value)[] headers)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Options.Create(options))
            .AddNotificationApiAuth()
            .BuildServiceProvider();
        await using var _ = services;
        var context = new DefaultHttpContext { RequestServices = services };
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        return await context.AuthenticateAsync(AuthSchemes.Ingress);
    }
}
