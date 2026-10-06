using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Platform;
using MPSellerTools.Infrastructure.Platform;

namespace MPSellerTools.Provisioning.Worker;

/// <summary>
/// Polls the platform database's job queue and executes one job at a time.
/// The lease (owner id + expiry, brief §10 step 3) means a second worker
/// instance — or this same worker after a crash-restart — never picks up a
/// job another live worker already holds, and a stale lease (crashed worker)
/// is automatically reclaimed after it expires rather than stuck forever.
/// </summary>
public class ProvisioningWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ProvisioningOptions> options,
    ILogger<ProvisioningWorker> logger) : BackgroundService
{
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Provisioning worker {WorkerId} starting", _workerId);

        // Supervision state (Tenant.ProcessId/ProcessStartTimeUtc) lives in the
        // platform database, not in this process's memory, so a worker that
        // restarts (or a machine that reboots) can always tell which Active
        // tenants are no longer actually running and bring them back — brief
        // §10's "recovery of supervision or restart of managed instances."
        await ReconcileActiveTenantsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReclaimStaleLeasesAsync(stoppingToken);
                var claimed = await TryClaimNextJobAsync(stoppingToken);
                if (claimed is { } jobId)
                {
                    await ExecuteJobAsync(jobId, stoppingToken);
                    continue; // check for more work immediately
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in provisioning worker loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken);
        }
    }

    private async Task ReconcileActiveTenantsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var supervisor = scope.ServiceProvider.GetRequiredService<TenantProcessSupervisor>();
        var provisioningService = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();

        var activeTenants = await db.Tenants.Where(t => t.Status == TenantStatus.Active).ToListAsync(cancellationToken);
        foreach (var tenant in activeTenants)
        {
            if (supervisor.IsRunning(tenant))
            {
                continue;
            }

            logger.LogWarning(
                "Tenant {Slug} is Active but its process is not running (crashed or manually stopped) — restarting it", tenant.Slug);
            try
            {
                await provisioningService.ResumeAsync(tenant, cancellationToken);
                tenant.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Leave it Active but unreachable rather than guessing at a new
                // state — an operator can see it's unhealthy via /api/health
                // on that tenant's URL and retry manually if this keeps failing.
                logger.LogError(ex, "Failed to restart tenant {Slug} during startup reconciliation", tenant.Slug);
            }
        }
    }

    private async Task ReclaimStaleLeasesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var stale = await db.ProvisioningJobs
            .Where(j => j.Status == ProvisioningJobStatus.Running && j.LeaseExpiresAtUtc < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var job in stale)
        {
            logger.LogWarning("Reclaiming stale lease on job {JobId} (previously held by {Owner})", job.Id, job.LeaseOwner);
            job.Status = ProvisioningJobStatus.Pending;
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Guid?> TryClaimNextJobAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var candidate = await db.ProvisioningJobs
            .Where(j => j.Status == ProvisioningJobStatus.Pending)
            .OrderBy(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            return null;
        }

        candidate.Status = ProvisioningJobStatus.Running;
        candidate.LeaseOwner = _workerId;
        candidate.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(options.Value.LeaseDurationSeconds);
        candidate.Attempts += 1;
        candidate.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return candidate.Id;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker claimed it first between our read and our write.
            return null;
        }
    }

    private async Task ExecuteJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var provisioningService = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();

        var job = await db.ProvisioningJobs.FindAsync([jobId], cancellationToken)
            ?? throw new InvalidOperationException($"Claimed job {jobId} disappeared.");
        var tenant = await db.Tenants.FindAsync([job.TenantId], cancellationToken)
            ?? throw new InvalidOperationException($"Job {jobId} references missing tenant {job.TenantId}.");

        var tenantRemoved = false;
        try
        {
            if (job.JobType == ProvisioningJobType.Delete)
            {
                await provisioningService.DeleteAsync(tenant, cancellationToken);

                // The company leaves the registry together with its jobs (this
                // one included), which only mean something next to their tenant.
                // The audit trail stays, and carries the name the row no longer can.
                var tenantJobs = await db.ProvisioningJobs.Where(j => j.TenantId == tenant.Id).ToListAsync(cancellationToken);
                db.ProvisioningJobs.RemoveRange(tenantJobs);
                db.Tenants.Remove(tenant);
                db.AuditEntries.Add(new PlatformAuditEntry
                {
                    Id = Guid.NewGuid(),
                    OccurredAtUtc = DateTime.UtcNow,
                    ActorUserId = Guid.Empty,
                    ActorEmail = "provisioning-worker",
                    Action = "TenantDeleteSucceeded",
                    TenantId = tenant.Id,
                    Details = $"slug={tenant.Slug} name=\"{tenant.Name}\"",
                });
                await db.SaveChangesAsync(cancellationToken);
                tenantRemoved = true;
                return;
            }

            switch (job.JobType)
            {
                case ProvisioningJobType.CreateTenant:
                    await provisioningService.CreateTenantAsync(tenant, cancellationToken);
                    break;
                case ProvisioningJobType.Suspend:
                    await provisioningService.SuspendAsync(tenant, cancellationToken);
                    break;
                case ProvisioningJobType.Resume:
                    await provisioningService.ResumeAsync(tenant, cancellationToken);
                    break;
                case ProvisioningJobType.Restart:
                    await provisioningService.RestartAsync(tenant, cancellationToken);
                    break;
            }

            job.Status = ProvisioningJobStatus.Succeeded;
            job.LastError = null;
            db.AuditEntries.Add(new PlatformAuditEntry
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = DateTime.UtcNow,
                ActorUserId = Guid.Empty,
                ActorEmail = "provisioning-worker",
                Action = $"Tenant{job.JobType}Succeeded",
                TenantId = tenant.Id,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} ({JobType}) for tenant {TenantId} failed", job.Id, job.JobType, job.TenantId);
            // Never expose the raw exception to the platform console — only a
            // short, safe description (brief §10: "safe error description").
            var safeMessage = $"{job.JobType} failed: {ex.GetType().Name}: {ex.Message}";
            job.LastError = safeMessage;
            job.Status = ProvisioningJobStatus.Failed;
            tenant.Status = TenantStatus.Failed;
            tenant.FailureReason = safeMessage;
            tenant.UpdatedAtUtc = DateTime.UtcNow;
        }
        finally
        {
            if (!tenantRemoved)
            {
                job.LeaseOwner = null;
                job.LeaseExpiresAtUtc = null;
                job.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
