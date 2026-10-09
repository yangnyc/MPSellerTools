using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace.Channels;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

/// <summary>Whether Amazon's catalog can be read, and through which account; <see cref="Problem"/> says why not.</summary>
public record AmazonImportStatus(bool Ready, string? AccountName, string? Marketplace, string? Problem, string SkuPrefix, int MaxItems);

public record AmazonLookupRequest(string? Query);

public record AmazonIdentifierResponse(ProductIdentifierType Type, string Value);

/// <summary>An item of Amazon's catalog as it would become a product, and the product already under its SKU, if there is one.</summary>
public record AmazonItemResponse(
    string Asin,
    string? Title,
    string? Brand,
    string? Description,
    string? Category,
    string? ProductType,
    decimal? ListPrice,
    string? Currency,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<AmazonIdentifierResponse> Identifiers,
    decimal? WeightValue,
    string? WeightUnit,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? DimensionUnit,
    string SuggestedSku,
    Guid? ExistingProductId);

public record AmazonImportItemRequest(string? Asin, string? Sku, decimal? Price, int StockQuantity = 0, bool UpdateExisting = false);

/// <summary>
/// <see cref="Lines"/> is the pasted list, one item to a line: an ASIN, an Amazon address or a barcode,
/// then, after a comma or a tab, the SKU its product is to have here when it is not to be made from the prefix.
/// </summary>
public record AmazonImportBulkRequest(string? Lines, string? SkuPrefix, bool UpdateExisting = false);

public record AmazonImportBulkResponse(Guid JobId, int Total);

/// <summary>
/// Importing products from Amazon's catalog: one looked up, shown and
/// imported, or a pasted list handed to a background job. TenantAdmin only.
/// </summary>
[ApiController]
[Route("api/amazon/import")]
[Authorize(Policy = Roles.TenantAdmin)]
public class AmazonImportController(TenantDbContext db, AmazonImport import, AuditLogger audit) : ControllerBase
{
    /// <summary>How many items one bulk import takes.</summary>
    public const int MaxItems = 5000;

