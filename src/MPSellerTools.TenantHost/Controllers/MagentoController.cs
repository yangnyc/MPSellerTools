using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

/// <summary><see cref="DryRun"/> (the default) only counts what would be deleted.</summary>
public record RemoveMagentoStoreProductsRequest(string SkuPrefix, bool DryRun = true, int Limit = 100);

/// <summary>
/// What is particular to the company's Magento store, beside the sales
/// channel account every marketplace has: checking that the saved address
/// and token reach the store, and reading its catalog as the listings
/// there. TenantAdmin only.
/// </summary>
[ApiController]
[Route("api/magento")]
[Authorize(Policy = Roles.TenantAdmin)]
public class MagentoController(TenantDbContext db, MagentoSync sync, IOptions<MarketplaceOptions> options) : ControllerBase
{
    /// <summary>Reaches the store with the saved address and token, and says which store answered.</summary>
    [HttpPost("test")]
    public Task<IActionResult> Test(CancellationToken cancellationToken) =>
        WithAccountAsync(async account => Ok(await sync.TestAsync(account, cancellationToken)), mustBeEnabled: false, cancellationToken);

    /// <summary>Reads the store's catalog and records its products as the listings on Magento.</summary>
    [HttpPost("import/listings")]
    public Task<IActionResult> ImportListings(CancellationToken cancellationToken) =>
        WithAccountAsync(async account => Ok(await sync.ImportListingsAsync(account, cancellationToken)), mustBeEnabled: true, cancellationToken);

    /// <summary>
    /// Deletes from the store products by the start of their SKU, a limited number at a call; products this
    /// workspace still lists on Magento are left. A dry run (the default) only counts. Deleting needs live
    /// writes on for the account, as it changes the store.
    /// </summary>
    [HttpPost("store-products/remove")]
    public Task<IActionResult> RemoveStoreProducts([FromBody] RemoveMagentoStoreProductsRequest request, CancellationToken cancellationToken)
    {
        var prefix = request.SkuPrefix?.Trim() ?? "";
        // A short prefix would take most of a store with it.
        if (prefix.Length is < 3 or > 64)
        {
            return Task.FromResult<IActionResult>(Problem("Give the start of the SKUs to remove, at least three characters.", statusCode: StatusCodes.Status400BadRequest));
        }

        return WithAccountAsync(async account =>
        {
            if (!request.DryRun && !(account.LiveWritesEnabled && options.Value.LiveWritesEnabled))
            {
                return Problem("Live writes are off for Magento, so nothing is deleted from the store.", statusCode: StatusCodes.Status409Conflict);
            }
            return Ok(await sync.RemoveStoreProductsAsync(account, prefix, Math.Clamp(request.Limit, 1, 200), request.DryRun, cancellationToken));
        }, mustBeEnabled: true, cancellationToken);
    }

    private async Task<IActionResult> WithAccountAsync(Func<ChannelAccount, Task<IActionResult>> work, bool mustBeEnabled, CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.OrderBy(a => a.CreatedAtUtc).FirstOrDefaultAsync(a => a.Channel == SalesChannel.Magento, cancellationToken);
        if (account is null)
        {
            return Problem("Add Magento as a sales channel first.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (mustBeEnabled && !account.IsEnabled)
        {
            return Problem("The Magento account is switched off, so nothing is read from the store.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return await work(account);
        }
        catch (ChannelException ex)
        {
            // Nothing from the failed read is kept.
            db.ChangeTracker.Clear();
            // The store's own refusal is a gateway error; a missing address or token is the request's.
            return Problem(ex.Message, statusCode: ex.HttpStatus is null && !ex.Ambiguous ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway);
        }
    }
}
