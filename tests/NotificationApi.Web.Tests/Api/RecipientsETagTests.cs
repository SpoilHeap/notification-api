using Microsoft.Extensions.Primitives;
using NotificationApi.Web.Api;

namespace NotificationApi.Web.Tests.Api;

public sealed class RecipientsETagTests
{
    [Fact]
    public void The_etag_is_the_quoted_revision() => RecipientsETag.For("17").ShouldBe("\"17\"");

    [Theory]
    [InlineData("\"17\"")]
    [InlineData("W/\"17\"")] // If-None-Match uses the weak comparison (RFC 9110 §13.1.2)
    [InlineData("\"16\", \"17\"")]
    [InlineData("*")]
    public void AC43_A_matching_if_none_match_means_not_modified(string header) =>
        RecipientsETag.IsNotModified(new StringValues(header), "17").ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"16\"")]
    [InlineData("17")] // unquoted: not a valid entity tag
    [InlineData("\"170\"")]
    [InlineData("garbage, \"")]
    public void AC43_Anything_else_means_modified(string? header) =>
        RecipientsETag.IsNotModified(header is null ? StringValues.Empty : new StringValues(header), "17").ShouldBeFalse();

    [Fact]
    public void Several_header_values_are_all_checked() =>
        RecipientsETag.IsNotModified(new StringValues(["\"1\"", "\"17\""]), "17").ShouldBeTrue();

    [Fact]
    public void A_revision_is_required() => Should.Throw<ArgumentException>(() => RecipientsETag.For(" "));
}
