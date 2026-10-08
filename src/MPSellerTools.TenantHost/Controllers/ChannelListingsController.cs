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
/// How each variant is offered on each marketplace: its channel content,
/// price and stock cap, validation, a preview of what would be sent, and
/// publishing, retrying and deactivating. Nothing here calls a channel: a
/// change is saved with an outbox event and the sync worker does the rest.
/// </summary>
[ApiController]
[Route("api/channel-listings")]
[Authorize(Policy = Roles.Employee)]
public class ChannelListingsController(
    TenantDbContext db, ListingService listings, SyncEngine engine, IOptions<MarketplaceOptions> options, AuditLogger audit) : ControllerBase
{
    /// <summary>The most listings one answer carries.</summary>
    private const int MaxListed = 10000;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? productId, [FromQuery] Guid? accountId, CancellationToken cancellationToken)
    {
        var query =
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            join variant in db.ProductVariants.AsNoTracking() on listing.VariantId equals variant.Id
            where (productId == null || variant.ProductId == productId) && (accountId == null || market.ChannelAccountId == accountId)
            orderby listing.SellerSku
            select listing.Id;
        // Read together: a company can have thousands of listings on one marketplace.
        var ids = await query.Take(MaxListed).ToListAsync(cancellationToken);
        return Ok((await listings.LoadManyAsync(ids, cancellationToken)).Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await listings.LoadAsync(id, cancellationToken) is { } bundle ? Ok(ToResponse(bundle)) : NotFound();

    /// <summary>
    /// Creates the variant's listing on a marketplace, or changes it. Saving
    /// never publishes: a new listing starts as a draft.
    /// </summary>
    [HttpPut]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Save([FromBody] SaveChannelListingRequest request, CancellationToken cancellationToken)
    {
        var market = await db.ChannelMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.ChannelMarketId, cancellationToken);
        var variant = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VariantId, cancellationToken);
        if (market is null || variant is null)
        {
            return NotFound();
        }

        var sellerSku = string.IsNullOrWhiteSpace(request.SellerSku) ? variant.Sku : request.SellerSku.Trim();
        if (sellerSku.Length > 64 || request.PriceOverride < 0 || request.QuantityCap < 0 || !Enum.IsDefined(request.FulfillmentMode)
            || request.ExternalCategoryId is { Length: > 100 } || request.ExistingCatalogItemId is { Length: > 128 })
        {
            return Problem("Check the seller SKU (up to 64 characters), price and quantity cap (not negative) and fulfillment mode.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (request.Content?.Keys.FirstOrDefault(k => !ContentOverrides.Fields.Contains(k)) is { } unknown)
        {
            return Problem($"\"{unknown}\" cannot be overridden; the fields are {string.Join(", ", ContentOverrides.Fields)}.", statusCode: StatusCodes.Status400BadRequest);
        }

        var account = await db.ChannelAccounts.AsNoTracking().FirstAsync(a => a.Id == market.ChannelAccountId, cancellationToken);
        if (request.FulfillmentMode == FulfillmentMode.ChannelFulfilled && account.Channel is SalesChannel.Ebay or SalesChannel.Website or SalesChannel.Magento)
        {
            return Problem($"{account.Channel} has no fulfillment by the channel.", statusCode: StatusCodes.Status400BadRequest);
        }

        var now = DateTime.UtcNow;
        var listing = await db.ChannelListings.FirstOrDefaultAsync(l => l.ChannelMarketId == market.Id && l.VariantId == variant.Id, cancellationToken);
        if (await db.ChannelListings.AnyAsync(
                l => l.ChannelMarketId == market.Id && l.SellerSku == sellerSku && l.VariantId != variant.Id, cancellationToken))
        {
            // One seller SKU, one offer: an order for it must lead to exactly one variant.
            return Problem("Another variant is already listed on this marketplace under that seller SKU.", statusCode: StatusCodes.Status409Conflict);
        }

        var isNew = listing is null;
        if (listing is null)
        {
            listing = new ChannelListing
            {
                Id = Guid.NewGuid(),
                ChannelMarketId = market.Id,
                VariantId = variant.Id,
                SellerSku = sellerSku,
                DesiredState = ListingDesiredState.Draft,
                CreatedAtUtc = now,
            };
            db.ChannelListings.Add(listing);
        }
        else if (listing.SellerSku != sellerSku && listing.ConfirmedContentVersion > 0)
        {
            return Problem("The seller SKU cannot change once the listing exists on the channel.", statusCode: StatusCodes.Status409Conflict);
        }

        var overrides = ContentOverrides.Parse(listing.ContentOverridesJson);
        foreach (var (field, value) in request.Content ?? [])
        {
            if (value is null)
            {
                overrides.Inherit(field);
            }
            else if (value.Cleared)
            {
                overrides.Clear(field);
            }
            else
            {
                overrides.Set(field, value.Value ?? "");
            }
        }

        if (request.ImageIds is { } chosen)
        {
            var own = await db.ProductMedia.AsNoTracking()
                .Where(m => m.ProductId == variant.ProductId && (m.VariantId == null || m.VariantId == variant.Id)).Select(m => m.Id).ToListAsync(cancellationToken);
            if (chosen.Count != chosen.Distinct().Count() || chosen.Any(id => !own.Contains(id)))
            {
                return Problem("Choose each picture once, from this product's own pictures.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var before = (listing.ContentOverridesJson, listing.AttributesJson, listing.ExternalCategoryId, listing.SellerSku, listing.ImageSelectionJson);
        var (priceBefore, stockBefore) = (listing.PriceOverride, (listing.QuantityCap, listing.FulfillmentMode));
        listing.SellerSku = sellerSku;
        listing.ContentOverridesJson = overrides.ToJson();
        listing.AttributesJson = request.Attributes is null ? listing.AttributesJson
            : request.Attributes.Count == 0 ? null
            : JsonSerializer.Serialize(new SortedDictionary<string, string>(request.Attributes));
        listing.ExternalCategoryId = string.IsNullOrWhiteSpace(request.ExternalCategoryId) ? null : request.ExternalCategoryId.Trim();
        listing.ImageSelectionJson = request.ImageIds is null ? listing.ImageSelectionJson
            : request.ImageIds.Count == 0 ? null
            : JsonSerializer.Serialize(request.ImageIds);
        listing.PriceOverride = request.PriceOverride;
        listing.FulfillmentMode = request.FulfillmentMode;
        listing.QuantityCap = request.QuantityCap;
        listing.UpdatedAtUtc = now;

        var catalogItemChanged = await SetCatalogItemAsync(listing, account, market, request.ExistingCatalogItemId, now, cancellationToken);
        if (isNew || catalogItemChanged || before != (listing.ContentOverridesJson, listing.AttributesJson, listing.ExternalCategoryId, listing.SellerSku, listing.ImageSelectionJson))
        {
            Raise(OutboxEvent.ListingContentChanged, listing.Id, now);
        }
        if (!isNew && priceBefore != listing.PriceOverride)
        {
            Raise(OutboxEvent.ListingPriceChanged, listing.Id, now);
        }
        if (!isNew && stockBefore != (listing.QuantityCap, listing.FulfillmentMode))
        {
            Raise(OutboxEvent.ListingInventoryChanged, listing.Id, now);
        }

        audit.Log("ChannelListingSaved", $"channel={account.Channel}; market={market.MarketplaceCode}; sku={sellerSku}; price={listing.PriceOverride}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse((await listings.LoadAsync(listing.Id, cancellationToken))!));
    }

    /// <summary>Runs every check that would stop a submission, and remembers the result on the listing.</summary>
    [HttpPost("{id:guid}/validate")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Validate(Guid id, CancellationToken cancellationToken)
    {
        var bundle = await listings.LoadAsync(id, cancellationToken);
        if (bundle is null)
        {
            return NotFound();
        }

        var issues = listings.Validate(bundle);
        await StoreIssuesAsync(id, issues, cancellationToken);
        var requirements = bundle.Work.Snapshot.Requirements;
        return Ok(new ListingValidationResponse(issues.Count == 0, issues, requirements.Source, requirements.Version));
    }

    /// <summary>
    /// A dry run in the request itself: validates and builds exactly what the
    /// operation would send, and sends nothing. No credentials are involved.
    /// </summary>
    [HttpPost("{id:guid}/preview")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Preview(Guid id, [FromQuery] SyncOperation operation = SyncOperation.Content, CancellationToken cancellationToken = default)
    {
        var bundle = await listings.LoadAsync(id, cancellationToken);
        if (bundle is null)
        {
            return NotFound();
        }

        // A draft is previewed as it would go out on being published; that is the question a preview answers.
        if (operation == SyncOperation.Content && bundle.Listing.DesiredState == ListingDesiredState.Draft)
        {
            bundle = bundle with { Work = bundle.Work with { DesiredState = ListingDesiredState.Active } };
        }

        // Bodies are serialized here the way the transport serializes them, so the preview shows the
        // channel's own field names to the letter rather than this API's naming convention.
        var account = bundle.Context.Account;
        var requests = listings.AdapterFor(account).Preview(operation, bundle.Work, bundle.Context)
            .Select(r => r with { Body = r.Body is null ? null : JsonSerializer.SerializeToElement(JsonDocument.Parse(ChannelHttp.Serialize(r.Body)).RootElement) })
            .ToList();
        return Ok(new ListingPreviewResponse(operation, LiveWrites(account), listings.Validate(bundle), requests));
    }

    /// <summary>Queues a dry run through the worker: the whole pipeline short of the channel call.</summary>
    [HttpPost("{id:guid}/dry-run")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> DryRun(Guid id, [FromQuery] SyncOperation operation = SyncOperation.Content, CancellationToken cancellationToken = default)
    {
        var bundle = await listings.LoadAsync(id, cancellationToken);
        if (bundle is null)
        {
            return NotFound();
        }
        if (operation is SyncOperation.OrderImport or SyncOperation.Reconcile)
        {
            return Problem("A dry run applies to content, price, inventory and deactivation.", statusCode: StatusCodes.Status400BadRequest);
        }

        var job = await engine.QueueAsync(bundle.Context.Account.Id, id, operation, 0, dryRun: true, DateTime.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new { jobId = job.Id });
    }

    /// <summary>
    /// Asks for the listing to be on sale. Refused, with the reasons, while
    /// the listing does not validate; the rest of the catalog stays editable.
    /// The answer only says it was queued: whether buyers can see it shows
    /// later as the observed status.
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        var bundle = await listings.LoadAsync(id, cancellationToken);
        if (bundle is null)
        {
            return NotFound();
        }

        var active = bundle.Work with { DesiredState = ListingDesiredState.Active };
        var issues = listings.Validate(bundle with { Work = active });
        await StoreIssuesAsync(id, issues, cancellationToken);
        if (issues.Count > 0)
        {
            return UnprocessableEntity(new ListingValidationResponse(
                false, issues, bundle.Work.Snapshot.Requirements.Source, bundle.Work.Snapshot.Requirements.Version));
        }

        return await SetDesiredStateAsync(id, ListingDesiredState.Active, "ChannelListingPublishRequested", bundle, cancellationToken);
    }

    /// <summary>Asks for the listing to come off sale. The record stays, so the listing on the channel can still be found and ended.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var bundle = await listings.LoadAsync(id, cancellationToken);
        return bundle is null
            ? NotFound()
            : await SetDesiredStateAsync(id, ListingDesiredState.Inactive, "ChannelListingDeactivateRequested", bundle, cancellationToken);
    }

    /// <summary>Tries again after a failure or a correction, sending the listing's current state.</summary>
    [HttpPost("{id:guid}/retry")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var bundle = await listings.LoadAsync(id, cancellationToken);
        if (bundle is null)
        {
            return NotFound();
        }
        if (bundle.Listing.DesiredState == ListingDesiredState.Draft)
        {
            return Problem("A draft has nothing to retry; publish it.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTime.UtcNow;
        var failed = await db.SyncJobs.AsNoTracking()
            .Where(j => j.ChannelListingId == id && (j.Status == SyncJobStatus.Failed || j.Status == SyncJobStatus.NeedsCorrection))
            .Select(j => j.Operation).Distinct().ToListAsync(cancellationToken);
        Raise(OutboxEvent.ListingContentChanged, id, now);
        if (failed.Contains(SyncOperation.Price))
        {
            Raise(OutboxEvent.ListingPriceChanged, id, now);
        }
        if (failed.Contains(SyncOperation.Inventory))
        {
            Raise(OutboxEvent.ListingInventoryChanged, id, now);
        }

        audit.Log("ChannelListingRetryRequested", $"sku={bundle.Listing.SellerSku}; channel={bundle.Context.Account.Channel}");
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new ListingQueuedResponse(id, bundle.Listing.DesiredState, LiveWrites(bundle.Context.Account)));
    }

    [HttpGet("groups")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Groups(CancellationToken cancellationToken)
    {
        var groups = await db.ListingGroups.AsNoTracking().OrderBy(g => g.GroupKey).ToListAsync(cancellationToken);
        var responses = new List<ListingGroupResponse>();
        foreach (var group in groups)
        {
            responses.Add(await ToResponseAsync(group, cancellationToken));
        }
        return Ok(responses);
    }

    /// <summary>
    /// Groups a product's listings on one marketplace into a variation
    /// family. Each marketplace has its own groups: they need not match one
    /// another or the product's own variants.
    /// </summary>
    [HttpPut("groups")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> SaveGroup([FromBody] SaveListingGroupRequest request, CancellationToken cancellationToken)
    {
        var key = request.GroupKey?.Trim() ?? "";
        var names = (request.VariationAttributes ?? []).Select(n => n.Trim()).Where(n => n.Length > 0).Distinct().ToList();
        var ids = (request.ListingIds ?? []).Distinct().ToList();
        if (key.Length is 0 or > 50 || names.Count == 0 || ids.Count < 2)
        {
            return Problem("A group needs a key of up to 50 characters, at least one varying attribute, and two or more listings.", statusCode: StatusCodes.Status400BadRequest);
        }

        var members = await (
            from listing in db.ChannelListings.AsNoTracking()
            join variant in db.ProductVariants.AsNoTracking() on listing.VariantId equals variant.Id
            where ids.Contains(listing.Id)
            select new { listing.Id, listing.ChannelMarketId, variant.ProductId, variant.OptionsJson, listing.SellerSku }).ToListAsync(cancellationToken);
        if (members.Count != ids.Count || members.Any(m => m.ChannelMarketId != request.ChannelMarketId || m.ProductId != request.ProductId))
        {
            return Problem("Every listing in a group must be of the same product on the same marketplace.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (members.Any(m => m.SellerSku == key))
        {
            return Problem("The group's key cannot be the seller SKU of one of its members: the group itself is not sold.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Each member has to say where it stands on every varying attribute, and no two may stand in the same place.
        var combinations = members.Select(m => ListingComposer.ParseMap(m.OptionsJson)).ToList();
        if (combinations.Any(options => names.Any(name => !options.ContainsKey(name))))
        {
            return Problem($"Every variant in the group needs a value for: {string.Join(", ", names)}.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (combinations.Select(options => string.Join("|", names.Select(n => options[n]))).Distinct().Count() != combinations.Count)
        {
            return Problem("Two variants in the group have the same combination of values.", statusCode: StatusCodes.Status400BadRequest);
        }

        var now = DateTime.UtcNow;
        var group = await db.ListingGroups.FirstOrDefaultAsync(g => g.ChannelMarketId == request.ChannelMarketId && g.GroupKey == key, cancellationToken);
        if (group is null)
        {
            group = new ListingGroup { Id = Guid.NewGuid(), ChannelMarketId = request.ChannelMarketId, ProductId = request.ProductId, GroupKey = key, CreatedAtUtc = now };
            db.ListingGroups.Add(group);
        }
        else if (group.ProductId != request.ProductId)
        {
            return Problem("This key is already the group of another product on this marketplace.", statusCode: StatusCodes.Status409Conflict);
        }

        group.VariationAttributesJson = JsonSerializer.Serialize(names);
        var current = await db.ListingGroupMembers.Where(m => m.ListingGroupId == group.Id || ids.Contains(m.ChannelListingId)).ToListAsync(cancellationToken);
        db.ListingGroupMembers.RemoveRange(current);
        db.ListingGroupMembers.AddRange(ids.Select(id => new ListingGroupMember { ListingGroupId = group.Id, ChannelListingId = id }));
        foreach (var id in ids)
        {
            Raise(OutboxEvent.ListingContentChanged, id, now);
        }

        audit.Log("ListingGroupSaved", $"key={key}; members={ids.Count}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await ToResponseAsync(group, cancellationToken));
    }

    private async Task<IActionResult> SetDesiredStateAsync(
        Guid id, ListingDesiredState state, string action, ListingBundle bundle, CancellationToken cancellationToken)
    {
        var listing = await db.ChannelListings.FirstAsync(l => l.Id == id, cancellationToken);
        var now = DateTime.UtcNow;
        listing.DesiredState = state;
        listing.UpdatedAtUtc = now;
        Raise(OutboxEvent.ListingContentChanged, id, now);
        audit.Log(action, $"sku={listing.SellerSku}; channel={bundle.Context.Account.Channel}; market={bundle.Context.Market.MarketplaceCode}");
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new ListingQueuedResponse(id, state, LiveWrites(bundle.Context.Account)));
    }

    /// <summary>Records the catalog item id the seller supplied (an ASIN). Returns whether it changed.</summary>
    private async Task<bool> SetCatalogItemAsync(
        ChannelListing listing, ChannelAccount account, ChannelMarket market, string? catalogItemId, DateTime now, CancellationToken cancellationToken)
    {
        if (catalogItemId is null)
        {
            return false;
        }

        var value = catalogItemId.Trim();
        var reference = await db.ExternalReferences.FirstOrDefaultAsync(
            r => r.OwnerType == ExternalOwnerType.ChannelListing && r.OwnerId == listing.Id && r.ResourceType == ExternalResourceType.CatalogItem,
            cancellationToken);
        if (value.Length == 0)
        {
            if (reference is not null)
            {
                db.ExternalReferences.Remove(reference);
            }
            return reference is not null;
        }

        if (reference is null)
        {
            db.ExternalReferences.Add(new ExternalReference
            {
                Id = Guid.NewGuid(),
                ChannelAccountId = account.Id,
                ChannelMarketId = market.Id,
                OwnerType = ExternalOwnerType.ChannelListing,
                OwnerId = listing.Id,
                ResourceType = ExternalResourceType.CatalogItem,
                Value = value,
                CreatedAtUtc = now,
            });
            return true;
        }

        var changed = reference.Value != value;
        reference.Value = value;
        return changed;
    }

    private Task<int> StoreIssuesAsync(Guid id, List<ValidationIssue> issues, CancellationToken cancellationToken)
    {
        var json = issues.Count == 0 ? null : JsonSerializer.Serialize(issues, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return db.ChannelListings.Where(l => l.Id == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.IssuesJson, json), cancellationToken);
    }

    private void Raise(string type, Guid listingId, DateTime now) =>
        db.OutboxEvents.Add(new OutboxEvent { Type = type, SubjectId = listingId, CreatedAtUtc = now });

    private bool LiveWrites(ChannelAccount account) =>
        account.Channel == SalesChannel.Website || (account.LiveWritesEnabled && options.Value.LiveWritesEnabled);

    private async Task<ListingGroupResponse> ToResponseAsync(ListingGroup group, CancellationToken cancellationToken)
    {
        var members = await db.ListingGroupMembers.AsNoTracking().Where(m => m.ListingGroupId == group.Id).Select(m => m.ChannelListingId).ToListAsync(cancellationToken);
        var listingId = await db.ExternalReferences.AsNoTracking()
            .Where(r => r.OwnerType == ExternalOwnerType.ListingGroup && r.OwnerId == group.Id && r.ResourceType == ExternalResourceType.Listing)
            .Select(r => r.Value).FirstOrDefaultAsync(cancellationToken);
        return new ListingGroupResponse(
            group.Id, group.ChannelMarketId, group.ProductId, group.GroupKey,
            JsonSerializer.Deserialize<List<string>>(group.VariationAttributesJson ?? "[]") ?? [], members, listingId);
    }

    private static ChannelListingResponse ToResponse(ListingBundle bundle)
    {
        var (l, s) = (bundle.Listing, bundle.Work.Snapshot);
        JsonElement? issues = null;
        if (!string.IsNullOrWhiteSpace(l.IssuesJson))
        {
            using var document = JsonDocument.Parse(l.IssuesJson);
            issues = document.RootElement.Clone();
        }

        return new ChannelListingResponse(
            l.Id, l.ChannelMarketId, bundle.Context.Account.Id, bundle.Context.Account.Channel, bundle.Context.Market.MarketplaceCode,
            l.VariantId, l.SellerSku, l.ExternalCategoryId,
            ContentOverrides.Parse(l.ContentOverridesJson).All, s.Attributes, l.PriceOverride, l.FulfillmentMode, l.QuantityCap,
            s.Title, s.Description, s.Brand, s.Price, s.Quantity,
            l.DesiredState, l.ObservedStatus, l.ObservedPrice, l.ObservedQuantity, l.ObservedAtUtc, l.HasPriceConflict,
            new ListingVersions(l.ContentVersion, l.ConfirmedContentVersion),
            new ListingVersions(l.PriceVersion, l.ConfirmedPriceVersion),
            new ListingVersions(l.InventoryVersion, l.ConfirmedInventoryVersion),
            issues, bundle.Work.References,
            bundle.AvailableImages, bundle.ImageSelection, s.ImageUrls, ChannelImageRules.For(bundle.Context.Account.Channel),
            bundle.ProductId);
    }
}

/// <summary>
/// What the company's own website shows: the variants published to the
/// website channel, with that channel's content, price and available
/// quantity. A read-only projection served by this backend to signed-in
/// users; a storefront never reads the database or sees a marketplace credential.
/// </summary>
[ApiController]
[Route("api/storefront")]
[Authorize(Policy = Roles.Employee)]
public class StorefrontController(TenantDbContext db, ListingService listings) : ControllerBase
{
    [HttpGet("products")]
    public async Task<IActionResult> Products(CancellationToken cancellationToken)
    {
        var ids = await (
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            join account in db.ChannelAccounts.AsNoTracking() on market.ChannelAccountId equals account.Id
            where account.Channel == SalesChannel.Website && account.IsEnabled
                && listing.DesiredState == ListingDesiredState.Active && listing.ObservedStatus == ListingObservedStatus.Live
            orderby listing.SellerSku
            select new { listing.Id, listing.VariantId }).Take(500).ToListAsync(cancellationToken);

        var variantIds = ids.Select(i => i.VariantId).ToList();
        var products = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.ProductId, cancellationToken);
        var responses = new List<StorefrontProductResponse>();
        foreach (var item in ids)
        {
            if (await listings.LoadAsync(item.Id, cancellationToken) is { } bundle)
            {
                var s = bundle.Work.Snapshot;
                responses.Add(new StorefrontProductResponse(
                    s.SellerSku, products[item.VariantId], item.VariantId, s.Title, s.Description, s.Brand, s.Price, s.Currency, s.Quantity, s.Options, s.ImageUrls));
            }
        }
        return Ok(responses);
    }
}
