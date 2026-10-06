using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.TenantHost.Marketplace;

/// <summary>
/// Turns low stock into work for someone: a variant at or below the company's
/// alert level gets one restocking task, assigned to whoever the company
/// chose. A variant keeps a single open task however long it stays low; a
/// new one is created only after the last was done or cancelled.
/// </summary>
public class LowStockMonitor(TenantDbContext db)
{
    public const string TitlePrefix = "Restock ";

    /// <summary>Returns how many tasks were created. Does nothing while the alerts are switched off.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var settings = await db.CompanySettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings is not { LowStockThreshold: { } threshold, LowStockAssigneeId: { } assignee })
        {
            return 0;
        }
        // A blocked user cannot work a task; the alerts wait until the company picks someone else.
        if (!await db.Users.AnyAsync(u => u.Id == assignee && !u.IsBlocked, cancellationToken))
        {
            return 0;
        }

        var low = await (
            from variant in db.ProductVariants.AsNoTracking()
            join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
            join balance in db.InventoryBalances.AsNoTracking() on variant.Id equals balance.VariantId
            where !variant.IsArchived && !product.IsArchived && balance.LocationId == InventoryLocation.DefaultId
                && balance.OnHand - balance.Reserved - balance.SafetyStock <= threshold
            orderby variant.Sku
            select new { variant.Sku, product.Name, balance.OnHand, balance.Reserved, balance.SafetyStock }).Take(500).ToListAsync(cancellationToken);
        if (low.Count == 0)
        {
            return 0;
        }

        var open = (await db.WorkItems.AsNoTracking()
            .Where(t => (t.Status == WorkItemStatus.Open || t.Status == WorkItemStatus.InProgress) && t.Title.StartsWith(TitlePrefix))
            .Select(t => t.Title).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var created = 0;
        foreach (var item in low)
        {
            var title = TitlePrefix + item.Sku;
            if (!open.Add(title))
            {
                continue;
            }

            var available = Availability.ToSell(item.OnHand, item.Reserved, item.SafetyStock);
            db.WorkItems.Add(new WorkItem
            {
                Id = Guid.NewGuid(),
                Title = title,
                Description = $"{item.Name} has {available} left to sell ({item.OnHand} on hand), at or below the alert level of {threshold}. Created automatically.",
                Status = WorkItemStatus.Open,
                AssignedUserId = assignee,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            created++;
        }

        if (created > 0)
        {
            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = now,
                ActorEmail = "system",
                Action = "LowStockTasksCreated",
                Details = $"tasks={created}; threshold={threshold}",
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        return created;
    }
}
