using NotificationApi.Domain.Settings;

namespace NotificationApi.Application.Settings;

public interface ISettingsRepository
{
    /// <summary>The settings row, or null before anything has been saved.</summary>
    Task<AppSettings?> GetAsync(CancellationToken cancellationToken);

    void Add(AppSettings settings);
}
