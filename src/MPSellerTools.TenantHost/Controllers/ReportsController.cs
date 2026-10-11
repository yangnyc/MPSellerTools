using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;

namespace MPSellerTools.TenantHost.Controllers;

public record SalesReportDay(DateOnly Date, int Orders, int Units, decimal Revenue);

public record SalesReportChannel(string Channel, int Orders, int Units, decimal Revenue);

public record SalesReportProduct(Guid ProductId, string Sku, string Name, int Units, decimal Revenue, int Orders);

/// <summary>Orders placed in the last <see cref="Days"/> days, cancelled ones left out, and the same span before it to compare with.</summary>
public record SalesReport(
    int Days,
    decimal Revenue,
    int Orders,
    int Units,
    decimal AverageOrder,
    decimal PreviousRevenue,
    int PreviousOrders,
    int Cancelled,
    IReadOnlyList<SalesReportDay> Daily,
    IReadOnlyList<SalesReportChannel> Channels,
    IReadOnlyList<SalesReportProduct> TopProducts);

public record InventoryReportItem(Guid ProductId, string Sku, string Name, string? Category, decimal Price, int OnHand, int Reserved, int AvailableToSell, decimal Value, int SoldLast30Days, int? DaysOfStock);

/// <summary><see cref="Value"/> is what the stock on hand would sell for at the products' own prices.</summary>
public record InventoryReport(
    int Products,
    int UnitsOnHand,
    decimal Value,
    int OutOfStock,
    int LowStock,
    int LowStockThreshold,
    int NotSelling,
    IReadOnlyList<InventoryReportItem> Items);

public record ListingsReportChannel(Guid AccountId, string Name, SalesChannel Channel, int Total, int Live, int Processing, int Drafts, int Rejected, int OffSale, int WithIssues);

public record ListingsReportProblem(Guid ListingId, string Channel, string Sku, string Problem);

/// <summary><see cref="NotListed"/> are active products that are on no sales channel at all.</summary>
public record ListingsReport(int Products, int NotListed, IReadOnlyList<ListingsReportChannel> Channels, IReadOnlyList<ListingsReportProblem> Problems);

