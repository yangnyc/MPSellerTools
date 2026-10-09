using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>One of the company's own product categories, and where it goes in the store.</summary>
public record MagentoCategoryRow(
    string Category,
    int Products,
    Guid? MappingId,
    string? StoreCategoryId,
    // The store category's path, when the store still has it.
    string? StoreCategoryPath,
    // Mapped to a number the store no longer has.
    bool StoreCategoryMissing);

public record MagentoCategoriesResponse(
    bool StoreReachable,
    string? StoreError,
    IReadOnlyList<MagentoStoreCategory> StoreCategories,
    IReadOnlyList<MagentoCategoryRow> Categories,
    // Where a product goes whose own category is not mapped; null for nowhere.
    string? DefaultCategoryId,
    // Whether anything may be created in the store from here.
    bool LiveWrites);

public record CreateMagentoCategoryRequest(string Name, long? ParentId, bool IsActive = true, bool IncludeInMenu = true);

/// <summary><see cref="DryRun"/> only reports what would be created and mapped.</summary>
public record CreateMissingMagentoCategoriesRequest(bool IsActive = true, bool IncludeInMenu = true, bool DryRun = false);

public record MagentoCategoryOutcome(string Category, string? StoreCategoryId, string StoreCategoryPath, int Created, string? Error);

public record MagentoCategoryBulkResponse(bool DryRun, int Mapped, int Created, IReadOnlyList<MagentoCategoryOutcome> Items);

public record SetMagentoDefaultCategoryRequest(string? CategoryId);

/// <summary>
/// The company's product categories beside the Magento store's own: which
/// goes where, creating the ones the store lacks, and where a product with
/// no category of its own ends up. Reading needs only the connection;
/// creating anything in the store needs live writes on for the account.
/// TenantAdmin only.
/// </summary>
[ApiController]
[Route("api/magento/categories")]
[Authorize(Policy = Roles.TenantAdmin)]
public class MagentoCategoriesController(TenantDbContext db, MagentoSync sync, IOptions<MarketplaceOptions> options, AuditLogger audit) : ControllerBase
{
    private const string Separator = MagentoSync.CategorySeparator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var (account, market) = await AccountAsync(cancellationToken);
        if (account is null || market is null)
        {
            return Problem("Add Magento as a sales channel first.", statusCode: StatusCodes.Status400BadRequest);
        }

        // The page is still useful when the store cannot be reached: the mappings are the company's own.
        List<MagentoStoreCategory> store = [];
        string? storeError = null;
        try
        {
            store = await sync.GetCategoriesAsync(account, cancellationToken);
        }
        catch (ChannelException ex)
        {
            storeError = ex.Message;
        }

        var byId = store.ToDictionary(c => c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var counts = await db.Products.AsNoTracking()
            .Where(p => !p.IsArchived && p.Category != null && p.Category != "")
            .GroupBy(p => p.Category!).Select(g => new { Category = g.Key, Products = g.Count() }).ToListAsync(cancellationToken);
        var mappings = await db.CategoryMappings.AsNoTracking().Where(m => m.ChannelMarketId == market.Id).ToListAsync(cancellationToken);

        // Every category a product has, and every one that is mapped though no product has it any more.
        var names = counts.Select(c => c.Category).Concat(mappings.Select(m => m.InternalCategory))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        var rows = names.Select(name =>
        {
            var mapping = mappings.FirstOrDefault(m => string.Equals(m.InternalCategory, name, StringComparison.OrdinalIgnoreCase));
            var inStore = mapping is not null && byId.TryGetValue(mapping.ExternalCategoryId, out var found) ? found : null;
            return new MagentoCategoryRow(
                name,
                counts.FirstOrDefault(c => string.Equals(c.Category, name, StringComparison.OrdinalIgnoreCase))?.Products ?? 0,
                mapping?.Id, mapping?.ExternalCategoryId, inStore?.Path,
                StoreCategoryMissing: mapping is not null && storeError is null && inStore is null);
        }).ToList();

        return Ok(new MagentoCategoriesResponse(
            storeError is null, storeError, store, rows, MagentoApi.Setting(account, "defaultCategoryId"), LiveWrites(account)));
    }

    /// <summary>Creates one category in the store.</summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateMagentoCategoryRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 255)
        {
            return Problem("A category needs a name of up to 255 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await InStoreAsync(async account =>
        {
            var store = await sync.GetCategoriesAsync(account, cancellationToken);
            var parent = request.ParentId is { } wanted ? store.FirstOrDefault(c => c.Id == wanted) : store.FirstOrDefault(c => c.Level == 1);
            if (parent is null)
            {
                return Problem("The store has no such category to put it under.", statusCode: StatusCodes.Status400BadRequest);
            }

            var id = await sync.CreateCategoryAsync(account, name, parent.Id, request.IsActive, request.IncludeInMenu, cancellationToken);
            audit.Log("MagentoCategoryCreated", $"name={name}; id={id}; parent={parent.Id}");
            await db.SaveChangesAsync(cancellationToken);
            var path = parent.Level <= 1 ? name : $"{parent.Path}{Separator}{name}";
            return Ok(new MagentoStoreCategory(id, parent.Id, name, path, parent.Level + 1, request.IsActive, 0));
        }, writes: true, cancellationToken);
    }

