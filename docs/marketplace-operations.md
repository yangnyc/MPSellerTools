# Multichannel catalog — running it

How to switch it on, set up accounts, publish, and deal with what goes
wrong. Design is in [`multichannel-catalog.md`](multichannel-catalog.md);
what is and is not verified against each marketplace is in
[`marketplace-integrations.md`](marketplace-integrations.md).

Everything is done through the tenant workspace's API (same origin and
session cookie as the workspace, `X-CSRF-TOKEN` on writes). **There is no
workspace screen for it yet.** All endpoints need the TenantAdmin role unless
marked *(read: Employee)*.

## Defaults: nothing leaves the machine

Out of the box, after the migration:

- every channel operation is a **dry run** — built, validated, recorded, not sent;
- orders do **not** touch stock, exactly as before;
- the worker runs, gives old products their default variant, and otherwise finds nothing to do.

## Bulk jobs

Work on thousands of listings is not done one click at a time. A **bulk
job** is asked for once — on the workspace's **Jobs** page, or with
*Publish all drafts* on a marketplace's product page — and carried out in
the background by `BulkJobWorker` in the tenant's host.

| Job | What it does to each listing of the account |
| --- | --- |
| Publish every draft | checks it; if it passes, marks it wanted on sale. One held back stays a draft, with the reason |
| Take everything off sale | marks everything wanted on sale as wanted off sale |
| Send again what did not arrive | queues again what is wanted on sale but is not sent, not listed or was rejected |
| Check every listing | checks it and records what stops it being published; sends nothing |
| Read the store's listings | eBay and Magento only: the same read as *Refresh* on the Listings page |

- Jobs are rows in the tenant's own database (`BulkJobs`), run one at a
  time, oldest first, a hundred listings to a batch. Progress is written
  after each batch.
- A job only *queues* its listings: the sync queue then sends each one,
  at its own pace and with its own retries, exactly as a single Publish
  would. With live writes off for the account that is a dry run.
- Stopping a waiting job cancels it at once; a running one stops after the
  batch it is on. What it already did stays done.
- A job works out what is left from the listings as they are, so one cut
  short by a restart is carried on by the next host to start, and *Run
  again* picks up whatever a finished job did not do.
- The same job cannot be queued twice at once for one account. The first
  hundred items held back are kept with the job; the rest are only counted.
- `Marketplace:WorkerEnabled = false` stops this worker too.

API: `GET /api/bulk-jobs`, `POST /api/bulk-jobs` (`type`, `channelAccountId`),
`POST /api/bulk-jobs/{id}/cancel`, `POST /api/bulk-jobs/{id}/run-again`,
`DELETE /api/bulk-jobs/{id}`. TenantAdmin only.

## Configuration

Host-wide, in the `Marketplace` section of the tenant host's configuration
(`appsettings.json`, the per-instance config file, or environment variables
such as `Marketplace__LiveWritesEnabled`). These are the shipped defaults;
there are no secrets in this section.

```json
{
  "Marketplace": {
    "LiveWritesEnabled": true,
    "InventoryAccountingEnabled": false,
    "WorkerEnabled": true,
    "AutoBackfill": true,
    "WorkerIntervalSeconds": 15,
    "LeaseSeconds": 300,
    "MaxAttempts": 6,
    "PollSeconds": 60,
    "MaxAwaitHours": 24,
    "OrderImportMinutes": 15,
    "OrderStaleMinutes": 60,
    "ReconcileMinutes": 360
  }
}
```

| Setting | Effect |
| --- | --- |
| `LiveWritesEnabled` | The operator's off switch, on unless turned off. Whether an account writes is the company's decision, made with the account's own `liveWritesEnabled`, which starts `false`. While this is `false`, every operation on every marketplace account is a dry run whatever the account says |
| `InventoryAccountingEnabled` | Orders reserve stock, ship it on completion and release it on cancellation. Required before stock is sent to any marketplace |
| `WorkerEnabled` | Runs the sync worker inside the tenant host |
| `AutoBackfill` | Runs the catalog backfill once at startup |
| `LeaseSeconds` | A running job older than this is assumed abandoned and re-queued. Keep it well above the slowest channel call |
| `MaxAttempts` | Sends per job before it is failed |
| `PollSeconds` / `MaxAwaitHours` | How often, and for how long, an accepted submission is checked for its final result |
| `OrderImportMinutes` | How often each importing account is read |
| `OrderStaleMinutes` | After this long without an order import, the account shows `ordersStale` |
| `ReconcileMinutes` | How often a live listing's state is read back from its channel |

