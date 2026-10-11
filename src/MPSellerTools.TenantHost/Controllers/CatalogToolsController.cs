using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

public record WebsiteImportStarted(Guid JobId, bool DryRun);

public record CleanApplyRequest(IReadOnlyList<CleanGroupChoice>? Groups);

public record CleanIgnoreRequest(string? Key);

/// <summary>
/// Bringing products in from another store's website, and keeping the
/// catalog clean of duplicates afterwards. TenantAdmin only.
/// </summary>
[ApiController]
[Route("api/catalog-tools")]
[Authorize(Policy = Roles.TenantAdmin)]
public class CatalogToolsController(TenantDbContext db, CatalogCleaner cleaner, AuditLogger audit) : ControllerBase
{
    private static readonly BulkJobStatus[] Unfinished = [BulkJobStatus.Queued, BulkJobStatus.Running];

    /// <summary>
    /// Queues an import of another store's catalog, or with <c>dryRun</c> only a reading of it that reports
    /// what would be imported. The options are checked here, so a wrong one is said at once.
    /// </summary>
    [HttpPost("website-import")]
    public async Task<IActionResult> StartWebsiteImport([FromBody] WebsiteImportOptions options, CancellationToken cancellationToken)
    {
        var (host, problem) = WebsiteImport.HostOf(options.Source);
        if (host is null)
        {
            return Problem(problem, statusCode: StatusCodes.Status400BadRequest);
        }
        if (options.Platform is not ("shopify" or "magento") || options.Rules is not ("health" or "general")
            || options.Variants is not ("first" or "all" or "skip") || options.Existing is not ("skip" or "refresh"))
        {
            return Problem("Choose the store's platform, the rules to sort by, what to do with variants and with products already here.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (options.PriceAdjustPercent is < -90 or > 500 || options.Stock is < 0 or > 100000 || options.MaxPictures is < 1 or > 12
            || options.Limit is < 0 or > 50000 || options.SkuPrefix is { Length: > 20 })
        {
            return Problem(
                "The price adjustment is between -90% and 500%, stock up to 100,000, pictures 1 to 12 a product, a limit up to 50,000 and a SKU prefix up to 20 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (await db.BulkJobs.AnyAsync(j => j.Type == BulkJobType.ImportFromWebsite && Unfinished.Contains(j.Status), cancellationToken))
        {
            return Problem("An import from a website is already waiting or running. Let it finish, or stop it on the Jobs page.", statusCode: StatusCodes.Status409Conflict);
        }

        var job = new BulkJob
        {
            Id = Guid.NewGuid(),
            Type = BulkJobType.ImportFromWebsite,
            Status = BulkJobStatus.Queued,
            ChannelAccountId = null,
            ParametersJson = WebsiteImport.Serialize(options with { Source = host }),
            CreatedByUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty,
            CreatedByEmail = User.Identity?.Name ?? "unknown",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.BulkJobs.Add(job);
        audit.Log("BulkJobQueued", $"type={job.Type}; source={host}; dryRun={options.DryRun}");
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new WebsiteImportStarted(job.Id, options.DryRun));
    }

    /// <summary>The catalog's health figures and its groups of duplicates.</summary>
    [HttpGet("clean")]
    public async Task<IActionResult> Scan([FromQuery] string? strategies, [FromQuery] int threshold = 80, CancellationToken cancellationToken = default)
    {
        var chosen = (strategies ?? string.Join(',', CatalogCleaner.AllStrategies))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => CatalogCleaner.AllStrategies.Contains(s)).Distinct().ToList();
        return Ok(await cleaner.ScanAsync(chosen, Math.Clamp(threshold, 50, 100) / 100d, cancellationToken));
    }

    /// <summary>Retires the products chosen as duplicates. Nothing is deleted, and the clean can be undone.</summary>
    [HttpPost("clean/apply")]
    public async Task<IActionResult> Apply([FromBody] CleanApplyRequest request, CancellationToken cancellationToken)
    {
        var groups = (request.Groups ?? []).Where(g => g.RetireIds is { Count: > 0 } && !string.IsNullOrWhiteSpace(g.Key)).ToList();
        if (groups.Count is 0 or > 500)
        {
            return Problem("Choose between 1 and 500 groups of duplicates to clean.", statusCode: StatusCodes.Status400BadRequest);
        }
        return Ok(await cleaner.RetireAsync(groups, cancellationToken));
    }

    /// <summary>Brings back every product the last clean retired.</summary>
    [HttpPost("clean/undo")]
    public async Task<IActionResult> Undo(CancellationToken cancellationToken) => Ok(new { restored = await cleaner.UndoLastAsync(cancellationToken) });

    /// <summary>Marks a group as not duplicates after all, so it is not shown again.</summary>
    [HttpPost("clean/ignore")]
    public async Task<IActionResult> Ignore([FromBody] CleanIgnoreRequest request, CancellationToken cancellationToken)
    {
        var key = request.Key?.Trim() ?? "";
        if (key.Length is 0 or > 64)
        {
            return Problem("Say which group to leave alone.", statusCode: StatusCodes.Status400BadRequest);
        }
        await cleaner.IgnoreAsync(key, cancellationToken);
        return NoContent();
    }
}
