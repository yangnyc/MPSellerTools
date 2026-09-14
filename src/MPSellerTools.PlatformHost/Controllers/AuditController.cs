using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Infrastructure.Platform;

namespace MPSellerTools.PlatformHost.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Core.Tenancy.Roles.PlatformAdmin)]
public class AuditController(PlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? tenantId, [FromQuery] string? action, [FromQuery] int take = 100)
    {
        var query = db.AuditEntries.AsQueryable();
        if (tenantId is { } id)
        {
            query = query.Where(a => a.TenantId == id);
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        var entries = await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        return Ok(entries);
    }
}