The website channel is local and is never a dry run.

## The worker

The worker is a hosted service in each tenant's own `TenantHost` process, so
starting the tenant starts it (`scripts/Start-Dev.ps1`, F5 on DevHost). It
uses the tenant's database as its queue; there is nothing else to run. Each
tick it queues due order imports and reconciliations, releases expired
holds, dispatches the outbox, recovers expired leases, and runs due jobs.

More than one tenant process against the same database is not a supported
setup in this project, but claiming is atomic, so it would not double-send.

If the database has pending migrations the worker logs a warning and stands
by; it does not migrate. The provisioning worker applies migrations when it
creates or resumes a tenant.

## Migration and backfill

```powershell
# Local/test databases only. From the solution root, x64 SDK first on PATH (see README).
.\scripts\Start-Dev.ps1        # the provisioning worker migrates each local tenant as it resumes it
```

A production connection string is not permission to migrate production: do
that deliberately, through your deployment process
(`docs/windows-deployment.md`).

The backfill runs at startup. To look first, or run it by hand:

```http
POST /api/catalog/backfill?dryRun=true     → what it would do, and any conflicts
POST /api/catalog/backfill?dryRun=false    → do it
```

It is safe to repeat. A conflict (a product whose SKU is already another
product's variant) is listed and left for you to resolve by renaming one of
the two.

## Setting up a channel

```http
POST /api/channels
{ "channel": 1, "name": "Amazon US", "environment": 0, "sellerId": "A1…",
  "settings": {}, "isEnabled": true, "liveWritesEnabled": false,
  "inventorySyncEnabled": false, "orderImportEnabled": false, "priceConflictPolicy": 2 }

POST /api/channels/{accountId}/markets        { "marketplaceCode": "ATVPDKIKX0DER", "language": "en-US", "currency": "USD" }
PUT  /api/channels/{accountId}/credentials    { "credentials": { … } }      (write-only)
PUT  /api/channels/category-mappings          { "channelMarketId": "…", "internalCategory": "Mugs", "externalCategoryId": "MUG",
                                                "requirements": { "required": ["Color"], "enums": { "Material": ["Ceramic","Glass"] },
                                                                  "conditional": [ { "when": "Material", "equals": "Glass", "require": ["Fragile"] } ] },
                                                "requirementsSource": "…", "requirementsVersion": "…" }
POST /api/channels/category-mappings/{id}/fetch-requirements     (needs live access; eBay: required aspects and allowed values, Amazon: definition version and schema link only)
```

`channel`: 0 eBay, 1 Amazon, 2 Walmart, 3 Website. `environment`: 0 sandbox,
1 production. `priceConflictPolicy`: 0 restore local, 1 import remote,
2 report conflict.

| Channel | Marketplace code | Credentials | Settings |
| --- | --- | --- | --- |
| Amazon | `ATVPDKIKX0DER` (US) | `clientId`, `clientSecret`, `refreshToken` (Login with Amazon); `sellerId` on the account | optional `endpoint` to use a region other than North America |
| eBay | `EBAY_US` | none here — keys and consent are on the workspace's eBay page. One eBay account per company | `merchantLocationKey`, `fulfillmentPolicyId`, `paymentPolicyId`, `returnPolicyId` (create these in eBay first) |
| Walmart | `WALMART_US` | `clientId`, `clientSecret` | `itemSpecVersion` — the item spec version Walmart currently publishes |
| Website | `default` (created with the account) | none | none |

Credentials are encrypted with the instance's data protection keys, are
never returned by any endpoint, and are not written to the audit trail or
job history. Tokens are cached in memory per account and fetched under a
lock, so concurrent work shares one token request.

**eBay and write access.** The eBay page's consent is read-only until an
eBay channel account has `liveWritesEnabled`. After turning that on, connect
again on the eBay page so the seller grants the write scopes.

## Catalog data

```http
GET  /api/catalog/products/{id}                 (read: Employee)  product, variants with stock, identifiers, images
PUT  /api/catalog/products/{id}/content         { "brand": "…", "description": "…", "category": "Mugs" }
POST /api/catalog/products/{id}/variants        { "sku": "TEE-RED", "name": "Red", "options": { "Color": "Red" }, "condition": 0, "price": 21 }
PUT  /api/catalog/variants/{variantId}          same body, plus "rowVersion"
PUT  /api/catalog/products/{id}/identifiers     { "type": 1, "value": "012345678905", "variantId": null }     (0 GTIN, 1 UPC, 2 EAN, 3 ISBN, 4 MPN)
POST /api/catalog/products/{id}/media           { "url": "https://…", "altText": "…", "variantId": null, "purpose": 0, "position": 0 }
PUT  /api/catalog/variants/{variantId}/inventory  { "onHand": 12, "safetyStock": 2 }
POST /api/catalog/returns                       { "variantId": "…", "quantity": 1, "receiptId": "RMA-1042" }
```

The product screen (`/api/products`) still edits the product's own SKU,
price and stock; those are its default variant's. Identifiers are stored as
entered and never generated. A return only adds stock once it is recorded
as received; the same `receiptId` counts once.

## Listing a variant on a channel

```http
PUT  /api/channel-listings
{ "channelMarketId": "…", "variantId": "…", "sellerSku": null, "fulfillmentMode": 0,
  "content": { "title": { "value": "Channel title" }, "brand": { "cleared": true } },
  "attributes": { "Color": "Blue" }, "priceOverride": 18.00, "quantityCap": 5,
  "existingCatalogItemId": "B0…" }

GET  /api/channel-listings?productId=…&accountId=…   (read: Employee)
POST /api/channel-listings/{id}/validate             problems, with the requirements source and version checked against
POST /api/channel-listings/{id}/preview?operation=0  exactly what would be sent — no credentials, nothing sent
POST /api/channel-listings/{id}/dry-run?operation=0  the same through the worker; ends as DryRunCompleted
POST /api/channel-listings/{id}/publish              202 queued, or 422 with the problems
POST /api/channel-listings/{id}/deactivate           202 queued
POST /api/channel-listings/{id}/retry                202 queued — after fixing data or an account
PUT  /api/channel-listings/groups                    { "channelMarketId": "…", "productId": "…", "groupKey": "TEE",
                                                       "variationAttributes": ["Color"], "listingIds": ["…","…"] }
```

`operation`: 0 content, 1 price, 2 inventory, 3 deactivate.
`fulfillmentMode`: 0 merchant, 1 fulfilled by the channel (FBA/WFS — no
quantity is ever sent).

In `content`, leave a field out to keep its override, send `null` to go back
to the product's value, `{"value":…}` to override, `{"cleared":true}` to
send it empty. Saving never publishes; a new listing is a draft.

`publish` answers `202` when the work is **queued**. Whether buyers can see
the item is `observedStatus` on the listing, which only the channel's own
answer changes:

| `observedStatus` | Meaning |
| --- | --- |
| 0 Unknown | never asked |
| 1 NotListed | the channel has no such listing (or a group is waiting for its other variants) |
| 2 Processing | the channel accepted a submission and has not finished |
| 3 Live | the channel reports it buyable |
| 4 Inactive | on the channel, not on sale |
| 5 Rejected | turned down — see `issues` |

`content`, `price` and `inventory` each show `desired` and `confirmed`
versions; equal means the channel has confirmed the current state.

## Watching the queue

```http
GET /api/channels/sync/health
GET /api/channels/sync/jobs?status=5&listingId=…&take=100
GET /api/channels/sync/jobs/{jobId}            with the attempt history
GET /api/channels/order-issues
POST /api/channels/order-issues/{id}/resolve
POST /api/channels/{accountId}/import-orders   queue an import now
```

`health` gives: undispatched events, jobs pending / running / awaiting the
channel / failed / needing correction, expired leases, jobs on a retry, the
oldest pending job, the last success, and per account whether live writes
are effective, when orders were last imported, whether they are stale, the
last account-level error and open order issues.

| Job status | Meaning | What to do |
| --- | --- | --- |
| 0 Pending | waiting, or waiting out a retry delay (`nextAttemptAtUtc`) | nothing |
| 1 Running | a worker holds it | nothing; recovered automatically if the worker died |
| 2 AwaitingRemote | accepted by the channel, final answer pending | nothing |
| 3 Succeeded | confirmed by the channel | — |
| 4 Failed | permanent refusal, authorization problem, or attempts used up | read `lastError`; fix the account if it says so; `retry` |
| 5 NeedsCorrection | the data is wrong — see the listing's `issues` | fix the listing or product; `retry` |
| 6 DryRunCompleted | built, not sent | turn on live writes when ready |
| 7 Cancelled | superseded by a newer job, or its listing/account is gone | — |

Error classes: 1 transient (retried automatically), 2 authorization (fix
credentials or consent; the message is also on the account), 3 data
correction, 4 permanent.

**Retries** happen by themselves for transient errors. `retry` on a listing
re-sends its current state after a manual fix. **Reconciliation** runs every
`ReconcileMinutes` for live listings on accounts with live writes, reading
the channel's state and applying the account's price policy. A job that is
accepted but never gets a final answer is failed after `MaxAwaitHours` and
picked up by the next reconciliation.

Order lines that could not be matched to a variant (`UnknownSku`), matched
more than one (`AmbiguousSku`), or could not be covered by stock
(`InventoryShortfall`) wait in `order-issues`. Nothing is guessed; resolving
one is acknowledging you dealt with it.

Audit entries (`/api/audit`) are written for account, credential, listing,
price, stock and backfill changes, and by the worker (as `system`) for each
confirmed channel operation, price restore/import and order import.

## Going live, one step at a time

1. Apply the migration (local/test first). Check `POST /api/catalog/backfill?dryRun=true` shows no conflicts.
2. Create accounts with `liveWritesEnabled: false`. Add markets, mappings, listings. Use `validate` and `preview`.
3. `publish` and confirm the jobs end `DryRunCompleted` with sensible requests in `preview`.
4. Check everything marked unverified in [`marketplace-integrations.md`](marketplace-integrations.md) against the channel's current documentation, in its sandbox, with real credentials.
5. Turn on one account's `liveWritesEnabled`, sandbox environment (and check `Marketplace:LiveWritesEnabled` has not been turned off). Publish one listing; watch it reach `Live`.
6. Turn on `orderImportEnabled` for every marketplace account, then `Marketplace:InventoryAccountingEnabled`, then `inventorySyncEnabled`. The API refuses stock sync until orders are imported from every active channel.

## Rolling back

| To undo | Do | Effect |
| --- | --- | --- |
| Calls to a channel | account `liveWritesEnabled: false`, or `Marketplace:LiveWritesEnabled: false` for all | operations become dry runs at once. Listings already on the channel stay as they are |
| Stock accounting | `Marketplace:InventoryAccountingEnabled: false` | orders stop reserving and deducting; stock stops being sent. Existing reservations stay recorded and are not released |
| Background work | `Marketplace:WorkerEnabled: false` | nothing is dispatched or sent; events and jobs accumulate and are handled when it is turned back on |
| The application build | redeploy the previous build | the added tables and columns are ignored by it. Do **not** run the migration's `Down`: it drops the new tables and with them variants, stock history, listings and the ids needed to end listings on the channels |

Turning a flag off reverts behaviour. It never deletes what was collected.

## End-to-end scenario, reproducibly

No credentials are needed; this is what the automated tests do.

```powershell
$env:PATH = "$HOME\.dotnet-x64;$env:PATH"; $env:DOTNET_ROOT = "$HOME\.dotnet-x64"   # ARM64 machines only, see README
dotnet test tests\MPSellerTools.Tests --filter "FullyQualifiedName~ChannelSyncTests|FullyQualifiedName~MultichannelFixtureTests|FullyQualifiedName~CatalogAndInventoryTests|FullyQualifiedName~CatalogBackfillTests|FullyQualifiedName~ChannelPayloadTests|FullyQualifiedName~ChannelSyncWorkerTests"
```

`ChannelSyncTests.Ebay_end_to_end_…` walks create → confirmed live → price
update → stock update → order import → the same order again, against an
in-memory eBay. The same steps by hand against a running tenant are the
requests in the sections above, in order; with live writes off they end as
dry runs.

## Progress checklist

Done and tested (against fakes):

- [x] Assessment, model mapping, design note
- [x] Variants, identifiers, media, channel accounts/markets, category mappings, listings, groups, external references
- [x] Inventory locations, balances, reservations, movements; atomic last-unit reservation; idempotent ship/release/return
- [x] Expand-only migration; default warehouse seeded; schema guard for unmigrated databases
- [x] Backfill: batched, repeatable, dry run, conflicts reported, order lines linked
- [x] Legacy product columns kept in step with the default variant in one place
- [x] Outbox written with the change; dispatcher with coalescing; per-stream versions
- [x] Worker: atomic claim, leases, recovery, backoff with jitter and Retry-After, error classes, attempt history, batches with per-item results
- [x] Desired / observed / operation state kept apart; stale answers cannot overwrite newer confirmations
- [x] Dry run by default; host and account switches; preview endpoint
- [x] eBay adapter: content, offer, publish, groups, bulk price/quantity, withdraw, status, orders, requirements; look-before-create
- [x] Amazon adapter: put/patch listing, offer-on-ASIN, parent/child, status polling, orders v2026-01-01; feed payload builder
- [x] Walmart adapter: item and match feeds, per-SKU feed results, item status, price, inventory, retire, orders
- [x] Website channel and storefront projection
- [x] One order pipeline for local, eBay-import and channel orders; unknown/ambiguous SKUs and shortfalls routed to issues
- [x] Price conflict policies; reconciliation; recurring order imports
- [x] Admin API; RBAC; secrets encrypted and never returned; audit entries; health endpoint

Not done — needs credentials, a decision, or more work:

- [ ] **Any call to a real marketplace API.** Sandbox verification of all three adapters
- [ ] Everything marked *from prior knowledge* or *unverified* in `marketplace-integrations.md`, the Walmart feed layout above all
- [ ] Validating payloads against the channels' machine-readable schemas for the chosen product types
- [x] Workspace UI: per-marketplace listings, add-product and account settings pages (eBay, Amazon, Walmart), the sync queue with order-line issues (`/sync`), and inventory with its ledger (`/inventory`). Product details (brand, description, category, identifiers, images) are edited from the Products page and category mappings from each marketplace's settings. A listing's own title, price, quantity cap, category, ASIN and choice of pictures are edited from its marketplace page, which also runs the validation and shows the request preview. Not yet in the UI: variants, category requirements and attributes, listing groups, and description/brand overrides — those are still API-only
- [x] Amazon: catalog search for existing ASINs (`GET /api/channels/{id}/catalog-search`, from prior knowledge of Catalog Items 2022-04-01, tested against a fake only)
- [ ] eBay: creating policies and locations; `bulkMigrateListing` for listings made outside the Inventory API; per-variant withdrawal from a group
- [ ] Walmart: `MP_MAINTENANCE` for updates; item spec retrieval
- [ ] Inbound notifications/webhooks with provider signature checks (`InboxEvent` is ready for their deduplication)
- [x] Proactive rate limiting: one token bucket per channel per host (`Marketplace:RequestsPerSecond`, default 5, burst `Marketplace:RequestBurst`, 0 = off), on top of `Retry-After` and backoff. Per channel, not per API operation: the channels' own per-operation limits are not modelled
- [ ] Automated allocation of shared stock across channels (today: full quantity with optional per-listing caps)
- [ ] FBA / WFS inventory import and lifecycle
- [ ] A storefront, cart and checkout — none exists in the project
- [x] Playwright coverage with mocked APIs (`tests/e2e/ui-tests/workspace-marketplace.spec.ts`); no browser test runs against a real tenant host for these pages
- [ ] Decision: when to stop writing `Product.Price` / `StockQuantity` and retire the legacy `Listings` table
