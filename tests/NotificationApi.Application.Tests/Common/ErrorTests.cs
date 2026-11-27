using NotificationApi.Application.Common;

namespace NotificationApi.Application.Tests.Common;

public sealed class ErrorTests
{
    [Fact]
    public void Validation_errors_carry_their_field_errors()
    {
        var fields = new Dictionary<string, string[]> { ["url"] = ["bad"] };

        var error = Error.Validation(fields);

        error.Code.ShouldBe(Error.ValidationCode);
        error.FieldErrors.ShouldBe(fields);
        Should.Throw<ArgumentNullException>(() => Error.Validation(null!));
    }

    [Fact]
    public void Other_errors_have_no_field_errors() =>
        new Error("x", "y").FieldErrors.ShouldBeEmpty();
}
