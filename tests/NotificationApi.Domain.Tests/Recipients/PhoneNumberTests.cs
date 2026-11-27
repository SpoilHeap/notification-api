using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Recipients;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+353861234567", "+353861234567")]
    [InlineData("+353 86 123 4567", "+353861234567")]
    [InlineData("+353-86-123-4567", "+353861234567")]
    [InlineData("+353 (86) 123-4567", "+353861234567")]
    [InlineData("  +44 20 7946 0958  ", "+442079460958")]
    [InlineData("+1 (555) 010-9999", "+15550109999")]
    [InlineData("+12345678", "+12345678")] // 8 digits: the shortest accepted
    [InlineData("+123456789012345", "+123456789012345")] // 15 digits: the E.164 maximum
    public void AC52_Valid_numbers_are_normalised_to_E164(string input, string expected)
    {
        PhoneNumber.TryParse(input, out var number).ShouldBeTrue();

        number.Value.ShouldBe(expected);
        PhoneNumber.Parse(input).ShouldBe(number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0861234567")] // no leading +
    [InlineData("00353861234567")] // international prefix instead of +
    [InlineData("353861234567")]
    [InlineData("+")]
    [InlineData("+1234567")] // 7 digits
    [InlineData("+1234567890123456")] // 16 digits
    [InlineData("+0353861234567")] // country codes don't start with 0
    [InlineData("+353 86 123 456a")]
    [InlineData("+353.86.123.4567")]
    [InlineData("++353861234567")]
    [InlineData("+353861234567 ext 2")]
    [InlineData("+٣٥٣٨٦١٢٣٤٥٦٧")] // non-ASCII digits
    public void AC52_Invalid_numbers_are_rejected(string? input)
    {
        PhoneNumber.TryParse(input, out var number).ShouldBeFalse();
        number.ShouldBeNull();
    }

    [Fact]
    public void Parse_throws_FormatException_for_invalid_input() =>
        Should.Throw<FormatException>(() => PhoneNumber.Parse("0861234567"));

    [Theory]
    [InlineData("+353861234567", "+3538****567")]
    [InlineData("+12345678", "+12****78")]
    [InlineData("+123456789012345", "+1234****345")]
    public void AC74_The_masked_form_hides_the_middle_digits(string input, string masked)
    {
        var number = PhoneNumber.Parse(input);

        number.Masked.ShouldBe(masked);
        number.ToString().ShouldBe(masked);
        $"{number}".ShouldNotContain(input);
    }

    [Fact]
    public void AC74_MaskAll_masks_every_full_number_in_text() =>
        PhoneNumber.MaskAll("from +353861234567 to +12345678, not 12345678 or +1234567")
            .ShouldBe("from +3538****567 to +12****78, not 12345678 or +1234567");

    [Fact]
    public void Numbers_with_the_same_digits_are_equal() =>
        PhoneNumber.Parse("+353 86 123 4567").ShouldBe(PhoneNumber.Parse("+353861234567"));
}
