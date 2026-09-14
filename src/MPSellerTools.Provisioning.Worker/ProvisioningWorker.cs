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

        try
        {
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
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
