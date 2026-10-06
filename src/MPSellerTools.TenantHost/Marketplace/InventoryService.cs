using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.TenantHost.Marketplace;

public enum ReserveResult
{
    Reserved,

    /// <summary>This key already holds its units; nothing more was taken.</summary>
    AlreadyReserved,
    Insufficient,
}

/// <summary>
/// The one place merchant stock changes because of an order: reserve, ship,
/// release, return. Website and marketplace orders both come through here.
///
/// available_to_sell = max(0, on_hand - reserved - safety_stock)
///
/// Balances are changed with conditional UPDATE statements, so two requests
/// for the last unit cannot both succeed, and every change is keyed so that
/// an order or event delivered twice counts once.
/// </summary>
public class InventoryService(TenantDbContext db)
{
    private static readonly Guid Location = InventoryLocation.DefaultId;

    /// <summary>
    /// Holds <paramref name="quantity"/> units. A buyer's checkout honours
    /// safety stock; an order a marketplace has already taken does not, as
    /// those units are sold whatever buffer was intended.
    /// </summary>
    public Task<ReserveResult> ReserveAsync(
        Guid variantId, int quantity, string key, Guid? orderId, bool honorSafetyStock, DateTime? expiresAtUtc, CancellationToken cancellationToken) =>
        InTransactionAsync(async transaction =>
        {
            if (await db.InventoryReservations.AnyAsync(r => r.IdempotencyKey == key, cancellationToken))
            {
                return ReserveResult.AlreadyReserved;
            }

            var now = DateTime.UtcNow;
            await transaction.CreateSavepointAsync("reserve", cancellationToken);
            var buffer = honorSafetyStock ? 1 : 0;
            var taken = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE InventoryBalances SET Reserved = Reserved + {quantity}, UpdatedAtUtc = {now}
                WHERE LocationId = {Location} AND VariantId = {variantId}
                  AND OnHand - Reserved - SafetyStock * {buffer} >= {quantity}
                """, cancellationToken);
            if (taken == 0)
            {
                return ReserveResult.Insufficient;
            }

            var reservation = new InventoryReservation
            {
                Id = Guid.NewGuid(),
                LocationId = Location,
                VariantId = variantId,
                Quantity = quantity,
                Status = ReservationStatus.Active,
                IdempotencyKey = key,
                OrderId = orderId,
                ExpiresAtUtc = expiresAtUtc,
                CreatedAtUtc = now,
            };
            db.InventoryReservations.Add(reservation);
            var added = Record(variantId, InventoryMovementType.Reserve, 0, quantity, key, now);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return ReserveResult.Reserved;
            }
            catch (DbUpdateException)
            {
                // The same key was reserved by a request running alongside this one: give the units back.
                await transaction.RollbackToSavepointAsync("reserve", cancellationToken);
                db.Entry(reservation).State = EntityState.Detached;
                foreach (var entity in added)
                {
                    db.Entry(entity).State = EntityState.Detached;
                }
                return ReserveResult.AlreadyReserved;
            }
        }, cancellationToken);

    /// <summary>The goods left: on hand and reserved both go down. False when the key holds nothing active.</summary>
    public Task<bool> ShipAsync(string key, CancellationToken cancellationToken) =>
        CloseAsync(key, ReservationStatus.Shipped, cancellationToken);

    /// <summary>Frees the units again. False when the key holds nothing active.</summary>
    public Task<bool> ReleaseAsync(string key, CancellationToken cancellationToken) =>
        CloseAsync(key, ReservationStatus.Released, cancellationToken);

    public async Task ShipOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        foreach (var key in await ActiveKeysAsync(orderId, cancellationToken))
        {
            await ShipAsync(key, cancellationToken);
        }
    }

    public async Task ReleaseOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        foreach (var key in await ActiveKeysAsync(orderId, cancellationToken))
        {
            await ReleaseAsync(key, cancellationToken);
        }
    }

    /// <summary>
    /// Puts returned units back on hand. Called for a confirmed receipt at
    /// the warehouse, never for a return merely requested. False when this
    /// receipt was already recorded.
    /// </summary>
    public Task<bool> ReceiveReturnAsync(Guid variantId, int quantity, string receiptKey, CancellationToken cancellationToken) =>
        InTransactionAsync(async transaction =>
        {
            if (await db.InventoryMovements.AnyAsync(m => m.IdempotencyKey == receiptKey, cancellationToken))
            {
                return false;
            }

            var now = DateTime.UtcNow;
            await transaction.CreateSavepointAsync("return", cancellationToken);
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE InventoryBalances SET OnHand = OnHand + {quantity}, UpdatedAtUtc = {now}
                WHERE LocationId = {Location} AND VariantId = {variantId}
                """, cancellationToken);
            if (updated == 0)
            {
                return false;
            }

            var added = Record(variantId, InventoryMovementType.ReturnReceipt, quantity, 0, receiptKey, now, receiptKey);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackToSavepointAsync("return", cancellationToken);
                foreach (var entity in added)
                {
                    db.Entry(entity).State = EntityState.Detached;
                }
                return false;
            }

