using NotificationApi.Domain.Recipients;

namespace NotificationApi.Domain.Tests.Recipients;

public sealed class HandleTests
{
    [Theory]
    [InlineData("contact:alice", HandleType.Contact, "alice")]
    [InlineData("group:family", HandleType.Group, "family")]
    [InlineData("group:kids_2", HandleType.Group, "kids_2")]
    public void Parses_valid_handles(string value, HandleType type, string name)
    {
        var handle = Handle.Parse(value);

        handle.Type.ShouldBe(type);
        handle.Name.ShouldBe(name);
        handle.ToString().ShouldBe(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("alice")]
    [InlineData(":alice")]
    [InlineData("contact:")]
    [InlineData("person:alice")]
    [InlineData("Contact:alice")]
    [InlineData("contact:Alice")]
    [InlineData("contact:al ice")]
    [InlineData("contact:al-ice")]
    [InlineData("contact:alice:extra")]
    public void Rejects_invalid_handles(string? value)
    {
        Handle.TryParse(value, out var handle).ShouldBeFalse();
        handle.ShouldBeNull();
    }

    [Fact]
    public void Parse_throws_FormatException_for_invalid_input() =>
        Should.Throw<FormatException>(() => Handle.Parse("nope"));

    [Fact]
    public void Name_length_is_limited()
    {
        Handle.IsValidName(new string('a', Handle.MaxNameLength)).ShouldBeTrue();
        Handle.IsValidName(new string('a', Handle.MaxNameLength + 1)).ShouldBeFalse();
    }

    [Fact]
    public void Factory_methods_validate_names()
    {
        Handle.Contact("alice").ToString().ShouldBe("contact:alice");
        Handle.Group("family").ToString().ShouldBe("group:family");
        Should.Throw<ArgumentException>(() => Handle.Contact("Not Valid"));
    }

    [Fact]
    public void Contact_and_group_with_the_same_name_are_different_handles() =>
        Handle.Contact("x").ShouldNotBe(Handle.Group("x")); // spec AC57

    [Fact]
    public void Handles_have_value_equality() =>
        Handle.Parse("group:family").ShouldBe(Handle.Group("family"));
}