    private static readonly BulkJobStatus[] Unfinished = [BulkJobStatus.Queued, BulkJobStatus.Running];

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var (context, problem) = await import.ContextAsync(cancellationToken);
        return Ok(new AmazonImportStatus(
            context is not null, context?.Account.Name, context?.Market.MarketplaceCode, problem, AmazonImport.DefaultSkuPrefix, MaxItems));
    }

    /// <summary>Looks one item up in Amazon's catalog by its ASIN, its page's address or a barcode. Nothing is saved.</summary>
    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup([FromBody] AmazonLookupRequest request, CancellationToken cancellationToken)
    {
        if (AmazonImport.ParseIdentifier(request.Query) is not { } wanted)
        {
            return Problem("Enter an ASIN (10 letters and digits), the address of the item's page on Amazon, or a UPC or EAN barcode.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await WithAmazonAsync(async context =>
        {
            var found = await import.LookupAsync(context, [new AmazonImportEntry(wanted.Type, wanted.Value, null)], cancellationToken);
            if (found[0].Item is not { } item)
            {
                return Problem($"Amazon's catalog has no item for {wanted.Value} in this marketplace.", statusCode: StatusCodes.Status404NotFound);
            }

            var sku = AmazonImport.SkuFor(item, null);
            var existing = await db.Products.AsNoTracking().Where(p => p.Sku == sku).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
            return Ok(new AmazonItemResponse(
                item.Asin, item.Title, item.Brand, AmazonImport.DescriptionOf(item), item.Category, item.ProductType, item.ListPrice, item.Currency, item.ImageUrls,
                item.Identifiers.OrderBy(i => i.Key).Select(i => new AmazonIdentifierResponse(i.Key, i.Value)).ToList(),
                item.WeightValue, item.WeightUnit, item.Length, item.Width, item.Height, item.DimensionUnit, sku, existing));
        }, cancellationToken);
    }

    /// <summary>Imports one item as a product, read from Amazon again so what is saved is what Amazon has now.</summary>
    [HttpPost("item")]
    public async Task<IActionResult> ImportItem([FromBody] AmazonImportItemRequest request, CancellationToken cancellationToken)
    {
        if (AmazonImport.ParseIdentifier(request.Asin) is not { Type: "ASIN" } wanted)
        {
            return Problem("Choose the item to import by its ASIN.", statusCode: StatusCodes.Status400BadRequest);
        }
        var sku = request.Sku?.Trim() ?? "";
        if (sku.Length > 64 || request.Price < 0 || request.StockQuantity < 0)
        {
            return Problem("A SKU takes up to 64 characters, and price and stock cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await WithAmazonAsync(async context =>
        {
            var found = await import.LookupAsync(context, [new AmazonImportEntry("ASIN", wanted.Value, null)], cancellationToken);
            if (found[0].Item is not { } item)
            {
                return Problem($"Amazon's catalog has no item for {wanted.Value} in this marketplace.", statusCode: StatusCodes.Status404NotFound);
            }

            AmazonImportResult result;
            try
            {
                result = await import.ApplyAsync(
                    item, sku.Length == 0 ? AmazonImport.SkuFor(item, null) : sku, request.Price, request.StockQuantity, request.UpdateExisting, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            if (result.Outcome == AmazonImportOutcome.AlreadyHere)
            {
                return Problem($"A product with the SKU {result.Sku} is already here. Choose another SKU, or choose to bring that product up to date.", statusCode: StatusCodes.Status409Conflict);
            }

            audit.Log("AmazonItemImported", $"asin={item.Asin}; sku={result.Sku}; outcome={result.Outcome}");
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Someone made a product with this SKU in the moment between the check and the save.
                return Problem($"The SKU {result.Sku} was taken meanwhile. Try again.", statusCode: StatusCodes.Status409Conflict);
            }
            return Ok(result);
        }, cancellationToken);
    }

    /// <summary>Queues a background job that imports every item of a pasted list. The list is checked here, so a line that cannot be read is said at once.</summary>
    [HttpPost("bulk")]
    public async Task<IActionResult> ImportBulk([FromBody] AmazonImportBulkRequest request, CancellationToken cancellationToken)
    {
        var prefix = request.SkuPrefix?.Trim() ?? AmazonImport.DefaultSkuPrefix;
        // An ASIN is ten characters, and the SKU has to fit.
        if (prefix.Length > 40)
        {
            return Problem("The SKU prefix takes up to 40 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var entries = new List<AmazonImportEntry>();
        var unreadable = new List<string>();
        var lines = (request.Lines ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var parts = line.Split([',', '\t', ';'], 2, StringSplitOptions.TrimEntries);
            var sku = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
            if (AmazonImport.ParseIdentifier(parts[0]) is { } wanted && sku is not { Length: > 64 })
            {
                entries.Add(new AmazonImportEntry(wanted.Type, wanted.Value, sku));
            }
            else
            {
                unreadable.Add(line.Length > 60 ? $"{line[..60]}…" : line);
            }
        }
        if (unreadable.Count > 0)
        {
            return Problem(
                $"{unreadable.Count} line(s) are not an ASIN, an Amazon address or a barcode, or have a SKU over 64 characters: {string.Join("; ", unreadable.Take(5))}"
                + (unreadable.Count > 5 ? "; …" : ""),
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Each item once, and those of one kind together, as Amazon is asked for one kind at a call.
        entries = entries.DistinctBy(e => (e.Type, e.Value)).OrderBy(e => e.Type, StringComparer.Ordinal).ToList();
        if (entries.Count is 0 or > MaxItems)
        {
            return Problem($"Paste between 1 and {MaxItems} items, one to a line.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (context, problem) = await import.ContextAsync(cancellationToken);
        if (context is null)
        {
            return Problem(problem, statusCode: StatusCodes.Status400BadRequest);
        }
        if (await db.BulkJobs.AnyAsync(j => j.Type == BulkJobType.ImportFromAmazon && Unfinished.Contains(j.Status), cancellationToken))
        {
            return Problem("An import from Amazon is already waiting or running. Let it finish, or stop it on the Jobs page.", statusCode: StatusCodes.Status409Conflict);
        }

        var job = new BulkJob
        {
            Id = Guid.NewGuid(),
            Type = BulkJobType.ImportFromAmazon,
            Status = BulkJobStatus.Queued,
            ChannelAccountId = context.Account.Id,
            ParametersJson = AmazonImport.Serialize(new AmazonImportParameters(entries, prefix, request.UpdateExisting)),
            Total = entries.Count,
            CreatedByUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty,
            CreatedByEmail = User.Identity?.Name ?? "unknown",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.BulkJobs.Add(job);
        audit.Log("BulkJobQueued", $"type={job.Type}; items={entries.Count}");
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new AmazonImportBulkResponse(job.Id, entries.Count));
    }

    /// <summary>Runs work that reads Amazon, answering what stops it and Amazon's own refusals as they are.</summary>
    private async Task<IActionResult> WithAmazonAsync(Func<ChannelContext, Task<IActionResult>> work, CancellationToken cancellationToken)
    {
        var (context, problem) = await import.ContextAsync(cancellationToken);
        if (context is null)
        {
            return Problem(problem, statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return await work(context);
        }
        catch (ChannelException ex)
        {
            db.ChangeTracker.Clear();
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
