using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

public record StartBulkJobRequest(BulkJobType Type, Guid ChannelAccountId);

public record BulkJobResponse(
    Guid Id,
    BulkJobType Type,
    BulkJobStatus Status,
    Guid ChannelAccountId,
    string? AccountName,
    SalesChannel? Channel,
    int Total,
    int Processed,
    int Succeeded,
    int Failed,
    bool CancelRequested,
    string? Summary,
    string? LastError,
    IReadOnlyList<BulkJobError> Errors,
    string CreatedByEmail,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc);

/// <summary>
/// The company's bulk jobs: large pieces of work on a sales channel's
/// listings, asked for here and carried out in the background by
/// <see cref="BulkJobWorker"/>. TenantAdmin only.
/// </summary>
[ApiController]
[Route("api/bulk-jobs")]
[Authorize(Policy = Roles.TenantAdmin)]
public class BulkJobsController(TenantDbContext db, AuditLogger audit) : ControllerBase
{
    private static readonly BulkJobStatus[] Unfinished = [BulkJobStatus.Queued, BulkJobStatus.Running];

    /// <summary>The newest jobs first, up to 200.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var jobs = await db.BulkJobs.AsNoTracking().OrderByDescending(j => j.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
        var accounts = await AccountsAsync(cancellationToken);
        return Ok(jobs.Select(job => ToResponse(job, accounts)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var job = await db.BulkJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        return job is null ? NotFound() : Ok(ToResponse(job, await AccountsAsync(cancellationToken)));
    }

    /// <summary>Queues a job. It starts when the jobs ahead of it are done.</summary>
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartBulkJobRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Type))
        {
            return Problem("Choose what the job should do.", statusCode: StatusCodes.Status400BadRequest);
        }

        var account = await db.ChannelAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.ChannelAccountId, cancellationToken);
        if (account is null)
        {
            return Problem("Choose the sales channel the job is for.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (request.Type == BulkJobType.ReadStore && account.Channel is not (SalesChannel.Ebay or SalesChannel.Magento))
        {
            return Problem($"{account.Channel} reports its listings through the sync queue; there is nothing to read on request.", statusCode: StatusCodes.Status400BadRequest);
        }
        // Two of the same at once would only do each other's work twice.
        if (await db.BulkJobs.AnyAsync(
                j => j.ChannelAccountId == account.Id && j.Type == request.Type && Unfinished.Contains(j.Status), cancellationToken))
        {
            return Problem("The same job is already waiting or running for this sales channel.", statusCode: StatusCodes.Status409Conflict);
        }

        var job = new BulkJob
        {
            Id = Guid.NewGuid(),
            Type = request.Type,
            Status = BulkJobStatus.Queued,
            ChannelAccountId = account.Id,
            CreatedByUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty,
            CreatedByEmail = User.Identity?.Name ?? "unknown",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.BulkJobs.Add(job);
        audit.Log("BulkJobQueued", $"type={job.Type}; channel={account.Channel}; account={account.Name}");
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(ToResponse(job, new Dictionary<Guid, (string, SalesChannel)> { [account.Id] = (account.Name, account.Channel) }));
    }

    /// <summary>Stops a job: one still waiting at once, a running one after the items it is on.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var job = await db.BulkJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }
        if (!Unfinished.Contains(job.Status))
        {
            return Problem("This job has already finished.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        job.CancelRequested = true;
        if (job.Status == BulkJobStatus.Queued)
        {
            job.Status = BulkJobStatus.Cancelled;
            job.Summary = "Cancelled before it started.";
            job.FinishedAtUtc = now;
        }
        audit.Log("BulkJobCancelRequested", $"type={job.Type}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(job, await AccountsAsync(cancellationToken)));
    }

    /// <summary>
    /// Queues the same work again as a new job. Each job works on what is
    /// left to do, so this picks up whatever the first one did not finish.
    /// </summary>
    [HttpPost("{id:guid}/run-again")]
    public async Task<IActionResult> RunAgain(Guid id, CancellationToken cancellationToken)
    {
        var job = await db.BulkJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }
        return Unfinished.Contains(job.Status)
            ? Problem("This job has not finished yet.", statusCode: StatusCodes.Status409Conflict)
            : await Start(new StartBulkJobRequest(job.Type, job.ChannelAccountId), cancellationToken);
    }

    /// <summary>Removes a finished job from the list. What it did stays done.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        var job = await db.BulkJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }
        if (Unfinished.Contains(job.Status))
        {
            return Problem("Stop the job before removing it.", statusCode: StatusCodes.Status409Conflict);
        }

        db.BulkJobs.Remove(job);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Dictionary<Guid, (string Name, SalesChannel Channel)>> AccountsAsync(CancellationToken cancellationToken) =>
        (await db.ChannelAccounts.AsNoTracking().Select(a => new { a.Id, a.Name, a.Channel }).ToListAsync(cancellationToken))
        .ToDictionary(a => a.Id, a => (a.Name, a.Channel));

    private static BulkJobResponse ToResponse(BulkJob job, IReadOnlyDictionary<Guid, (string Name, SalesChannel Channel)> accounts)
    {
        (string Name, SalesChannel Channel)? account = accounts.TryGetValue(job.ChannelAccountId, out var found) ? found : null;
        return new BulkJobResponse(
            job.Id, job.Type, job.Status, job.ChannelAccountId, account?.Name, account?.Channel,
            job.Total, job.Processed, job.Succeeded, job.Failed, job.CancelRequested, job.Summary, job.LastError,
            BulkJobRunner.ParseErrors(job.ErrorsJson), job.CreatedByEmail, job.CreatedAtUtc, job.StartedAtUtc, job.FinishedAtUtc);
    }
}
