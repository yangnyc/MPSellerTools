using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

/// <summary>
/// The parts of the catalog the product screen never had: a product's
/// variants, identifiers, images and base content, and its stock. The
/// product itself (SKU, name, price, stock of its default variant) is still
/// edited through <see cref="ProductsController"/>; both write the same rows.
/// </summary>
[ApiController]
[Route("api/catalog")]
[Authorize(Policy = Roles.Employee)]
public class CatalogController(TenantDbContext db, InventoryService inventory, CatalogBackfill backfill, AuditLogger audit) : ControllerBase
{
    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return product is null ? NotFound() : Ok(await ToResponseAsync(product, cancellationToken));
    }

    [HttpPut("products/{id:guid}/content")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> UpdateContent(Guid id, [FromBody] UpdateProductContentRequest request, CancellationToken cancellationToken)
    {
        if (request.Brand is { Length: > 200 } || request.Category is { Length: > 100 })
        {
            return Problem("Brand takes up to 200 characters and category up to 100.", statusCode: StatusCodes.Status400BadRequest);
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        product.Brand = Blank(request.Brand);
        product.Description = Blank(request.Description);
        product.Category = Blank(request.Category);
        product.UpdatedAtUtc = DateTime.UtcNow;
        audit.Log("ProductContentUpdated", $"sku={product.Sku}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToResponseAsync(product, cancellationToken));
    }

    [HttpPost("products/{id:guid}/variants")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> AddVariant(Guid id, [FromBody] SaveVariantRequest request, CancellationToken cancellationToken)
    {
        var sku = request.Sku?.Trim() ?? "";
        if (sku.Length is 0 or > 64)
        {
            return Problem("A variant needs its own SKU of up to 64 characters.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (Invalid(request) is { } error)
        {
            return Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }
        if (await db.ProductVariants.AnyAsync(v => v.Sku == sku, cancellationToken) || await db.Products.AnyAsync(p => p.Sku == sku && p.Id != id, cancellationToken))
        {
            return Problem("This SKU is already in use.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = id, Sku = sku, CreatedAtUtc = now };
        Apply(variant, request, now);
        db.ProductVariants.Add(variant);
        db.InventoryBalances.Add(CatalogSaveChangesInterceptor.NewBalance(variant.Id, 0, now));
        audit.Log("VariantCreated", $"sku={variant.Sku}; product={product.Sku}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToResponseAsync(product, cancellationToken));
    }

    [HttpPut("variants/{variantId:guid}")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> UpdateVariant(Guid variantId, [FromBody] SaveVariantRequest request, CancellationToken cancellationToken)
    {
        if (Invalid(request) is { } error)
        {
            return Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.Id == variantId, cancellationToken);
        if (variant is null)
        {
            return NotFound();
        }

        var product = await db.Products.FirstAsync(p => p.Id == variant.ProductId, cancellationToken);
        var priceChanged = variant.Price != request.Price;
        Apply(variant, request, DateTime.UtcNow);
        if (variant.IsDefault && priceChanged)
        {
            // The default variant's price is the product's price; they move together.
            product.Price = request.Price;
            product.UpdatedAtUtc = variant.UpdatedAtUtc;
        }
        if (!string.IsNullOrEmpty(request.RowVersion))
        {
            db.Entry(variant).Property(v => v.RowVersion).OriginalValue = RowVersionCodec.Decode(request.RowVersion);
        }

        audit.Log("VariantUpdated", $"sku={variant.Sku}; price={variant.Price}");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("This variant was modified by someone else. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }
        return Ok(await ToResponseAsync(product, cancellationToken));
    }

    [HttpPut("products/{id:guid}/identifiers")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> SetIdentifier(Guid id, [FromBody] SetIdentifierRequest request, CancellationToken cancellationToken)
    {
        var value = request.Value?.Trim() ?? "";
        if (!Enum.IsDefined(request.Type) || value.Length > 64)
        {
            return Problem("Choose an identifier type and a value of up to 64 characters.", statusCode: StatusCodes.Status400BadRequest);
        }
        // Barcodes are digits of a known length; anything else is a typing mistake, not something to pass on to a marketplace.
        if (value.Length > 0 && request.Type != ProductIdentifierType.Mpn && !(value.All(char.IsAsciiDigit) && value.Length is 8 or 10 or 12 or 13 or 14))
        {
            return Problem("A GTIN, UPC, EAN or ISBN is 8, 10, 12, 13 or 14 digits.", statusCode: StatusCodes.Status400BadRequest);
        }

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null || (request.VariantId is { } variantId && !await db.ProductVariants.AnyAsync(v => v.Id == variantId && v.ProductId == id, cancellationToken)))
        {
            return NotFound();
        }

        var existing = request.VariantId is null
            ? await db.ProductIdentifiers.FirstOrDefaultAsync(i => i.ProductId == id && i.Type == request.Type, cancellationToken)
            : await db.ProductIdentifiers.FirstOrDefaultAsync(i => i.VariantId == request.VariantId && i.Type == request.Type, cancellationToken);
        if (value.Length == 0)
        {
            if (existing is not null)
            {
                db.ProductIdentifiers.Remove(existing);
            }
        }
        else if (existing is null)
        {
            db.ProductIdentifiers.Add(new ProductIdentifier
            {
                Id = Guid.NewGuid(),
                ProductId = request.VariantId is null ? id : null,
                VariantId = request.VariantId,
                Type = request.Type,
                Value = value,
            });
        }
        else
        {
            existing.Value = value;
        }

        // Identifiers are part of what a channel is told, so the listings hear about the change.
        db.OutboxEvents.Add(new OutboxEvent { Type = OutboxEvent.ProductContentChanged, SubjectId = id, CreatedAtUtc = DateTime.UtcNow });
        audit.Log("ProductIdentifierSet", $"sku={product.Sku}; type={request.Type}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToResponseAsync(product, cancellationToken));
    }

    [HttpPost("products/{id:guid}/media")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> AddMedia(Guid id, [FromBody] AddMediaRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || request.Url.Length > 1000)
        {
            return Problem("An image needs an https address of up to 1000 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null || (request.VariantId is { } variantId && !await db.ProductVariants.AnyAsync(v => v.Id == variantId && v.ProductId == id, cancellationToken)))
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var asset = new MediaAsset { Id = Guid.NewGuid(), Url = request.Url, AltText = Blank(request.AltText), CreatedAtUtc = now };
        db.MediaAssets.Add(asset);
        db.ProductMedia.Add(new ProductMedia
        {
            Id = Guid.NewGuid(),
            ProductId = id,
            VariantId = request.VariantId,
            MediaAssetId = asset.Id,
            Purpose = request.Purpose,
            Position = request.Position,
        });
        db.OutboxEvents.Add(new OutboxEvent { Type = OutboxEvent.ProductContentChanged, SubjectId = id, CreatedAtUtc = now });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToResponseAsync(product, cancellationToken));
    }

    [HttpDelete("media/{mediaId:guid}")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> RemoveMedia(Guid mediaId, CancellationToken cancellationToken)
    {
        var media = await db.ProductMedia.FirstOrDefaultAsync(m => m.Id == mediaId, cancellationToken);
        if (media is null)
        {
            return NotFound();
        }

        db.ProductMedia.Remove(media);
        db.OutboxEvents.Add(new OutboxEvent { Type = OutboxEvent.ProductContentChanged, SubjectId = media.ProductId, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Counted stock and the safety buffer of one variant in the merchant warehouse.</summary>
    [HttpPut("variants/{variantId:guid}/inventory")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> AdjustInventory(Guid variantId, [FromBody] AdjustInventoryRequest request, CancellationToken cancellationToken)
    {
        if (request.OnHand < 0 || request.SafetyStock < 0)
        {
            return Problem("Stock and safety stock cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
        }

        audit.Log("InventoryAdjusted", $"variant={variantId}; onHand={request.OnHand}; safetyStock={request.SafetyStock}");
        if (!await inventory.AdjustAsync(variantId, request.OnHand, request.SafetyStock, cancellationToken))
        {
            return NotFound();
        }

        var balance = await db.InventoryBalances.AsNoTracking().FirstAsync(b => b.VariantId == variantId && b.LocationId == InventoryLocation.DefaultId, cancellationToken);
        return Ok(new { balance.OnHand, balance.Reserved, balance.SafetyStock, balance.AvailableToSell });
    }

    /// <summary>Records returned goods as received at the warehouse; only this puts them back on hand.</summary>
    [HttpPost("returns")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> ReceiveReturn([FromBody] ReturnReceiptRequest request, CancellationToken cancellationToken)
    {
        var receipt = request.ReceiptId?.Trim() ?? "";
        if (request.Quantity <= 0 || receipt.Length is 0 or > 100)
        {
            return Problem("A receipt needs a positive quantity and a reference of up to 100 characters.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (!await db.ProductVariants.AnyAsync(v => v.Id == request.VariantId, cancellationToken))
        {
            return NotFound();
        }

        var recorded = await inventory.ReceiveReturnAsync(request.VariantId, request.Quantity, $"return:{receipt}", cancellationToken);
        return Ok(new { recorded });
    }

    /// <summary>Gives old products their default variant. With dryRun (the default) it only reports.</summary>
    [HttpPost("backfill")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Backfill([FromQuery] bool dryRun = true, CancellationToken cancellationToken = default)
    {
        var report = await backfill.RunAsync(dryRun, cancellationToken);
        if (!dryRun)
        {
            audit.Log("CatalogBackfilled", $"variants={report.VariantsCreated}; orderLines={report.OrderLinesLinked}; conflicts={report.Conflicts.Count}");
            await db.SaveChangesAsync(cancellationToken);
        }
        return Ok(report);
    }

    private static string? Invalid(SaveVariantRequest request) =>
        request.Price < 0 ? "Price cannot be negative."
        : !Enum.IsDefined(request.Condition) ? "Choose a condition."
        : request.Name is { Length: > 200 } ? "A variant name takes up to 200 characters."
        : request.WeightValue < 0 || request.Length < 0 || request.Width < 0 || request.Height < 0 ? "Weight and dimensions cannot be negative."
        : request.WeightValue is not null && request.WeightUnit is not ("lb" or "oz" or "kg" or "g") ? "Weight needs a unit of lb, oz, kg or g."
        : (request.Length ?? request.Width ?? request.Height) is not null && request.DimensionUnit is not ("in" or "cm") ? "Dimensions need a unit of in or cm."
        : null;

    private static void Apply(ProductVariant variant, SaveVariantRequest request, DateTime now)
    {
        variant.Name = Blank(request.Name);
        variant.OptionsJson = request.Options is { Count: > 0 } ? JsonSerializer.Serialize(new SortedDictionary<string, string>(request.Options)) : null;
        variant.Condition = request.Condition;
        variant.Price = request.Price;
        variant.WeightValue = request.WeightValue;
        variant.WeightUnit = request.WeightValue is null ? null : request.WeightUnit;
        variant.Length = request.Length;
        variant.Width = request.Width;
        variant.Height = request.Height;
        variant.DimensionUnit = (request.Length ?? request.Width ?? request.Height) is null ? null : request.DimensionUnit;
        variant.UpdatedAtUtc = now;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task<CatalogProductResponse> ToResponseAsync(Product product, CancellationToken cancellationToken)
    {
        var variants = await db.ProductVariants.AsNoTracking().Where(v => v.ProductId == product.Id)
            .OrderByDescending(v => v.IsDefault).ThenBy(v => v.Sku).ToListAsync(cancellationToken);
        var variantIds = variants.Select(v => v.Id).ToList();
        var balances = await db.InventoryBalances.AsNoTracking()
            .Where(b => variantIds.Contains(b.VariantId) && b.LocationId == InventoryLocation.DefaultId)
            .ToDictionaryAsync(b => b.VariantId, cancellationToken);
        var identifiers = await db.ProductIdentifiers.AsNoTracking()
            .Where(i => i.ProductId == product.Id || (i.VariantId != null && variantIds.Contains(i.VariantId.Value)))
            .OrderBy(i => i.Type).ToListAsync(cancellationToken);
        var media = await (
            from m in db.ProductMedia.AsNoTracking()
            join a in db.MediaAssets.AsNoTracking() on m.MediaAssetId equals a.Id
            where m.ProductId == product.Id
            orderby m.Purpose, m.Position
            select new MediaResponse(m.Id, a.Url, a.AltText, m.VariantId, m.Purpose, m.Position)).ToListAsync(cancellationToken);

        return new CatalogProductResponse(
            product.Id, product.Sku, product.Name, product.Brand, product.Description, product.Category,
            variants.Select(v =>
            {
                var b = balances.GetValueOrDefault(v.Id);
                return new VariantResponse(
                    v.Id, v.ProductId, v.Sku, v.Name, ListingComposer.ParseMap(v.OptionsJson), v.Condition, v.Price, v.Currency,
                    v.WeightValue, v.WeightUnit, v.Length, v.Width, v.Height, v.DimensionUnit, v.IsDefault, v.IsArchived,
                    b?.OnHand ?? 0, b?.Reserved ?? 0, b?.SafetyStock ?? 0, b?.AvailableToSell ?? 0, RowVersionCodec.Encode(v.RowVersion));
            }).ToList(),
            identifiers.Select(i => new IdentifierResponse(i.Id, i.Type, i.Value, i.VariantId)).ToList(),
            media);
    }
}
