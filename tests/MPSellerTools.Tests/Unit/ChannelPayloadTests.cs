using System.Text.Json;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.Tests.Unit;

/// <summary>
/// What each adapter would send, checked against the field names of the
/// channels' published contracts. No network: payloads are built from a
/// listing and inspected. Every external id below is made up for the tests.
/// </summary>
public class ChannelPayloadTests
{
    private static readonly ListingGroupSnapshot Family = new(
        "TEE-FAMILY", ["Color"], ["TEE-BLUE", "TEE-RED"], new Dictionary<string, IReadOnlyList<string>> { ["Color"] = ["Blue", "Red"] });

    private static ListingSnapshot Snapshot(
        SalesChannel channel, string marketplace, string sku = "TEE-BLUE", ListingGroupSnapshot? group = null, string? catalogItemId = null,
        string? category = "CAT", IReadOnlyDictionary<string, string>? attributes = null) => new(
        Guid.NewGuid(), channel, marketplace, "en-US", "USD", sku, "Cotton tee - Blue", "A soft cotton tee.", "TestBrand", category,
        attributes ?? new Dictionary<string, string>(), new Dictionary<string, string> { ["Color"] = "Blue" },
        ItemCondition.New, 19.9m, 4, FulfillmentMode.Merchant,
        new Dictionary<ProductIdentifierType, string> { [ProductIdentifierType.Upc] = "000012345678", [ProductIdentifierType.Mpn] = "TT-01" },
        ["https://images.example.com/tee-1.jpg", "https://images.example.com/tee-2.jpg"],
        0.5m, "lb", 10m, 8m, 1m, "in", group, catalogItemId, new CategoryRequirements());

    private static JsonElement Json(object payload) => JsonDocument.Parse(ChannelHttp.Serialize(payload)).RootElement;

    private static ChannelContext Context(SalesChannel channel, string marketplace, string settings = "{}", string? sellerId = null) => new(
        new ChannelAccount { Id = Guid.NewGuid(), Channel = channel, Name = "Test", SellerId = sellerId },
        new ChannelMarket { Id = Guid.NewGuid(), MarketplaceCode = marketplace },
        JsonDocument.Parse(settings).RootElement);

    [Fact]
    public void Ebay_inventory_item_and_offer_carry_the_fields_the_inventory_api_names()
    {
        var s = Snapshot(SalesChannel.Ebay, "EBAY_US", category: "15687");
        var item = Json(EbayPayloads.InventoryItem(s));
        Assert.Equal(4, item.GetProperty("availability").GetProperty("shipToLocationAvailability").GetProperty("quantity").GetInt32());
        Assert.Equal("NEW", item.GetProperty("condition").GetString());
        var product = item.GetProperty("product");
        Assert.Equal("Cotton tee - Blue", product.GetProperty("title").GetString());
        Assert.Equal("000012345678", product.GetProperty("upc")[0].GetString());
        Assert.Equal("TT-01", product.GetProperty("mpn").GetString());
        // Item specifics: the variant's options and the brand, each as a list of values.
        Assert.Equal("Blue", product.GetProperty("aspects").GetProperty("Color")[0].GetString());
        Assert.Equal("TestBrand", product.GetProperty("aspects").GetProperty("Brand")[0].GetString());
        Assert.Equal(2, product.GetProperty("imageUrls").GetArrayLength());
        Assert.Equal("POUND", item.GetProperty("packageWeightAndSize").GetProperty("weight").GetProperty("unit").GetString());
        Assert.Equal("INCH", item.GetProperty("packageWeightAndSize").GetProperty("dimensions").GetProperty("unit").GetString());

        var context = Context(SalesChannel.Ebay, "EBAY_US",
            """{"merchantLocationKey":"MAIN","fulfillmentPolicyId":"fp","paymentPolicyId":"pp","returnPolicyId":"rp"}""");
        var offer = Json(EbayPayloads.Offer(s, context));
        Assert.Equal(("TEE-BLUE", "EBAY_US", "FIXED_PRICE", "15687", "MAIN"), (
            offer.GetProperty("sku").GetString(), offer.GetProperty("marketplaceId").GetString(), offer.GetProperty("format").GetString(),
            offer.GetProperty("categoryId").GetString(), offer.GetProperty("merchantLocationKey").GetString()));
        // Money goes as a decimal string with its currency, never as a float.
        Assert.Equal(("19.90", "USD"), (
            offer.GetProperty("pricingSummary").GetProperty("price").GetProperty("value").GetString(),
            offer.GetProperty("pricingSummary").GetProperty("price").GetProperty("currency").GetString()));
        Assert.Equal("rp", offer.GetProperty("listingPolicies").GetProperty("returnPolicyId").GetString());
    }