    /// <summary>
    /// Creates in the store every category the company's products have and
    /// the store does not, a category inside another ("A / B") as a
    /// category inside a category, and maps each to it. One the store
    /// already has under the same name is mapped without being created again.
    /// </summary>
    [HttpPost("create-missing")]
    public Task<IActionResult> CreateMissing([FromBody] CreateMissingMagentoCategoriesRequest request, CancellationToken cancellationToken) =>
        InStoreAsync(async account =>
        {
            var market = await db.ChannelMarkets.FirstAsync(m => m.ChannelAccountId == account.Id, cancellationToken);
            var store = await sync.GetCategoriesAsync(account, cancellationToken);
            var root = store.FirstOrDefault(c => c.Level == 1);
            if (root is null)
            {
                return Problem("The store has no root category to create under.", statusCode: StatusCodes.Status502BadGateway);
            }

            // Everything below the root, by its path. A path the store has twice goes to the first.
            var byPath = store.Where(c => c.Level >= 2).GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            var mappings = await db.CategoryMappings.Where(m => m.ChannelMarketId == market.Id).ToListAsync(cancellationToken);
            var unmapped = (await db.Products.AsNoTracking().Where(p => !p.IsArchived && p.Category != null && p.Category != "")
                    .Select(p => p.Category!).Distinct().ToListAsync(cancellationToken))
                .Where(name => !mappings.Any(m => string.Equals(m.InternalCategory, name, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

            var items = new List<MagentoCategoryOutcome>();
            // In a dry run nothing gets a number, so the ones that would be created are told apart by a negative one.
            long pretend = -1;
            foreach (var category in unmapped)
            {
                var segments = category.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var (parentId, path, created) = (root.Id, "", 0);
                try
                {
                    foreach (var segment in segments)
                    {
                        path = path.Length == 0 ? segment : $"{path}{Separator}{segment}";
                        if (!byPath.TryGetValue(path, out var id))
                        {
                            var name = segment.Length > 255 ? segment[..255] : segment;
                            id = request.DryRun ? pretend-- : await sync.CreateCategoryAsync(account, name, parentId, request.IsActive, request.IncludeInMenu, cancellationToken);
                            byPath[path] = id;
                            created++;
                        }
                        parentId = id;
                    }

                    var storeId = parentId > 0 ? parentId.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                    if (!request.DryRun)
                    {
                        db.CategoryMappings.Add(new CategoryMapping { Id = Guid.NewGuid(), ChannelMarketId = market.Id, InternalCategory = category, ExternalCategoryId = storeId! });
                    }
                    items.Add(new MagentoCategoryOutcome(category, storeId, path, created, null));
                }
                catch (ChannelException ex)
                {
                    // One the store turns down does not stop the others; what was created for it stays created.
                    items.Add(new MagentoCategoryOutcome(category, null, path, created, ex.Message));
                }
            }

            if (!request.DryRun)
            {
                audit.Log("MagentoCategoriesCreated", $"mapped={items.Count(i => i.Error is null)}; created={items.Sum(i => i.Created)}");
                await db.SaveChangesAsync(cancellationToken);
            }
            return Ok(new MagentoCategoryBulkResponse(request.DryRun, items.Count(i => i.Error is null), items.Sum(i => i.Created), items));
        }, writes: !request.DryRun, cancellationToken);

    /// <summary>
    /// Maps every unmapped category of the company to the store category of
    /// the same path, or failing that the one category of the same name.
    /// Nothing is created or changed in the store.
    /// </summary>
    [HttpPost("match")]
    public Task<IActionResult> Match(CancellationToken cancellationToken) =>
        InStoreAsync(async account =>
        {
            var market = await db.ChannelMarkets.FirstAsync(m => m.ChannelAccountId == account.Id, cancellationToken);
            var store = (await sync.GetCategoriesAsync(account, cancellationToken)).Where(c => c.Level >= 2).ToList();
            var mappings = await db.CategoryMappings.Where(m => m.ChannelMarketId == market.Id).ToListAsync(cancellationToken);
            var unmapped = (await db.Products.AsNoTracking().Where(p => !p.IsArchived && p.Category != null && p.Category != "")
                    .Select(p => p.Category!).Distinct().ToListAsync(cancellationToken))
                .Where(name => !mappings.Any(m => string.Equals(m.InternalCategory, name, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

            var items = new List<MagentoCategoryOutcome>();
            foreach (var category in unmapped)
            {
                var last = category.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? category;
                var byPath = store.Where(c => string.Equals(c.Path, category, StringComparison.OrdinalIgnoreCase)).ToList();
                var byName = store.Where(c => string.Equals(c.Name, last, StringComparison.OrdinalIgnoreCase)).ToList();
                // A name the store has more than once is not guessed at.
                var match = byPath.Count > 0 ? byPath[0] : byName.Count == 1 ? byName[0] : null;
                if (match is null)
                {
                    continue;
                }

                var storeId = match.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                db.CategoryMappings.Add(new CategoryMapping { Id = Guid.NewGuid(), ChannelMarketId = market.Id, InternalCategory = category, ExternalCategoryId = storeId });
                items.Add(new MagentoCategoryOutcome(category, storeId, match.Path, 0, null));
            }

            audit.Log("MagentoCategoriesMatched", $"mapped={items.Count}");
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new MagentoCategoryBulkResponse(false, items.Count, 0, items));
        }, writes: false, cancellationToken);

    /// <summary>Sets where a product goes whose own category is not mapped; null for nowhere.</summary>
    [HttpPut("default")]
    public async Task<IActionResult> SetDefault([FromBody] SetMagentoDefaultCategoryRequest request, CancellationToken cancellationToken)
    {
        var id = request.CategoryId?.Trim();
        if (!string.IsNullOrEmpty(id) && !long.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return Problem("A Magento category is its number.", statusCode: StatusCodes.Status400BadRequest);
        }

        var account = await db.ChannelAccounts.OrderBy(a => a.CreatedAtUtc).FirstOrDefaultAsync(a => a.Channel == SalesChannel.Magento, cancellationToken);
        if (account is null)
        {
            return Problem("Add Magento as a sales channel first.", statusCode: StatusCodes.Status400BadRequest);
        }

        // The other settings of the account are kept as they are.
        var settings = string.IsNullOrWhiteSpace(account.SettingsJson) ? new JsonObject() : JsonNode.Parse(account.SettingsJson)!.AsObject();
        if (string.IsNullOrEmpty(id))
        {
            settings.Remove("defaultCategoryId");
        }
        else
        {
            settings["defaultCategoryId"] = id;
        }
        account.SettingsJson = settings.Count == 0 ? null : settings.ToJsonString();
        account.UpdatedAtUtc = DateTime.UtcNow;
        audit.Log("MagentoDefaultCategorySet", $"category={id ?? "none"}");
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Removes the mappings of categories no product has any more. The store is not touched.</summary>
    [HttpPost("remove-unused")]
    public async Task<IActionResult> RemoveUnused(CancellationToken cancellationToken)
    {
        var (account, market) = await AccountAsync(cancellationToken);
        if (account is null || market is null)
        {
            return Problem("Add Magento as a sales channel first.", statusCode: StatusCodes.Status400BadRequest);
        }

        var used = (await db.Products.AsNoTracking().Where(p => !p.IsArchived && p.Category != null && p.Category != "")
            .Select(p => p.Category!).Distinct().ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unused = (await db.CategoryMappings.Where(m => m.ChannelMarketId == market.Id).ToListAsync(cancellationToken))
            .Where(m => !used.Contains(m.InternalCategory)).ToList();
        db.CategoryMappings.RemoveRange(unused);
        audit.Log("MagentoCategoryMappingsRemoved", $"removed={unused.Count}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { removed = unused.Count });
    }

    private bool LiveWrites(ChannelAccount account) => account.LiveWritesEnabled && options.Value.LiveWritesEnabled;

    private async Task<(ChannelAccount? Account, ChannelMarket? Market)> AccountAsync(CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.AsNoTracking().OrderBy(a => a.CreatedAtUtc).FirstOrDefaultAsync(a => a.Channel == SalesChannel.Magento, cancellationToken);
        var market = account is null ? null : await db.ChannelMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.ChannelAccountId == account.Id, cancellationToken);
        return (account, market);
    }

    /// <summary>Runs work that talks to the store, answering its refusals as they are; work that writes there needs live writes on.</summary>
    private async Task<IActionResult> InStoreAsync(Func<ChannelAccount, Task<IActionResult>> work, bool writes, CancellationToken cancellationToken)
    {
        var (account, market) = await AccountAsync(cancellationToken);
        if (account is null || market is null)
        {
            return Problem("Add Magento as a sales channel first.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (writes && !LiveWrites(account))
        {
            return Problem("Live writes are off for Magento, so nothing is created in the store. Switch them on on the Connection page.", statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            return await work(account);
        }
        catch (ChannelException ex)
        {
            db.ChangeTracker.Clear();
            return Problem(ex.Message, statusCode: ex.HttpStatus is null && !ex.Ambiguous ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway);
        }
    }
}
