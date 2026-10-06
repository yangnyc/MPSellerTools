using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.TenantHost.Marketplace;

public enum UnknownSkuPolicy
{
    /// <summary>Leave the line off the order and record it for someone to resolve. Nothing is guessed.</summary>
    RouteForResolution,

    /// <summary>
    /// Add a product for the SKU, as the manual eBay import has always done,
    /// so the order shows what was sold.
    /// </summary>
    CreatePlaceholderProduct,
}

public enum IngestOutcome
{
    Created,
    Updated,
    Unchanged,
}

public record IngestResult(IngestOutcome Outcome, int ProductsCreated, int Issues);

/// <summary>
/// Turns an order from any channel into an order here. An order is known by
/// its channel account and the channel's own id, so one that arrives again
/// only has its status brought up to date; it never becomes a second order
/// and never holds or deducts stock a second time.
/// </summary>
public class OrderIngestionService(TenantDbContext db, InventoryService inventory, IOptions<MarketplaceOptions> options)
{
    public async Task<IngestResult> IngestAsync(
        ChannelAccount? account, SalesChannel channel, ChannelOrder incoming, UnknownSkuPolicy policy, CancellationToken cancellationToken)
    {
        var accounting = options.Value.InventoryAccountingEnabled && !incoming.FulfilledByChannel;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await FindAsync(account, channel, incoming.ExternalOrderId, cancellationToken);
        if (order is not null)
        {
            // An order imported before variants or accounts existed is adopted rather than duplicated.
            var adopted = account is not null && order.ChannelAccountId is null;
            if (adopted)
            {
                order.ChannelAccountId = account!.Id;
                order.ExternalOrderId = incoming.ExternalOrderId;
            }

            var changed = order.Status != incoming.Status;
            if (changed)
            {
                order.Status = incoming.Status;
                order.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
            if (changed && accounting)
            {
                await ApplyStatusToStockAsync(order.Id, incoming.Status, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new IngestResult(changed ? IngestOutcome.Updated : IngestOutcome.Unchanged, 0, 0);
        }

        var now = DateTime.UtcNow;
        var productsCreated = 0;
        var issues = new List<OrderLineIssue>();
        var items = new List<OrderItem>();
        foreach (var line in incoming.Lines)
        {
            var (variant, reason) = await MapAsync(account, line.SellerSku, cancellationToken);
            if (variant is null && policy == UnknownSkuPolicy.CreatePlaceholderProduct && reason == OrderLineIssueReason.UnknownSku)
            {
                variant = await CreatePlaceholderAsync(channel, line, now, cancellationToken);
                productsCreated++;
            }

            if (variant is null)
            {
                issues.Add(NewIssue(account, incoming, line, reason, null, now));
                continue;
            }

            items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                ProductId = variant.ProductId,
                VariantId = variant.Id,
                ExternalLineId = Truncate(line.ExternalLineId, 64),
                SellerSku = line.SellerSku is null ? null : Truncate(line.SellerSku, 64),
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
            });
        }

        order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"{Prefix(channel)}-{incoming.ExternalOrderId}",
            EbayOrderId = channel == SalesChannel.Ebay ? incoming.ExternalOrderId : null,
            ChannelAccountId = account?.Id,
            ExternalOrderId = incoming.ExternalOrderId,
            Currency = incoming.Currency is { Length: 3 } currency ? currency : null,
            Status = incoming.Status,
            Items = items,
            Total = items.Sum(i => i.UnitPrice * i.Quantity),
            CreatedAtUtc = incoming.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
        };
        db.Orders.Add(order);
        foreach (var issue in issues)
        {
            issue.OrderId = order.Id;
        }
        db.OrderLineIssues.AddRange(issues);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (account is not null || channel == SalesChannel.Ebay)
        {
            // The same order was imported by a run alongside this one; its copy stands.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new IngestResult(IngestOutcome.Unchanged, 0, 0);
        }

        if (accounting && incoming.Status != OrderStatus.Cancelled)
        {
            foreach (var item in items)
            {
                var key = ReservationKey(order.Id, item.Id);
                var result = await inventory.ReserveAsync(
                    item.VariantId!.Value, item.Quantity, key, order.Id, honorSafetyStock: false, expiresAtUtc: null, cancellationToken);
                if (result == ReserveResult.Insufficient)
                {
                    // The channel sold what was not there. The order stands; the shortfall is put in front of a person.
                    var line = incoming.Lines.First(l => Truncate(l.ExternalLineId, 64) == item.ExternalLineId);
                    issues.Add(NewIssue(account, incoming, line, OrderLineIssueReason.InventoryShortfall, order.Id, now));
                    db.OrderLineIssues.Add(issues[^1]);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await ApplyStatusToStockAsync(order.Id, incoming.Status, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new IngestResult(IngestOutcome.Created, productsCreated, issues.Count);
    }

    public static string ReservationKey(Guid orderId, Guid orderItemId) => $"order:{orderId:N}:{orderItemId:N}";

    private Task ApplyStatusToStockAsync(Guid orderId, OrderStatus status, CancellationToken cancellationToken) => status switch
    {
        OrderStatus.Completed => inventory.ShipOrderAsync(orderId, cancellationToken),
        OrderStatus.Cancelled => inventory.ReleaseOrderAsync(orderId, cancellationToken),
        _ => Task.CompletedTask,
    };

    private Task<Order?> FindAsync(ChannelAccount? account, SalesChannel channel, string externalOrderId, CancellationToken cancellationToken)
    {
        var accountId = account?.Id;
        return db.Orders.Include(o => o.Items).FirstOrDefaultAsync(
            o => (accountId != null && o.ChannelAccountId == accountId && o.ExternalOrderId == externalOrderId)
                || (channel == SalesChannel.Ebay && o.EbayOrderId == externalOrderId),
            cancellationToken);
    }

    /// <summary>
    /// Finds the variant a channel's SKU stands for: through the account's
    /// own listings first, then as an internal SKU. A SKU that points at
    /// more than one variant is reported as ambiguous, not resolved by picking one.
    /// </summary>
    private async Task<(ProductVariant? Variant, OrderLineIssueReason Reason)> MapAsync(
        ChannelAccount? account, string? sellerSku, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sellerSku))
        {
            return (null, OrderLineIssueReason.UnknownSku);
        }

        if (account is not null)
        {
            var listed = await (
                from listing in db.ChannelListings
                join market in db.ChannelMarkets on listing.ChannelMarketId equals market.Id
                where market.ChannelAccountId == account.Id && listing.SellerSku == sellerSku
                select listing.VariantId).Distinct().ToListAsync(cancellationToken);
            if (listed.Count > 1)
            {
                return (null, OrderLineIssueReason.AmbiguousSku);
            }
            if (listed.Count == 1)
            {
                return (await db.ProductVariants.FirstAsync(v => v.Id == listed[0], cancellationToken), OrderLineIssueReason.UnknownSku);
            }
        }

        var variant = db.ProductVariants.Local.FirstOrDefault(v => v.Sku == sellerSku)
            ?? await db.ProductVariants.FirstOrDefaultAsync(v => v.Sku == sellerSku, cancellationToken);
        return (variant, OrderLineIssueReason.UnknownSku);
    }

    private async Task<ProductVariant?> CreatePlaceholderAsync(SalesChannel channel, ChannelOrderLine line, DateTime now, CancellationToken cancellationToken)
    {
        // A listing sold without a SKU still needs one here, so the channel's line id stands in.
        var sku = Truncate(string.IsNullOrWhiteSpace(line.SellerSku) ? $"{Prefix(channel)}-{line.ExternalLineId}" : line.SellerSku.Trim(), 64);
        var existing = db.ProductVariants.Local.FirstOrDefault(v => v.Sku == sku)
            ?? await db.ProductVariants.FirstOrDefaultAsync(v => v.Sku == sku, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        // Stock is not known from an order.
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku,
            Name = Truncate(string.IsNullOrWhiteSpace(line.Title) ? sku : line.Title.Trim(), 200),
            Price = line.UnitPrice,
            StockQuantity = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var variant = CatalogSaveChangesInterceptor.NewDefaultVariant(product, now);
        db.Products.Add(product);
        db.ProductVariants.Add(variant);
        db.InventoryBalances.Add(CatalogSaveChangesInterceptor.NewBalance(variant.Id, 0, now));
        return variant;
    }

    private static OrderLineIssue NewIssue(
        ChannelAccount? account, ChannelOrder order, ChannelOrderLine line, OrderLineIssueReason reason, Guid? orderId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ChannelAccountId = account?.Id,
        ExternalOrderId = Truncate(order.ExternalOrderId, 64),
        ExternalLineId = Truncate(line.ExternalLineId, 64),
        SellerSku = line.SellerSku is null ? null : Truncate(line.SellerSku, 64),
        Quantity = line.Quantity,
        Reason = reason,
        OrderId = orderId,
        CreatedAtUtc = now,
    };

    private static string Prefix(SalesChannel channel) => channel switch
    {
        SalesChannel.Amazon => "AMZ",
        SalesChannel.Walmart => "WMT",
        SalesChannel.Website => "WEB",
        _ => "EBAY",
    };

    private static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;
}