    [Fact]
    public void Ebay_variations_are_one_group_of_several_skus_and_updates_touch_only_what_they_are_for()
    {
        var s = Snapshot(SalesChannel.Ebay, "EBAY_US", group: Family);
        var group = Json(EbayPayloads.ItemGroup(s));
        Assert.Equal(["TEE-BLUE", "TEE-RED"], group.GetProperty("variantSKUs").EnumerateArray().Select(x => x.GetString()));
        var specification = group.GetProperty("variesBy").GetProperty("specifications")[0];
        Assert.Equal("Color", specification.GetProperty("name").GetString());
        Assert.Equal(["Blue", "Red"], specification.GetProperty("values").EnumerateArray().Select(x => x.GetString()));

        var priceOnly = Json(EbayPayloads.PriceQuantity([(s, "offer-test-only")], price: true, quantity: false)).GetProperty("requests")[0];
        Assert.False(priceOnly.TryGetProperty("shipToLocationAvailability", out _));
        Assert.False(priceOnly.GetProperty("offers")[0].TryGetProperty("availableQuantity", out _));
        Assert.Equal("19.90", priceOnly.GetProperty("offers")[0].GetProperty("price").GetProperty("value").GetString());

        var quantityOnly = Json(EbayPayloads.PriceQuantity([(s, "offer-test-only")], price: false, quantity: true)).GetProperty("requests")[0];
        Assert.False(quantityOnly.GetProperty("offers")[0].TryGetProperty("price", out _));
        Assert.Equal(4, quantityOnly.GetProperty("shipToLocationAvailability").GetProperty("quantity").GetInt32());
    }

    [Fact]
    public void Amazon_tells_an_offer_on_an_existing_asin_apart_from_a_new_catalog_item()
    {
        var offer = Json(AmazonPayloads.PutListing(Snapshot(SalesChannel.Amazon, "ATVPDKIKX0DER", catalogItemId: "B0TESTONLY1")));
        Assert.Equal("LISTING_OFFER_ONLY", offer.GetProperty("requirements").GetString());
        var attributes = offer.GetProperty("attributes");
        Assert.Equal("B0TESTONLY1", attributes.GetProperty("merchant_suggested_asin")[0].GetProperty("value").GetString());
        // An offer describes the sale, not the product: no title or identifier is sent against someone else's catalog item.
        Assert.False(attributes.TryGetProperty("item_name", out _));
        Assert.False(attributes.TryGetProperty("externally_assigned_product_identifier", out _));

        var created = Json(AmazonPayloads.PutListing(Snapshot(SalesChannel.Amazon, "ATVPDKIKX0DER", category: "SHIRT")));
        Assert.Equal(("SHIRT", "LISTING"), (created.GetProperty("productType").GetString(), created.GetProperty("requirements").GetString()));
        attributes = created.GetProperty("attributes");
        Assert.Equal("Cotton tee - Blue", attributes.GetProperty("item_name")[0].GetProperty("value").GetString());
        Assert.Equal("ATVPDKIKX0DER", attributes.GetProperty("item_name")[0].GetProperty("marketplace_id").GetString());
        Assert.Equal(("upc", "000012345678"), (
            attributes.GetProperty("externally_assigned_product_identifier")[0].GetProperty("type").GetString(),
            attributes.GetProperty("externally_assigned_product_identifier")[0].GetProperty("value").GetString()));
        Assert.Equal("new_new", attributes.GetProperty("condition_type")[0].GetProperty("value").GetString());
        var price = attributes.GetProperty("purchasable_offer")[0];
        Assert.Equal(("USD", 19.9m), (price.GetProperty("currency").GetString(), price.GetProperty("our_price")[0].GetProperty("schedule")[0].GetProperty("value_with_tax").GetDecimal()));
        Assert.Equal(("DEFAULT", 4), (
            attributes.GetProperty("fulfillment_availability")[0].GetProperty("fulfillment_channel_code").GetString(),
            attributes.GetProperty("fulfillment_availability")[0].GetProperty("quantity").GetInt32()));
    }