            await MirrorProductStockAsync(variantId, cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>Sets counted stock and/or the safety buffer by hand.</summary>
    public Task<bool> AdjustAsync(Guid variantId, int? onHand, int? safetyStock, CancellationToken cancellationToken) =>
        InTransactionAsync(async _ =>
        {
            var balance = await db.InventoryBalances.FirstOrDefaultAsync(b => b.LocationId == Location && b.VariantId == variantId, cancellationToken);
            var now = DateTime.UtcNow;
            if (balance is null)
            {
                if (!await db.ProductVariants.AnyAsync(v => v.Id == variantId, cancellationToken))
                {
                    return false;
                }

                balance = CatalogSaveChangesInterceptor.NewBalance(variantId, 0, now);
                db.InventoryBalances.Add(balance);
            }
            else
            {
                // Read fresh: reservations change these rows without going through the change tracker.
                await db.Entry(balance).ReloadAsync(cancellationToken);
            }

            if (onHand is { } counted && counted != balance.OnHand)
            {
                Record(variantId, InventoryMovementType.Adjustment, counted - balance.OnHand, 0, "Stock counted", now);
                balance.OnHand = counted;
            }

            balance.SafetyStock = safetyStock ?? balance.SafetyStock;
            balance.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            await MirrorProductStockAsync(variantId, cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>Releases holds whose time ran out. Returns how many.</summary>
    public async Task<int> ExpireAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var keys = await db.InventoryReservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAtUtc != null && r.ExpiresAtUtc < now)
            .Select(r => r.IdempotencyKey)
            .Take(200)
            .ToListAsync(cancellationToken);
        var released = 0;
        foreach (var key in keys)
        {
            released += await ReleaseAsync(key, cancellationToken) ? 1 : 0;
        }
        return released;
    }

    private Task<bool> CloseAsync(string key, ReservationStatus outcome, CancellationToken cancellationToken) =>
        InTransactionAsync(async _ =>
        {
            var reservation = await db.InventoryReservations.AsNoTracking().FirstOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken);
            if (reservation is not { Status: ReservationStatus.Active })
            {
                return false;
            }

            var now = DateTime.UtcNow;
            // The status check in the WHERE is what makes a second, concurrent close a no-op.
            var closed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE InventoryReservations SET Status = {(int)outcome}, ClosedAtUtc = {now}
                WHERE Id = {reservation.Id} AND Status = {(int)ReservationStatus.Active}
                """, cancellationToken);
            if (closed == 0)
            {
                return false;
            }

            var quantity = reservation.Quantity;
            var shipped = outcome == ReservationStatus.Shipped ? quantity : 0;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE InventoryBalances SET
                    OnHand = CASE WHEN OnHand >= {shipped} THEN OnHand - {shipped} ELSE 0 END,
                    Reserved = CASE WHEN Reserved >= {quantity} THEN Reserved - {quantity} ELSE 0 END,
                    UpdatedAtUtc = {now}
                WHERE LocationId = {reservation.LocationId} AND VariantId = {reservation.VariantId}
                """, cancellationToken);

            Record(
                reservation.VariantId,
                outcome == ReservationStatus.Shipped ? InventoryMovementType.Ship : InventoryMovementType.Release,
                -shipped, -quantity, key, now);
            await db.SaveChangesAsync(cancellationToken);
            if (shipped > 0)
            {
                await MirrorProductStockAsync(reservation.VariantId, cancellationToken);
            }
            return true;
        }, cancellationToken);

    private Task<List<string>> ActiveKeysAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.InventoryReservations.AsNoTracking()
            .Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Active)
            .Select(r => r.IdempotencyKey)
            .ToListAsync(cancellationToken);

    /// <summary>Queues the movement and the outbox event; they are saved with the caller's next SaveChanges.</summary>
    private List<object> Record(
        Guid variantId, InventoryMovementType type, int onHandDelta, int reservedDelta, string reference, DateTime now, string? idempotencyKey = null)
    {
        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            LocationId = Location,
            VariantId = variantId,
            Type = type,
            OnHandDelta = onHandDelta,
            ReservedDelta = reservedDelta,
            Reference = reference.Length > 200 ? reference[..200] : reference,
            IdempotencyKey = idempotencyKey,
            OccurredAtUtc = now,
        };
        // The balance itself was changed by SQL the change tracker never saw, so the event is written by hand.
        var outbox = new OutboxEvent { Type = OutboxEvent.InventoryChanged, SubjectId = variantId, CreatedAtUtc = now };
        db.InventoryMovements.Add(movement);
        db.OutboxEvents.Add(outbox);
        return [movement, outbox];
    }

    /// <summary>Brings the product's own stock column back in step with its default variant's balance.</summary>
    private Task<int> MirrorProductStockAsync(Guid variantId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE p SET StockQuantity = b.OnHand
            FROM Products p
            JOIN ProductVariants v ON v.ProductId = p.Id AND v.IsDefault = 1
            JOIN InventoryBalances b ON b.VariantId = v.Id AND b.LocationId = {Location}
            WHERE v.Id = {variantId} AND p.StockQuantity <> b.OnHand
            """, cancellationToken);

    private async Task<T> InTransactionAsync<T>(
        Func<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction, Task<T>> work, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is { } ambient)
        {
            return await work(ambient);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await work(transaction);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
