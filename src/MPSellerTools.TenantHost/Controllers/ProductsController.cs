using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Policy = Roles.Employee)]
public class ProductsController(TenantDbContext db, AuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        // Employees get the read-only catalog (brief §8) — archived products
        // are excluded for everyone since they are no longer sellable, only
        // kept for historical order references.
        var query = db.Products.Where(p => !p.IsArchived);
        var products = await query.OrderBy(p => p.Name).ToListAsync();
        return Ok(products.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }
        return Ok(ToResponse(product));
    }

    [HttpPost]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        var error = ValidateProductFields(request.Sku, request.Name, request.Price, request.StockQuantity);
        if (error is not null)
        {
            return Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        if (await db.Products.AnyAsync(p => p.Sku == request.Sku))
        {
            return Problem("A product with this SKU already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = request.Sku.Trim(),
            Name = request.Name.Trim(),
            Price = request.Price,
            StockQuantity = request.StockQuantity,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Products.Add(product);
        audit.Log("ProductCreated", $"sku={product.Sku}");
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = product.Id }, ToResponse(product));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request)
    {
        var error = ValidateProductFields("n/a", request.Name, request.Price, request.StockQuantity);
        if (error is not null)
        {
            return Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var product = await db.Products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        product.Name = request.Name.Trim();
        product.Price = request.Price;
        product.StockQuantity = request.StockQuantity;
        product.UpdatedAtUtc = DateTime.UtcNow;
        db.Entry(product).Property(p => p.RowVersion).OriginalValue = RowVersionCodec.Decode(request.RowVersion);

        audit.Log("ProductUpdated", $"sku={product.Sku}");

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem("This product was modified by someone else. Reload and try again.", statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(ToResponse(product));
    }

    /// <summary>
    /// Creates and updates products from a spreadsheet's rows, matched by
    /// SKU. Rows that are wrong are reported and left out; the rest are saved
    /// together. With dryRun (the default) it only reports.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Import([FromBody] ImportProductsRequest request, [FromQuery] bool dryRun = true)
    {
        var rows = request.Rows ?? [];
        if (rows.Count is 0 or > MaxImportRows)
        {
            return Problem($"Send between 1 and {MaxImportRows} rows.", statusCode: StatusCodes.Status400BadRequest);
        }

        var skus = rows.Select(r => r.Sku?.Trim() ?? "").Where(s => s.Length > 0).Distinct().ToList();
        var existing = await db.Products.Where(p => EF.Parameter(skus).Contains(p.Sku)).ToDictionaryAsync(p => p.Sku, StringComparer.OrdinalIgnoreCase);
        // A SKU held by another product's variant cannot become a product of its own.
        var variantSkus = (await db.ProductVariants.AsNoTracking().Where(v => EF.Parameter(skus).Contains(v.Sku) && !v.IsDefault).Select(v => v.Sku).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var errors = new List<ImportProductError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (created, updated, unchanged) = (0, 0, 0);
        var now = DateTime.UtcNow;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var sku = row.Sku?.Trim() ?? "";
            var name = row.Name?.Trim() ?? "";
            var error = ValidateProductFields(sku, name, row.Price, row.StockQuantity)
                ?? (!seen.Add(sku) ? "This SKU appears more than once in the file."
                    : variantSkus.Contains(sku) ? "This SKU belongs to a variant of another product."
                    : existing.TryGetValue(sku, out var found) && found.IsArchived ? "This product is archived."
                    : null);
            if (error is not null)
            {
                errors.Add(new ImportProductError(i + 1, sku.Length > 0 ? sku : null, error));
                continue;
            }

            if (!existing.TryGetValue(sku, out var product))
            {
                created++;
                if (!dryRun)
                {
                    db.Products.Add(new Product
                    {
                        Id = Guid.NewGuid(), Sku = sku, Name = name, Price = row.Price, StockQuantity = row.StockQuantity, CreatedAtUtc = now, UpdatedAtUtc = now,
                    });
                }
            }
            else if (product.Name == name && product.Price == row.Price && product.StockQuantity == row.StockQuantity)
            {
                unchanged++;
            }
            else
            {
                updated++;
                if (!dryRun)
                {
                    product.Name = name;
                    product.Price = row.Price;
                    product.StockQuantity = row.StockQuantity;
                    product.UpdatedAtUtc = now;
                }
            }
        }

        if (!dryRun && created + updated > 0)
        {
            audit.Log("ProductsImported", $"created={created}; updated={updated}; errors={errors.Count}");
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Problem("The catalog changed while the import was running. Nothing was saved; try again.", statusCode: StatusCodes.Status409Conflict);
            }
        }

        return Ok(new ImportProductsResponse(dryRun, created, updated, unchanged, errors));
    }

    private const int MaxImportRows = 2000;

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Archive(Guid id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        product.IsArchived = true;
        product.UpdatedAtUtc = DateTime.UtcNow;
        audit.Log("ProductArchived", $"sku={product.Sku}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string? ValidateProductFields(string sku, string name, decimal price, int stockQuantity)
    {
        if (string.IsNullOrWhiteSpace(sku) || sku.Length > 64)
        {
            return "SKU is required and must be 64 characters or fewer.";
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            return "Name is required and must be 200 characters or fewer.";
        }
        if (price < 0)
        {
            return "Price cannot be negative.";
        }
        if (stockQuantity < 0)
        {
            return "Stock quantity cannot be negative.";
        }
        return null;
    }

    private static ProductResponse ToResponse(Product p) =>
        new(p.Id, p.Sku, p.Name, p.Price, p.StockQuantity, p.IsArchived, RowVersionCodec.Encode(p.RowVersion));
}