/// <summary>
/// The company's figures over a span of time: what sold, what is in stock
/// and how the listings stand. Read only. TenantAdmin only.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = Roles.TenantAdmin)]
public class ReportsController(TenantDbContext db, IOptions<MarketplaceOptions> options) : ControllerBase
{
    [HttpGet("sales")]
    public async Task<IActionResult> Sales([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 365);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = today.AddDays(-(days - 1));
        var since = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var before = since.AddDays(-days);

        var orders = await db.Orders.AsNoTracking().Where(o => o.CreatedAtUtc >= before)
            .Select(o => new { o.Id, o.CreatedAtUtc, o.Total, o.Status, o.ChannelAccountId, FromEbay = o.EbayOrderId != null })
            .ToListAsync(cancellationToken);
        var current = orders.Where(o => o.CreatedAtUtc >= since && o.Status != OrderStatus.Cancelled).ToList();
        var previous = orders.Where(o => o.CreatedAtUtc < since && o.Status != OrderStatus.Cancelled).ToList();
        var currentIds = current.Select(o => o.Id).ToList();

        var lines = await (
            from item in db.OrderItems.AsNoTracking()
            join order in db.Orders.AsNoTracking() on item.OrderId equals order.Id
            where order.CreatedAtUtc >= since && order.Status != OrderStatus.Cancelled
            select new { item.OrderId, item.ProductId, item.Quantity, item.UnitPrice }).ToListAsync(cancellationToken);
        var unitsByOrder = lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var accountNames = await db.ChannelAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var channels = current
            // An order from the older eBay import carries no account; one created in the workspace carries neither.
            .GroupBy(o => o.ChannelAccountId is { } id && accountNames.TryGetValue(id, out var name) ? name : o.FromEbay ? "eBay" : "Created here")
            .Select(g => new SalesReportChannel(g.Key, g.Count(), g.Sum(o => unitsByOrder.GetValueOrDefault(o.Id)), g.Sum(o => o.Total)))
            .OrderByDescending(c => c.Revenue).ThenBy(c => c.Channel).ToList();

        var byDay = current.GroupBy(o => DateOnly.FromDateTime(o.CreatedAtUtc))
            .ToDictionary(g => g.Key, g => new SalesReportDay(g.Key, g.Count(), g.Sum(o => unitsByOrder.GetValueOrDefault(o.Id)), g.Sum(o => o.Total)));
        var daily = Enumerable.Range(0, days).Select(i => first.AddDays(i)).Select(day => byDay.GetValueOrDefault(day) ?? new SalesReportDay(day, 0, 0, 0m)).ToList();

        var top = lines.GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Units = g.Sum(l => l.Quantity), Revenue = g.Sum(l => l.Quantity * l.UnitPrice), Orders = g.Select(l => l.OrderId).Distinct().Count() })
            .OrderByDescending(p => p.Revenue).ThenByDescending(p => p.Units).Take(25).ToList();
        var topIds = top.Select(p => p.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => topIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var revenue = current.Sum(o => o.Total);
        return Ok(new SalesReport(
            days, revenue, current.Count, lines.Sum(l => l.Quantity), current.Count == 0 ? 0m : Math.Round(revenue / current.Count, 2),
            previous.Sum(o => o.Total), previous.Count,
            orders.Count(o => o.CreatedAtUtc >= since && o.Status == OrderStatus.Cancelled),
            daily, channels,
            top.Select(p => new SalesReportProduct(
                p.ProductId, products.GetValueOrDefault(p.ProductId)?.Sku ?? "", products.GetValueOrDefault(p.ProductId)?.Name ?? "(deleted product)", p.Units, p.Revenue, p.Orders)).ToList()));
    }

    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(CancellationToken cancellationToken)
    {
        var threshold = await db.CompanySettings.AsNoTracking().Select(s => s.LowStockThreshold).FirstOrDefaultAsync(cancellationToken)
            ?? options.Value.DefaultLowStockThreshold;
        var rows = await (
            from product in db.Products.AsNoTracking()
            join variant in db.ProductVariants.AsNoTracking() on product.Id equals variant.ProductId
            join b in db.InventoryBalances.AsNoTracking().Where(b => b.LocationId == InventoryLocation.DefaultId) on variant.Id equals b.VariantId into balances
            from balance in balances.DefaultIfEmpty()
            where !product.IsArchived && !variant.IsArchived && variant.IsDefault
            select new { product.Id, product.Sku, product.Name, product.Category, product.Price, OnHand = (int?)balance.OnHand, Reserved = (int?)balance.Reserved, Safety = (int?)balance.SafetyStock })
            .ToListAsync(cancellationToken);

        var since = DateTime.UtcNow.AddDays(-30);
        var sold = (await (
            from item in db.OrderItems.AsNoTracking()
            join order in db.Orders.AsNoTracking() on item.OrderId equals order.Id
            where order.CreatedAtUtc >= since && order.Status != OrderStatus.Cancelled
            group item by item.ProductId into g
            select new { ProductId = g.Key, Units = g.Sum(i => i.Quantity) }).ToListAsync(cancellationToken)).ToDictionary(s => s.ProductId, s => s.Units);

        var items = rows.Select(r =>
        {
            var (onHand, reserved, safety) = (r.OnHand ?? 0, r.Reserved ?? 0, r.Safety ?? 0);
            var soldUnits = sold.GetValueOrDefault(r.Id);
            return new InventoryReportItem(
                r.Id, r.Sku, r.Name, r.Category, r.Price, onHand, reserved, Availability.ToSell(onHand, reserved, safety), onHand * r.Price, soldUnits,
                // At the pace of the last thirty days, how long what is on hand lasts.
                soldUnits > 0 ? (int)((long)onHand * 30 / soldUnits) : null);
        }).ToList();

        return Ok(new InventoryReport(
            items.Count, items.Sum(i => i.OnHand), items.Sum(i => i.Value),
            items.Count(i => i.AvailableToSell <= 0),
            items.Count(i => i.AvailableToSell > 0 && i.AvailableToSell <= threshold),
            threshold,
            items.Count(i => i.OnHand > 0 && i.SoldLast30Days == 0),
            // The ones that need a decision first: about to run out at the pace they sell, then by what the stock is worth.
            items.OrderBy(i => i.DaysOfStock ?? int.MaxValue).ThenByDescending(i => i.Value).Take(2000).ToList()));
    }

    [HttpGet("listings")]
    public async Task<IActionResult> Listings(CancellationToken cancellationToken)
    {
        var accounts = await db.ChannelAccounts.AsNoTracking().OrderBy(a => a.CreatedAtUtc).ToListAsync(cancellationToken);
        var counted = await (
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            group listing by new { market.ChannelAccountId, listing.DesiredState, listing.ObservedStatus, HasIssues = listing.IssuesJson != null } into g
            select new { g.Key.ChannelAccountId, g.Key.DesiredState, g.Key.ObservedStatus, g.Key.HasIssues, Count = g.Count() }).ToListAsync(cancellationToken);

        var channels = accounts.Select(account =>
        {
            var own = counted.Where(c => c.ChannelAccountId == account.Id).ToList();
            int Sum(Func<ListingDesiredState, ListingObservedStatus, bool> match) => own.Where(c => match(c.DesiredState, c.ObservedStatus)).Sum(c => c.Count);
            return new ListingsReportChannel(
                account.Id, account.Name, account.Channel, own.Sum(c => c.Count),
                Sum((desired, observed) => desired != ListingDesiredState.Draft && observed == ListingObservedStatus.Live),
                Sum((desired, observed) => desired != ListingDesiredState.Draft && observed is ListingObservedStatus.Processing or ListingObservedStatus.Unknown),
                Sum((desired, _) => desired == ListingDesiredState.Draft),
                Sum((desired, observed) => desired != ListingDesiredState.Draft && observed == ListingObservedStatus.Rejected),
                Sum((desired, observed) => desired != ListingDesiredState.Draft && observed is ListingObservedStatus.Inactive or ListingObservedStatus.NotListed),
                own.Where(c => c.HasIssues).Sum(c => c.Count));
        }).ToList();

        var problems = await (
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            join account in db.ChannelAccounts.AsNoTracking() on market.ChannelAccountId equals account.Id
            where listing.IssuesJson != null || (listing.DesiredState != ListingDesiredState.Draft && listing.ObservedStatus == ListingObservedStatus.Rejected)
            orderby account.Name, listing.SellerSku
            select new { listing.Id, account.Name, listing.SellerSku, listing.IssuesJson, listing.ObservedStatus }).Take(500).ToListAsync(cancellationToken);

        var products = await db.Products.AsNoTracking().CountAsync(p => !p.IsArchived, cancellationToken);
        var listed = await (
            from listing in db.ChannelListings.AsNoTracking()
            join variant in db.ProductVariants.AsNoTracking() on listing.VariantId equals variant.Id
            join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
            where !product.IsArchived
            select product.Id).Distinct().CountAsync(cancellationToken);

        return Ok(new ListingsReport(
            products, Math.Max(0, products - listed), channels,
            problems.Select(p => new ListingsReportProblem(p.Id, p.Name, p.SellerSku, Problem(p.IssuesJson, p.ObservedStatus))).ToList()));
    }

    /// <summary>The first of a listing's recorded issues in words, or that the marketplace turned it down.</summary>
    private static string Problem(string? issuesJson, ListingObservedStatus status)
    {
        if (!string.IsNullOrWhiteSpace(issuesJson))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(issuesJson);
                var messages = document.RootElement.EnumerateArray()
                    .Select(issue => issue.TryGetProperty("message", out var message) ? message.GetString() : null).OfType<string>().ToList();
                if (messages.Count > 0)
                {
                    return messages.Count == 1 ? messages[0] : $"{messages[0]} (and {messages.Count - 1} more)";
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Recorded in a shape this does not read; said plainly below.
            }
        }
        return status == ListingObservedStatus.Rejected ? "Turned down by the marketplace; its reason is in the sync queue." : "Has a problem recorded; open the listing to read it.";
    }
}
