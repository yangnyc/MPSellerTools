using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

/// <summary>
/// Something wrong that stays wrong until it is put right. <see cref="Key"/> names the problem, so the same
/// one is the same alert from one reading to the next; <see cref="Link"/> is the page where it is put right.
/// </summary>
public record WorkspaceAlert(string Key, string Title, string Message, string Action, string Link);

/// <summary>
/// The company's standing problems, worked out afresh each time from how things are: an alert is not
/// dismissed, it goes when its cause does. A TenantAdmin sees them; anyone else has none.
/// </summary>
[ApiController]
[Route("api/alerts")]
[Authorize(Policy = Roles.Employee)]
public class AlertsController(TenantDbContext db, MagentoSync magento, SyncHealthReader health, IMemoryCache cache) : ControllerBase
{
    /// <summary>How far back a failed job is still a standing problem.</summary>
    private const int JobDays = 7;

    /// <summary>How long a store's list of categories is trusted before the store is asked again.</summary>
    private static readonly TimeSpan StoreCategoriesFor = TimeSpan.FromMinutes(5);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(Roles.TenantAdmin))
        {
            return Ok(Array.Empty<WorkspaceAlert>());
        }

        var alerts = new List<WorkspaceAlert>();
        if (await BrokenMagentoMappingsAsync(cancellationToken) is { Count: > 0 } broken)
        {
            alerts.Add(new WorkspaceAlert(
                "magento-categories",
                $"{Count(broken.Count, "Magento category mapping points", "Magento category mappings point")} at a store category that no longer exists",
                $"{string.Join(", ", broken.Take(8))}{(broken.Count > 8 ? ", …" : "")}. Products in them are refused when sent. Open Magento → Categories and press Repair mappings.",
                "Repair mappings", "/magento/categories"));
        }

        var sync = await health.ReadAsync(cancellationToken);
        if (sync.FailedJobs + sync.NeedsCorrectionJobs is > 0 and var refused)
        {
            alerts.Add(new WorkspaceAlert(
                "sync-failed",
                $"{Count(refused, "listing was", "listings were")} not accepted by a marketplace",
                "Open each in the sync queue to read the marketplace's reason, correct the listing, and send it again.",
                "Open sync queue", "/sync"));
        }

        var unplaced = await db.OrderLineIssues.CountAsync(i => i.ResolvedAtUtc == null, cancellationToken);
        if (unplaced > 0)
        {
            alerts.Add(new WorkspaceAlert(
                "order-lines",
                $"{Count(unplaced, "order line", "order lines")} could not be matched to a product",
                "The SKU on the order is not in your catalog, or there was not enough stock. Add the product or correct the stock, then clear the line in the sync queue.",
                "Open sync queue", "/sync"));
        }

        // A job that failed is a standing problem until the same job is run again and goes through, or it is removed from the list.
        var since = DateTime.UtcNow.AddDays(-JobDays);
        var failed = (await db.BulkJobs.AsNoTracking().Where(j => j.CreatedAtUtc >= since)
                .Select(j => new { j.Type, j.ChannelAccountId, j.Status, j.CreatedAtUtc }).ToListAsync(cancellationToken))
            .GroupBy(j => (j.Type, j.ChannelAccountId))
            .Count(g => g.MaxBy(j => j.CreatedAtUtc)!.Status == BulkJobStatus.Failed);
        if (failed > 0)
        {
            alerts.Add(new WorkspaceAlert(
                "jobs-failed",
                $"{Count(failed, "background job", "background jobs")} failed",
                "Open Jobs to read why. Run again once that is put right, or remove the job from the list if it is no longer wanted.",
                "Open jobs", "/jobs"));
        }

        return Ok(alerts);
    }

    /// <summary>
    /// The product categories mapped to a number the Magento store does not have. The store's own list is
    /// asked for at most every few minutes; a store that cannot be read is no proof of anything, so it yields none.
    /// </summary>
    private async Task<List<string>> BrokenMagentoMappingsAsync(CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.AsNoTracking().OrderBy(a => a.CreatedAtUtc).FirstOrDefaultAsync(a => a.Channel == SalesChannel.Magento && a.IsEnabled, cancellationToken);
        if (account is null)
        {
            return [];
        }

        var mappings = await (
            from mapping in db.CategoryMappings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on mapping.ChannelMarketId equals market.Id
            where market.ChannelAccountId == account.Id
            orderby mapping.InternalCategory
            select new { mapping.InternalCategory, mapping.ExternalCategoryId }).ToListAsync(cancellationToken);
        if (mappings.Count == 0)
        {
            return [];
        }

        var key = $"magento-category-ids:{account.Id}:{account.UpdatedAtUtc.Ticks}";
        if (!cache.TryGetValue(key, out HashSet<string>? known))
        {
            try
            {
                known = (await magento.GetCategoriesAsync(account, cancellationToken)).Select(c => c.Id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
            }
            catch (ChannelException)
            {
                known = null;
            }
            // Not being able to ask is remembered a short while too, so a store that is down is not asked on every page.
            cache.Set(key, known, known is null ? TimeSpan.FromMinutes(1) : StoreCategoriesFor);
        }

        return known is null ? [] : mappings.Where(m => !known.Contains(m.ExternalCategoryId)).Select(m => m.InternalCategory).ToList();
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
