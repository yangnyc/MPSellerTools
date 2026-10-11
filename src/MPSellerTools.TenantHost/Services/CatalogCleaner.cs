using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.TenantHost.Services;

/// <summary>What the catalog still lacks, counted over the products that are not archived.</summary>
public record CatalogHealth(
    int Products, int Archived, int NoPicture, int NoDescription, int NoPrice, int NoCategory, int NoBrand, int OutOfStock, int NotListed,
    int DuplicateGroups, int DuplicateProducts, int RetiredAsDuplicates);

/// <summary>One product of a group of duplicates. <see cref="Score"/> says how complete and established it is; <see cref="ScoreParts"/> why.</summary>
public record DuplicateProduct(
    Guid Id, string Sku, string Name, string? Brand, string? Category, decimal Price, int Stock, int Pictures, int Listings, int Orders, int Score,
    IReadOnlyDictionary<string, int> ScoreParts);

/// <summary>Products that look like the same item listed more than once. <see cref="KeepId"/> is the one suggested to keep.</summary>
public record DuplicateGroup(string Key, string Reason, int Similarity, Guid KeepId, IReadOnlyList<DuplicateProduct> Products);

public record CatalogScan(CatalogHealth Health, IReadOnlyList<DuplicateGroup> Groups, bool CanUndo, DateTime? LastCleanAtUtc, int LastCleanRetired);

public record CleanGroupChoice(string Key, Guid KeepId, IReadOnlyList<Guid> RetireIds);

public record CleanResult(int Retired, int Skipped);

/// <summary>
/// Finds products that are the same item entered more than once, and
/// retires all but the best of each. Nothing is deleted: a retired product
/// is archived, as from the Products page, and the last clean can be undone.
/// </summary>
public partial class CatalogCleaner(TenantDbContext db, AuditLogger audit)
{
    public static readonly string[] AllStrategies = ["exact", "sku", "normalized", "fuzzy"];

