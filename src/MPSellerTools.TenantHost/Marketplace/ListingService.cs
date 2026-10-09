using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.TenantHost.Marketplace;

/// <summary>A listing with everything around it that an operation needs, read in one go.</summary>
/// <summary>One of the product's pictures that this listing could use.</summary>
public record ListingImage(Guid Id, string Url);

public record ListingBundle(ChannelListing Listing, ChannelContext Context, ListingWork Work)
{
    /// <summary>Every picture of the product and of this variant, whether or not the listing uses it.</summary>
    public IReadOnlyList<ListingImage> AvailableImages { get; init; } = [];

    /// <summary>The pictures chosen for this marketplace, in order; null when the listing follows the product.</summary>
    public IReadOnlyList<Guid>? ImageSelection { get; init; }

    public Guid ProductId { get; init; }

    /// <summary>The product's own category, as the company names it; null when it has none.</summary>
    public string? ProductCategory { get; init; }
}

/// <summary>
/// Reads a channel listing together with its product, variant, stock, images
/// and group, and resolves them into what would be sent. Reading only: it
/// changes nothing and calls no channel.
/// </summary>
public class ListingService(TenantDbContext db, IEnumerable<IChannelAdapter> adapters)
{
    public IChannelAdapter AdapterFor(ChannelAccount account) =>
        adapters.FirstOrDefault(a => a.Channel == account.Channel)
        ?? throw new InvalidOperationException($"No adapter is registered for {account.Channel}.");

    public static ChannelContext ContextFor(ChannelAccount account, ChannelMarket market)
    {
        using var settings = JsonDocument.Parse(string.IsNullOrWhiteSpace(account.SettingsJson) ? "{}" : account.SettingsJson);
        return new ChannelContext(account, market, settings.RootElement.Clone());
    }

    public async Task<ListingBundle?> LoadAsync(Guid listingId, CancellationToken cancellationToken) =>
        (await LoadManyAsync([listingId], cancellationToken)).FirstOrDefault();

    /// <summary>
    /// Reads many listings with a fixed number of queries, however many there
    /// are, in the order asked for. One that no longer exists is left out.
    /// </summary>
    public async Task<List<ListingBundle>> LoadManyAsync(IReadOnlyList<Guid> listingIds, CancellationToken cancellationToken)
    {
        var bundles = new List<ListingBundle>(listingIds.Count);
        foreach (var chunk in listingIds.Chunk(500))
        {
            bundles.AddRange(await LoadChunkAsync(chunk, cancellationToken));
        }
        return bundles;
    }

