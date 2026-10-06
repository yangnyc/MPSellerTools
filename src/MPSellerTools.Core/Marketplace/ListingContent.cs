using System.Text.Json;
using System.Text.Json.Serialization;
using MPSellerTools.Core.Business;

namespace MPSellerTools.Core.Marketplace;

/// <summary>One overridden field: a replacement value, or a deliberate blank.</summary>
public record FieldOverride(
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("cleared")] bool Cleared = false);

/// <summary>
/// A listing's content overrides. A field can be in three states, and they
/// are kept distinct: not present (inherit the product's value), set
/// (use this one), cleared (send it empty on purpose).
/// </summary>
public class ContentOverrides
{
    public const string Title = "title";
    public const string Description = "description";
    public const string Brand = "brand";

    public static readonly string[] Fields = [Title, Description, Brand];

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    private readonly Dictionary<string, FieldOverride> _fields;

    private ContentOverrides(Dictionary<string, FieldOverride> fields) => _fields = fields;

    public static ContentOverrides Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ContentOverrides([]);
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, FieldOverride>>(json, Json) ?? [];
        return new ContentOverrides(parsed.Where(p => Fields.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
    }

    public IReadOnlyDictionary<string, FieldOverride> All => _fields;

    public void Inherit(string field) => _fields.Remove(field);

    public void Set(string field, string value) => _fields[field] = new FieldOverride(Value: value);

    public void Clear(string field) => _fields[field] = new FieldOverride(Cleared: true);

    /// <summary>The value to send: the override when there is one, else the product's.</summary>
    public string? Resolve(string field, string? inherited) =>
        _fields.TryGetValue(field, out var o) ? (o.Cleared ? null : o.Value) : inherited;

    public string? ToJson() => _fields.Count == 0 ? null : JsonSerializer.Serialize(_fields, Json);
}

/// <summary>Everything an adapter needs to describe one listing, already resolved. Built without touching any channel.</summary>
public record ListingSnapshot(
    Guid ListingId,
    SalesChannel Channel,
    string MarketplaceCode,
    string Language,
    string Currency,
    string SellerSku,
    string? Title,
    string? Description,
    string? Brand,
    string? ExternalCategoryId,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> Options,
    ItemCondition Condition,
    decimal Price,
    int Quantity,
    FulfillmentMode FulfillmentMode,
    IReadOnlyDictionary<ProductIdentifierType, string> Identifiers,
    IReadOnlyList<string> ImageUrls,
    decimal? WeightValue,
    string? WeightUnit,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? DimensionUnit,
    ListingGroupSnapshot? Group,
    string? ExistingCatalogItemId,
    CategoryRequirements Requirements);

/// <summary><see cref="VariationValues"/> lists, per varying option, the values found across the members.</summary>
public record ListingGroupSnapshot(
    string GroupKey,
    IReadOnlyList<string> VariationAttributes,
    IReadOnlyList<string> MemberSkus,
    IReadOnlyDictionary<string, IReadOnlyList<string>> VariationValues);

public record ConditionalRequirement(
    [property: JsonPropertyName("when")] string When,
    [property: JsonPropertyName("equals")] string EqualTo,
    [property: JsonPropertyName("require")] List<string> Require);

/// <summary>What a category requires, as last retrieved. <see cref="Source"/> and <see cref="Version"/> say from where.</summary>
public record CategoryRequirements(
    [property: JsonPropertyName("required")] List<string>? Required = null,
    [property: JsonPropertyName("enums")] Dictionary<string, List<string>>? Enums = null,
    [property: JsonPropertyName("conditional")] List<ConditionalRequirement>? Conditional = null)
{
    [JsonIgnore]
    public string? Source { get; init; }

    [JsonIgnore]
    public string? Version { get; init; }

    public static CategoryRequirements Parse(string? json, string? source, string? version)
    {
        var parsed = string.IsNullOrWhiteSpace(json) ? new CategoryRequirements() : JsonSerializer.Deserialize<CategoryRequirements>(json) ?? new CategoryRequirements();
        return parsed with { Source = source, Version = version };
    }
}

/// <summary>A reason a listing cannot be submitted, tied to the field at fault.</summary>
public record ValidationIssue(SalesChannel Channel, string Path, string Code, string Message);

public static class ListingComposer
{
    /// <summary>
    /// Resolves the listing's effective content: the product and variant as
    /// the base, the listing's overrides on top.
    /// </summary>
    public static ListingSnapshot Compose(
        Product product,
        ProductVariant variant,
        ChannelListing listing,
        ChannelMarket market,
        SalesChannel channel,
        CategoryMapping? mapping,
        int availableToSell,
        IEnumerable<ProductIdentifier> identifiers,
        IEnumerable<string> imageUrls,
        ListingGroupSnapshot? group,
        string? existingCatalogItemId)
    {
        var overrides = ContentOverrides.Parse(listing.ContentOverridesJson);
        var baseTitle = string.IsNullOrWhiteSpace(variant.Name) ? product.Name : $"{product.Name} - {variant.Name}";

        // A variant's own identifier wins over the product's of the same type.
        var ids = identifiers
            .OrderBy(i => i.VariantId is null ? 0 : 1)
            .GroupBy(i => i.Type)
            .ToDictionary(g => g.Key, g => g.Last().Value);

        return new ListingSnapshot(
            listing.Id,
            channel,
            market.MarketplaceCode,
            market.Language,
            market.Currency,
            listing.SellerSku,
            overrides.Resolve(ContentOverrides.Title, baseTitle),
            overrides.Resolve(ContentOverrides.Description, product.Description),
            overrides.Resolve(ContentOverrides.Brand, product.Brand),
            listing.ExternalCategoryId ?? mapping?.ExternalCategoryId,
            ParseMap(listing.AttributesJson),
            ParseMap(variant.OptionsJson),
            variant.Condition,
            listing.PriceOverride ?? variant.Price,
            listing.FulfillmentMode == FulfillmentMode.Merchant ? Availability.ForChannel(availableToSell, listing.QuantityCap) : 0,
            listing.FulfillmentMode,
            ids,
            imageUrls.ToList(),
            variant.WeightValue,
            variant.WeightUnit,
            variant.Length,
            variant.Width,
            variant.Height,
            variant.DimensionUnit,
            group,
            existingCatalogItemId,
            CategoryRequirements.Parse(mapping?.RequirementsJson, mapping?.RequirementsSource, mapping?.RequirementsVersion));
    }

