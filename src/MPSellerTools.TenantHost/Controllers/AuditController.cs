using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Roles.TenantAdmin)]
public class AuditController(TenantDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? action, [FromQuery] int take = 100)
    {
        var query = db.AuditEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        var entries = await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(a => new AuditEntryResponse(a.Id, a.OccurredAtUtc, a.ActorEmail, a.Action, a.Details))
            .ToListAsync();

        return Ok(entries);
    }
}
