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
