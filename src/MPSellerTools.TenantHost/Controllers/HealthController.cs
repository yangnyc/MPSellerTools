using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/health")]
[AllowAnonymous]
public class HealthController(TenantDbContext db, TenantOptions tenantOptions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        bool databaseReachable;
        bool migrationsApplied;
        try
        {
            databaseReachable = await db.Database.CanConnectAsync();
            var pending = await db.Database.GetPendingMigrationsAsync();
            migrationsApplied = databaseReachable && !pending.Any();
        }
        catch
        {
            databaseReachable = false;
            migrationsApplied = false;
        }

        return Ok(new HealthResponse(
            tenantOptions.TenantId, tenantOptions.ApplicationInstanceId, databaseReachable, migrationsApplied));
    }
}
