using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Marketplace;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Roles.Employee)]
public class DashboardController(TenantDbContext db, SyncHealthReader health, IOptions<MarketplaceOptions> options) : ControllerBase
{
    private const int SalesDays = 30;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var isTenantAdmin = User.IsInRole(Roles.TenantAdmin);
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // brief §8: TenantAdmin sees company-wide metrics; Employee sees only
        // their own personal tasks and assigned orders.
        var orders = db.Orders.AsQueryable();
        var tasks = db.WorkItems.AsQueryable();
        if (!isTenantAdmin)
        {
            orders = orders.Where(o => o.AssignedUserId == currentUserId);
            tasks = tasks.Where(t => t.AssignedUserId == currentUserId);
        }

        var response = new TenantDashboardResponse(
            ProductCount: await db.Products.CountAsync(p => !p.IsArchived, cancellationToken),
            OpenOrderCount: await orders.CountAsync(o => o.Status == OrderStatus.New || o.Status == OrderStatus.InProgress, cancellationToken),
            OpenTaskCount: await tasks.CountAsync(t => t.Status == WorkItemStatus.Open || t.Status == WorkItemStatus.InProgress, cancellationToken),
            TotalOrderCount: await orders.CountAsync(cancellationToken),
            TotalTaskCount: await tasks.CountAsync(cancellationToken),
            Sales: isTenantAdmin ? await SalesAsync(cancellationToken) : null);

        return Ok(response);
    }

    private async Task<SalesDashboard> SalesAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = today.AddDays(-(SalesDays - 1));
        var since = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var recent = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= since && o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.CreatedAtUtc, o.Total, o.ChannelAccountId, FromEbay = o.EbayOrderId != null })
            .ToListAsync(cancellationToken);

        var accountNames = await db.ChannelAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var channels = recent
            // An order from the older eBay import carries no account; one created in the workspace carries neither.
            .GroupBy(o => o.ChannelAccountId is { } id && accountNames.TryGetValue(id, out var name) ? name : o.FromEbay ? "eBay" : "Created here")
            .Select(g => new ChannelSales(g.Key, g.Count(), g.Sum(o => o.Total)))
            .OrderByDescending(c => c.Revenue).ThenBy(c => c.Channel).ToList();
        var byDay = recent.GroupBy(o => DateOnly.FromDateTime(o.CreatedAtUtc)).ToDictionary(g => g.Key, g => (Orders: g.Count(), Revenue: g.Sum(o => o.Total)));
        var daily = Enumerable.Range(0, SalesDays).Select(i => first.AddDays(i))
            .Select(day => byDay.TryGetValue(day, out var d) ? new DailySales(day, d.Orders, d.Revenue) : new DailySales(day, 0, 0m)).ToList();

        var threshold = await db.CompanySettings.AsNoTracking().Select(s => s.LowStockThreshold).FirstOrDefaultAsync(cancellationToken)
            ?? options.Value.DefaultLowStockThreshold;
        var low =
            from variant in db.ProductVariants.AsNoTracking()
            join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
            join balance in db.InventoryBalances.AsNoTracking() on variant.Id equals balance.VariantId
            where !variant.IsArchived && !product.IsArchived && balance.LocationId == InventoryLocation.DefaultId
                && balance.OnHand - balance.Reserved - balance.SafetyStock <= threshold
            select new { variant.Id, variant.Sku, product.Name, balance.OnHand, balance.Reserved, balance.SafetyStock };
        var lowest = await low.OrderBy(v => v.OnHand - v.Reserved - v.SafetyStock).ThenBy(v => v.Sku).Take(5).ToListAsync(cancellationToken);

        var listings = await db.ChannelListings.AsNoTracking()
            .GroupBy(l => new { l.DesiredState, l.ObservedStatus }).Select(g => new { g.Key.DesiredState, g.Key.ObservedStatus, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Listed(Func<ListingDesiredState, ListingObservedStatus, bool> match) => listings.Where(l => match(l.DesiredState, l.ObservedStatus)).Sum(l => l.Count);

        var sync = await health.ReadAsync(cancellationToken);
        return new SalesDashboard(
            SalesDays,
            recent.Sum(o => o.Total),
            recent.Count,
            channels,
            daily,
            threshold,
            await low.CountAsync(cancellationToken),
            lowest.Select(v => new LowStockItem(v.Id, v.Sku, v.Name, Availability.ToSell(v.OnHand, v.Reserved, v.SafetyStock), v.OnHand)).ToList(),
            new ListingStateCounts(
                Draft: Listed((desired, _) => desired == ListingDesiredState.Draft),
                Live: Listed((desired, observed) => desired != ListingDesiredState.Draft && observed == ListingObservedStatus.Live),
                Processing: Listed((desired, observed) => desired != ListingDesiredState.Draft && observed is ListingObservedStatus.Processing or ListingObservedStatus.Unknown),
                Rejected: Listed((desired, observed) => desired != ListingDesiredState.Draft && observed == ListingObservedStatus.Rejected),
                OffSale: Listed((desired, observed) => desired != ListingDesiredState.Draft && observed is ListingObservedStatus.Inactive or ListingObservedStatus.NotListed)),
            sync.FailedJobs + sync.NeedsCorrectionJobs,
            await db.OrderLineIssues.CountAsync(i => i.ResolvedAtUtc == null, cancellationToken),
            sync.Accounts.Where(a => a.OrdersStale).Select(a => a.Name).ToList());
    }
}