    // Each list of ids goes to the database as one parameter (EF.Parameter) rather than one per id:
    // with hundreds of separate parameters SQL Server took seconds over every one of these queries.
    private async Task<List<ListingBundle>> LoadChunkAsync(Guid[] listingIds, CancellationToken cancellationToken)
    {
        var listings = await db.ChannelListings.AsNoTracking().Where(l => EF.Parameter(listingIds).Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        if (listings.Count == 0)
        {
            return [];
        }

        var marketIds = listings.Values.Select(l => l.ChannelMarketId).Distinct().ToList();
        var markets = await db.ChannelMarkets.AsNoTracking().Where(m => EF.Parameter(marketIds).Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        var accountIds = markets.Values.Select(m => m.ChannelAccountId).Distinct().ToList();
        var accounts = await db.ChannelAccounts.AsNoTracking().Where(a => EF.Parameter(accountIds).Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        var variantIds = listings.Values.Select(l => l.VariantId).Distinct().ToList();
        var variants = await db.ProductVariants.AsNoTracking().Where(v => EF.Parameter(variantIds).Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
        var productIds = variants.Values.Select(v => v.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => EF.Parameter(productIds).Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        // Matched without regard to case, as the database itself compares a category's name.
        var mappings = (await db.CategoryMappings.AsNoTracking().Where(c => EF.Parameter(marketIds).Contains(c.ChannelMarketId)).ToListAsync(cancellationToken))
            .GroupBy(c => c.ChannelMarketId)
            .ToDictionary(g => g.Key, g => g.GroupBy(c => c.InternalCategory, StringComparer.OrdinalIgnoreCase).ToDictionary(c => c.Key, c => c.First(), StringComparer.OrdinalIgnoreCase));
        var balances = (await db.InventoryBalances.AsNoTracking()
                .Where(b => EF.Parameter(variantIds).Contains(b.VariantId) && b.LocationId == InventoryLocation.DefaultId).ToListAsync(cancellationToken))
            .GroupBy(b => b.VariantId).ToDictionary(g => g.Key, g => g.First());
        // Two queries rather than one "either" condition, which SQL Server plans badly over two long lists.
        var productIdentifiers = (await db.ProductIdentifiers.AsNoTracking()
                .Where(i => i.ProductId != null && EF.Parameter(productIds).Contains(i.ProductId.Value)).ToListAsync(cancellationToken))
            .ToLookup(i => i.ProductId!.Value);
        var variantIdentifiers = (await db.ProductIdentifiers.AsNoTracking()
                .Where(i => i.VariantId != null && EF.Parameter(variantIds).Contains(i.VariantId.Value)).ToListAsync(cancellationToken))
            .ToLookup(i => i.VariantId!.Value);
        var media = (await (
            from picture in db.ProductMedia.AsNoTracking()
            join asset in db.MediaAssets.AsNoTracking() on picture.MediaAssetId equals asset.Id
            where EF.Parameter(productIds).Contains(picture.ProductId)
            orderby picture.Purpose, picture.Position
            select new { picture.ProductId, picture.VariantId, Image = new ListingImage(picture.Id, asset.Url) }).ToListAsync(cancellationToken))
            .ToLookup(m => m.ProductId);
        var references = (await db.ExternalReferences.AsNoTracking()
                .Where(r => r.OwnerType == ExternalOwnerType.ChannelListing && EF.Parameter(listingIds).Contains(r.OwnerId)).ToListAsync(cancellationToken))
            .ToLookup(r => r.OwnerId);
        // Few listings belong to a variation group, so those are read one by one.
        var grouped = (await db.ListingGroupMembers.AsNoTracking()
            .Where(m => EF.Parameter(listingIds).Contains(m.ChannelListingId)).Select(m => m.ChannelListingId).ToListAsync(cancellationToken)).ToHashSet();

        var bundles = new List<ListingBundle>(listings.Count);
        foreach (var listingId in listingIds)
        {
            if (!listings.TryGetValue(listingId, out var listing))
            {
                continue;
            }

            var market = markets[listing.ChannelMarketId];
            var account = accounts[market.ChannelAccountId];
            var variant = variants[listing.VariantId];
            var product = products[variant.ProductId];
            var mapping = product.Category is not null && mappings.TryGetValue(market.Id, out var ofMarket) ? ofMarket.GetValueOrDefault(product.Category) : null;
            var available = media[product.Id].Where(m => m.VariantId == null || m.VariantId == variant.Id).Select(m => m.Image).ToList();
            // A chosen picture that has since been removed from the product simply drops out.
            var selection = ParseSelection(listing.ImageSelectionJson);
            var images = selection is null
                ? available.Select(i => i.Url).ToList()
                : selection.Select(id => available.FirstOrDefault(i => i.Id == id)?.Url).OfType<string>().ToList();
            var ownReferences = references[listing.Id].ToDictionary(r => r.ResourceType, r => r.Value);
            var ownIdentifiers = productIdentifiers[product.Id].Concat(variantIdentifiers[variant.Id]).DistinctBy(i => i.Id).ToList();

            var (group, groupReady) = grouped.Contains(listing.Id) ? await LoadGroupAsync(listing.Id, cancellationToken) : (null, true);
            var snapshot = ListingComposer.Compose(
                product, variant, listing, market, account.Channel, mapping, balances.GetValueOrDefault(variant.Id)?.AvailableToSell ?? 0, ownIdentifiers, images, group,
                ownReferences.GetValueOrDefault(ExternalResourceType.CatalogItem));
            var work = new ListingWork(
                snapshot, listing.DesiredState, listing.ContentVersion, listing.PriceVersion, listing.InventoryVersion, ownReferences, groupReady);
            bundles.Add(new ListingBundle(listing, ContextFor(account, market), work) { AvailableImages = available, ImageSelection = selection, ProductId = product.Id, ProductCategory = product.Category });
        }
        return bundles;
    }

    public static List<Guid>? ParseSelection(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<List<Guid>>(json);

    /// <summary>Everything that stops the listing being submitted: the checks common to all channels, then the channel's own.</summary>
    public List<ValidationIssue> Validate(ListingBundle bundle)
    {
        var issues = ListingValidator.Common(bundle.Work.Snapshot);
        issues.AddRange(AdapterFor(bundle.Context.Account).Validate(bundle.Work, bundle.Context));

        // Too many is said here rather than left to the channel, which would drop or refuse the extra ones.
        var channel = bundle.Context.Account.Channel;
        var rules = ChannelImageRules.For(channel);
        if (bundle.Work.Snapshot.ImageUrls.Count > rules.MaxImages)
        {
            issues.Add(new ValidationIssue(
                channel, "images", "too_many",
                $"{channel} takes up to {rules.MaxImages} pictures and this listing has {bundle.Work.Snapshot.ImageUrls.Count}. Choose which ones to send."));
        }
        return issues;
    }

    /// <summary>
    /// Checks again the account's listings that were held back by something
    /// about the account itself (a missing setting, say), after the account
    /// changed, so they do not go on showing a problem that is gone. Issues
    /// of any other kind are the listing's own and are left for its next check.
    /// Returns how many listings' recorded issues changed.
    /// </summary>
    public async Task<int> RecheckAccountIssuesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var ids = await (
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            where market.ChannelAccountId == accountId && listing.IssuesJson != null && listing.IssuesJson.Contains("\"path\":\"account.")
            select listing.Id).ToListAsync(cancellationToken);

        // Most listings come out the same, so they are written together, one statement for each distinct result.
        var byResult = new Dictionary<string, List<Guid>>();
        foreach (var bundle in await LoadManyAsync(ids, cancellationToken))
        {
            var issues = Validate(bundle);
            var json = issues.Count == 0 ? "" : JsonSerializer.Serialize(issues, IssuesJson);
            if (json != (bundle.Listing.IssuesJson ?? ""))
            {
                (byResult.TryGetValue(json, out var same) ? same : byResult[json] = []).Add(bundle.Listing.Id);
            }
        }

        foreach (var (json, listingIds) in byResult)
        {
            var stored = json.Length == 0 ? null : json;
            foreach (var chunk in listingIds.Chunk(1000))
            {
                await db.ChannelListings.Where(l => EF.Parameter(chunk).Contains(l.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.IssuesJson, stored), cancellationToken);
            }
        }
        return byResult.Values.Sum(list => list.Count);
    }

    private static readonly JsonSerializerOptions IssuesJson = new(JsonSerializerDefaults.Web);

    private async Task<(ListingGroupSnapshot? Group, bool Ready)> LoadGroupAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var membership = await db.ListingGroupMembers.AsNoTracking().FirstOrDefaultAsync(m => m.ChannelListingId == listingId, cancellationToken);
        if (membership is null)
        {
            return (null, true);
        }

        var family = await db.ListingGroups.AsNoTracking().FirstAsync(g => g.Id == membership.ListingGroupId, cancellationToken);
        var members = await (
            from member in db.ListingGroupMembers.AsNoTracking()
            join listing in db.ChannelListings.AsNoTracking() on member.ChannelListingId equals listing.Id
            join variant in db.ProductVariants.AsNoTracking() on listing.VariantId equals variant.Id
            where member.ListingGroupId == family.Id
            orderby listing.SellerSku
            select new { listing.Id, listing.SellerSku, variant.OptionsJson }).ToListAsync(cancellationToken);

        var names = string.IsNullOrWhiteSpace(family.VariationAttributesJson)
            ? []
            : JsonSerializer.Deserialize<List<string>>(family.VariationAttributesJson) ?? [];
        var options = members.Select(m => ListingComposer.ParseMap(m.OptionsJson)).ToList();
        var values = names.ToDictionary(
            name => name,
            name => (IReadOnlyList<string>)options.Select(o => o.GetValueOrDefault(name)).OfType<string>().Distinct().ToList());

        // The group can be published once every other member has been created on the channel.
        var others = members.Where(m => m.Id != listingId).Select(m => m.Id).ToList();
        var created = await db.ExternalReferences.AsNoTracking().CountAsync(
            r => r.OwnerType == ExternalOwnerType.ChannelListing && r.ResourceType == ExternalResourceType.Offer && others.Contains(r.OwnerId),
            cancellationToken);
        return (new ListingGroupSnapshot(family.GroupKey, names, members.Select(m => m.SellerSku).ToList(), values), created == others.Count);
    }
}
