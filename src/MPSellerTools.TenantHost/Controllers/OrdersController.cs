using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using Microsoft.Extensions.Options;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = Roles.Employee)]
public class OrdersController(
    TenantDbContext db, AuditLogger audit, InventoryService inventory, IOptions<MarketplaceOptions> marketplace) : ControllerBase
{
    /// <summary>
    /// Whether orders hold and deduct stock (Marketplace:InventoryAccountingEnabled).
    /// Off, orders leave stock alone, as they did before stock was accounted for.
    /// </summary>
    private bool Accounting => marketplace.Value.InventoryAccountingEnabled;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsTenantAdmin => User.IsInRole(Roles.TenantAdmin);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        // Employees have read-only access to orders assigned to them (brief §5/§9);
        // TenantAdmins see everything.
        var query = db.Orders.Include(o => o.Items).AsQueryable();
        if (!IsTenantAdmin)
        {
            query = query.Where(o => o.AssignedUserId == CurrentUserId);
        }

        var orders = await query.OrderByDescending(o => o.CreatedAtUtc).ToListAsync();
        return Ok(await Task.WhenAll(orders.Select(ToResponseAsync)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
        {
            return NotFound();
        }
        if (!IsTenantAdmin && order.AssignedUserId != CurrentUserId)
        {
            // 404, not 403: do not confirm the existence of orders this
            // employee has no business knowing about (brief §10's "missing
            // or unauthorized resources do not disclose data" principle).
            return NotFound();
        }
        return Ok(await ToResponseAsync(order));
    }

    [HttpPost]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var (items, error) = await BuildItemsAsync(request.Items);
        if (error is not null)
        {
            return Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.AssignedUserId is { } assignee && !await IsActiveUserAsync(assignee))
        {
            return Problem("Assigned user must be an existing, active user.", statusCode: StatusCodes.Status400BadRequest);
        }

        var now = DateTime.UtcNow;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = GenerateOrderNumber(),
                Status = OrderStatus.New,
                AssignedUserId = request.AssignedUserId,
                Items = items!,
                Total = items!.Sum(i => i.Quantity * i.UnitPrice),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.Orders.Add(order);
            audit.Log("OrderCreated", $"orderNumber={order.OrderNumber}");

            await using var transaction = Accounting ? await db.Database.BeginTransactionAsync() : null;
            try
            {
                await db.SaveChangesAsync();
                if (transaction is not null)
                {
                    if (await ReserveAsync(order) is { } shortfall)
                    {
                        await transaction.RollbackAsync();
                        return Problem(shortfall, statusCode: StatusCodes.Status409Conflict);
                    }
                    await transaction.CommitAsync();
                }
                return CreatedAtAction(nameof(Get), new { id = order.Id }, await ToResponseAsync(order));
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Orders_OrderNumber", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync();
                }
                db.ChangeTracker.Clear();
                items = (await BuildItemsAsync(request.Items)).Items!;
            }
        }

        return Problem("Could not allocate a unique order number; please retry.", statusCode: StatusCodes.Status409Conflict);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrderRequest request)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
        {
            return NotFound();
        }

        var (items, error) = await BuildItemsAsync(request.Items);
        if (error is not null)
        {
            return Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }
        if (request.AssignedUserId is { } assignee && !await IsActiveUserAsync(assignee))
        {
            return Problem("Assigned user must be an existing, active user.", statusCode: StatusCodes.Status400BadRequest);
        }

        db.OrderItems.RemoveRange(order.Items);
        // AddRange explicitly marks these as Added — without it, EF Core
        // cannot tell a brand-new OrderItem (client-generated Guid key) from
        // an existing row being updated once it's attached to an
        // already-tracked Order via navigation alone, and silently emits an
        // UPDATE instead of an INSERT. That UPDATE affects zero rows (the
        // row doesn't exist yet) and throws a spurious
        // DbUpdateConcurrencyException that has nothing to do with the
        // Order's own RowVersion — reproduced and fixed during Increment 5.
        db.OrderItems.AddRange(items!);
        order.Items = items!;
        order.AssignedUserId = request.AssignedUserId;
        order.Total = items!.Sum(i => i.Quantity * i.UnitPrice);
        order.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(order).Property(o => o.RowVersion).OriginalValue = RowVersionCodec.Decode(request.RowVersion);

        audit.Log("OrderUpdated", $"orderNumber={order.OrderNumber}");

        var open = order.Status is OrderStatus.New or OrderStatus.InProgress;
        await using var transaction = Accounting && open ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("This order was modified by someone else. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }

        if (transaction is not null)
        {
            // The lines were replaced, so what the old ones held is freed and the new ones hold afresh.
            await inventory.ReleaseOrderAsync(order.Id, HttpContext.RequestAborted);
            if (await ReserveAsync(order) is { } shortfall)
            {
                await transaction.RollbackAsync();
                return Problem(shortfall, statusCode: StatusCodes.Status409Conflict);
            }
            await transaction.CommitAsync();
        }

        return Ok(await ToResponseAsync(order));
    }

    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeOrderStatusRequest request)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
        {
            return NotFound();
        }

        if (!OrderStatusTransitions.IsValid(order.Status, request.Status))
        {
            return Problem($"Cannot transition an order from {order.Status} to {request.Status}.", statusCode: StatusCodes.Status400BadRequest);
        }

        order.Status = request.Status;
        order.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(order).Property(o => o.RowVersion).OriginalValue = RowVersionCodec.Decode(request.RowVersion);

        audit.Log("OrderStatusChanged", $"orderNumber={order.OrderNumber}; status={request.Status}");

        await using var transaction = Accounting ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("This order was modified by someone else. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }

        if (transaction is not null)
        {
            // Completing ships what the order held; cancelling frees it. Either is a no-op the second time.
            if (order.Status == OrderStatus.Completed)
            {
                await inventory.ShipOrderAsync(order.Id, HttpContext.RequestAborted);
            }
            else if (order.Status == OrderStatus.Cancelled)
            {
                await inventory.ReleaseOrderAsync(order.Id, HttpContext.RequestAborted);
            }
            await transaction.CommitAsync();
        }

        return Ok(await ToResponseAsync(order));
    }

    private async Task<(List<OrderItem>? Items, string? Error)> BuildItemsAsync(IReadOnlyList<OrderItemRequest> requestedItems)
    {
        if (requestedItems.Count == 0)
        {
            return (null, "An order must have at least one item.");
        }
        if (requestedItems.Any(i => i.Quantity <= 0))
        {
            return (null, "Item quantities must be positive.");
        }

        var productIds = requestedItems.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        // An order made here is for the product as such, which is its default variant.
        var variants = await db.ProductVariants.Where(v => productIds.Contains(v.ProductId) && v.IsDefault)
            .ToDictionaryAsync(v => v.ProductId, v => v.Id);

        var items = new List<OrderItem>();
        foreach (var line in requestedItems)
        {
            if (!products.TryGetValue(line.ProductId, out var product) || product.IsArchived)
            {
                return (null, $"Product {line.ProductId} does not exist or is archived.");
            }
            items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                VariantId = variants.TryGetValue(product.Id, out var variantId) ? variantId : null,
                Quantity = line.Quantity,
                UnitPrice = product.Price,
            });
        }

        return (items, null);
    }

    /// <summary>
    /// Holds stock for each line through the same service marketplace orders
    /// use. Returns why it could not, or null. The caller's transaction takes
    /// back whatever was held when one line falls short.
    /// </summary>
    private async Task<string?> ReserveAsync(Order order)
    {
        foreach (var item in order.Items)
        {
            if (item.VariantId is not { } variantId)
            {
                return $"Product {item.ProductId} has no variant to take stock from yet; run the catalog backfill.";
            }

            var result = await inventory.ReserveAsync(
                variantId, item.Quantity, OrderIngestionService.ReservationKey(order.Id, item.Id), order.Id,
                honorSafetyStock: true, expiresAtUtc: null, HttpContext.RequestAborted);
            if (result == ReserveResult.Insufficient)
            {
                return $"Not enough stock for product {item.ProductId}.";
            }
        }
        return null;
    }

    private async Task<bool> IsActiveUserAsync(Guid userId) =>
        await db.Users.AnyAsync(u => u.Id == userId && !u.IsBlocked);

    private static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";

    private async Task<OrderResponse> ToResponseAsync(Order order)
    {
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var lines = order.Items.Select(i => new OrderItemLine(
            i.ProductId,
            products.TryGetValue(i.ProductId, out var product) ? product.Name : "(deleted product)",
            products.TryGetValue(i.ProductId, out var product2) ? product2.Sku : "",
            i.Quantity,
            i.UnitPrice)).ToList();

        return new OrderResponse(
            order.Id, order.OrderNumber, order.Status, order.AssignedUserId, lines,
            order.Total, RowVersionCodec.Encode(order.RowVersion), order.CreatedAtUtc, order.UpdatedAtUtc);
    }
}
