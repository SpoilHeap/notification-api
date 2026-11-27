using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NotificationApi.Application.Channels;
using NotificationApi.Application.Common;
using NotificationApi.Application.Notifications;
using NotificationApi.Application.Recipients;
using NotificationApi.Domain.Notifications;
using NotificationApi.Domain.Recipients;
using NotificationApi.Web.Hosting;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NotificationApi.Web.Tests.Acceptance;

/// <summary>
/// The app through its real HTTP API (spec §12 "A"), with recording fake channels in place of Telegram and SMS.to,
/// and the spec §12 fixtures: Alice (Telegram + SMS; normal → telegram, high → telegram, sms), Bob (Telegram;
/// normal/high → telegram) and Carol (no endpoints).
/// </summary>
internal sealed class DeliveryApp : IAsyncDisposable
{
    public const string AliceNumber = "+353861234567";
    public const string AliceChat = "1001";
    public const string BobChat = "1002";

    private DeliveryApp(NotificationApiFactory factory, FakeTimeProvider? time, RecordingChannel telegram, RecordingChannel sms)
    {
        Factory = factory;
        Time = time;
        Telegram = telegram;
        Sms = sms;
        Client = factory.CreateClient(); // starts the host, worker included, so the fakes must exist already
    }

    public NotificationApiFactory Factory { get; }

    public HttpClient Client { get; }

    /// <summary>Set when the app runs on fake time.</summary>
    public FakeTimeProvider? Time { get; }

    public RecordingChannel Telegram { get; }

    public RecordingChannel Sms { get; }

    public string Token { get; private set; } = default!;

    public WireMockServer? Ha { get; private set; }

