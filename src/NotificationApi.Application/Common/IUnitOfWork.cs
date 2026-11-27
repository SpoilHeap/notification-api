namespace NotificationApi.Application.Common;

/// <summary>Commits the changes made through repositories in the current scope.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