    /// <summary>Different spellings of the same word in product names.</summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["tablets"] = "tab", ["tablet"] = "tab", ["tabs"] = "tab", ["caplets"] = "caplet", ["capsules"] = "cap", ["capsule"] = "cap", ["caps"] = "cap",
        ["softgels"] = "softgel", ["gelcaps"] = "gelcap", ["count"] = "ct", ["ounce"] = "oz", ["ounces"] = "oz", ["milligrams"] = "mg",
        ["lozenges"] = "lozenge", ["patches"] = "patch", ["packs"] = "pack", ["pk"] = "pack", ["strength"] = "str", ["childrens"] = "children", ["kids"] = "children",
    };

    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal) { "by", "the", "and", "with", "for", "of", "a", "an", "in", "each", "generic", "compare", "to" };

    [GeneratedRegex(@"(\d)\s*(mg|mcg|ml|oz|g|iu|ct)\b")]
    private static partial Regex UnitAfterNumber();

    [GeneratedRegex(@"[’']s\b|[’']")]
    private static partial Regex Apostrophes();

    [GeneratedRegex(@"[^a-z0-9.]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@" - ([^-]{1,30})$")]
    private static partial Regex TrailingVariant();

    [GeneratedRegex(@"^[A-Z]{2,4}-\d{10,}")]
    private static partial Regex ImporterSku();

    private sealed record Candidate(
        Guid Id, string Sku, string Name, string? Brand, string? Category, decimal Price, int Stock, int DescriptionLength, int Pictures, int Listings, int Orders,
        string[] Tokens, string Numbers);

    /// <summary>A product's name as a sorted set of normalised words: "Tylenol 325mg Tablets, 100 Count" and "tylenol 325 mg tabs 100ct" come out the same.</summary>
    public static string[] TokensOf(string name)
    {
        var text = name.ToLowerInvariant();
        text = UnitAfterNumber().Replace(text, "$1 $2");
        text = Apostrophes().Replace(text, "");
        var tokens = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in NotWord().Split(text))
        {
            var word = raw.Trim('.');
            word = Synonyms.GetValueOrDefault(word, word);
            if (word.Length > 0 && !Noise.Contains(word))
            {
                tokens.Add(word);
            }
        }
        // "Name - 3 Pack" and "Name - 1 Pack" hold nearly the same words but are different pack sizes: what
        // follows the last " - " counts as a word of its own.
        if (TrailingVariant().Match(text) is { Success: true } variant)
        {
            tokens.Add("variant:" + Regex.Replace(variant.Groups[1].Value, "[^a-z0-9]+", ""));
        }
        return [.. tokens];
    }

    /// <summary>How complete and established a product is; the highest in a group is the one to keep.</summary>
    private static (int Score, Dictionary<string, int> Parts) ScoreOf(Candidate p)
    {
        var parts = new Dictionary<string, int>
        {
            ["Sold before"] = p.Orders > 0 ? 25 : 0,
            ["Listed on a sales channel"] = Math.Min(15, p.Listings * 5),
            ["In stock"] = p.Stock > 0 ? 5 : 0,
            ["Pictures"] = Math.Min(12, p.Pictures * 3),
            ["Description"] = Math.Min(12, p.DescriptionLength / 150),
            ["Brand"] = string.IsNullOrWhiteSpace(p.Brand) ? 0 : 5,
            ["Category"] = string.IsNullOrWhiteSpace(p.Category) ? 0 : 5,
            ["Has a price"] = p.Price > 0 ? 5 : 0,
        };
        return (parts.Values.Sum(), parts.Where(part => part.Value > 0).ToDictionary(part => part.Key, part => part.Value));
    }

    private async Task<List<Candidate>> LoadAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking().Where(p => !p.IsArchived)
            .Select(p => new { p.Id, p.Sku, p.Name, p.Brand, p.Category, p.Price, p.StockQuantity, DescriptionLength = p.Description == null ? 0 : p.Description.Length })
            .ToListAsync(cancellationToken);
        var pictures = (await db.ProductMedia.AsNoTracking().GroupBy(m => m.ProductId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(g => g.Key, g => g.Count);
        var orders = (await db.OrderItems.AsNoTracking().GroupBy(i => i.ProductId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .ToDictionary(g => g.Key, g => g.Count);
        var listings = (await (
            from listing in db.ChannelListings.AsNoTracking()
            join variant in db.ProductVariants.AsNoTracking() on listing.VariantId equals variant.Id
            group listing by variant.ProductId into g
            select new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).ToDictionary(g => g.Key, g => g.Count);

        return products.Select(p =>
        {
            var tokens = TokensOf(p.Name);
            return new Candidate(
                p.Id, p.Sku, p.Name, p.Brand, p.Category, p.Price, p.StockQuantity, p.DescriptionLength,
                pictures.GetValueOrDefault(p.Id), listings.GetValueOrDefault(p.Id), orders.GetValueOrDefault(p.Id),
                // The numbers in a name (strength, size, count) have to agree before two names are compared at all.
                tokens, string.Join(' ', tokens.Where(t => t.Any(char.IsAsciiDigit))));
        }).ToList();
    }

    /// <summary>
    /// The catalog's health figures and its groups of duplicates, found by the chosen ways: "exact" (the same
    /// name), "sku" (the same manufacturer code written two ways), "normalized" (the same words in any order
    /// and spelling) and "fuzzy" (names sharing at least <paramref name="threshold"/> of their words).
    /// </summary>
    public async Task<CatalogScan> ScanAsync(IReadOnlyCollection<string> strategies, double threshold, CancellationToken cancellationToken)
    {
        var products = await LoadAsync(cancellationToken);
        var byId = products.ToDictionary(p => p.Id);
        var links = new Dictionary<(Guid, Guid), (string Reason, double Similarity)>();

        void Link(Candidate a, Candidate b, string reason, double similarity)
        {
            // The same item is not sold at well over one and a half times the price: a gap like that means a
            // case against a single piece, or another size, however alike the names are.
            var (low, high) = (Math.Min(a.Price, b.Price), Math.Max(a.Price, b.Price));
            if (low > 0 && high / low > 1.6m)
            {
                return;
            }
            var key = a.Id.CompareTo(b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
            if (!links.TryGetValue(key, out var known) || known.Similarity < similarity)
            {
                links[key] = (reason, similarity);
            }
        }

        void LinkByKey(Func<Candidate, string> keyOf, string reason)
        {
            foreach (var bucket in products.Select(p => (Key: keyOf(p), Product: p)).Where(p => p.Key.Length > 0).GroupBy(p => p.Key, p => p.Product).Where(g => g.Count() > 1))
            {
                // Every pair, not each against the first: one of them may be a case of the others, which its
                // price rules out, and the rest are still each other's duplicates. A very large bucket is
                // linked neighbour to neighbour by price instead.
                var same = bucket.OrderBy(p => p.Price).ToList();
                for (var i = 0; i < same.Count; i++)
                {
                    for (var j = i + 1; j < (same.Count <= 40 ? same.Count : Math.Min(same.Count, i + 2)); j++)
                    {
                        Link(same[i], same[j], reason, 1);
                    }
                }
            }
        }

        if (strategies.Contains("exact"))
        {
            LinkByKey(p => p.Name.Trim().ToLowerInvariant(), "Same name");
        }
        if (strategies.Contains("sku"))
        {
            // The same manufacturer code written two ways ("0904-7280-80" and "00904-7280-80"). Letters stay
            // in: they tell colours and pack sizes apart.
            LinkByKey(p =>
            {
                if (ImporterSku().IsMatch(p.Sku))
                {
                    return ""; // a SKU made up by an importer, not a manufacturer code
                }
                var code = Regex.Replace(p.Sku, "[^A-Za-z0-9]+", "").ToUpperInvariant().TrimStart('0');
                return code.Length >= 8 && code.Count(char.IsAsciiDigit) >= 6 ? code : "";
            }, "Same code");
        }
        if (strategies.Contains("normalized"))
        {
            LinkByKey(p => p.Tokens.Length == 0 ? "" : $"{string.Join(' ', p.Tokens)}|{p.Brand?.Trim().ToLowerInvariant()}", "Same words");
        }
        if (strategies.Contains("fuzzy"))
        {
            foreach (var block in products.Where(p => p.Numbers.Length > 0 && p.Tokens.Length >= 3).GroupBy(p => p.Numbers).Where(g => g.Count() > 1))
            {
                var same = block.ToList();
                for (var i = 0; i < same.Count; i++)
                {
                    for (var j = i + 1; j < same.Count; j++)
                    {
                        var (a, b) = (same[i], same[j]);
                        if (!string.IsNullOrWhiteSpace(a.Brand) && !string.IsNullOrWhiteSpace(b.Brand) && !string.Equals(a.Brand.Trim(), b.Brand.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        var common = a.Tokens.Intersect(b.Tokens, StringComparer.Ordinal).Count();
                        var similarity = (double)common / (a.Tokens.Length + b.Tokens.Length - common);
                        if (similarity >= threshold)
                        {
                            Link(a, b, "Similar name", similarity);
                        }
                    }
                }
            }
        }

        // Each connected set of links is one group.
        var parent = new Dictionary<Guid, Guid>();
        Guid Find(Guid id) => parent.TryGetValue(id, out var up) && up != id ? parent[id] = Find(up) : id;
        foreach (var (a, b) in links.Keys)
        {
            parent.TryAdd(a, a);
            parent.TryAdd(b, b);
            parent[Find(a)] = Find(b);
        }

        var ignored = (await db.CatalogCleanRecords.AsNoTracking().Where(r => r.Kind == CatalogCleanRecordKind.Ignored).Select(r => r.GroupKey).ToListAsync(cancellationToken)).ToHashSet();
        var groups = new List<DuplicateGroup>();
        foreach (var component in parent.Keys.GroupBy(Find))
        {
            var ids = component.OrderBy(id => id).ToList();
            var key = KeyOf(ids);
            if (ignored.Contains(key))
            {
                continue;
            }
            var own = links.Where(link => ids.Contains(link.Key.Item1)).ToList();
            var members = ids.Select(id => byId[id]).Select(p =>
            {
                var (score, parts) = ScoreOf(p);
                return new DuplicateProduct(p.Id, p.Sku, p.Name, p.Brand, p.Category, p.Price, p.Stock, p.Pictures, p.Listings, p.Orders, score, parts);
            }).OrderByDescending(p => p.Score).ThenBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).ToList();
            groups.Add(new DuplicateGroup(
                key, string.Join(", ", own.Select(link => link.Value.Reason).Distinct()), (int)Math.Round(own.Min(link => link.Value.Similarity) * 100), members[0].Id, members));
        }
        groups = groups.OrderByDescending(g => g.Similarity).ThenByDescending(g => g.Products.Count).ToList();

        var retired = await db.CatalogCleanRecords.AsNoTracking().Where(r => r.Kind == CatalogCleanRecordKind.Retired)
            .OrderByDescending(r => r.CreatedAtUtc).Select(r => new { r.RunId, r.CreatedAtUtc }).ToListAsync(cancellationToken);
        var lastRun = retired.FirstOrDefault();
        var notListed = products.Count(p => p.Listings == 0);
        return new CatalogScan(
            new CatalogHealth(
                products.Count,
                await db.Products.CountAsync(p => p.IsArchived, cancellationToken),
                products.Count(p => p.Pictures == 0), products.Count(p => p.DescriptionLength < 40), products.Count(p => p.Price <= 0),
                products.Count(p => string.IsNullOrWhiteSpace(p.Category)), products.Count(p => string.IsNullOrWhiteSpace(p.Brand)), products.Count(p => p.Stock <= 0), notListed,
                groups.Count, groups.Sum(g => g.Products.Count - 1), retired.Count),
            groups.Take(300).ToList(),
            lastRun is not null, lastRun?.CreatedAtUtc, lastRun is null ? 0 : retired.Count(r => r.RunId == lastRun.RunId));
    }

    private static string KeyOf(IEnumerable<Guid> ids) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(string.Join(',', ids.OrderBy(id => id)))));

    /// <summary>
    /// Retires the products chosen as duplicates: each is archived, as from the Products page, and what it
    /// was a duplicate of is recorded so the clean can be undone. One that is listed on a sales channel or has
    /// stock is passed over: it has to be taken off sale, or its stock moved, by a person first.
    /// </summary>
    public async Task<CleanResult> RetireAsync(IReadOnlyList<CleanGroupChoice> choices, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var ids = choices.SelectMany(c => c.RetireIds).Distinct().ToList();
        var products = await db.Products.Where(p => EF.Parameter(ids).Contains(p.Id) && !p.IsArchived).ToDictionaryAsync(p => p.Id, cancellationToken);
        var active = (await (
            from listing in db.ChannelListings.AsNoTracking()
            join variant in db.ProductVariants.AsNoTracking() on listing.VariantId equals variant.Id
            where EF.Parameter(ids).Contains(variant.ProductId) && listing.DesiredState == ListingDesiredState.Active
            select variant.ProductId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        var (retired, skipped) = (0, 0);
        foreach (var choice in choices)
        {
            foreach (var id in choice.RetireIds.Where(id => id != choice.KeepId).Distinct())
            {
                if (!products.TryGetValue(id, out var product) || active.Contains(id))
                {
                    skipped++;
                    continue;
                }
                product.IsArchived = true;
                product.UpdatedAtUtc = now;
                db.CatalogCleanRecords.Add(new CatalogCleanRecord
                {
                    Id = Guid.NewGuid(), Kind = CatalogCleanRecordKind.Retired, GroupKey = choice.Key.Length > 64 ? choice.Key[..64] : choice.Key,
                    RunId = runId, ProductId = id, KeptProductId = choice.KeepId, CreatedAtUtc = now,
                });
                retired++;
            }
        }
        audit.Log("CatalogDuplicatesRetired", $"retired={retired}; skipped={skipped}");
        await db.SaveChangesAsync(cancellationToken);
        return new CleanResult(retired, skipped);
    }

    /// <summary>Brings back every product the last clean retired. Returns how many.</summary>
    public async Task<int> UndoLastAsync(CancellationToken cancellationToken)
    {
        var last = await db.CatalogCleanRecords.Where(r => r.Kind == CatalogCleanRecordKind.Retired).OrderByDescending(r => r.CreatedAtUtc).Select(r => (Guid?)r.RunId).FirstOrDefaultAsync(cancellationToken);
        if (last is null)
        {
            return 0;
        }
        var records = await db.CatalogCleanRecords.Where(r => r.RunId == last && r.Kind == CatalogCleanRecordKind.Retired).ToListAsync(cancellationToken);
        var ids = records.Select(r => r.ProductId).OfType<Guid>().ToList();
        var products = await db.Products.Where(p => EF.Parameter(ids).Contains(p.Id)).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var product in products.Where(p => p.IsArchived))
        {
            product.IsArchived = false;
            product.UpdatedAtUtc = now;
        }
        db.CatalogCleanRecords.RemoveRange(records);
        audit.Log("CatalogCleanUndone", $"restored={products.Count}");
        await db.SaveChangesAsync(cancellationToken);
        return products.Count;
    }

    /// <summary>Marks a group as not duplicates after all, so it is not shown again.</summary>
    public async Task IgnoreAsync(string key, CancellationToken cancellationToken)
    {
        if (!await db.CatalogCleanRecords.AnyAsync(r => r.Kind == CatalogCleanRecordKind.Ignored && r.GroupKey == key, cancellationToken))
        {
            db.CatalogCleanRecords.Add(new CatalogCleanRecord { Id = Guid.NewGuid(), Kind = CatalogCleanRecordKind.Ignored, GroupKey = key, RunId = Guid.Empty, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