    /// <summary>A JSON object of names to values, read so that a name is found whatever its capitals.</summary>
    public static Dictionary<string, string> ParseMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind != JsonValueKind.Object
            ? []
            : document.RootElement.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText(),
                StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Checks that hold on every channel, run before anything is sent. Passing
/// them says the submission is well formed here; the channel still applies
/// its own business rules afterwards.
/// </summary>
public static class ListingValidator
{
    public static List<ValidationIssue> Common(ListingSnapshot s)
    {
        var issues = new List<ValidationIssue>();
        void Add(string path, string code, string message) => issues.Add(new ValidationIssue(s.Channel, path, code, message));

        if (string.IsNullOrWhiteSpace(s.Title))
        {
            Add("title", "required", "A title is required.");
        }

        if (s.Price <= 0)
        {
            Add("price", "range", "The price must be greater than zero.");
        }

        if (s.WeightValue is not null && s.WeightUnit is not ("lb" or "oz" or "kg" or "g"))
        {
            Add("weightUnit", "unit", "Weight needs a unit of lb, oz, kg or g.");
        }

        if ((s.Length ?? s.Width ?? s.Height) is not null && s.DimensionUnit is not ("in" or "cm"))
        {
            Add("dimensionUnit", "unit", "Dimensions need a unit of in or cm.");
        }

        // Names are matched without regard to case: requirement lists and the people filling
        // attributes in do not agree on capitals, and "color" missing because "Color" was given helps no one.
        string? ValueOf(string name) =>
            s.Attributes.Concat(s.Options).Where(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).FirstOrDefault()
            ?? (name.Equals("Brand", StringComparison.OrdinalIgnoreCase) ? s.Brand : null);

        var required = new List<string>(s.Requirements.Required ?? []);
        foreach (var rule in s.Requirements.Conditional ?? [])
        {
            if (string.Equals(ValueOf(rule.When), rule.EqualTo, StringComparison.OrdinalIgnoreCase))
            {
                required.AddRange(rule.Require);
            }
        }

        foreach (var name in required.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ValueOf(name)))
            {
                Add($"attributes.{name}", "required", $"The category requires \"{name}\".");
            }
        }

        foreach (var (name, allowed) in s.Requirements.Enums ?? [])
        {
            if (ValueOf(name) is { Length: > 0 } value && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                Add($"attributes.{name}", "enum", $"\"{value}\" is not an accepted value for \"{name}\".");
            }
        }

        return issues;
    }
}