    [Fact]
    public void Amazon_children_point_at_a_parent_that_holds_no_offer_and_patches_replace_one_attribute()
    {
        var s = Snapshot(SalesChannel.Amazon, "ATVPDKIKX0DER", group: Family, category: "SHIRT");
        var child = Json(AmazonPayloads.PutListing(s)).GetProperty("attributes");
        Assert.Equal("child", child.GetProperty("parentage_level")[0].GetProperty("value").GetString());
        Assert.Equal(("variation", "TEE-FAMILY"), (
            child.GetProperty("child_parent_sku_relationship")[0].GetProperty("child_relationship_type").GetString(),
            child.GetProperty("child_parent_sku_relationship")[0].GetProperty("parent_sku").GetString()));
        Assert.Equal("COLOR", child.GetProperty("variation_theme")[0].GetProperty("name").GetString());

        var parent = Json(AmazonPayloads.PutParent(s));
        Assert.Equal("LISTING_PRODUCT_ONLY", parent.GetProperty("requirements").GetString());
        Assert.Equal("parent", parent.GetProperty("attributes").GetProperty("parentage_level")[0].GetProperty("value").GetString());
        // A parent is not sold: it has no price and no quantity.
        Assert.False(parent.GetProperty("attributes").TryGetProperty("purchasable_offer", out _));
        Assert.False(parent.GetProperty("attributes").TryGetProperty("fulfillment_availability", out _));

        var patch = Json(AmazonPayloads.PatchQuantity(s, 0)).GetProperty("patches")[0];
        Assert.Equal(("replace", "/attributes/fulfillment_availability"), (patch.GetProperty("op").GetString(), patch.GetProperty("path").GetString()));
        Assert.Equal(0, patch.GetProperty("value")[0].GetProperty("quantity").GetInt32());
        Assert.Equal("/attributes/purchasable_offer", Json(AmazonPayloads.PatchPrice(s)).GetProperty("patches")[0].GetProperty("path").GetString());
    }

    [Fact]
    public void Amazon_bulk_feed_follows_the_json_listings_feed_schema_version_2()
    {
        var feed = Json(AmazonPayloads.ListingsFeed("SELLERTESTONLY",
        [
            Snapshot(SalesChannel.Amazon, "ATVPDKIKX0DER", "TEE-BLUE", category: "SHIRT"),
            Snapshot(SalesChannel.Amazon, "ATVPDKIKX0DER", "TEE-RED", category: "SHIRT"),
        ]));

        // Required by the schema: header.sellerId, header.version "2.0", and per message a messageId, sku and operationType.
        Assert.Equal(("SELLERTESTONLY", "2.0"), (feed.GetProperty("header").GetProperty("sellerId").GetString(), feed.GetProperty("header").GetProperty("version").GetString()));
        var messages = feed.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal([1, 2], messages.Select(m => m.GetProperty("messageId").GetInt32()));
        Assert.Equal(["TEE-BLUE", "TEE-RED"], messages.Select(m => m.GetProperty("sku").GetString()));
        Assert.All(messages, m =>
        {
            Assert.Equal("UPDATE", m.GetProperty("operationType").GetString());
            Assert.Equal("SHIRT", m.GetProperty("productType").GetString());
            Assert.Contains(m.GetProperty("requirements").GetString(), new[] { "LISTING", "LISTING_PRODUCT_ONLY", "LISTING_OFFER_ONLY" });
            Assert.True(m.GetProperty("attributes").EnumerateObject().Any());
        });
    }

