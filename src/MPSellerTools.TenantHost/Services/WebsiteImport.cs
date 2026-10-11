using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.TenantHost.Services;

/// <summary>
/// How another storefront's catalog is brought in. <see cref="Source"/> is the store's host name;
/// <see cref="Platform"/> is "shopify" (its public products.json) or "magento" (its public GraphQL API).
/// <see cref="Rules"/> is "health" for the over-the-counter rules (prescription and clinical items left out,
/// products sorted into health categories) or "general" for none. <see cref="Variants"/> is "first" (one
/// product from a product's first available variant), "all" (every variant a product of its own) or
/// "skip" (products with variants left out). <see cref="Existing"/> is "skip" or "refresh".
/// </summary>
public record WebsiteImportOptions(
    string Source,
    string Platform = "shopify",
    string Rules = "health",
    int? Limit = null,
    string Existing = "skip",
    bool UpdatePrices = false,
    decimal PriceAdjustPercent = 0,
    int Stock = 0,
    int MaxPictures = 8,
    bool InStockOnly = false,
    string Variants = "first",
    bool AllowClinical = false,
    bool KeepUncategorised = false,
    bool IncludeNeedsReview = false,
    string? SkuPrefix = null,
    bool PlainTextDescriptions = false,
    bool DryRun = false);

/// <summary>A product as the source lists it, the same shape whichever platform it came from.</summary>
public record SourceProduct(
    string Id, string Title, string Handle, string BodyHtml, string Vendor, string ProductType, IReadOnlyList<string> Tags,
    IReadOnlyList<SourceVariant> Variants, IReadOnlyList<string> Images);

public record SourceVariant(string Title, string? Sku, decimal Price, decimal? CompareAtPrice, bool Available, int Grams);

/// <summary>
/// Brings another storefront's catalog into the products here, cleaned up
/// on the way: items that do not belong are left out, descriptions are
/// rebuilt from plain tags without the source store's own words, each
/// product is sorted into a category, and the price can be adjusted. The
/// source is only read, through what it publishes for anyone. The text and
/// pictures belong to the source store and the brands: import only what
/// you may publish.
/// </summary>
public partial class WebsiteImport(TenantDbContext db, IHttpClientFactory httpClientFactory, ILogger<WebsiteImport> logger)
{
    public const string HttpClientName = "storefront";

    /// <summary>How long is waited between pages of the source, to stay well below its rate limit.</summary>
    public static TimeSpan PageDelay { get; set; } = TimeSpan.FromSeconds(1);

    private const int MaxPages = 200;
    private const int SaveEvery = 25;
    private const int MaxErrorsKept = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(WebsiteImportOptions options) => JsonSerializer.Serialize(options, Json);

    public static WebsiteImportOptions? Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<WebsiteImportOptions>(json, Json);

    // --- The source's address ----------------------------------------------------------------------------

