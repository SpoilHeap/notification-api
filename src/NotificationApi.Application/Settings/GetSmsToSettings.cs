namespace NotificationApi.Application.Settings;

/// <summary>The SMS.to section of the Settings page.</summary>
public sealed class GetSmsToSettings(ISettingsRepository settings)
{
    public async Task<SmsToSettingsView> HandleAsync(CancellationToken cancellationToken) =>
        SmsToSettingsView.From(await settings.GetAsync(cancellationToken));
}
