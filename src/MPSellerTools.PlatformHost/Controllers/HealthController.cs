using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Infrastructure.Platform;
using MPSellerTools.PlatformHost.Contracts;

namespace MPSellerTools.PlatformHost.Controllers;

/// <summary>Used by DevHost/scripts/Verify.ps1 to detect readiness. No secrets.</summary>
[ApiController]
[Route("api/health")]
[AllowAnonymous]
public class HealthController(PlatformDbContext db) : ControllerBase
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

        return Ok(new HealthResponse(databaseReachable, migrationsApplied));
    }
}
