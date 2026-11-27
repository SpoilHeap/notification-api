using NotificationApi.Web.Api;

namespace NotificationApi.Web.Tests.Api;

public sealed class PingVersionTests
{
    [Theory]
    [InlineData("0.2.0+a42b607c9e", "0.2.0")]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("0.0.0-dev+abc", "0.0.0-dev")]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    public void Reports_the_version_without_build_metadata(string? informational, string expected) =>
        ApiEndpoints.AddOnVersion(informational).ShouldBe(expected);
}
