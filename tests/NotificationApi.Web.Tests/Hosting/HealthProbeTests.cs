using System.Net;
using NotificationApi.Web.Hosting;

namespace NotificationApi.Web.Tests.Hosting;

public sealed class HealthProbeTests
{
    private static readonly Uri Health = new("http://127.0.0.1:8080/health");

    [Theory]
    [InlineData(null, 8080)]
    [InlineData("", 8080)]
    [InlineData("8080", 8080)]
    [InlineData("9000;9001", 9000)]
    [InlineData(" 9000 , 9001", 9000)]
    [InlineData("not-a-port", 8080)]
    [InlineData("70000", 8080)]
    public void Probes_the_first_configured_http_port_on_loopback(string? httpPorts, int expectedPort) =>
        HealthProbe.LocalHealthUri(httpPorts).ShouldBe(new Uri($"http://127.0.0.1:{expectedPort}/health"));

    [Fact]
    public void Only_the_exact_argument_triggers_the_probe()
    {
        HealthProbe.IsRequested(["--healthcheck"]).ShouldBeTrue();
        HealthProbe.IsRequested([]).ShouldBeFalse();
        HealthProbe.IsRequested(["--healthcheck", "--other"]).ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, 0)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 1)]
    public async Task Exit_code_reflects_the_health_status(HttpStatusCode status, int expected)
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(status));

        (await HealthProbe.CheckAsync(Health, handler)).ShouldBe(expected);
    }

    [Fact]
    public async Task Unreachable_app_is_unhealthy()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        (await HealthProbe.CheckAsync(Health, handler)).ShouldBe(1);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