    [Fact]
    public void Walmart_separates_matching_an_existing_item_from_setting_up_a_new_one()
    {
        var full = Json(WalmartPayloads.ItemFeed([Snapshot(SalesChannel.Walmart, "WALMART_US", group: Family, category: "T-Shirts")], "TEST-ONLY-SPEC"));
        Assert.Equal(("WALMART_US", "TEST-ONLY-SPEC"), (
            full.GetProperty("MPItemFeedHeader").GetProperty("businessUnit").GetString(), full.GetProperty("MPItemFeedHeader").GetProperty("version").GetString()));
        var item = full.GetProperty("MPItem")[0];
        Assert.Equal("TEE-BLUE", item.GetProperty("Orderable").GetProperty("sku").GetString());
        Assert.Equal(("UPC", "000012345678"), (
            item.GetProperty("Orderable").GetProperty("productIdentifiers").GetProperty("productIdType").GetString(),
            item.GetProperty("Orderable").GetProperty("productIdentifiers").GetProperty("productId").GetString()));
        var visible = item.GetProperty("Visible").GetProperty("T-Shirts");
        Assert.Equal("TEE-FAMILY", visible.GetProperty("variantGroupId").GetString());
        Assert.Equal("Blue", visible.GetProperty("Color").GetString());

        var match = Snapshot(SalesChannel.Walmart, "WALMART_US", attributes: new Dictionary<string, string> { ["walmart.setup"] = "match" });
        Assert.True(WalmartPayloads.IsMatch(match));
        var matched = Json(WalmartPayloads.MatchFeed([match], "TEST-ONLY-SPEC")).GetProperty("MPItem")[0].GetProperty("Item");
        // A match brings an offer to Walmart's own item: identifier and price, no content of ours.
        Assert.Equal(19.9m, matched.GetProperty("price").GetDecimal());
        Assert.False(matched.TryGetProperty("productName", out _));

        var price = Json(WalmartPayloads.Price(match)).GetProperty("pricing")[0];
        Assert.Equal(("BASE", "USD", 19.9m), (
            price.GetProperty("currentPriceType").GetString(), price.GetProperty("currentPrice").GetProperty("currency").GetString(),
            price.GetProperty("currentPrice").GetProperty("amount").GetDecimal()));
        var quantity = Json(WalmartPayloads.Inventory(match, 4)).GetProperty("quantity");
        Assert.Equal(("EACH", 4), (quantity.GetProperty("unit").GetString(), quantity.GetProperty("amount").GetInt32()));
    }

    [Fact]
    public void Each_channel_reports_what_it_is_missing_with_the_field_at_fault()
    {
        var bare = Snapshot(SalesChannel.Ebay, "EBAY_US", category: null) with
        {
            Description = null, ImageUrls = [], Identifiers = new Dictionary<ProductIdentifierType, string>(), Brand = null,
        };
        ListingWork Work(ListingSnapshot s) => new(s, ListingDesiredState.Active, 1, 1, 1, new Dictionary<ExternalResourceType, string>(), true);

        var ebay = new EbayChannelAdapter(null!, null!, null!, null!, null!).Validate(Work(bare), Context(SalesChannel.Ebay, "EBAY_US"));
        Assert.Contains(ebay, i => i.Path == "description");
        Assert.Contains(ebay, i => i.Path == "category");
        Assert.Contains(ebay, i => i.Path == "images");
        Assert.Contains(ebay, i => i.Path == "account.settings.returnPolicyId");
        Assert.All(ebay, i => Assert.Equal(SalesChannel.Ebay, i.Channel));

        var amazon = new AmazonChannelAdapter(null!, null!, null!);
        var missing = amazon.Validate(Work(bare with { Channel = SalesChannel.Amazon }), Context(SalesChannel.Amazon, "ATVPDKIKX0DER"));
        Assert.Contains(missing, i => i.Path == "identifiers");
        Assert.Contains(missing, i => i.Path == "account.sellerId");
        // No identifier is invented: with the ASIN of an existing item none is needed.
        var onAsin = amazon.Validate(Work(bare with { Channel = SalesChannel.Amazon, ExistingCatalogItemId = "B0TESTONLY1" }), Context(SalesChannel.Amazon, "ATVPDKIKX0DER", sellerId: "S"));
        Assert.Empty(onAsin);

        var walmart = new WalmartChannelAdapter(null!, null!, null!).Validate(Work(bare with { Channel = SalesChannel.Walmart }), Context(SalesChannel.Walmart, "WALMART_US"));
        Assert.Contains(walmart, i => i.Path == "identifiers");
        Assert.Contains(walmart, i => i.Path == "account.settings.itemSpecVersion");
    }

