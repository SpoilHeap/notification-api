using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationApi.Application.Common;
using NotificationApi.Infrastructure.Security;

namespace NotificationApi.Infrastructure.Tests.Security;

public sealed class DataProtectionSecretProtectorTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "notification-api-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Round_trips_a_secret_without_storing_it_in_clear()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        var stored = protector.Protect("s3cret-token");

        stored.ShouldNotContain("s3cret-token");
        protector.Unprotect(stored).Value.ShouldBe("s3cret-token");
    }

    [Fact]
    public void A_secret_from_another_key_ring_is_unreadable()
    {
        var stored = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Protect("s3cret-token");

        var result = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Unprotect(stored);

        result.Error.ShouldBe(SecretErrors.Unreadable);
    }

    [Fact]
    public void Garbage_is_unreadable() =>
        new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Unprotect("not protected").Error.ShouldBe(SecretErrors.Unreadable);

    [Fact]
    public void Null_input_throws()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        Should.Throw<ArgumentNullException>(() => protector.Protect(null!));
        Should.Throw<ArgumentNullException>(() => protector.Unprotect(null!));
    }

    [Fact]
    public void Keys_persist_in_the_data_path_so_secrets_survive_a_restart()
    {
        string stored;
        using (var first = BuildProvider())
        {
            stored = first.GetRequiredService<ISecretProtector>().Protect("s3cret-token");
        }

        using var second = BuildProvider();

        second.GetRequiredService<ISecretProtector>().Unprotect(stored).Value.ShouldBe("s3cret-token");
        Directory.EnumerateFiles(Path.Combine(_dataPath, "keys"), "key-*.xml").ShouldNotBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    private ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataPath"] = _dataPath })
                .Build())
            .BuildServiceProvider();
}
