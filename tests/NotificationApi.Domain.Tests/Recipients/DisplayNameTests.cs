using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Recipients;

public sealed class DisplayNameTests
{
    [Theory]
    [InlineData("Alice", "Alice")]
    [InlineData("  Alice Smith  ", "Alice Smith")]
    [InlineData("Seán Ó Briain", "Seán Ó Briain")]
    public void Names_are_trimmed(string input, string expected)
    {
        DisplayName.TryCreate(input, out var name).ShouldBeTrue();
        name.Value.ShouldBe(expected);
        name.ToString().ShouldBe(expected);
        DisplayName.Create(input).ShouldBe(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Line\nbreak")]
    [InlineData("Tab\there")]
    public void Empty_or_multi_line_names_are_rejected(string? input)
    {
        DisplayName.TryCreate(input, out var name).ShouldBeFalse();
        name.ShouldBeNull();
    }

    [Fact]
    public void Names_are_at_most_100_characters()
    {
        DisplayName.TryCreate(new string('a', DisplayName.MaxLength), out _).ShouldBeTrue();
        DisplayName.TryCreate(new string('a', DisplayName.MaxLength + 1), out _).ShouldBeFalse();
        DisplayName.TryCreate("  " + new string('a', DisplayName.MaxLength) + "  ", out _).ShouldBeTrue(); // trimmed first
        Should.Throw<ArgumentException>(() => DisplayName.Create(new string('a', DisplayName.MaxLength + 1)));
    }
}
