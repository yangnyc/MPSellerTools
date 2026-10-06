using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;

namespace MPSellerTools.Infrastructure.Tenants;

/// <summary>
/// Runs inside every save of a <see cref="TenantDbContext"/>, so its work
/// commits or rolls back with the change that caused it:
///
/// 1. Keeps a product's own SKU, price and stock (the columns the product
///    API, the eBay import and old rows use) in step with its default
///    variant and that variant's warehouse balance. This is the one place
///    the two are tied together, whichever code path made the change.
/// 2. Writes an <see cref="OutboxEvent"/> for each catalog, price or stock
///    change the channels may need to hear about.
///
/// Its few lookups are synchronous on purpose: one code path serves both
/// SaveChanges and SaveChangesAsync, and each is a single-row read by key.
///
/// On a database that has not yet been given the multichannel migration it
/// does nothing at all, so a host started against an older schema keeps
/// saving products exactly as before; the backfill catches up afterwards.
/// </summary>
public sealed class CatalogSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly string[] ProductContent =
        [nameof(Product.Name), nameof(Product.Description), nameof(Product.Brand), nameof(Product.Category), nameof(Product.IsArchived)];

    private static readonly string[] VariantContent =
    [
        nameof(ProductVariant.Name), nameof(ProductVariant.OptionsJson), nameof(ProductVariant.Condition),
        nameof(ProductVariant.WeightValue), nameof(ProductVariant.WeightUnit), nameof(ProductVariant.Length),
        nameof(ProductVariant.Width), nameof(ProductVariant.Height), nameof(ProductVariant.DimensionUnit),
        nameof(ProductVariant.IsArchived),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>Databases known to have the tables this writes to. Only "yes" is remembered: a "no" is asked again.</summary>
    private static readonly ConcurrentDictionary<string, bool> Migrated = new();

    private static bool SchemaReady(TenantDbContext db)
    {
        var key = db.Database.GetConnectionString() ?? "";
        if (Migrated.ContainsKey(key))
        {
            return true;
        }

        var ready = db.Database
            .SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'OutboxEvents') IS NULL THEN 0 ELSE 1 END AS [Value]")
            .AsEnumerable()
            .First() == 1;
        if (ready)
        {
            Migrated[key] = true;
        }
        return ready;
    }

    private static void Apply(DbContext? context)
    {
        if (context is not TenantDbContext db || !db.ChangeTracker.HasChanges() || !SchemaReady(db))
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var entry in db.ChangeTracker.Entries<Product>().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                MirrorNewProduct(db, entry.Entity, now);
            }
            else if (entry.State == EntityState.Modified)
            {
                MirrorChangedProduct(
                    db, entry.Entity, entry.Property(p => p.Price).IsModified, entry.Property(p => p.StockQuantity).IsModified, now);
            }
        }

        db.ChangeTracker.DetectChanges();
        var events = new HashSet<(string Type, Guid Subject)>();

        foreach (var entry in db.ChangeTracker.Entries<Product>().Where(e => e.State == EntityState.Modified))
        {
            if (ProductContent.Any(name => entry.Property(name).IsModified))
            {
                events.Add((OutboxEvent.ProductContentChanged, entry.Entity.Id));
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<ProductVariant>().Where(e => e.State == EntityState.Modified))
        {
            if (entry.Property(v => v.Price).IsModified)
            {
                events.Add((OutboxEvent.VariantPriceChanged, entry.Entity.Id));
            }

            if (VariantContent.Any(name => entry.Property(name).IsModified))
            {
                events.Add((OutboxEvent.VariantContentChanged, entry.Entity.Id));
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<InventoryBalance>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            events.Add((OutboxEvent.InventoryChanged, entry.Entity.VariantId));
        }

        foreach (var (type, subject) in events)
        {
            db.OutboxEvents.Add(new OutboxEvent { Type = type, SubjectId = subject, CreatedAtUtc = now });
        }
    }

    private static void MirrorNewProduct(TenantDbContext db, Product product, DateTime now)
    {
        if (db.ProductVariants.Local.Any(v => v.ProductId == product.Id && v.IsDefault))
        {
            return;
        }

        var variant = NewDefaultVariant(product, now);
        db.ProductVariants.Add(variant);
        db.InventoryBalances.Add(NewBalance(variant.Id, product.StockQuantity, now));
    }

    private static void MirrorChangedProduct(TenantDbContext db, Product product, bool priceChanged, bool stockChanged, DateTime now)
    {
        if (!priceChanged && !stockChanged)
        {
            return;
        }

        var variant = db.ProductVariants.Local.FirstOrDefault(v => v.ProductId == product.Id && v.IsDefault)
            ?? db.ProductVariants.FirstOrDefault(v => v.ProductId == product.Id && v.IsDefault);
        if (variant is null)
        {
            // A product from before variants existed, not yet reached by the
            // backfill. If its SKU is already some other product's variant the
            // backfill reports that conflict; nothing is invented here.
            if (db.ProductVariants.Any(v => v.Sku == product.Sku))
            {
                return;
            }

            variant = NewDefaultVariant(product, now);
            db.ProductVariants.Add(variant);
            db.InventoryBalances.Add(NewBalance(variant.Id, product.StockQuantity, now));
            return;
        }

        if (priceChanged && variant.Price != product.Price)
        {
            variant.Price = product.Price;
            variant.UpdatedAtUtc = now;
        }

        if (!stockChanged)
        {
            return;
        }

        var balance = db.InventoryBalances.Local.FirstOrDefault(b => b.VariantId == variant.Id && b.LocationId == InventoryLocation.DefaultId)
            ?? db.InventoryBalances.FirstOrDefault(b => b.VariantId == variant.Id && b.LocationId == InventoryLocation.DefaultId);
        if (balance is null)
        {
            db.InventoryBalances.Add(NewBalance(variant.Id, product.StockQuantity, now));
        }
        else if (balance.OnHand != product.StockQuantity)
        {
            db.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(),
                LocationId = balance.LocationId,
                VariantId = variant.Id,
                Type = InventoryMovementType.Adjustment,
                OnHandDelta = product.StockQuantity - balance.OnHand,
                Reference = "Product stock edited",
                OccurredAtUtc = now,
            });
            balance.OnHand = product.StockQuantity;
            balance.UpdatedAtUtc = now;
        }
    }

    public static ProductVariant NewDefaultVariant(Product product, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = product.Id,
        Sku = product.Sku,
        Price = product.Price,
        IsDefault = true,
        IsArchived = product.IsArchived,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };

    public static InventoryBalance NewBalance(Guid variantId, int onHand, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        LocationId = InventoryLocation.DefaultId,
        VariantId = variantId,
        OnHand = Math.Max(0, onHand),
        UpdatedAtUtc = now,
    };
}
