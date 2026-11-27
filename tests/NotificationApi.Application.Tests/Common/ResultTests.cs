using NotificationApi.Application.Common;

namespace NotificationApi.Application.Tests.Common;

public sealed class ResultTests
{
    private static readonly Error Boom = new("test.boom", "Boom.");

    [Fact]
    public void Success_carries_the_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        Result<int> result = Boom;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Boom);
        Should.Throw<InvalidOperationException>(() => result.Value).Message.ShouldContain("test.boom");
    }

    [Fact]
    public void Failure_requires_an_error() =>
        Should.Throw<ArgumentNullException>(() => Result<int>.Failure(null!));
}
