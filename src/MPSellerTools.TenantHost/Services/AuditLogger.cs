using MPSellerTools.Core.Business;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.TenantHost.Services;

/// <summary>Records a tenant audit entry (brief §8 `/audit`) for the current request's actor.</summary>
public class AuditLogger(TenantDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public void Log(string action, string? details = null)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var actorId = Guid.TryParse(
            user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
        var actorEmail = user?.Identity?.Name ?? "unknown";

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = actorId,
            ActorEmail = actorEmail,
            Action = action,
            Details = details,
        });
    }
}
