using System.Security.Cryptography;
using System.Text;
using NotificationApi.Domain.ApiClients;

namespace NotificationApi.Domain.Tests.ApiClients;

public sealed class ApiTokenHashTests
{
    [Fact]
    public void Is_the_sha256_of_the_token_text()
    {
        var token = ApiToken.Generate();

        token.ComputeHash().ToArray().ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(token.Value)));
    }

    [Fact]
    public void Round_trips_through_bytes()
    {
        var token = ApiToken.Generate();
        var bytes = token.ComputeHash().ToArray();

        ApiTokenHash.FromBytes(bytes).Matches(token).ShouldBeTrue();
    }

    [Fact]
    public void Is_immutable_through_its_byte_arrays()
    {
        var token = ApiToken.Generate();
        var bytes = token.ComputeHash().ToArray();
        var hash = ApiTokenHash.FromBytes(bytes);

        bytes[0] ^= 0xFF;
        hash.ToArray()[1] ^= 0xFF;

        hash.Matches(token).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Rejects_bytes_of_the_wrong_length(int length) =>
        Should.Throw<ArgumentException>(() => ApiTokenHash.FromBytes(new byte[length]));

    [Fact]
    public void Rejects_null_input()
    {
        Should.Throw<ArgumentNullException>(() => ApiTokenHash.FromBytes(null!));
        Should.Throw<ArgumentNullException>(() => ApiTokenHash.Of(null!));
    }
}