    /// <summary>
    /// The store's host name from whatever was typed (a name or a whole address), or why it cannot be read.
    /// Only a public name is taken: the server calls whatever is entered here, so it must not be pointed
    /// at this machine or the network behind it.
    /// </summary>
    public static (string? Host, string? Problem) HostOf(string? typed)
    {
        var text = (typed ?? "").Trim();
        if (text.Length == 0)
        {
            return (null, "Enter the address of the store to import from, such as shop.example.com.");
        }
        if (!Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : $"https://{text}", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || !uri.Host.Contains('.'))
        {
            return (null, "That is not a store's address. Enter its name, such as shop.example.com.");
        }
        if (uri.IsLoopback || IPAddress.TryParse(uri.Host.Trim('[', ']'), out _) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "The store has to be a public website with a name of its own, not an address on this network.");
        }
        return (uri.Host.ToLowerInvariant(), null);
    }

    // --- The rules ---------------------------------------------------------------------------------------

    private const RegexOptions Rx = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>Clinical and professional items, which do not belong in an over-the-counter catalog.</summary>
    private static readonly Regex Clinical = new(string.Join('|',
        @"\(rx\)", @"\brx\b", "prescription", "inject", @"\bvials?\b", "syringe", "needle", "catheter", @"\biv\b",
        "infusion", "surg", "suture", "scalpel", "forceps", "tube feeding", "feeding tube", "enteral", "trach", "ostomy",
        "specimen", "exam glove", @"gowns?\b", "laborator", "blood collection", "sharps", "oxygen", "cannula", "suction",
        "heparin", "anesth", "ampule", "intraven", "irrigation", "urinal", "bedpan", "drainage", "nebuliz", "airway",
        "stethoscope", "defibrill", "electrode", "gift card", "shipping protection", @"\bcase of\b", @"\bbundle\b",
        "test kit", @"\bclia\b", "antigen", "cpap", "tubing", @"\bcase\)", @"/ ?(case|cs)\b"), Rx);

    /// <summary>Prescription drugs and research compounds, which sources list without saying so; never imported under the health rules.</summary>
    private static readonly Regex Prescription = new(string.Join('|',
        @"\(rx\)", @"\brx only", "prescription (only|required|drug)", "controlled substance",
        "peptide(?! complex)", @"\bbpc", "tb.?500", "semaglutide", "tirzepatide", "glp-?1", "bacteriostatic", "testosterone",
        "gabapentin", "prednis", "sildenafil", "tadalafil", "finasteride", "penicillin", "amoxicillin", "azithromycin",
        "cephalexin", "doxycycline", "ciprofloxacin", "erythromycin", "clindamycin", "mupirocin", "nystatin", "fluconazole",
        "triamcinolone acetonide (cream|ointment|lotion)", "clobetasol", "betamethasone", @"hydrocortisone.*2\.5", "tretinoin",
        "naloxone", "epinephrine", "lidocaine hcl", "ketorolac", "ondansetron", "dexameth", "methylpred", "albuterol",
        "levothyroxine", "metformin", "lisinopril", "atorvastatin", "oseltamivir", "for compounding", @"\(api\)", "ivermectin",
        "tramadol", "cartridge"), Rx);

    private static readonly Regex ExcludedTag = new("^(doctor-only|rx|prescription)$", Rx);

    /// <summary>Something that reads like a medicine: a strength, or a dosage form.</summary>
    private static readonly Regex MedicineLike = new(
        @"\d\s?(mg|mcg|%)|\b(tablets?|capsules?|caplets?|softgels?|cream|ointment|solution|suspension|syrup|spray|drops|lotion|patch(es)?|powder|gel|liquid|elixir|suppositor\w+)\b", Rx);

    /// <summary>The common over-the-counter actives and brands. A medicine-like product naming none of them is held for a person to look at.</summary>
    private static readonly Regex KnownOtc = new(
        "acetaminophen|ibuprofen|aspirin|naproxen|loratadine|cetirizine|fexofenadine|diphenhydramine|chlorpheniramine|guaifenesin|dextromethorphan|phenylephrine|famotidine|omeprazole|esomeprazole|lansoprazole|calcium|antacid|simethicone|loperamide|bismuth|docusate|senna|bisacodyl|polyethylene glycol|psyllium|fiber|melatonin|doxylamine|hydrocortisone|clotrimazole|miconazole|terbinafine|tolnaftate|bacitracin|neomycin|polymyxin|antibiotic (ointment|cream)|benzoyl peroxide|salicylic|adapalene|minoxidil|nicotine|zinc|petrolatum|lidocaine|menthol|camphor|capsaicin|vitamin|multivit|saline|oxymetazoline|fluticasone|nasal spray|artificial tears|eye drops|lubricant|sunscreen|spf|moistur|soap|shampoo|conditioner|deodorant|sanitizer|toothpaste|mouthwash|lip balm|baby|diaper|wipes|lozenge|cough|cold|flu|allergy|laxative|stool|heartburn|gas relief|probiotic|supplement|magnesium|iron|potassium|omega|fish oil|glucosamine|biotin|collagen|electrolyte|witch hazel|peroxide|alcohol|iodine|aloe|calamine|body wash|skin protectant|dimethicone|tylenol|advil|motrin|aleve|bayer|excedrin|benadryl|claritin|zyrtec|allegra|mucinex|robitussin|vicks|tums|pepto|imodium|miralax|dulcolax|neosporin|cortizone|gold bond|aveeno|cerave|cetaphil|eucerin|aquaphor|vaseline|icy hot|bengay|biofreeze|salonpas|aspercreme|voltaren|diclofenac|orajel|abreva|preparation h|monistat|levonorgestrel|pregnancy|test strips?|lancets?|thermometer|condom|bandage|gauze|dressing|tape", Rx);

    private static readonly (Regex Pattern, string Category)[] AudienceRules =
    [
        (new(@"\b(bab(y|ies)|infants?|child(ren)?('?s)?|kids?|pediatric|toddlers?|diapers?)\b", Rx), "Infants & Children"),
    ];

    /// <summary>Keyword rules, most specific first: a product goes to the first category whose words its type or name has.</summary>
    private static readonly (Regex Pattern, string Category)[] CategoryRules =
    [
        (new(@"\b(nicotine|quit smoking|stop smoking)\b", Rx), "Medicines / Quit Smoking"),
        (new(@"\b(sleep aids?|sleep|melatonin|snor(e|ing))\b", Rx), "Medicines / Sleep Aids"),
        (new(@"\b(allerg\w*|antihistamine|cetirizine|loratadine|fexofenadine|hay fever)\b", Rx), "Medicines / Allergy"),
        (new(@"\b(cough\w*|cold|flu|mucus|congestion|decongestant|sore throat|lozenges?|sinus|nasal|expectorant|guaifenesin)\b", Rx), "Medicines / Cough, Cold & Flu"),
        (new(@"\b(laxatives?|antacids?|acid reduc\w*|heartburn|gas relief|anti-?gas|diarrh\w*|antidiarrheal|nausea|antinausea|digest\w*|stool soften\w*|hemorrhoid\w*|fiber|constipation|famotidine|omeprazole|simethicone|bismuth)\b", Rx), "Medicines / Digestive Health"),
        (new(@"\b(arthritis)\b", Rx), "Medicines / Arthritis Pain"),
        (new(@"\b(patch(es)?|rubs?|topical analgesic|lidocaine|menthol|capsaicin|muscle rub|liniment)\b", Rx), "Medicines / Patches & Rubs"),
        (new(@"\b(pain|acetaminophen|ibuprofen|aspirin|naproxen|analgesics?|fever|headache|migraine)\b", Rx), "Medicines / Pain & Fever"),
        (new(@"\b(diabet\w*|glucose|lancets?|test strips?)\b", Rx), "Diabetic Care"),
        // Wound care comes before the ingredient rules: a "Calcium Alginate Dressing" is a dressing, not a mineral.
        (new(@"\b(bandages?|gauze|dressings?|first aid|antiseptic\w*|antibiotic|wound\w*)\b", Rx), "First Aid"),
        (new(@"\b(prenatal)\b", Rx), "Vitamins & Supplements / Prenatal"),
        (new(@"\b(multi-?vitamins?|vitamins?|b-?12|b-?complex|folic acid|biotin|niacin)\b", Rx), "Vitamins & Supplements / Vitamins"),
        (new(@"\b(minerals?|calcium|magnesium|zinc|iron|potassium|selenium|chromium)\b", Rx), "Vitamins & Supplements / Minerals"),
        (new(@"\b(herbal|herbs?|turmeric|ginkgo|elderberry|echinacea|ginseng|ashwagandha|cranberry|garlic)\b", Rx), "Vitamins & Supplements / Herbal Supplements"),
        (new(@"\b(immune)\b", Rx), "Vitamins & Supplements / Immune Support"),
        (new(@"\b(supplements?|probiotics?|omega|fish oil|collagen|coq-?10|glucosamine|protein|electrolytes?)\b", Rx), "Vitamins & Supplements / Supplements"),
        (new(@"\b(incontinence|briefs?|underpads?|protective underwear|bladder|adult diapers?|pull-?ups?)\b", Rx), "Home Health Care / Incontinence"),
        (new(@"\b(walkers?|rollators?|canes?|crutch(es)?|wheelchairs?|mobility|transfer)\b", Rx), "Home Health Care / Mobility"),
        (new(@"\b(blood pressure|thermometers?|oximeters?|scales?|monitors?)\b", Rx), "Home Health Care / Health Monitors"),
        (new(@"\b(shower chairs?|commodes?|bath safety|raised toilet|grab bars?|bath bench|shower)\b", Rx), "Home Health Care / Bath Safety"),
        (new(@"\b(braces?|supports?|compression|splints?|stockings?|slings?|insoles?|orthopedic)\b", Rx), "Home Health Care / Braces & Supports"),
        (new(@"\b(drinks?|shakes?|nutrition\w*|thickener\w*|formula)\b", Rx), "Home Health Care / Nutrition"),
        (new(@"\b(eye|eyes|ophthalmic|contact lens|artificial tears)\b", Rx), "Personal Care / Eye Care"),
        (new(@"\b(ear|ears|earwax)\b", Rx), "Personal Care / Ear Care"),
        (new(@"\b((?<!\d[ -])foot|feet|athlete'?s|corn|callus|toe|wart)\b", Rx), "Personal Care / Foot Care"),
        (new(@"\b(lips?|oral|tooth\w*|teeth|dental|mouth\w*|denture\w*|floss)\b", Rx), "Personal Care / Lip & Oral Care"),
        (new(@"\b(feminine|women'?s?|menstrual|contracepti\w*|yeast|vaginal|pregnancy|tampons?|pads)\b", Rx), "Personal Care / Women's Care"),
        (new(@"\b(shav\w*|razors?|men'?s|condoms?)\b", Rx), "Personal Care / Men's Care"),
        (new(@"\b(shampoos?|conditioners?|hair)\b", Rx), "Personal Care / Hair Care"),
        (new(@"\b(tapes?|peroxide|alcohol|burns?|cold packs?|hot packs?)\b", Rx), "First Aid"),
        (new(@"\b(acne|skin|lotions?|moisturiz\w*|creams?|ointments?|sunscreen|anti-?itch|itch|hydrocortisone|antifungal|eczema|rash|blemish)\b", Rx), "Personal Care / Skin Care"),
        (new(@"\b(soaps?|body wash|deodorant\w*|sanitizers?|bath|wipes?|tissues?|washcloths?|cleansers?|hygiene|personal care)\b", Rx), "Personal Care / Bath & Body"),
    ];

    /// <summary>
    /// Where a product goes, as "A / B": by the health rules, or under the source's own product type. Null
    /// when nothing says. A product for children under the health rules goes to their category.
    /// </summary>
    public static string? CategoryOf(SourceProduct product, bool health)
    {
        if (!health)
        {
            return product.ProductType.Trim() is { Length: > 0 } type ? Truncate(type, 100) : null;
        }

        // Type and title decide; tags are only consulted when those say nothing, because they are noisy.
        var named = $"{product.ProductType} {product.Title}";
        foreach (var text in new[] { named, string.Join(' ', product.Tags) })
        {
            var match = CategoryRules.FirstOrDefault(rule => rule.Pattern.IsMatch(text));
            if (match.Category is not null)
            {
                return AudienceRules.FirstOrDefault(rule => rule.Pattern.IsMatch(named)).Category ?? match.Category;
            }
        }
        return null;
    }

    /// <summary>Why a product is left out, or null when it is taken.</summary>
    public static string? LeftOut(SourceProduct product, WebsiteImportOptions options, Regex ownName)
    {
        var health = options.Rules != "general";
        var variant = VariantOf(product);
        var text = $"{product.ProductType} {product.Title}";
        if (product.Images.Count == 0)
        {
            return "no pictures";
        }
        if (variant is null || variant.Price <= 0)
        {
            return "no price";
        }
        if (options.InStockOnly && !variant.Available)
        {
            return "out of stock";
        }
        if (product.Variants.Count > 1 && options.Variants == "skip")
        {
            return "has variants";
        }
        if (ownName.IsMatch(product.Title))
        {
            return "the source store's own item";
        }
        if (health && (Prescription.IsMatch(text) || product.Tags.Any(tag => ExcludedTag.IsMatch(tag)) || (!options.AllowClinical && Clinical.IsMatch(text))))
        {
            return "prescription or clinical";
        }
        if (health && !options.IncludeNeedsReview && NeedsReview(product))
        {
            return "needs a person to review";
        }
        if (CategoryOf(product, health) is null && !options.KeepUncategorised)
        {
            return "no category";
        }
        return null;
    }

    /// <summary>
    /// A source that mixes in medicines without marking which need a prescription cannot be trusted on its
    /// word: something that looks like a medicine but names none of the common over-the-counter actives
    /// or brands is for a person to look at before it is sold.
    /// </summary>
    public static bool NeedsReview(SourceProduct product) =>
        MedicineLike.IsMatch(product.Title) && !KnownOtc.IsMatch($"{product.Title} {product.ProductType}");

    public static SourceVariant? VariantOf(SourceProduct product) =>
        product.Variants.FirstOrDefault(v => v.Available) ?? product.Variants.FirstOrDefault();

    public static string NameOf(SourceProduct product)
    {
        var variant = VariantOf(product);
        var pack = product.Variants.Count > 1 && variant is not null && variant.Title != "Default Title" ? $" - {variant.Title}" : "";
        return Truncate(Spaces().Replace(product.Title + pack, " ").Trim(), 200);
    }

    // --- Text --------------------------------------------------------------------------------------------

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    public static string PlainText(string html) =>
        Spaces().Replace(
            WebUtility.HtmlDecode(Tags().Replace(html, " ")).Replace(' ', ' '), " ").Trim();

    /// <summary>
    /// A source's description rebuilt from a small set of plain tags: its inline styles, wrappers, links,
    /// scripts and pictures are dropped, tables become lines, and what is left is tidied.
    /// </summary>
    public static string CleanHtml(string html)
    {
        var clean = html;
        clean = Regex.Replace(clean, @"<(script|iframe|style|svg|form|button|video)[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<!--[\s\S]*?-->", "");
        clean = clean.Replace("&nbsp;", " ").Replace(' ', ' ');
        // A table becomes one line per row.
        clean = Regex.Replace(clean, @"</t[dh]>\s*<t[dh][^>]*>", ": ", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<(/?)tr\b[^>]*>", "<$1p>", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<(/?)h[1-6]\b[^>]*>", "<$1h3>", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<(/?)div\b[^>]*>", "<$1p>", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<(/?)b\b[^>]*>", "<$1strong>", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<(/?)i\b[^>]*>", "<$1em>", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<(/?)(p|ul|ol|li|strong|em|br|h3)\b[^>]*>", m => $"<{m.Groups[1].Value}{m.Groups[2].Value.ToLowerInvariant()}>", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"<(?!/?(p|ul|ol|li|strong|em|br|h3)>)[^>]*>", "", RegexOptions.IgnoreCase);
        clean = Spaces().Replace(clean, " ");
        // Tidy what unwrapping leaves behind: empty and doubled blocks, breaks at the end of a block.
        for (string? previous = null; previous != clean;)
        {
            previous = clean;
            clean = Regex.Replace(clean, @"(<br>\s*)+(</(p|li|h3)>)", "$2");
            clean = Regex.Replace(clean, @"<(p|li|h3)>(\s*<br>)+", "<$1>");
            clean = Regex.Replace(clean, @"<(p|li|ul|ol|strong|em|h3)>[\s:]*</\1>", "");
            clean = Regex.Replace(clean, @"<p>\s*<p>", "<p>");
            clean = Regex.Replace(clean, @"</p>\s*</p>", "</p>");
            clean = Regex.Replace(clean, @"<p>\s*<(ul|ol|h3)>", "<$1>");
            clean = Regex.Replace(clean, @"</(ul|ol|h3)>\s*</p>", "</$1>");
            clean = Regex.Replace(clean, @">\s+<", "><");
            clean = Regex.Replace(clean, @"<(p|li|h3)>\s+", "<$1>");
            clean = Regex.Replace(clean, @"\s+</(p|li|h3)>", "</$1>");
        }
        clean = clean.Trim();
        return clean.Length > 0 && !Regex.IsMatch(clean, "^<(p|ul|ol|h3)>") ? $"<p>{clean}</p>" : clean;
    }

    private static readonly Regex SalesTalk = new(
        @"\b(our (store|website|site|pharmacy|team)|order (now|today|online)|buy (now|online)|add to cart|free shipping|shop (now|our)|click here|customer service|call us|contact us)\b", Rx);

    /// <summary>Paragraphs, list items and headings that talk about the source store, or sell rather than describe, are dropped.</summary>
    public static string WithoutSourceStore(string html, Regex ownName)
    {
        var kept = Regex.Replace(html, @"<(p|li|h3)>((?:(?!</?(?:p|li|h3)>)[\s\S])*)</\1>",
            m => ownName.IsMatch(m.Groups[2].Value) || SalesTalk.IsMatch(m.Groups[2].Value) ? "" : m.Value);
        kept = Regex.Replace(kept, @"<(ul|ol)>\s*</\1>", "");
        return Regex.Replace(kept, @"<h3>[^<]*</h3>\s*(?=<h3>|$)", "");
    }

    /// <summary>A cleaned description as plain text: headings and list items on lines of their own.</summary>
    public static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<li>", "\n- ");
        text = Regex.Replace(text, @"</(p|h3|ul|ol)>|<br>", "\n");
        text = Regex.Replace(text, @"<h3>", "\n");
        text = WebUtility.HtmlDecode(Tags().Replace(text, ""));
        text = Regex.Replace(text, @"[ \t]+", " ");
        return Regex.Replace(text, @"\s*\n\s*", "\n").Trim();
    }

    /// <summary>The product's description as it is kept here, and the manufacturer where the description names one.</summary>
    public static (string? Description, string? Brand) ContentOf(SourceProduct product, Regex ownName, bool plainText)
    {
        var description = WithoutSourceStore(CleanHtml(product.BodyHtml), ownName);
        var named = Regex.Match(PlainText(description), @"\bManufacture(?:r|d by)\s*:?\s*([A-Z][\w&'’. -]{2,60}?)(?:\.|,|;|$| (?:Active|Inactive|Directions|Warnings|Uses))");
        var vendor = product.Vendor.Trim();
        var brand = named.Success ? named.Groups[1].Value.Trim() : vendor.Length > 0 && !ownName.IsMatch(vendor) ? vendor : null;
        if (brand is not null)
        {
            brand = Truncate(Regex.Replace(brand, @"\s+Pharmaceuticals?$", "", RegexOptions.IgnoreCase).Trim(), 200);
        }
        if (description.Length == 0)
        {
            return (null, brand);
        }
        return (plainText ? HtmlToText(description) : description, brand);
    }

    // --- Reading the source ------------------------------------------------------------------------------

    /// <summary>The source's whole catalog, as far as it publishes it.</summary>
    public async Task<List<SourceProduct>> ReadCatalogAsync(string host, string platform, CancellationToken cancellationToken)
    {
        var products = new List<SourceProduct>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var (batch, more) = platform == "magento"
                ? await MagentoPageAsync(host, page, cancellationToken)
                : await ShopifyPageAsync(host, page, cancellationToken);
            products.AddRange(batch);
            if (!more)
            {
                break;
            }
            await Task.Delay(PageDelay, cancellationToken);
        }
        return products;
    }

    private async Task<JsonElement> GetJsonAsync(string host, string url, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        string text;
        try
        {
            // A store often sends its bare name on to "www." or the like. That is followed a few times, and
            // only to another public name: where a redirect leads is checked like the address typed in.
            for (var hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
                if ((int)response.StatusCode is not (301 or 302 or 307 or 308) || response.Headers.Location is not { } location || hop >= 3)
                {
                    break;
                }
                var next = location.IsAbsoluteUri ? location : new Uri(new Uri(url), location);
                response.Dispose();
                if (next.Scheme != Uri.UriSchemeHttps || HostOf(next.Host).Host is null)
                {
                    throw new ChannelException(SyncErrorClass.DataCorrection, $"{host} sends its catalog on to an address that cannot be read from here.");
                }
                url = next.ToString();
            }
            text = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ChannelException(SyncErrorClass.Transient, $"{host} did not answer. Check the address, and that the store is up, then try again.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ChannelException(
                    ChannelHttp.Classify((int)response.StatusCode),
                    (int)response.StatusCode switch
                    {
                        404 => $"{host} has no catalog to read at this address. Check the address, and whether the platform chosen is the store's own.",
                        401 or 403 => $"{host} does not let its catalog be read ({(int)response.StatusCode}). It may be password-protected, or block automatic reading.",
                        429 => $"{host} says it is being asked too often. Try again later.",
                        _ => $"{host} answered with an error ({(int)response.StatusCode}).",
                    },
                    (int)response.StatusCode);
            }
            try
            {
                using var document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new ChannelException(SyncErrorClass.DataCorrection, $"{host} did not answer with a catalog. Check whether the platform chosen is the store's own.");
            }
        }
    }

    private async Task<(List<SourceProduct> Products, bool More)> ShopifyPageAsync(string host, int page, CancellationToken cancellationToken)
    {
        var body = await GetJsonAsync(host, $"https://{host}/products.json?limit=250&page={page}", cancellationToken);
        var items = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("products", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
        var products = items.Select(item => new SourceProduct(
            Text(item, "id") ?? "",
            Text(item, "title") ?? "",
            Text(item, "handle") ?? "",
            Text(item, "body_html") ?? "",
            Text(item, "vendor") ?? "",
            Text(item, "product_type") ?? "",
            item.TryGetProperty("tags", out var tags)
                ? tags.ValueKind == JsonValueKind.Array
                    ? tags.EnumerateArray().Select(t => t.GetString()?.Trim()).OfType<string>().ToList()
                    : (tags.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : [],
            item.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Array
                ? variants.EnumerateArray().Select(v => new SourceVariant(
                    Text(v, "title") ?? "Default Title", Text(v, "sku"), Number(v, "price") ?? 0m, Number(v, "compare_at_price"),
                    !v.TryGetProperty("available", out var available) || available.ValueKind != JsonValueKind.False, (int)(Number(v, "grams") ?? 0m))).ToList()
                : [],
            item.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array
                ? images.EnumerateArray().Select(i => Text(i, "src")).OfType<string>().ToList()
                : [])).Where(p => p.Id.Length > 0 && p.Title.Length > 0).ToList();
        return (products, items.Count >= 250);
    }

    /// <summary>A Magento storefront is read through its public GraphQL API and turned into the same shape.</summary>
    private async Task<(List<SourceProduct> Products, bool More)> MagentoPageAsync(string host, int page, CancellationToken cancellationToken)
    {
        var query = "{ products(filter: { price: { from: \"0\" } }, pageSize: 50, currentPage: " + page.ToString(CultureInfo.InvariantCulture) + ") { page_info { total_pages } "
            + "items { id sku name url_key stock_status description { html } categories { name } media_gallery { url disabled position } "
            + "price_range { minimum_price { regular_price { value } final_price { value } } } } } }";
        var body = await GetJsonAsync(host, $"https://{host}/graphql?query={Uri.EscapeDataString(query)}", cancellationToken);
        if (body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            throw new ChannelException(SyncErrorClass.DataCorrection, $"{host} would not list its catalog: {Text(errors[0], "message") ?? "no reason given"}.");
        }
        if (!body.TryGetProperty("data", out var data) || !data.TryGetProperty("products", out var found))
        {
            throw new ChannelException(SyncErrorClass.DataCorrection, $"{host} did not answer as a Magento store does. Check whether the platform chosen is the store's own.");
        }

        var products = new List<SourceProduct>();
        foreach (var item in found.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : [])
        {
            var minimum = item.TryGetProperty("price_range", out var range) && range.TryGetProperty("minimum_price", out var price) ? price : default;
            var final = minimum.ValueKind == JsonValueKind.Object && minimum.TryGetProperty("final_price", out var f) ? Number(f, "value") ?? 0m : 0m;
            var regular = minimum.ValueKind == JsonValueKind.Object && minimum.TryGetProperty("regular_price", out var r) ? Number(r, "value") ?? 0m : 0m;
            products.Add(new SourceProduct(
                Text(item, "id") ?? Text(item, "sku") ?? "",
                Text(item, "name") ?? "",
                Text(item, "url_key") ?? "",
                item.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.Object ? Text(description, "html") ?? "" : "",
                "",
                item.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array
                    ? string.Join(' ', categories.EnumerateArray().Select(c => Text(c, "name")).OfType<string>())
                    : "",
                [],
                [new SourceVariant("Default Title", Text(item, "sku"), final, regular > final ? regular : null, Text(item, "stock_status") == "IN_STOCK", 0)],
                item.TryGetProperty("media_gallery", out var gallery) && gallery.ValueKind == JsonValueKind.Array
                    ? gallery.EnumerateArray()
                        .Where(i => !i.TryGetProperty("disabled", out var disabled) || disabled.ValueKind != JsonValueKind.True)
                        .OrderBy(i => Number(i, "position") ?? 0m).Select(i => Text(i, "url")).OfType<string>().ToList()
                    : []));
        }
        var pages = found.TryGetProperty("page_info", out var info) ? (int)(Number(info, "total_pages") ?? 0m) : 0;
        return (products.Where(p => p.Id.Length > 0 && p.Title.Length > 0).ToList(), page < pages);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    private static decimal? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
                JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null,
            }
            : null;

    // --- The job -----------------------------------------------------------------------------------------

    /// <summary>
    /// Carries out an import job: reads the source, leaves out what does not belong, and makes or brings up
    /// to date a product for each of the rest, saving as it goes so its progress shows and a request to stop
    /// is noticed. A dry run reads and sorts the same way and only reports.
    /// </summary>
    public async Task RunAsync(BulkJob job, CancellationToken cancellationToken)
    {
        var options = Parse(job.ParametersJson);
        var (host, problem) = HostOf(options?.Source);
        if (options is null || host is null)
        {
            await FinishAsync(job, BulkJobStatus.Failed, null, problem ?? "The job has no store to import from.", cancellationToken);
            return;
        }

        var health = options.Rules != "general";
        // The source store's own name, which is taken out of descriptions and marks its own items (gift cards and the like).
        var hostName = Regex.Replace(host, @"^www\.|:\d+$", "").Split('.')[0];
        var ownName = new Regex(Regex.Replace(Regex.Escape(hostName), @"[^a-z0-9]+", ".?", RegexOptions.IgnoreCase), Rx);
        var prefix = (options.SkuPrefix?.Trim() is { Length: > 0 } chosen ? chosen : Regex.Replace(hostName, "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToUpperInvariant() is { Length: > 0 } made ? made[..Math.Min(4, made.Length)] : "SRC")
            .TrimEnd('-');

        var catalog = await ReadCatalogAsync(host, options.Platform == "magento" ? "magento" : "shopify", cancellationToken);
        if (options.Variants == "all")
        {
            // Every further variant ("3 Pack", "6 Pack") becomes a product of its own, beside the one made from the first.
            foreach (var product in catalog.Where(p => p.Variants.Count > 1).ToList())
            {
                foreach (var variant in product.Variants.Where(v => v != VariantOf(product)))
                {
                    catalog.Add(product with { Id = $"{product.Id}-{Regex.Replace(variant.Title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')}", Title = $"{product.Title} - {variant.Title}", Variants = [variant] });
                }
            }
        }

        var leftOut = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var selected = new List<SourceProduct>();
        foreach (var product in catalog)
        {
            if (LeftOut(product, options, ownName) is { } reason)
            {
                leftOut[reason] = leftOut.GetValueOrDefault(reason) + 1;
            }
            else
            {
                selected.Add(product);
            }
        }
        if (options.Limit is > 0 and var limit)
        {
            selected = selected.Take(limit).ToList();
        }

        // Which of them are here already, by the SKU each would get.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planned = selected.Select(product =>
        {
            var own = Regex.Replace((VariantOf(product)?.Sku ?? "").Trim(), @"[^\w.-]+", "-");
            own = own.Length > 60 ? own[..60] : own;
            var sku = own.Length > 0 && used.Add(own) ? own : $"{prefix}-{product.Id}";
            sku = sku.Length > 64 ? sku[..64] : sku;
            used.Add(sku);
            return (Product: product, Sku: sku);
        }).ToList();
        var skus = planned.Select(p => p.Sku).ToList();
        var existing = (await db.Products.AsNoTracking().Where(p => EF.Parameter(skus).Contains(p.Sku)).Select(p => p.Sku).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var work = planned.Where(p => options.Existing == "refresh" || !existing.Contains(p.Sku)).ToList();

        var categories = work.GroupBy(p => CategoryOf(p.Product, health) ?? "(no category)").OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var report = new List<BulkJobReportLine>
        {
            new("Products in the source", catalog.Count.ToString("N0", CultureInfo.InvariantCulture)),
            new("Selected", selected.Count.ToString("N0", CultureInfo.InvariantCulture)),
            new("New to your catalog", planned.Count(p => !existing.Contains(p.Sku)).ToString("N0", CultureInfo.InvariantCulture)),
            new("Already in your catalog", planned.Count(p => existing.Contains(p.Sku)).ToString("N0", CultureInfo.InvariantCulture)),
        };
        report.AddRange(leftOut.Select(pair => new BulkJobReportLine($"Left out: {pair.Key}", pair.Value.ToString("N0", CultureInfo.InvariantCulture))));
        report.AddRange(categories.Take(60).Select(g => new BulkJobReportLine($"Category: {g.Key}", g.Count().ToString("N0", CultureInfo.InvariantCulture))));
        job.ReportJson = JsonSerializer.Serialize(report, Json);
        job.Total = work.Count;
        job.HeartbeatAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var left = leftOut.Count == 0 ? "" : " Left out: " + string.Join(", ", leftOut.Select(pair => $"{pair.Value} {pair.Key}")) + ".";
        if (options.DryRun)
        {
            job.Processed = job.Succeeded = work.Count;
            await FinishAsync(job, BulkJobStatus.Succeeded,
                $"Dry run, nothing was changed: {work.Count} of the {catalog.Count} products at {host} would be imported.{left}", null, cancellationToken);
            return;
        }

        var errors = new List<BulkJobError>();
        var (created, refreshed) = (0, 0);
        var factor = 1 + options.PriceAdjustPercent / 100m;
        foreach (var batch in work.Chunk(SaveEvery))
        {
            if (await db.BulkJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.CancelRequested).FirstAsync(cancellationToken))
            {
                await FinishAsync(job, BulkJobStatus.Cancelled, $"Stopped after {job.Processed} of {job.Total}: {created} imported, {refreshed} brought up to date.", null, cancellationToken);
                return;
            }

            foreach (var (product, sku) in batch)
            {
                job.Processed++;
                try
                {
                    if (await ApplyAsync(product, sku, options, ownName, factor, health, cancellationToken))
                    {
                        created++;
                    }
                    else
                    {
                        refreshed++;
                    }
                    job.Succeeded++;
                }
                catch (InvalidOperationException ex)
                {
                    job.Failed++;
                    if (errors.Count < MaxErrorsKept)
                    {
                        errors.Add(new BulkJobError(sku, ex.Message.Length > 500 ? ex.Message[..500] : ex.Message));
                    }
                }
            }

            job.ErrorsJson = errors.Count == 0 ? null : JsonSerializer.Serialize(errors, Json);
            job.HeartbeatAtUtc = DateTime.UtcNow;
            // The products and the job's progress, together.
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            db.Attach(job);
        }

        logger.LogInformation("Imported {Created} and refreshed {Refreshed} products from {Host}", created, refreshed, host);
        await FinishAsync(
            job, job.Failed == 0 ? BulkJobStatus.Succeeded : BulkJobStatus.CompletedWithErrors,
            $"{created} product(s) imported from {host}, {refreshed} brought up to date" + (job.Failed == 0 ? "." : $"; {job.Failed} held back.") + left,
            null, cancellationToken);
    }

    /// <summary>Makes the product, or brings the one already under the SKU up to date. Returns whether it was made new.</summary>
    private async Task<bool> ApplyAsync(
        SourceProduct source, string sku, WebsiteImportOptions options, Regex ownName, decimal factor, bool health, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var variant = VariantOf(source)!;
        var price = Math.Round(variant.Price * factor, 2, MidpointRounding.AwayFromZero);
        var (description, brand) = ContentOf(source, ownName, options.PlainTextDescriptions);
        var category = CategoryOf(source, health);
        var name = NameOf(source);

        var product = db.Products.Local.FirstOrDefault(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase))
            ?? await db.Products.FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);
        if (product is not null)
        {
            if (product.IsArchived)
            {
                throw new InvalidOperationException("A product with this SKU is archived here; it was left as it is.");
            }
            // Its text and sorting follow the source again; its pictures and stock are its own by now.
            product.Name = name;
            product.Brand = brand ?? product.Brand;
            product.Description = description ?? product.Description;
            product.Category = category ?? product.Category;
            if (options.UpdatePrices)
            {
                product.Price = price;
            }
            product.UpdatedAtUtc = now;
            return false;
        }

        if (await db.ProductVariants.AnyAsync(v => v.Sku == sku, cancellationToken))
        {
            throw new InvalidOperationException("This SKU is already in use by a variant of another product.");
        }

        product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku,
            Name = name,
            Brand = brand,
            Description = description,
            Category = category,
            Price = price,
            StockQuantity = Math.Max(0, options.Stock),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Products.Add(product);
        // Made here rather than left to the save, so it can carry the weight.
        var made = CatalogSaveChangesInterceptor.NewDefaultVariant(product, now);
        if (variant.Grams > 0)
        {
            (made.WeightValue, made.WeightUnit) = (variant.Grams, "g");
        }
        db.ProductVariants.Add(made);
        db.InventoryBalances.Add(CatalogSaveChangesInterceptor.NewBalance(made.Id, product.StockQuantity, now));

        var pictures = source.Images
            .Select(url => url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url)
            .Where(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && url.Length <= 1000)
            .Distinct().Take(Math.Clamp(options.MaxPictures, 1, 12)).ToList();
        for (var i = 0; i < pictures.Count; i++)
        {
            var asset = new MediaAsset { Id = Guid.NewGuid(), Url = pictures[i], AltText = Truncate(name, 500), CreatedAtUtc = now };
            db.MediaAssets.Add(asset);
            db.ProductMedia.Add(new ProductMedia { Id = Guid.NewGuid(), ProductId = product.Id, MediaAssetId = asset.Id, Purpose = i == 0 ? MediaPurpose.Main : MediaPurpose.Gallery, Position = i });
        }
        return true;
    }

    private async Task FinishAsync(BulkJob job, BulkJobStatus status, string? summary, string? error, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        job.Status = status;
        job.Summary = summary is { Length: > 500 } ? summary[..500] : summary;
        job.LastError = error is { Length: > 1000 } ? error[..1000] : error;
        job.FinishedAtUtc = now;
        job.HeartbeatAtUtc = now;
        // In the audit trail under whoever asked for it, as the work was theirs.
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = now,
            ActorUserId = job.CreatedByUserId,
            ActorEmail = job.CreatedByEmail,
            Action = "BulkJobFinished",
            Details = $"type={job.Type}; status={status}; succeeded={job.Succeeded}; failed={job.Failed}; total={job.Total}",
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;
}
