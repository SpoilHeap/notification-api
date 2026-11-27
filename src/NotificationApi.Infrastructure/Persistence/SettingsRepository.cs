using Microsoft.EntityFrameworkCore;
using NotificationApi.Application.Settings;
using NotificationApi.Domain.Settings;

namespace NotificationApi.Infrastructure.Persistence;

internal sealed class SettingsRepository(NotificationDbContext db) : ISettingsRepository
{
    public Task<AppSettings?> GetAsync(CancellationToken cancellationToken) =>
        db.Settings.SingleOrDefaultAsync(s => s.Id == AppSettings.SingletonId, cancellationToken);

    public void Add(AppSettings settings) => db.Settings.Add(settings);
}
