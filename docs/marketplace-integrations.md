# Marketplace integrations — what was verified, and what was not

Documentation was read on **2026-10-06**. Only the marketplaces' own
developer sites were used. Every adapter has been exercised against an
in-process fake of the channel's API and against nothing else: **no request
has been sent to a real Amazon, eBay or Walmart endpoint**, sandbox included,
because no credentials were available. Recheck before going live — API
versions and item specs move.

Three levels of confidence are used below:

- **Verified** — the operation, path or field was read in the official documentation on the date above.
- **From prior knowledge** — written from knowledge of the API, not re-read in the documentation on that date. Treat as an assumption.
- **Unverified** — the documentation could not be reached or did not show it. Must be checked before live use.

Code: `src/MPSellerTools.TenantHost/Marketplace/Channels/`. Each adapter has
a static `*Payloads` class that builds request bodies with no network, and
an adapter class that does the transport.

## Amazon — Selling Partner API

Sources:
[Manage product listings](https://developer-docs.amazon.com/sp-api/docs/manage-product-listings-guide),
[Building listings management workflows](https://developer-docs.amazon.com/sp-api/docs/building-listings-management-workflows-guide),
[Listings feed type values](https://developer-docs.amazon.com/sp-api/docs/listings-feed-type-values),
[listings-feed-schema-v2.json](https://github.com/amzn/selling-partner-api-models/blob/main/schemas/feeds/listings-feed-schema-v2.json),
[Orders API migration guide](https://developer-docs.amazon.com/sp-api/docs/orders-api-migration-guide),
[searchOrders](https://developer-docs.amazon.com/sp-api/reference/searchorders).
(`developer-docs.amazon.com` currently redirects to `developer-docs.amazon`.)

| Operation | Used for | Status |
| --- | --- | --- |
| Listings Items API **2021-08-01** `putListingsItem` | create/replace a listing | Verified: version, operation, `productType` / `requirements` / `attributes` body, `ACCEPTED` / `INVALID` status, `submissionId`, `issues[]` |
| `requirements`: `LISTING`, `LISTING_OFFER_ONLY`, `LISTING_PRODUCT_ONLY` | new item vs. offer on an existing ASIN vs. variation parent | Verified |
| `merchant_suggested_asin` | offer on an existing ASIN | Verified (attribute name) |
| `purchasable_offer[].our_price[].schedule[].value_with_tax`, `fulfillment_availability[].fulfillment_channel_code = DEFAULT` + `quantity` | price, merchant quantity | Verified (quoted from the guide) |
| `patchListingsItem` with `op: replace`, `path: /attributes/…` | price and quantity updates | Verified: operation and patch shape. The guide says inventory "is managed through the Inventory API (not detailed in this guide)" — patching `fulfillment_availability` for merchant-fulfilled stock is **from prior knowledge** |
| `getListingsItem` with `includedData=summaries,issues,offers,fulfillmentAvailability`; `summaries[].status` containing `BUYABLE` | the only thing that marks a listing live | Verified |
| `parentage_level`, `child_parent_sku_relationship`, `variation_theme` | variation parent/child | Verified (attribute names and example) |
| Path `/listings/2021-08-01/items/{sellerId}/{sku}?marketplaceIds=…&issueLocale=…` | all of the above | **From prior knowledge** — the reference page fetched listed operations and parameters but not the path template |
| Product Type Definitions API **2020-09-01** `getDefinitionsProductType` | requirement source and version | Verified: version, `requirements`, `parentageLevel`. Only the definition's version and schema link are stored; the linked JSON Schema is **not** downloaded or evaluated, so no required attributes are derived from it |
| Orders API **v2026-01-01** `GET /orders/2026-01-01/orders` (`searchOrders`) | order import | Verified: path, query parameters, `orders[]`, `fulfillment.fulfillmentStatus` values, `fulfillment.fulfilledBy`, `orderItems[].product.sellerSku`. `pagination.nextToken` as the field name of the next-page token is **unverified** |
| Orders API v0 | — | Not used: deprecated 2026-01-28, removal 2027-03-27 (verified) |
| `JSON_LISTINGS_FEED` (schema 2.0) | bulk submissions | Verified: header `sellerId`, `version: "2.0"`, messages with `messageId`, `sku`, `operationType`, max 25,000. The **payload builder** exists and is tested; **submitting a feed through the Feeds API 2021-06-30 is not implemented** — listings go one per call |
| XML / flat-file listing feeds | — | Not used: unsupported since 2025-07-31 (verified) |
| Login with Amazon token exchange (`https://api.amazon.com/auth/o2/token`, `grant_type=refresh_token`), `x-amz-access-token` header, regional endpoints | authentication | **From prior knowledge** |
| Catalog Items API 2022-04-01 `searchCatalogItems` | finding an existing ASIN | Not implemented. The seller supplies the ASIN (`existingCatalogItemId` on the listing) |
| Notifications (`LISTINGS_ITEM_STATUS_CHANGE`, `LISTINGS_ITEM_ISSUES_CHANGE`) | — | Not implemented; status is polled |

Behaviour to know:

- Every write ends as *accepted*, never *live*; the job then polls
  `getListingsItem` until the listing is `BUYABLE` or reports an error.
- "Deactivate" sets the merchant quantity to 0. It does not call
  `deleteListingsItem`, which would remove the SKU.
- No GTIN, ASIN or exemption is ever generated. A new catalog item needs a
  stored identifier or the attribute
  `supplier_declared_has_product_identifier_exemption` supplied by the seller.
- Attribute names beyond the handful above depend on the product type. The
  listing's own attributes are passed through lower-cased with underscores;
  getting them right for a given product type is the seller's job, and
  Amazon's validation is the judge.

## eBay — Inventory API

Sources:
[From inventory item to offer](https://developer.ebay.com/api-docs/sell/static/inventory/inventory-item-to-offer.html),
[Inventory API overview](https://developer.ebay.com/api-docs/sell/inventory/overview.html),
[Inventory API OpenAPI spec](https://developer.ebay.com/develop/api/spec/inventory_api.json).

| Operation | Used for | Status |
| --- | --- | --- |
| Workflow: location → `createOrReplaceInventoryItem` → `createOffer` → `publishOffer` | single-SKU listing | Verified |
| Workflow: items → `createOrReplaceInventoryItemGroup` → one offer per variant → `publishOfferByInventoryItemGroup` | multi-SKU listing | Verified, including that category, description, policies, location, marketplace and format must match across the variants |
| Payment, fulfillment and return policies and a `merchantLocationKey` on every offer | account settings required before publishing | Verified |
| `PUT /inventory_item/{sku}` with `Content-Language`; SKU max length 50 | content | Verified (spec) |
| `POST /bulk_update_price_quantity`, up to 25 offers per call | price and quantity | Verified: path and limit. Request/response field names (`requests[].offers[].offerId/availableQuantity/price`, `responses[].statusCode/errors`) are **from prior knowledge** — the spec fetch returned only a summary |
| `POST /offer`, `PUT /offer/{id}`, `GET /offer?sku=…&marketplace_id=…`, `GET /offer/{id}`, `POST /offer/{id}/publish`, `POST /offer/{id}/withdraw`, `POST /offer/publish_by_inventory_item_group`, `PUT /inventory_item_group/{key}` | offers | Operation names verified; exact paths and response fields **from prior knowledge** |
| OAuth scopes `sell.inventory`, `sell.inventory.readonly` | consent | Verified. `sell.fulfillment`, `sell.account.readonly` **from prior knowledge** |
| Refresh-token grant with no `scope` returning all consented scopes | write access token | **From prior knowledge** |
| Taxonomy API `get_default_category_tree_id`, `get_item_aspects_for_category` | category requirements | **From prior knowledge** |
| Fulfillment API `getOrders` | order import | Already in the project before this work (`EbayClient`) |
| Listings created outside the Inventory API | — | Verified: they cannot be managed through it. `bulkMigrateListing` exists for converting them; **not implemented** |
| Business policy and location *creation* (Account API, `createInventoryLocation`) | — | Not implemented: ids are entered in the account's settings |

Behaviour to know:

- The existing consent asks for read-only scopes. Write scopes are requested
  only once an eBay channel account has `liveWritesEnabled`; the seller must
  then **connect again** on the eBay page to grant them.
- Before creating an offer the adapter looks it up by SKU and marketplace.
  After a create whose answer was lost, the retry finds the offer and
  updates it instead of creating a second one.
- A grouped variant is not withdrawn on its own (that would end the whole
  listing); offers are withdrawn per offer, which for a group member is a
  limitation to resolve before relying on per-variant deactivation.
- The manual import on the eBay page still works as before and now shares
  the order pipeline (`OrderIngestionService`).

## Walmart — Marketplace API

Sources:
[Integrate with Marketplace APIs](https://developer.walmart.com/us-marketplace/docs/integrate-with-marketplace-apis),
[Bulk item setup](https://developer.walmart.com/us-marketplace/docs/bulk-item-setup-1),
[Feed item status API](https://developer.walmart.com/us-marketplace/docs/feed-item-status-api),
[Get an access token](https://developer.walmart.com/us-marketplace/docs/get-an-access-token),
[All orders](https://developer.walmart.com/us-marketplace/docs/get-all-orders).

| Operation | Used for | Status |
| --- | --- | --- |
| `POST /v3/feeds?feedType=MP_ITEM` | full setup of a new item | Verified: endpoint, feed type, up to 10,000 items / 25 MB |
| `feedType=MP_ITEM_MATCH` | offer on an existing Walmart catalog item | Verified: feed type and purpose |
| **Feed body layout** (`MPItemFeedHeader`, `MPItem[].Orderable` / `Visible[productType]`, `productIdentifiers`) | item setup | **Unverified.** The documentation page links to downloadable spec files and shows no structure. The layout follows prior knowledge of the 4.x/5.x item spec and **must be checked against the current spec file for each product type before live use** |
| Item spec version | feed header | **Unverified by design**: nothing is assumed. The account must carry `itemSpecVersion` in its settings or validation refuses the listing |
| `GET /v3/feeds/{feedId}?includeDetails=true` — `feedStatus` (`RECEIVED`, `INPROGRESS`, `PROCESSED`, `ERROR`), `itemDetails.itemIngestionStatus[].sku` / `ingestionStatus` (`SUCCESS`, `INPROGRESS`, `DATA_ERROR`, `SYSTEM_ERROR`, `TIMEOUT_ERROR`) | per-SKU results | Verified. The `ingestionErrors.ingestionError[]` shape is **from prior knowledge** |
| `GET /v3/items/{sku}` — `publishedStatus`, `lifecycleStatus`, `wpid`, `unpublishedReasons` | whether the item is actually published | **From prior knowledge** |
| `PUT /v3/inventory?sku=…` | quantity | Endpoint verified; body (`quantity.unit = EACH`, `amount`) **from prior knowledge** |
| `PUT /v3/price` | price | **Unverified** — not found in the pages read |
| `DELETE /v3/items/{sku}` | retire | **Unverified** (the documentation also lists a `RETIRE_ITEM` feed type) |
| `GET /v3/orders?createdStartDate=…` — 180 days of history | order import | Endpoint verified; response field names **from prior knowledge** |
| `POST /v3/token`, `grant_type=client_credentials`, 15-minute tokens; headers `WM_SEC.ACCESS_TOKEN`, `WM_QOS.CORRELATION_ID`, `WM_SVC.NAME` | authentication | Verified. Sandbox host `sandbox.walmartapis.com` verified from a sample request |
| `MP_MAINTENANCE` feed | updating an existing item | Verified to exist; **not used** — content updates are sent as `MP_ITEM` again, which needs checking |
| Item spec retrieval, WFS feeds | — | Not implemented |

Behaviour to know:

- A feed id means the feed was taken; `SUCCESS` means the SKU was ingested;
  only `publishedStatus = PUBLISHED` marks the listing live.
- A feed is not idempotent. Before setting up a SKU with no known Walmart
  item id, the adapter reads the item; if Walmart already has it, its state
  is used and no second feed is sent.
- `SYSTEM_ERROR` / `TIMEOUT_ERROR` for one SKU sends that SKU again, alone.
- A listing is set up by match when it has an `existingCatalogItemId` or the
  attribute `walmart.setup = match`.

## Company website

No external API. Publishing is a local state change carried out by the same
queue, so the website behaves like any other channel (drafts, validation,
deactivation, audit) without an HTTP call. `/api/storefront/products`
returns what is live there. The project has no storefront, cart or checkout
to connect it to; that is a business decision still to be made.

## Test fixtures

All external ids in the tests (`B0TESTONLY1`, `WPIDTESTONLY1`,
`FEED-TEST-ONLY-1`, `sub-test-only-1`, `110700009999`, offer ids `o-…`, the
UPCs `000012345678` / `000012345685`, seller ids) are invented.
`ExternalReference.IsTestOnly` exists to mark seeded fixture ids in a
database; nothing a channel issues is ever flagged with it.

Payload tests assert the field names listed as verified above. They are
**not** validated against the marketplaces' machine-readable schemas (Amazon
product type JSON Schemas, Walmart item spec, eBay OpenAPI): doing that needs
the schema files for the chosen product types, which is the first thing to
do once product types are decided.
