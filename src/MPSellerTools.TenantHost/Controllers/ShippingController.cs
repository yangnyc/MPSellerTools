using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

public record ShippingLine(Guid ProductId, string Sku, string Name, int Quantity, int OnHand);

/// <summary>An order waiting to go out. <see cref="Short"/> says a line asks for more than is on the shelf.</summary>
public record ShippingOrder(
    Guid Id, string OrderNumber, OrderStatus Status, string Channel, string? AssignedTo, decimal Total, DateTime CreatedAtUtc, int Units, bool Short,
    IReadOnlyList<ShippingLine> Lines);

/// <summary>One product to fetch from the shelf, for every open order together.</summary>
public record PickLine(Guid ProductId, string Sku, string Name, int Quantity, int OnHand, int Orders, IReadOnlyList<string> OrderNumbers);

public record ShippedOrder(
    Guid Id, string OrderNumber, string Channel, decimal Total, int Units, string? Carrier, string? TrackingNumber, DateTime ShippedAtUtc);

public record ShipOrderRequest(string? Carrier, string? TrackingNumber);

/// <summary>
/// Getting orders out of the door: what is waiting, what to take off the
/// shelves for it, marking an order shipped with its carrier and tracking
/// number, and what has gone. TenantAdmin only.
/// </summary>
[ApiController]
[Route("api/shipping")]
[Authorize(Policy = Roles.TenantAdmin)]
public class ShippingController(
    TenantDbContext db, InventoryService inventory, IOptions<MarketplaceOptions> options, AuditLogger audit) : ControllerBase
{
    private static readonly OrderStatus[] Open = [OrderStatus.New, OrderStatus.InProgress];

    /// <summary>The orders waiting to be shipped, oldest first.</summary>
    [HttpGet("queue")]
    public async Task<IActionResult> Queue(CancellationToken cancellationToken)
    {
        var orders = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => Open.Contains(o.Status)).OrderBy(o => o.CreatedAtUtc).Take(1000).ToListAsync(cancellationToken);
        var (products, onHand) = await ProductsAsync(orders.SelectMany(o => o.Items).Select(i => i.ProductId), cancellationToken);
        var channels = await ChannelNamesAsync(cancellationToken);
        var userIds = orders.Select(o => o.AssignedUserId).OfType<Guid>().Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return Ok(orders.Select(order =>
        {
            var lines = order.Items.Select(i => new ShippingLine(
                i.ProductId, products.GetValueOrDefault(i.ProductId)?.Sku ?? i.SellerSku ?? "", products.GetValueOrDefault(i.ProductId)?.Name ?? "(deleted product)",
                i.Quantity, onHand.GetValueOrDefault(i.ProductId))).ToList();
            return new ShippingOrder(
                order.Id, order.OrderNumber, order.Status, ChannelOf(order, channels),
                order.AssignedUserId is { } user ? users.GetValueOrDefault(user) : null,
                order.Total, order.CreatedAtUtc, lines.Sum(l => l.Quantity), lines.Any(l => l.Quantity > l.OnHand), lines);
        }).ToList());
    }

    /// <summary>Everything the open orders need, product by product, most wanted first.</summary>
    [HttpGet("pick-list")]
    public async Task<IActionResult> PickList(CancellationToken cancellationToken)
    {
        var lines = await (
            from item in db.OrderItems.AsNoTracking()
            join order in db.Orders.AsNoTracking() on item.OrderId equals order.Id
            where Open.Contains(order.Status)
            select new { item.ProductId, item.Quantity, order.OrderNumber }).ToListAsync(cancellationToken);
        var (products, onHand) = await ProductsAsync(lines.Select(l => l.ProductId), cancellationToken);

        return Ok(lines.GroupBy(l => l.ProductId)
            .Select(g => new PickLine(
                g.Key, products.GetValueOrDefault(g.Key)?.Sku ?? "", products.GetValueOrDefault(g.Key)?.Name ?? "(deleted product)",
                g.Sum(l => l.Quantity), onHand.GetValueOrDefault(g.Key), g.Select(l => l.OrderNumber).Distinct().Count(),
                g.Select(l => l.OrderNumber).Distinct().OrderBy(n => n).Take(20).ToList()))
            .OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Orders marked shipped from here in the last <paramref name="days"/> days, newest first.</summary>
    [HttpGet("shipped")]
    public async Task<IActionResult> Shipped([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var orders = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Completed && o.ShippedAtUtc != null && o.ShippedAtUtc >= since)
            .OrderByDescending(o => o.ShippedAtUtc).Take(1000).ToListAsync(cancellationToken);
        var channels = await ChannelNamesAsync(cancellationToken);
        return Ok(orders.Select(o => new ShippedOrder(
            o.Id, o.OrderNumber, ChannelOf(o, channels), o.Total, o.Items.Sum(i => i.Quantity), o.ShippingCarrier, o.TrackingNumber, o.ShippedAtUtc!.Value)).ToList());
    }

    /// <summary>
    /// Marks an order shipped: it is completed, with who carried it and the tracking number when given, and
    /// where stock is accounted for, what it held leaves the shelf. The marketplace is not told from here.
    /// </summary>
    [HttpPost("{orderId:guid}/ship")]
    public async Task<IActionResult> Ship(Guid orderId, [FromBody] ShipOrderRequest request, CancellationToken cancellationToken)
    {
        var carrier = request.Carrier?.Trim() ?? "";
        var tracking = request.TrackingNumber?.Trim() ?? "";
        if (carrier.Length > 64 || tracking.Length > 100)
        {
            return Problem("A carrier takes up to 64 characters and a tracking number up to 100.", statusCode: StatusCodes.Status400BadRequest);
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }
        if (!Open.Contains(order.Status))
        {
            return Problem(
                order.Status == OrderStatus.Completed ? "This order was already completed." : "This order was cancelled, so there is nothing to ship.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        order.Status = OrderStatus.Completed;
        order.ShippingCarrier = carrier.Length == 0 ? null : carrier;
        order.TrackingNumber = tracking.Length == 0 ? null : tracking;
        order.ShippedAtUtc = now;
        order.UpdatedAtUtc = now;
        audit.Log("OrderShipped", $"orderNumber={order.OrderNumber}; carrier={order.ShippingCarrier ?? "none"}; tracking={(order.TrackingNumber is null ? "none" : "given")}");

        var accounting = options.Value.InventoryAccountingEnabled;
        await using var transaction = accounting ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("This order was changed by someone else meanwhile. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }
        if (transaction is not null)
        {
            // Shipping takes what the order held off the shelf; a no-op the second time.
            await inventory.ShipOrderAsync(order.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return NoContent();
    }

    private async Task<(Dictionary<Guid, Product> Products, Dictionary<Guid, int> OnHand)> ProductsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var productIds = ids.Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => EF.Parameter(productIds).Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        return (products, products.Values.ToDictionary(p => p.Id, p => p.StockQuantity));
    }

    private async Task<Dictionary<Guid, string>> ChannelNamesAsync(CancellationToken cancellationToken) =>
        await db.ChannelAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

    // An order from the older eBay import carries no account; one created in the workspace carries neither.
    private static string ChannelOf(Order order, Dictionary<Guid, string> channels) =>
        order.ChannelAccountId is { } id && channels.TryGetValue(id, out var name) ? name : order.EbayOrderId != null ? "eBay" : "Created here";
}