    [Fact]
    public void Channel_fulfilled_stock_is_never_given_a_quantity_from_the_merchant_warehouse()
    {
        var product = new Product { Id = Guid.NewGuid(), Sku = "FBA-1", Name = "Thing" };
        var variant = new ProductVariant { Id = Guid.NewGuid(), ProductId = product.Id, Sku = "FBA-1", Price = 9m };
        var market = new ChannelMarket { Id = Guid.NewGuid(), MarketplaceCode = "ATVPDKIKX0DER" };
        ChannelListing Listing(FulfillmentMode mode, int? cap = null) =>
            new() { Id = Guid.NewGuid(), ChannelMarketId = market.Id, VariantId = variant.Id, SellerSku = "FBA-1", FulfillmentMode = mode, QuantityCap = cap };

        ListingSnapshot Compose(ChannelListing listing) =>
            ListingComposer.Compose(product, variant, listing, market, SalesChannel.Amazon, null, availableToSell: 12, [], [], null, null);

        Assert.Equal(0, Compose(Listing(FulfillmentMode.ChannelFulfilled)).Quantity);
        Assert.Equal(12, Compose(Listing(FulfillmentMode.Merchant)).Quantity);
        // A listing's own cap limits what that one channel is told.
        Assert.Equal(5, Compose(Listing(FulfillmentMode.Merchant, cap: 5)).Quantity);
        Assert.False(Json(AmazonPayloads.PutListing(Compose(Listing(FulfillmentMode.ChannelFulfilled)))).GetProperty("attributes").TryGetProperty("fulfillment_availability", out _));
    }

    [Theory]
    [InlineData(10, 3, 2, 5)]
    [InlineData(4, 3, 2, 0)]
    [InlineData(1, 0, 0, 1)]
    public void Available_to_sell_is_on_hand_less_reserved_less_safety_stock_and_never_negative(int onHand, int reserved, int safety, int expected) =>
        Assert.Equal(expected, Availability.ToSell(onHand, reserved, safety));

    [Fact]
    public void Retries_back_off_with_jitter_are_capped_and_defer_to_the_channels_retry_after()
    {
        var random = new Random(7);
        var first = RetryPolicy.Delay(1, null, random).TotalSeconds;
        var third = RetryPolicy.Delay(3, null, random).TotalSeconds;
        Assert.InRange(first, 30, 37.5);
        Assert.InRange(third, 120, 150);
        Assert.InRange(RetryPolicy.Delay(30, null, random).TotalSeconds, 3600, 4500);
        Assert.Equal(TimeSpan.FromSeconds(90), RetryPolicy.Delay(1, TimeSpan.FromSeconds(90), random));
        // Not every job comes back at the same instant.
        Assert.NotEqual(RetryPolicy.Delay(2, null, random), RetryPolicy.Delay(2, null, random));
    }

    [Fact]
    public void Failures_are_sorted_into_retry_fix_the_account_fix_the_data_or_give_up()
    {
        Assert.Equal(SyncErrorClass.Transient, ChannelHttp.Classify(429));
        Assert.Equal(SyncErrorClass.Transient, ChannelHttp.Classify(503));
        Assert.Equal(SyncErrorClass.Authorization, ChannelHttp.Classify(401));
        Assert.Equal(SyncErrorClass.Authorization, ChannelHttp.Classify(403));
        Assert.Equal(SyncErrorClass.DataCorrection, ChannelHttp.Classify(400));
        Assert.Equal(SyncErrorClass.Permanent, ChannelHttp.Classify(405));
    }

    [Fact]
    public void An_override_is_inherited_set_or_cleared_and_survives_being_stored()
    {
        var overrides = ContentOverrides.Parse(null);
        Assert.Equal("Base", overrides.Resolve(ContentOverrides.Title, "Base"));

        overrides.Set(ContentOverrides.Title, "Channel title");
        overrides.Clear(ContentOverrides.Description);
        var stored = ContentOverrides.Parse(overrides.ToJson());
        Assert.Equal("Channel title", stored.Resolve(ContentOverrides.Title, "Base"));
        Assert.Null(stored.Resolve(ContentOverrides.Description, "Base description"));
        Assert.Equal("Base brand", stored.Resolve(ContentOverrides.Brand, "Base brand"));

        stored.Inherit(ContentOverrides.Description);
        Assert.Equal("Base description", stored.Resolve(ContentOverrides.Description, "Base description"));
    }
}
