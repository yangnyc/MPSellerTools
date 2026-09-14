using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using MPSellerTools.Core.Platform;
using MPSellerTools.Infrastructure.Platform;
using MPSellerTools.PlatformHost.Contracts;

namespace MPSellerTools.PlatformHost.Controllers;

[ApiController]
[Route("api/dashboard")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = Core.Tenancy.Roles.PlatformAdmin)]
public class DashboardController(PlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var counts = new TenantStatusCounts(
            Provisioning: await db.Tenants.CountAsync(t => t.Status == TenantStatus.Provisioning),
            Active: await db.Tenants.CountAsync(t => t.Status == TenantStatus.Active),
            Suspended: await db.Tenants.CountAsync(t => t.Status == TenantStatus.Suspended),
            Failed: await db.Tenants.CountAsync(t => t.Status == TenantStatus.Failed));

        var recentJobs = await (
            from job in db.ProvisioningJobs
            join tenant in db.Tenants on job.TenantId equals tenant.Id
            orderby job.UpdatedAtUtc descending
            select new RecentJobSummary(job.Id, tenant.Name, job.JobType, job.Status, job.UpdatedAtUtc)
        ).Take(10).ToListAsync();

        return Ok(new PlatformDashboardResponse(counts, recentJobs));
    }
}
