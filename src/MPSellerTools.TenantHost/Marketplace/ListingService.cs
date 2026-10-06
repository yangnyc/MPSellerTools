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

    public async Task<ListingBundle?> LoadAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await db.ChannelListings.AsNoTracking().FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken);
        if (listing is null)
        {
            return null;
        }

        var market = await db.ChannelMarkets.AsNoTracking().FirstAsync(m => m.Id == listing.ChannelMarketId, cancellationToken);
        var account = await db.ChannelAccounts.AsNoTracking().FirstAsync(a => a.Id == market.ChannelAccountId, cancellationToken);
        var variant = await db.ProductVariants.AsNoTracking().FirstAsync(v => v.Id == listing.VariantId, cancellationToken);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == variant.ProductId, cancellationToken);
        var mapping = product.Category is null
            ? null
            : await db.CategoryMappings.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ChannelMarketId == market.Id && c.InternalCategory == product.Category, cancellationToken);
        var balance = await db.InventoryBalances.AsNoTracking()
            .FirstOrDefaultAsync(b => b.VariantId == variant.Id && b.LocationId == InventoryLocation.DefaultId, cancellationToken);
        var identifiers = await db.ProductIdentifiers.AsNoTracking()
            .Where(i => i.ProductId == product.Id || i.VariantId == variant.Id)
            .ToListAsync(cancellationToken);
        var available = await (
            from media in db.ProductMedia.AsNoTracking()
            join asset in db.MediaAssets.AsNoTracking() on media.MediaAssetId equals asset.Id
            where media.ProductId == product.Id && (media.VariantId == null || media.VariantId == variant.Id)
            orderby media.Purpose, media.Position
            select new ListingImage(media.Id, asset.Url)).ToListAsync(cancellationToken);
        // A chosen picture that has since been removed from the product simply drops out.
        var selection = ParseSelection(listing.ImageSelectionJson);
        var images = selection is null
            ? available.Select(i => i.Url).ToList()
            : selection.Select(id => available.FirstOrDefault(i => i.Id == id)?.Url).OfType<string>().ToList();
        var references = await db.ExternalReferences.AsNoTracking()
            .Where(r => r.OwnerType == ExternalOwnerType.ChannelListing && r.OwnerId == listing.Id)
            .ToDictionaryAsync(r => r.ResourceType, r => r.Value, cancellationToken);

        var (group, groupReady) = await LoadGroupAsync(listing.Id, cancellationToken);
        var snapshot = ListingComposer.Compose(
            product, variant, listing, market, account.Channel, mapping, balance?.AvailableToSell ?? 0, identifiers, images, group,
            references.GetValueOrDefault(ExternalResourceType.CatalogItem));
        var work = new ListingWork(
            snapshot, listing.DesiredState, listing.ContentVersion, listing.PriceVersion, listing.InventoryVersion, references, groupReady);
        return new ListingBundle(listing, ContextFor(account, market), work) { AvailableImages = available, ImageSelection = selection };
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
