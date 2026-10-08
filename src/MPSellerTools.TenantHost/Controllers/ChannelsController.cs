using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

/// <summary>
/// The company's sales channels: accounts on Amazon, eBay and Walmart and the
/// company's own website, their marketplaces, category mappings, and the
/// state of the sync queue. TenantAdmin only. Credentials go in and never
/// come back out.
/// </summary>
[ApiController]
[Route("api/channels")]
[Authorize(Policy = Roles.TenantAdmin)]
public class ChannelsController(
    TenantDbContext db,
    ListingService listings,
    SyncEngine engine,
    SyncHealthReader health,
    ChannelSecrets secrets,
    ChannelTokenCache tokens,
    IOptions<MarketplaceOptions> options,
    AuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var accounts = await db.ChannelAccounts.AsNoTracking().OrderBy(a => a.Channel).ThenBy(a => a.Name).ToListAsync(cancellationToken);
        var markets = await db.ChannelMarkets.AsNoTracking().ToListAsync(cancellationToken);
        return Ok(accounts.Select(a => ToResponse(a, markets)));
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] SaveChannelAccountRequest request, CancellationToken cancellationToken) =>
        SaveAsync(null, request, cancellationToken);

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, [FromBody] SaveChannelAccountRequest request, CancellationToken cancellationToken) =>
        SaveAsync(id, request, cancellationToken);

    private async Task<IActionResult> SaveAsync(Guid? id, SaveChannelAccountRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100 || !Enum.IsDefined(request.Channel) || !Enum.IsDefined(request.Environment)
            || !Enum.IsDefined(request.PriceConflictPolicy) || request.SellerId is { Length: > 100 })
        {
            return Problem("An account needs a channel, an environment and a name of up to 100 characters.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (request.Settings is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            return Problem("Settings must be a JSON object.", statusCode: StatusCodes.Status400BadRequest);
        }

        ChannelAccount? account = null;
        if (id is not null)
        {
            account = await db.ChannelAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (account is null)
            {
                return NotFound();
            }
            if (account.Channel != request.Channel)
            {
                return Problem("An account's channel cannot be changed.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        if (await db.ChannelAccounts.AnyAsync(a => a.Channel == request.Channel && a.Name == name && a.Id != id, cancellationToken))
        {
            return Problem("This channel already has an account with that name.", statusCode: StatusCodes.Status409Conflict);
        }
        // The eBay keys and consent live in the company's single eBay connection, so there is one eBay account to match.
        if (request.Channel == SalesChannel.Ebay && await db.ChannelAccounts.AnyAsync(a => a.Channel == SalesChannel.Ebay && a.Id != id, cancellationToken))
        {
            return Problem("There is already an eBay account; it uses the company's eBay connection.", statusCode: StatusCodes.Status409Conflict);
        }

        if (request.InventorySyncEnabled && request.Channel != SalesChannel.Website)
        {
            // Stock sent to a channel is only as good as the orders known from every channel that sells it.
            if (!options.Value.InventoryAccountingEnabled)
            {
                return Problem(
                    "Stock can be sent to channels only once inventory accounting is switched on for this host (Marketplace:InventoryAccountingEnabled).",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var notImporting = await db.ChannelAccounts
                .Where(a => a.IsEnabled && a.Channel != SalesChannel.Website && !a.OrderImportEnabled && a.Id != id)
                .Select(a => a.Name).ToListAsync(cancellationToken);
            if (!request.OrderImportEnabled || notImporting.Count > 0)
            {
                return Problem(
                    "Stock can be sent to channels only when orders are imported from every active channel"
                    + (notImporting.Count > 0 ? $" (not yet: {string.Join(", ", notImporting)})." : "."),
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        var now = DateTime.UtcNow;
        if (account is null)
        {
            account = new ChannelAccount { Id = Guid.NewGuid(), Channel = request.Channel, Name = name, CreatedAtUtc = now };
            db.ChannelAccounts.Add(account);
            if (request.Channel == SalesChannel.Website)
            {
                db.ChannelMarkets.Add(new ChannelMarket { Id = Guid.NewGuid(), ChannelAccountId = account.Id, MarketplaceCode = "default" });
            }
        }

        var settingsBefore = account.SettingsJson;
        account.Name = name;
        account.Environment = request.Environment;
        account.SellerId = string.IsNullOrWhiteSpace(request.SellerId) ? null : request.SellerId.Trim();
        account.SettingsJson = request.Settings is { ValueKind: JsonValueKind.Object } settings ? settings.GetRawText() : null;
        account.IsEnabled = request.IsEnabled;
        account.LiveWritesEnabled = request.LiveWritesEnabled;
        account.InventorySyncEnabled = request.InventorySyncEnabled;
        account.OrderImportEnabled = request.OrderImportEnabled;
        account.PriceConflictPolicy = request.PriceConflictPolicy;
        account.UpdatedAtUtc = now;
        audit.Log("ChannelAccountSaved", $"channel={account.Channel}; name={account.Name}; liveWrites={account.LiveWritesEnabled}");
        await db.SaveChangesAsync(cancellationToken);
        if (id is not null && settingsBefore != account.SettingsJson)
        {
            // A listing held back by a setting that has now been filled in should stop saying so.
            await listings.RecheckAccountIssuesAsync(account.Id, cancellationToken);
        }

        var markets = await db.ChannelMarkets.AsNoTracking().Where(m => m.ChannelAccountId == account.Id).ToListAsync(cancellationToken);
        return Ok(ToResponse(account, markets));
    }

    [HttpPut("{id:guid}/credentials")]
    public async Task<IActionResult> SetCredentials(Guid id, [FromBody] SetCredentialsRequest request, CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }
        if (account.Channel is SalesChannel.Ebay or SalesChannel.Website)
        {
            return Problem(
                account.Channel == SalesChannel.Ebay ? "eBay's keys are saved on the eBay page." : "The website needs no credentials.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var required = account.Channel switch
        {
            SalesChannel.Amazon => new[] { "clientId", "clientSecret", "refreshToken" },
            SalesChannel.Magento => ["accessToken"],
            _ => ["clientId", "clientSecret"],
        };
        var given = (request.Credentials ?? []).ToDictionary(c => c.Key, c => c.Value?.Trim() ?? "");
        if (required.Any(key => !given.TryGetValue(key, out var value) || value.Length is 0 or > 4000))
        {
            return Problem($"{account.Channel} needs: {string.Join(", ", required)}.", statusCode: StatusCodes.Status400BadRequest);
        }

        account.CredentialsProtected = secrets.Protect(required.ToDictionary(key => key, key => given[key]));
        account.LastError = null;
        account.UpdatedAtUtc = DateTime.UtcNow;
        tokens.Invalidate(account.Id);
        // What was saved is not written to the audit trail, only that something was.
        audit.Log("ChannelCredentialsSaved", $"channel={account.Channel}; name={account.Name}");
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/markets")]
    public async Task<IActionResult> AddMarket(Guid id, [FromBody] SaveMarketRequest request, CancellationToken cancellationToken)
    {
        var code = request.MarketplaceCode?.Trim() ?? "";
        if (code.Length is 0 or > 32 || request.Language is { Length: > 10 } || request.Currency is { Length: not 3 })
        {
            return Problem("A marketplace needs a code of up to 32 characters; currency is a three-letter code.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (!await db.ChannelAccounts.AnyAsync(a => a.Id == id, cancellationToken))
        {
            return NotFound();
        }
        if (await db.ChannelMarkets.AnyAsync(m => m.ChannelAccountId == id && m.MarketplaceCode == code, cancellationToken))
        {
            return Problem("The account already has this marketplace.", statusCode: StatusCodes.Status409Conflict);
        }

        var market = new ChannelMarket
        {
            Id = Guid.NewGuid(),
            ChannelAccountId = id,
            MarketplaceCode = code,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en-US" : request.Language.Trim(),
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant(),
        };
        db.ChannelMarkets.Add(market);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ChannelMarketResponse(market.Id, market.MarketplaceCode, market.Language, market.Currency));
    }

    [HttpGet("category-mappings")]
    public async Task<IActionResult> CategoryMappings(CancellationToken cancellationToken) =>
        Ok((await db.CategoryMappings.AsNoTracking().OrderBy(c => c.InternalCategory).ToListAsync(cancellationToken)).Select(ToResponse));

    /// <summary>
    /// Maps an internal category to a marketplace's. Requirements given here
    /// are kept as given; without any, they are fetched from the channel
    /// where it offers that and live access is on.
    /// </summary>
    [HttpPut("category-mappings")]
    public async Task<IActionResult> SaveCategoryMapping([FromBody] SaveCategoryMappingRequest request, CancellationToken cancellationToken)
    {
        var category = request.InternalCategory?.Trim() ?? "";
        var external = request.ExternalCategoryId?.Trim() ?? "";
        if (category.Length is 0 or > 100 || external.Length is 0 or > 100)
        {
            return Problem("Both the internal category and the marketplace's category are required, up to 100 characters each.", statusCode: StatusCodes.Status400BadRequest);
        }

        var market = await db.ChannelMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.ChannelMarketId, cancellationToken);
        if (market is null)
        {
            return NotFound();
        }

        CategoryRequirements? requirements = null;
        if (request.Requirements is { ValueKind: JsonValueKind.Object } given)
        {
            try
            {
                requirements = CategoryRequirements.Parse(given.GetRawText(), request.RequirementsSource, request.RequirementsVersion);
            }
            catch (JsonException)
            {
                return Problem("Requirements must look like {\"required\":[...],\"enums\":{...},\"conditional\":[...]}.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var mapping = await db.CategoryMappings.FirstOrDefaultAsync(c => c.ChannelMarketId == market.Id && c.InternalCategory == category, cancellationToken);
        if (mapping is null)
        {
            mapping = new CategoryMapping { Id = Guid.NewGuid(), ChannelMarketId = market.Id, InternalCategory = category, ExternalCategoryId = external };
            db.CategoryMappings.Add(mapping);
        }

        mapping.ExternalCategoryId = external;
        if (requirements is not null)
        {
            mapping.RequirementsJson = JsonSerializer.Serialize(requirements);
            mapping.RequirementsSource = Truncate(request.RequirementsSource, 500) ?? "entered by hand";
            mapping.RequirementsVersion = Truncate(request.RequirementsVersion, 100);
            mapping.RequirementsRetrievedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(mapping));
    }

    /// <summary>Removes a mapping: the category's products then go to no category of the marketplace's, or to its default.</summary>
    [HttpDelete("category-mappings/{mappingId:guid}")]
    public async Task<IActionResult> RemoveCategoryMapping(Guid mappingId, CancellationToken cancellationToken)
    {
        var mapping = await db.CategoryMappings.FirstOrDefaultAsync(c => c.Id == mappingId, cancellationToken);
        if (mapping is null)
        {
            return NotFound();
        }

        db.CategoryMappings.Remove(mapping);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Fetches the mapped category's requirements from the channel and stores them with their source and version.</summary>
    [HttpPost("category-mappings/{mappingId:guid}/fetch-requirements")]
    public async Task<IActionResult> FetchRequirements(Guid mappingId, CancellationToken cancellationToken)
    {
        var mapping = await db.CategoryMappings.FirstOrDefaultAsync(c => c.Id == mappingId, cancellationToken);
        if (mapping is null)
        {
            return NotFound();
        }

        var market = await db.ChannelMarkets.AsNoTracking().FirstAsync(m => m.Id == mapping.ChannelMarketId, cancellationToken);
        var account = await db.ChannelAccounts.AsNoTracking().FirstAsync(a => a.Id == market.ChannelAccountId, cancellationToken);
        if (!(options.Value.LiveWritesEnabled && account.LiveWritesEnabled))
        {
            return Problem("Live access to this channel is switched off; enter the requirements by hand or switch it on.", statusCode: StatusCodes.Status409Conflict);
        }

        CategoryRequirements? fetched;
        try
        {
            fetched = await listings.AdapterFor(account).FetchRequirementsAsync(ListingService.ContextFor(account, market), mapping.ExternalCategoryId, cancellationToken);
        }
        catch (ChannelException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        if (fetched is null)
        {
            return Problem($"Fetching requirements is not available for {account.Channel}; enter them by hand.", statusCode: StatusCodes.Status501NotImplemented);
        }

        mapping.RequirementsJson = JsonSerializer.Serialize(fetched);
        mapping.RequirementsSource = Truncate(fetched.Source, 500);
        mapping.RequirementsVersion = Truncate(fetched.Version, 100);
        mapping.RequirementsRetrievedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(mapping));
    }

    /// <summary>
    /// Looks an item up in the channel's own catalog, so a listing can be an
    /// offer on what is already there. Amazon only, and only with live access
    /// on: it is a call to the marketplace, though one that changes nothing.
    /// </summary>
    [HttpGet("{id:guid}/catalog-search")]
    public async Task<IActionResult> CatalogSearch(Guid id, [FromQuery] string? q, CancellationToken cancellationToken)
    {
        var query = q?.Trim() ?? "";
        if (query.Length is < 2 or > 200)
        {
            return Problem("Search for a product name or a barcode of 2 to 200 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var account = await db.ChannelAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        var market = await db.ChannelMarkets.AsNoTracking().OrderBy(m => m.MarketplaceCode).FirstOrDefaultAsync(m => m.ChannelAccountId == id, cancellationToken);
        if (account is null || market is null)
        {
            return NotFound();
        }
        if (listings.AdapterFor(account) is not AmazonChannelAdapter amazon)
        {
            return Problem($"Searching the catalog is not available for {account.Channel}.", statusCode: StatusCodes.Status501NotImplemented);
        }
        if (!(options.Value.LiveWritesEnabled && account.LiveWritesEnabled))
        {
            return Problem("Live access to this channel is switched off; enter the ASIN by hand or switch it on.", statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            return Ok(await amazon.SearchCatalogAsync(ListingService.ContextFor(account, market), query, cancellationToken));
        }
        catch (ChannelException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Queues an order import for the account now, rather than at its next scheduled time.</summary>
    [HttpPost("{id:guid}/import-orders")]
    public async Task<IActionResult> ImportOrders(Guid id, CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }
        if (account.Channel == SalesChannel.Website || !account.IsEnabled)
        {
            return Problem("Orders are imported from enabled marketplace accounts.", statusCode: StatusCodes.Status400BadRequest);
        }

        var job = await engine.QueueAsync(account.Id, null, SyncOperation.OrderImport, 0, dryRun: false, DateTime.UtcNow, cancellationToken);
        audit.Log("ChannelOrderImportQueued", $"channel={account.Channel}; name={account.Name}");
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("sync/health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken) => Ok(await health.ReadAsync(cancellationToken));

    [HttpGet("sync/jobs")]
    public async Task<IActionResult> Jobs(
        [FromQuery] SyncJobStatus? status, [FromQuery] Guid? listingId, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        var query = db.SyncJobs.AsNoTracking();
        if (status is { } wanted)
        {
            query = query.Where(j => j.Status == wanted);
        }
        if (listingId is { } listing)
        {
            query = query.Where(j => j.ChannelListingId == listing);
        }

        var jobs = await query.OrderByDescending(j => j.UpdatedAtUtc).Take(Math.Clamp(take, 1, 500)).ToListAsync(cancellationToken);
        return Ok(jobs.Select(j => ToResponse(j, null)));
    }

    [HttpGet("sync/jobs/{jobId:guid}")]
    public async Task<IActionResult> Job(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.SyncJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var attempts = await db.SyncAttempts.AsNoTracking().Where(a => a.SyncJobId == jobId).OrderBy(a => a.StartedAtUtc).ToListAsync(cancellationToken);
        return Ok(ToResponse(job, attempts));
    }

    /// <summary>Order lines that could not be matched to a variant or reserved, waiting for someone to sort them out.</summary>
    [HttpGet("order-issues")]
    public async Task<IActionResult> OrderIssues([FromQuery] bool includeResolved = false, CancellationToken cancellationToken = default)
    {
        var issues = await db.OrderLineIssues.AsNoTracking()
            .Where(i => includeResolved || i.ResolvedAtUtc == null)
            .OrderByDescending(i => i.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
        return Ok(issues.Select(i => new OrderLineIssueResponse(
            i.Id, i.ChannelAccountId, i.ExternalOrderId, i.ExternalLineId, i.SellerSku, i.Quantity, i.Reason, i.OrderId, i.CreatedAtUtc, i.ResolvedAtUtc)));
    }

    [HttpPost("order-issues/{issueId:guid}/resolve")]
    public async Task<IActionResult> ResolveOrderIssue(Guid issueId, CancellationToken cancellationToken)
    {
        var issue = await db.OrderLineIssues.FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);
        if (issue is null)
        {
            return NotFound();
        }

        issue.ResolvedAtUtc ??= DateTime.UtcNow;
        audit.Log("OrderLineIssueResolved", $"order={issue.ExternalOrderId}; line={issue.ExternalLineId}; reason={issue.Reason}");
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private ChannelAccountResponse ToResponse(ChannelAccount a, IEnumerable<ChannelMarket> markets)
    {
        JsonElement? settings = null;
        if (!string.IsNullOrWhiteSpace(a.SettingsJson))
        {
            using var document = JsonDocument.Parse(a.SettingsJson);
            settings = document.RootElement.Clone();
        }

        return new ChannelAccountResponse(
            a.Id, a.Channel, a.Name, a.Environment, a.SellerId, settings,
            HasCredentials: a.Channel == SalesChannel.Website || !string.IsNullOrEmpty(a.CredentialsProtected),
            a.IsEnabled, a.LiveWritesEnabled,
            EffectiveLiveWrites: a.Channel == SalesChannel.Website || (a.LiveWritesEnabled && options.Value.LiveWritesEnabled),
            a.InventorySyncEnabled, a.OrderImportEnabled, a.PriceConflictPolicy, a.LastOrderImportAtUtc, a.LastError,
            markets.Where(m => m.ChannelAccountId == a.Id).OrderBy(m => m.MarketplaceCode)
                .Select(m => new ChannelMarketResponse(m.Id, m.MarketplaceCode, m.Language, m.Currency)).ToList());
    }

    private static CategoryMappingResponse ToResponse(CategoryMapping c)
    {
        JsonElement? requirements = null;
        if (!string.IsNullOrWhiteSpace(c.RequirementsJson))
        {
            using var document = JsonDocument.Parse(c.RequirementsJson);
            requirements = document.RootElement.Clone();
        }
        return new CategoryMappingResponse(
            c.Id, c.ChannelMarketId, c.InternalCategory, c.ExternalCategoryId, requirements, c.RequirementsSource, c.RequirementsVersion, c.RequirementsRetrievedAtUtc);
    }

    private static SyncJobResponse ToResponse(SyncJob j, List<SyncAttempt>? attempts) => new(
        j.Id, j.ChannelAccountId, j.ChannelListingId, j.Operation, j.Status, j.TargetVersion, j.Attempts, j.MaxAttempts, j.NextAttemptAtUtc,
        j.ExternalSubmissionId, j.ErrorClass, j.LastError, j.DryRun, j.CreatedAtUtc, j.CompletedAtUtc,
        attempts?.Select(a => new SyncAttemptResponse(
            a.Number, a.StartedAtUtc, a.FinishedAtUtc, a.Outcome, a.ErrorClass, a.HttpStatus, a.ExternalRequestId, a.Detail)).ToList());

    private static string? Truncate(string? text, int length) => text is not null && text.Length > length ? text[..length] : text;
}
