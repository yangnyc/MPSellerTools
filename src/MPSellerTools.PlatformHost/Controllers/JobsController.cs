using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Infrastructure.Platform;
using MPSellerTools.PlatformHost.Contracts;

namespace MPSellerTools.PlatformHost.Controllers;

[ApiController]
[Route("api/jobs")]
[Authorize(Policy = Core.Tenancy.Roles.PlatformAdmin)]
public class JobsController(PlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var jobs = await (
            from job in db.ProvisioningJobs
            join tenant in db.Tenants on job.TenantId equals tenant.Id
            orderby job.CreatedAtUtc descending
            select new ProvisioningJobResponse(
                job.Id, job.TenantId, tenant.Name, job.JobType, job.Status,
                job.Attempts, job.LastError, job.CreatedAtUtc, job.UpdatedAtUtc)
        ).ToListAsync();

        return Ok(jobs);
    }
}
