using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NotificationApi.Infrastructure.Persistence;

namespace NotificationApi.Infrastructure.Tests.Persistence;

public sealed class StorageRegistrationTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "notification-api-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Database_is_created_inside_the_configured_data_path()
    {
        await using var provider = BuildProvider(_dataPath);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        (await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        File.Exists(Path.Combine(_dataPath, "notification-api.db")).ShouldBeTrue();
    }

    [Fact]
    public async Task Empty_data_path_fails_validation()
    {
        await using var provider = BuildProvider(dataPath: "");

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<StorageOptions>>().Value);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    private static ServiceProvider BuildProvider(string dataPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataPath"] = dataPath })
            .Build();

        return new ServiceCollection()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(validateScopes: true);
    }
}
