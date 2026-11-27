using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NotificationApi.Application.ApiAccess;
using NotificationApi.Application.Audit;
using NotificationApi.Application.Contacts;
using NotificationApi.Application.Discovery;
using NotificationApi.Application.Events;
using NotificationApi.Application.Groups;
using NotificationApi.Application.Instances;
using NotificationApi.Application.Notifications;
using NotificationApi.Application.Recipients;
using NotificationApi.Application.Settings;
using NotificationApi.Application.Webhooks;

namespace NotificationApi.Application;

public static class DependencyInjection
{
    /// <summary>Registers application use cases. Each use case is injected directly (no mediator).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<DiscoveryOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<DiscoveryOptions>, DiscoveryOptionsValidator>();
        services.AddSingleton<DiscoveryStatusStore>();

        services.AddScoped<EnsureApiToken>();
        services.AddScoped<VerifyApiToken>();
        services.AddScoped<RevealApiToken>();
        services.AddScoped<PublishDiscovery>();
        services.AddScoped<GetIntegrationDetails>();

        services.AddScoped<EnsureInstance>();
        services.AddScoped<GetRecipients>();
        services.AddScoped<RecordRecipientsChange>();

        services.AddScoped<AuditTrail>();
        services.AddScoped<GetAuditLog>();

        services.AddScoped<CreateContact>();
        services.AddScoped<RenameContact>();
        services.AddScoped<SetContactSmsNumber>();
        services.AddScoped<RemoveContactEndpoint>();
        services.AddScoped<SetContactRouting>();
        services.AddScoped<DeleteContact>();
        services.AddScoped<ListContacts>();
        services.AddScoped<GetContact>();

        services.AddScoped<CreateGroup>();
        services.AddScoped<RenameGroup>();
        services.AddScoped<AddGroupMember>();
        services.AddScoped<RemoveGroupMember>();
        services.AddScoped<DeleteGroup>();
        services.AddScoped<ListGroups>();
        services.AddScoped<GetGroup>();

        services.AddScoped<RegisterWebhook>();
        services.AddScoped<GetWebhook>();
        services.AddScoped<RemoveWebhook>();
        services.AddScoped<PublishHaEvent>();
        services.AddScoped<SendTestEvent>();

        // Delivery (spec §5, ADR 0014). The Delivery section is bound by the composition root.
        services.AddOptions<DeliveryOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<DeliveryOptions>, DeliveryOptionsValidator>();
        services.AddSingleton<DeliveryWakeUp>();
        services.AddScoped<SendNotification>();
        services.AddScoped<ProcessDueDeliveries>();
        services.AddScoped<AttemptDelivery>();
        services.AddScoped<RecoverInterruptedDeliveries>();
        services.AddScoped<AbandonDelivery>();

        services.AddScoped<GetSmsToSettings>();
        services.AddScoped<SaveSmsToSettings>();
        services.AddScoped<RemoveSmsToSettings>();
        services.AddScoped<ReadSmsToCredentials>();
        return services;
    }
}
