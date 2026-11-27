using System.Security.Claims;
using NotificationApi.Domain.Audit;

namespace NotificationApi.Web.Authentication;

internal static class AuditActors
{
    /// <summary>The HA user behind an Ingress request, for the audit log (spec §8.2).</summary>
    public static AuditActor ToAuditActor(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("A UI request reached a page without the Ingress user id.");
        }

        return new AuditActor(id, user.FindFirstValue(NotificationApiClaimTypes.DisplayName) ?? user.Identity?.Name ?? id);
    }
}
