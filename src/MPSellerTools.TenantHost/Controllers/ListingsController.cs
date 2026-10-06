using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;

namespace MPSellerTools.TenantHost.Controllers;

/// <summary>
/// The company's products as posted on e-commerce sites. Read-only, like the
/// catalog is for employees: the rows come from the site itself, through the
/// eBay import, and are never edited here.
/// </summary>
[ApiController]
[Route("api/listings")]
[Authorize(Policy = Roles.Employee)]
public class ListingsController(TenantDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var listings = await (
            from listing in db.Listings
            join product in db.Products on listing.ProductId equals product.Id
            orderby product.Name, listing.ExternalId
            select new ListingResponse(
                listing.Id, product.Id, product.Sku, product.Name, listing.Channel, listing.ExternalId, listing.Marketplace,
                listing.Url, listing.Status, listing.Price, listing.Currency, listing.AvailableQuantity, listing.SoldQuantity,
                listing.LastSyncedAtUtc)
        ).ToListAsync(cancellationToken);

        var connection = await db.EbayConnections.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return Ok(new ListingsResponse(listings, connection?.RefreshTokenProtected is not null, connection?.LastProductSyncAtUtc));
    }
}