    /// <param name="fakeTime">Run the app on a <see cref="FakeTimeProvider"/> (for retries and the 24 h idempotency window).</param>
    /// <param name="channels">Register the recording channels (false leaves the real SMS.to channel).</param>
    /// <param name="worker">Run the delivery worker (false to leave deliveries queued, e.g. before a restart).</param>
    /// <param name="factory">A factory to use, e.g. one that shares a data path.</param>
    /// <param name="telegram">Register the Telegram fake (false: no Telegram channel at all, as in production today).</param>
    public static async Task<DeliveryApp> StartAsync(
        bool fakeTime = false, bool channels = true, bool worker = true, NotificationApiFactory? factory = null, bool telegram = true)
    {
        factory ??= new NotificationApiFactory();
        var time = fakeTime ? new FakeTimeProvider(DateTimeOffset.UtcNow) : null;
        var clock = (TimeProvider?)time ?? TimeProvider.System;
        var telegramFake = new RecordingChannel(ChannelType.Telegram) { Time = clock };
        var smsFake = new RecordingChannel(ChannelType.Sms) { Time = clock };
        factory.TestServices.Add(services =>
        {
            if (time is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
            }

            if (channels)
            {
                services.RemoveAll<INotificationChannel>();
                if (telegram)
                {
                    services.AddSingleton<INotificationChannel>(_ => telegramFake);
                }

                services.AddSingleton<INotificationChannel>(_ => smsFake);
            }

            if (!worker)
            {
                var descriptor = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DeliveryWorkerService));
                services.Remove(descriptor);
            }
        });

        var app = new DeliveryApp(factory, time, telegramFake, smsFake);
        app.Token = await factory.GetApiTokenAsync();
        return app;
    }

    /// <summary>Alice, Bob and Carol as in spec §12.</summary>
    public async Task SeedFixturesAsync()
    {
        await InScopeAsync(async sp =>
        {
            var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
            var contacts = sp.GetRequiredService<IContactRepository>();
            var alice = Contact.Create(Handle.Contact("alice"), DisplayName.Create("Alice"), now);
            alice.LinkTelegram(AliceChat, now);
            alice.SetSmsNumber(PhoneNumber.Parse(AliceNumber), now);
            alice.SetRouting(Priority.Normal, [ChannelType.Telegram]);
            alice.SetRouting(Priority.High, [ChannelType.Telegram, ChannelType.Sms]);
            var bob = Contact.Create(Handle.Contact("bob"), DisplayName.Create("Bob"), now);
            bob.LinkTelegram(BobChat, now);
            bob.SetRouting(Priority.Normal, [ChannelType.Telegram]);
            bob.SetRouting(Priority.High, [ChannelType.Telegram]);
            var carol = Contact.Create(Handle.Contact("carol"), DisplayName.Create("Carol"), now);
            contacts.Add(alice);
            contacts.Add(bob);
            contacts.Add(carol);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        });
    }

    /// <summary>Creates a group whose members are contact or group handles, in order.</summary>
    public Task GroupAsync(string name, params string[] members) => InScopeAsync(async sp =>
    {
        var contacts = sp.GetRequiredService<IContactRepository>();
        var groups = sp.GetRequiredService<IGroupRepository>();
        var group = Group.Create(Handle.Group(name), DisplayName.Create(name), DateTimeOffset.UtcNow);
        foreach (var member in members.Select(Handle.Parse))
        {
            if (member.Type == HandleType.Contact)
            {
                group.AddMember((await contacts.FindByHandleAsync(member, CancellationToken.None))!);
            }
            else
            {
                group.AddMember((await groups.FindByHandleAsync(member, CancellationToken.None))!, new GroupGraph([.. await groups.ListAsync(CancellationToken.None), group]));
            }
        }

        groups.Add(group);
        await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    });

    /// <summary>Starts a fake HA webhook and registers it, as the integration does (spec §4.3).</summary>
    public async Task RegisterWebhookAsync(int status = 200)
    {
        Ha = WireMockServer.Start();
        Ha.Given(Request.Create().UsingPost()).RespondWith(Response.Create().WithStatusCode(status));
        using var response = await Client.PutWebhookAsync(Token, Ha.Url + "/api/webhook/test", TestRequests.NewWebhookSecret());
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary><c>POST /api/v1/notify</c> with a JSON body. Dispose the response.</summary>
    public Task<HttpResponseMessage> NotifyAsync(object body) => NotifyRawAsync(JsonSerializer.Serialize(body));

    public async Task<HttpResponseMessage> NotifyRawAsync(string json)
    {
        using var request = TestRequests.Api(HttpMethod.Post, "/api/v1/notify", Token);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A request that must be accepted: checks the 202 and returns the body.</summary>
    public async Task<Accepted> NotifyAcceptedAsync(object body)
    {
        using var response = await NotifyAsync(body);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, text);
        return new Accepted(text);
    }

    public Task<IReadOnlyList<Delivery>> DeliveriesAsync(string notificationId) => InScopeAsync(sp =>
    {
        NotificationId.TryParse(notificationId, out var id).ShouldBeTrue();
        return sp.GetRequiredService<INotificationRepository>().ListDeliveriesAsync(id, CancellationToken.None);
    });

    public Task<int> NotificationCountAsync() => InScopeAsync(sp =>
        Task.FromResult(sp.GetRequiredService<Infrastructure.Persistence.NotificationDbContext>().Notifications.Count()));

    /// <summary>Polls (bounded) until every delivery of the notification satisfies <paramref name="condition"/>.</summary>
    public async Task<IReadOnlyList<Delivery>> WaitForDeliveriesAsync(string notificationId, Func<Delivery, bool> condition, string because)
    {
        IReadOnlyList<Delivery> deliveries = [];
        for (var i = 0; i < 1000; i++)
        {
            deliveries = await DeliveriesAsync(notificationId);
            if (deliveries.Count > 0 && deliveries.All(condition))
            {
                return deliveries;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        deliveries.ShouldAllBe(d => condition(d), because);
        return deliveries;
    }

    public Task<IReadOnlyList<Delivery>> WaitForFinalAsync(string notificationId) =>
        WaitForDeliveriesAsync(notificationId, d => d.IsFinal, "every delivery should reach sent or failed");

    /// <summary>A factory with fast retries, so retry tests run in real time.</summary>
    public static NotificationApiFactory FastRetries(int maxRetries = 3, string interval = "00:00:00.050")
    {
        var factory = new NotificationApiFactory();
        factory.Settings["Delivery:MaxRetries"] = maxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture);
        factory.Settings["Delivery:RetryInterval"] = interval;
        return factory;
    }

    /// <summary>Polls (bounded) until HA has received <paramref name="count"/> events.</summary>
    public async Task<List<JsonElement>> WaitForHaEventsAsync(int count)
    {
        await TestRequests.WaitUntilAsync(() => Ha!.LogEntries.Count >= count, $"HA should receive {count} event(s)");
        return HaEvents();
    }

    /// <summary>The events HA received, parsed.</summary>
    public List<JsonElement> HaEvents() =>
        [.. Ha!.LogEntries.Select(e => JsonDocument.Parse(e.RequestMessage!.Body!).RootElement.Clone())];

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Telegram.ReleaseAll();
        Sms.ReleaseAll();
        await Factory.DisposeAsync();
        Ha?.Stop();
        Ha?.Dispose();
    }

    /// <summary>A parsed 202 body.</summary>
    internal sealed class Accepted
    {
        public Accepted(string text)
        {
            Text = text;
            using var json = JsonDocument.Parse(text);
            NotificationId = json.RootElement.GetProperty("notification_id").GetString()!;
            Deliveries = [.. json.RootElement.GetProperty("deliveries").EnumerateArray().Select(d => new AcceptedDelivery(
                d.GetProperty("delivery_id").GetString()!,
                d.GetProperty("contact").GetString()!,
                d.GetProperty("via").GetString()!,
                d.GetProperty("channel").GetString()!,
                d.GetProperty("status").GetString()!))];
        }

        public string Text { get; }

        public string NotificationId { get; }

        public IReadOnlyList<AcceptedDelivery> Deliveries { get; }

        /// <summary>contact/via/channel per delivery, e.g. <c>alice/group:family/telegram</c>.</summary>
        public IEnumerable<string> Routes => Deliveries.Select(d => $"{d.Contact}/{d.Via}/{d.Channel}");
    }

    internal sealed record AcceptedDelivery(string DeliveryId, string Contact, string Via, string Channel, string Status);
}

/// <summary>A fake channel that records what it was asked to send, and when. Thread-safe.</summary>
internal sealed class RecordingChannel(ChannelType type) : INotificationChannel
{
    private readonly ConcurrentQueue<(OutboundMessage Message, DateTimeOffset At)> _sent = new();
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public ChannelType Type { get; } = type;

    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>The answer for the n-th call (1-based). Default: sent, with a provider id.</summary>
    public Func<OutboundMessage, int, ChannelResult> Answer { get; set; } = (_, n) => ChannelResult.Sent($"{type}-{n}".ToLowerInvariant());

    /// <summary>Real-time delay before answering, to simulate a slow provider.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>When set, every send waits until <see cref="ReleaseAll"/> (to hold a delivery mid-send).</summary>
    public bool Hold { get; set; }

    public IReadOnlyList<OutboundMessage> Sent => [.. _sent.Select(s => s.Message)];

    public IReadOnlyList<DateTimeOffset> SentAt => [.. _sent.Select(s => s.At)];

    public int Calls => Volatile.Read(ref _calls);

    public void ReleaseAll() => _release.TrySetResult();

    public async Task<ChannelResult> SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        _sent.Enqueue((message, Time.GetUtcNow()));
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (Hold)
        {
            await _release.Task.WaitAsync(cancellationToken);
        }

        return Answer(message, call);
    }
}
