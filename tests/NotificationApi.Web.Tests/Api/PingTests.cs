using System.Net;
using System.Text.Json;

namespace NotificationApi.Web.Tests.Api;

public sealed class PingTests
{
    private const string UuidV4 = "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";

    [Fact]
    public async Task AC44_Ping_returns_ok_the_version_and_a_lower_case_uuid_v4_instance_id()
    {
        await using var factory = new NotificationApiFactory();

        using var json = await PingAsync(factory);

        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["ok", "version", "instance_id"]);
        json.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("instance_id").GetString().ShouldNotBeNull().ShouldMatch(UuidV4);
    }

    [Fact]
    public async Task AC44_The_instance_id_survives_a_restart_and_changes_only_with_a_new_database()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "notification-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var first = await InstanceIdAsync(dataPath);
            var afterRestart = await InstanceIdAsync(dataPath);
            await using var fresh = new NotificationApiFactory();
            using var freshPing = await PingAsync(fresh);

            afterRestart.ShouldBe(first);
            freshPing.RootElement.GetProperty("instance_id").GetString().ShouldNotBe(first);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dataPath, recursive: true);
        }
    }

    private static async Task<string> InstanceIdAsync(string dataPath)
    {
        await using var factory = new NotificationApiFactory { DataPath = dataPath, DeleteDataOnDispose = false };
        using var json = await PingAsync(factory);
        return json.RootElement.GetProperty("instance_id").GetString()!;
    }

    private static async Task<JsonDocument> PingAsync(NotificationApiFactory factory)
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(
            TestRequests.Api(HttpMethod.Get, "/api/v1/ping", await factory.GetApiTokenAsync()), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
