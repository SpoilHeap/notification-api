using NotificationApi.Application.Channels;

namespace NotificationApi.Application.Tests.Channels;

public sealed class ChannelResultTests
{
    [Fact]
    public void Sent_result_carries_the_provider_message_id()
    {
        var result = ChannelResult.Sent("msg-123");

        result.Succeeded.ShouldBeTrue();
        result.ProviderMessageId.ShouldBe("msg-123");
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failed_result_carries_the_error()
    {
        var result = ChannelResult.Failed("timeout");

        result.Succeeded.ShouldBeFalse();
        result.ProviderMessageId.ShouldBeNull();
        result.Error.ShouldBe("timeout");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Failed_result_requires_an_error(string error) =>
        Should.Throw<ArgumentException>(() => ChannelResult.Failed(error));
}
