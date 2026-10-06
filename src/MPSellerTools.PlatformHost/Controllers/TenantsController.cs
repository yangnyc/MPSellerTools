using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Platform;
using MPSellerTools.Infrastructure.Platform;
using MPSellerTools.PlatformHost.Contracts;

namespace MPSellerTools.PlatformHost.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize(Policy = Core.Tenancy.Roles.PlatformAdmin)]
public class TenantsController(PlatformDbContext db, UserManager<PlatformUser> userManager) : ControllerBase
{
    private const int MaxPortAllocationAttempts = 5;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tenants = await db.Tenants
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new TenantSummaryResponse(t.Id, t.Name, t.Slug, t.Status, t.Url, t.CreatedAtUtc))
            .ToListAsync();
        return Ok(tenants);
    }

    [HttpGet("runtime")]
    public async Task<IActionResult> Runtime()
    {
        var tenants = await db.Tenants
            .OrderBy(t => t.Name)
            .Select(t => new TenantRuntimeResponse(
                t.Id, t.Name, t.Slug, t.Status, t.Url, t.Port, t.DatabaseName,
                t.ApplicationInstanceId, t.ProcessId, t.ProcessStartTimeUtc, t.UpdatedAtUtc))
            .ToListAsync();
        return Ok(tenants);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null)
        {
            return NotFound();
        }

        return Ok(new TenantDetailResponse(
            tenant.Id, tenant.Name, tenant.Slug, tenant.Status, tenant.Url,
            tenant.FailureReason, tenant.CreatedAtUtc, tenant.UpdatedAtUtc));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest request)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null)
        {
            return NotFound();
        }

        var name = request.Name.Trim();
        if (name.Length is 0 or > 200)
        {
            return Problem("Name is required and must be 200 characters or fewer.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (name != tenant.Name)
        {
            var actorId = userManager.GetUserId(User) is { } uid ? Guid.Parse(uid) : Guid.Empty;
            var now = DateTime.UtcNow;

            db.AuditEntries.Add(new PlatformAuditEntry
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = now,
                ActorUserId = actorId,
                ActorEmail = User.Identity?.Name ?? "unknown",
                Action = "TenantRenamed",
                TenantId = tenant.Id,
                Details = $"from=\"{tenant.Name}\" to=\"{name}\"",
            });

            tenant.Name = name;
            tenant.UpdatedAtUtc = now;
            await db.SaveChangesAsync();
        }

        return Ok(new TenantDetailResponse(
            tenant.Id, tenant.Name, tenant.Slug, tenant.Status, tenant.Url,
            tenant.FailureReason, tenant.CreatedAtUtc, tenant.UpdatedAtUtc));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            return Problem("Name is required and must be 200 characters or fewer.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!System.Net.Mail.MailAddress.TryCreate(request.InitialAdminEmail, out _))
        {
            return Problem("A valid initial administrator email is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var slug = SlugValidator.Normalize(request.Slug);
        if (!SlugValidator.IsValid(slug))
        {
            return Problem(
                "Slug must be 3-40 characters: lowercase letters, digits, and single hyphens only.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (await db.Tenants.AnyAsync(t => t.Slug == slug))
        {
            return Problem("A company with this slug already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var actorId = userManager.GetUserId(User) is { } id ? Guid.Parse(id) : Guid.Empty;

        for (var attempt = 0; attempt < MaxPortAllocationAttempts; attempt++)
        {
            var maxPort = await db.Tenants.MaxAsync(t => (int?)t.Port) ?? (PortAllocation.MinPort - 1);
            var candidatePort = Math.Max(maxPort + 1, PortAllocation.MinPort);
            if (candidatePort > PortAllocation.MaxPort)
            {
                return Problem("No local ports remain available for a new tenant instance.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var now = DateTime.UtcNow;
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Slug = slug,
                InitialAdminEmail = request.InitialAdminEmail.Trim(),
                Status = TenantStatus.Provisioning,
                Port = candidatePort,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            var job = new ProvisioningJob
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                JobType = ProvisioningJobType.CreateTenant,
                Status = ProvisioningJobStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            db.Tenants.Add(tenant);
            db.ProvisioningJobs.Add(job);
            db.AuditEntries.Add(new PlatformAuditEntry
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = now,
                ActorUserId = actorId,
                ActorEmail = User.Identity?.Name ?? "unknown",
                Action = "TenantCreateRequested",
                TenantId = tenant.Id,
                Details = $"slug={slug}",
            });

            try
            {
                await db.SaveChangesAsync();
                return Accepted(new CreateTenantResponse(tenant.Id, job.Id));
            }
            catch (DbUpdateException ex) when (IsUniqueIndexViolation(ex, "IX_Tenants_Port"))
            {
                db.ChangeTracker.Clear();
                _ = ex; // retry with a freshly recomputed port
            }
            catch (DbUpdateException ex) when (IsUniqueIndexViolation(ex, "IX_Tenants_Slug"))
            {
                _ = ex;
                return Problem("A company with this slug already exists.", statusCode: StatusCodes.Status409Conflict);
            }
        }

        return Problem("Could not allocate a port for the new tenant after several attempts; please retry.", statusCode: StatusCodes.Status409Conflict);
    }

    [HttpPost("{id:guid}/suspend")]
    public Task<IActionResult> Suspend(Guid id) => EnqueueLifecycleJob(id, ProvisioningJobType.Suspend, TenantStatus.Active);

    [HttpPost("{id:guid}/resume")]
    public Task<IActionResult> Resume(Guid id) => EnqueueLifecycleJob(id, ProvisioningJobType.Resume, TenantStatus.Suspended);

    [HttpPost("{id:guid}/restart")]
    public Task<IActionResult> Restart(Guid id) => EnqueueLifecycleJob(id, ProvisioningJobType.Restart, TenantStatus.Active);

    [HttpPost("{id:guid}/retry-provisioning")]
    public async Task<IActionResult> RetryProvisioning(Guid id)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null)
        {
            return NotFound();
        }
        if (tenant.Status != TenantStatus.Failed)
        {
            return Problem("Only a Failed tenant can be retried.", statusCode: StatusCodes.Status400BadRequest);
        }

        var existingJob = await db.ProvisioningJobs
            .Where(j => j.TenantId == id && j.JobType == ProvisioningJobType.CreateTenant)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync();

        var now = DateTime.UtcNow;
        tenant.Status = TenantStatus.Provisioning;
        tenant.FailureReason = null;
        tenant.UpdatedAtUtc = now;

        if (existingJob is not null)
        {
            existingJob.Status = ProvisioningJobStatus.Pending;
            existingJob.LastError = null;
            existingJob.LeaseOwner = null;
            existingJob.LeaseExpiresAtUtc = null;
            existingJob.UpdatedAtUtc = now;
        }
        else
        {
            db.ProvisioningJobs.Add(new ProvisioningJob
            {
                Id = Guid.NewGuid(),
                TenantId = id,
                JobType = ProvisioningJobType.CreateTenant,
                Status = ProvisioningJobStatus.Pending,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        await db.SaveChangesAsync();
        return Accepted();
    }

    /// <summary>
    /// Queues the permanent removal of a company: its instance, its database and
    /// its local files. The caller must repeat the slug, so a mistyped id or a
    /// replayed request cannot delete a company by accident.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string? confirmSlug)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null)
        {
            return NotFound();
        }
        if (!string.Equals(confirmSlug?.Trim(), tenant.Slug, StringComparison.Ordinal))
        {
            return Problem("Type the company's slug exactly to confirm the deletion.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (tenant.Status is TenantStatus.Provisioning or TenantStatus.Deleting)
        {
            return Problem(
                tenant.Status == TenantStatus.Deleting
                    ? "This company is already being deleted."
                    : "This company is still being provisioned. Wait until that finishes, then delete it.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var actorId = userManager.GetUserId(User) is { } uid ? Guid.Parse(uid) : Guid.Empty;
        var now = DateTime.UtcNow;

        tenant.Status = TenantStatus.Deleting;
        tenant.FailureReason = null;
        tenant.UpdatedAtUtc = now;
        db.ProvisioningJobs.Add(new ProvisioningJob
        {
            Id = Guid.NewGuid(),
            TenantId = id,
            JobType = ProvisioningJobType.Delete,
            Status = ProvisioningJobStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        db.AuditEntries.Add(new PlatformAuditEntry
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = now,
            ActorUserId = actorId,
            ActorEmail = User.Identity?.Name ?? "unknown",
            Action = "TenantDeleteRequested",
            TenantId = tenant.Id,
            Details = $"slug={tenant.Slug} name=\"{tenant.Name}\"",
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("This company changed while you were deleting it. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return Accepted();
    }

    private async Task<IActionResult> EnqueueLifecycleJob(Guid tenantId, ProvisioningJobType jobType, TenantStatus requiredCurrentStatus)
    {
        var tenant = await db.Tenants.FindAsync(tenantId);
        if (tenant is null)
        {
            return NotFound();
        }
        if (tenant.Status != requiredCurrentStatus)
        {
            return Problem(
                $"Tenant must be {requiredCurrentStatus} for this action (currently {tenant.Status}).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var now = DateTime.UtcNow;
        db.ProvisioningJobs.Add(new ProvisioningJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            JobType = jobType,
            Status = ProvisioningJobStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();
        return Accepted();
    }

    private static bool IsUniqueIndexViolation(DbUpdateException ex, string indexName) =>
        ex.InnerException?.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase) == true;
}
