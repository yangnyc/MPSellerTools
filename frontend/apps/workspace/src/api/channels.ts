import { apiFetch } from "../lib/api";

// Mirrors MarketplaceContracts.cs in MPSellerTools.TenantHost.Contracts: the
// company's sales channels and how its products are offered on them.

export type ChannelKind = 0 | 1 | 2 | 3 | 4; // Ebay, Amazon, Walmart, Website, Magento

// What the workspace needs to know to give a marketplace its pages: the same
// two pages serve every marketplace, told apart only by this.
export type Marketplace = {
  kind: ChannelKind;
  name: string;
  // The marketplace's own code for its United States site.
  marketplaceCode: string;
  // Where its pages live, e.g. "/amazon".
  path: string;
  icon: string;
  // Whether the marketplace identifies the seller by an id entered here.
  asksSellerId: boolean;
  // What a draft still needs before it can be published, in the marketplace's own terms.
  publishNeeds: string;
  // The secrets the account is saved with here; empty when they are kept elsewhere.
  credentials: { key: string; label: string }[];
  // Where the secrets are kept instead, when not here.
  credentialsNote?: string;
  // The account's non-secret settings the marketplace's adapter reads. `link`: the value is an address,
  // shown as a link to open once saved, with a button to change it.
  settings: { key: string; label: string; help: string; link?: boolean }[];
  // Whether its catalog can be searched for an item to offer on.
  catalogSearch: boolean;
  // What the marketplace calls the place a category maps to.
  categoryLabel: string;
  // What its settings page is called, when not "Settings"; the page then leads its menu.
  settingsName?: string;
  // Said under the credentials: where they come from.
  credentialsHelp?: string;
  // Whether the saved address and credentials can be tried out from its settings page.
  connectionTest?: boolean;
  // Whether it has a single site, with no sandbox beside production.
  singleEnvironment?: boolean;
};

// Where a marketplace's settings page lives.
export const settingsPath = (marketplace: Marketplace) => `${marketplace.path}/${marketplace.settingsName ? "connection" : "settings"}`;

export const EBAY: Marketplace = {
  kind: 0,
  name: "eBay",
  marketplaceCode: "EBAY_US",
  // "/ebay" itself is the connection page, where the keys and the seller's consent live.
  path: "/ebay/products",
  icon: "storefront",
  asksSellerId: false,
  publishNeeds: "eBay also needs a description, a category, an image, and the account's policies and location before the draft can be published.",
  credentials: [],
  credentialsNote: "eBay's keys and the seller's consent are saved on the eBay connection page.",
  settings: [
    { key: "merchantLocationKey", label: "Inventory location key", help: "The key of the location your stock ships from, as created in your eBay account." },
    { key: "fulfillmentPolicyId", label: "Shipping policy ID", help: "Your eBay fulfillment (shipping) business policy." },
    { key: "paymentPolicyId", label: "Payment policy ID", help: "Your eBay payment business policy." },
    { key: "returnPolicyId", label: "Return policy ID", help: "Your eBay return business policy." },
  ],
  catalogSearch: false,
  categoryLabel: "eBay category ID",
};

export const AMAZON: Marketplace = {
  kind: 1,
  name: "Amazon",
  marketplaceCode: "ATVPDKIKX0DER",
  path: "/amazon",
  icon: "shopping_cart",
  asksSellerId: true,
  publishNeeds: "Amazon also needs a description, a category and a UPC or ASIN before the draft can be published.",
  credentials: [
    { key: "clientId", label: "LWA client ID" },
    { key: "clientSecret", label: "LWA client secret" },
    { key: "refreshToken", label: "Refresh token" },
  ],
  settings: [],
  catalogSearch: true,
  categoryLabel: "Amazon product type",
};

export const WALMART: Marketplace = {
  kind: 2,
  name: "Walmart",
  marketplaceCode: "WALMART_US",
  path: "/walmart",
  icon: "local_mall",
  asksSellerId: false,
  publishNeeds: "Walmart also needs a brand, a category, an image and a GTIN or UPC before the draft can be published.",
  credentials: [
    { key: "clientId", label: "Client ID" },
    { key: "clientSecret", label: "Client secret" },
  ],
  settings: [{ key: "itemSpecVersion", label: "Item spec version", help: "The version of Walmart's item spec your feeds are built for. Leave empty for the default." }],
  catalogSearch: false,
  categoryLabel: "Walmart product type",
};

// The company's own Magento Open Source store, reached through its REST API. Its one "marketplace" is the store's default view.
export const MAGENTO: Marketplace = {
  kind: 4,
  name: "Magento",
  marketplaceCode: "default",
  path: "/magento",
  icon: "shopping_bag",
  asksSellerId: false,
  publishNeeds: "Magento also needs the store's address and access token, on the Connection page, before the draft can be published.",
  credentials: [{ key: "accessToken", label: "Integration access token" }],
  credentialsHelp:
    "The access token of an integration created in the Magento admin under System › Extensions › Integrations, with access to Catalog, Inventory and Sales. Magento 2.4.4 and later also need “Allow OAuth Access Tokens to be used as standalone Bearer tokens” switched on, under Stores › Configuration › Services › OAuth.",
  settings: [
    { key: "baseUrl", label: "Store address", help: "Where the store is, starting with https://. Its API is reached under /rest.", link: true },
    { key: "attributeSetId", label: "Attribute set ID", help: "The attribute set new products are filed under. Leave empty for Default (4)." },
    { key: "weightUnit", label: "Weight unit", help: "The store's own weight unit: lbs or kgs. Leave empty for lbs." },
  ],
  catalogSearch: false,
  categoryLabel: "Magento category ID",
  settingsName: "Connection",
  connectionTest: true,
  singleEnvironment: true,
};

export type PriceConflictPolicy = 0 | 1 | 2; // RestoreLocal, ImportRemote, ReportConflict

// Everything about an account that can be changed after it was added. Credentials are saved separately.
export type ChannelAccountUpdate = {
  channel: ChannelKind;
  name: string;
  environment: 0 | 1;
  sellerId: string | null;
  settings: Record<string, string>;
  isEnabled: boolean;
  liveWritesEnabled: boolean;
  inventorySyncEnabled: boolean;
  orderImportEnabled: boolean;
  priceConflictPolicy: PriceConflictPolicy;
};

export type CatalogSearchResult = { catalogItemId: string; title: string | null; brand: string | null };

// Every marketplace the workspace has pages for, in the order they are shown.
export const marketplaces = (): Marketplace[] => [EBAY, AMAZON, WALMART, MAGENTO];

export type ChannelMarket = { id: string; marketplaceCode: string; language: string; currency: string };

// Credentials are never part of it, only whether any are saved.
export type ChannelAccount = {
  id: string;
  channel: ChannelKind;
  name: string;
  environment: 0 | 1; // Sandbox, Production
  sellerId: string | null;
  // Non-secret settings, such as eBay's policy ids.
  settings: Record<string, string> | null;
  hasCredentials: boolean;
  isEnabled: boolean;
  liveWritesEnabled: boolean;
  // What actually applies: the account's own switch and the host's together.
  effectiveLiveWrites: boolean;
  inventorySyncEnabled: boolean;
  orderImportEnabled: boolean;
  priceConflictPolicy: PriceConflictPolicy;
  lastOrderImportAtUtc: string | null;
  lastError: string | null;
  markets: ChannelMarket[];
};

export type ListingDesiredState = 0 | 1 | 2; // Draft, Active, Inactive
export type ListingObservedStatus = 0 | 1 | 2 | 3 | 4 | 5; // Unknown, NotListed, Processing, Live, Inactive, Rejected

export type ListingIssue = { channel: ChannelKind; path: string; code: string; message: string };

export type ChannelListing = {
  id: string;
  channelMarketId: string;
  channelAccountId: string;
  channel: ChannelKind;
  marketplaceCode: string;
  variantId: string;
  sellerSku: string;
  // The listing's own settings; null where it follows the product or the category mapping.
  externalCategoryId: string | null;
  contentOverrides: Record<string, { value?: string | null; cleared?: boolean }>;
  priceOverride: number | null;
  fulfillmentMode: 0 | 1; // Merchant, ChannelFulfilled
  quantityCap: number | null;
  // Ids the marketplace gave it, or the seller supplied: CatalogItem (an ASIN), Offer, Listing.
  references: Record<string, string>;
  // Its own page on the marketplace or store, once it has a number there.
  storeUrl?: string | null;
  hasPriceConflict: boolean;
  observedPrice: number | null;
  observedAtUtc: string | null;
  // Every picture of the product this listing could use, and the ones chosen for this marketplace
  // in the order they are sent; imageIds null means all of them, in the product's own order.
  availableImages: ListingImage[];
  imageIds: string[] | null;
  effectiveImageUrls: string[];
  imageRules: ImageRules;
  productId: string;
  effectiveTitle: string | null;
  effectivePrice: number;
  effectiveQuantity: number;
  desiredState: ListingDesiredState;
  observedStatus: ListingObservedStatus;
  issues: ListingIssue[] | null;
};

// Wanted on sale, but the marketplace has not got it: never sent (a dry run, say), not there, or turned down.
// Such a listing can be sent again; one that is live or still being processed cannot usefully be.
export const canSendAgain = (listing: ChannelListing) => listing.desiredState === 1 && [0, 1, 5].includes(listing.observedStatus);

export type ListingImage = { id: string; url: string };

// What the marketplace asks of a listing's pictures. Only the counts are checked by the server:
// the pictures are addresses, so the rest is guidance. `source` says where the figures come from.
export type ImageRules = { minImages: number; maxImages: number; mainImage: string; formats: string; size: string; source: string };

export type ListingValidation = { valid: boolean; issues: ListingIssue[] };

// What publishing would send, built without sending anything.
export type ListingPreview = { liveWrites: boolean; issues: ListingIssue[]; requests: { method: string; url: string; body: unknown }[] };

// null: follow the product (title, price) or the category mapping, or have no cap. An empty ASIN removes it.
export type ListingEdit = {
  title: string | null;
  priceOverride: number | null;
  quantityCap: number | null;
  externalCategoryId: string | null;
  existingCatalogItemId: string;
  // The product's pictures to send, in order; empty to send all of them in the product's own order.
  imageIds: string[];
};

// liveWrites false: the queued work is carried out as a dry run and nothing reaches the channel.
export type ListingQueued = { listingId: string; desiredState: ListingDesiredState; liveWrites: boolean };

export type CatalogVariant = {
  id: string;
  sku: string;
  name: string | null;
  price: number;
  isDefault: boolean;
  isArchived: boolean;
  onHand: number;
  reserved: number;
  safetyStock: number;
  availableToSell: number;
};

export type ProductIdentifierType = 0 | 1 | 2 | 3 | 4; // Gtin, Upc, Ean, Isbn, Mpn
export const IDENTIFIER_LABELS: Record<ProductIdentifierType, string> = { 0: "GTIN", 1: "UPC", 2: "EAN", 3: "ISBN", 4: "MPN" };

// variantId null: the identifier or image belongs to the whole product.
export type CatalogIdentifier = { id: string; type: ProductIdentifierType; value: string; variantId: string | null };
export type CatalogMedia = { id: string; url: string; altText: string | null; variantId: string | null; purpose: 0 | 1 | 2; position: number };

// The product with what the marketplaces are told about it, beyond its SKU, name, price and stock.
export type CatalogProduct = {
  id: string;
  sku: string;
  name: string;
  brand: string | null;
  description: string | null;
  category: string | null;
  variants: CatalogVariant[];
  identifiers: CatalogIdentifier[];
  media: CatalogMedia[];
};

// Where one of the company's own categories goes on one marketplace.
export type CategoryMapping = {
  id: string;
  channelMarketId: string;
  internalCategory: string;
  externalCategoryId: string;
  requirementsSource: string | null;
  requirementsRetrievedAtUtc: string | null;
};

export const ChannelsApi = {
  list: () => apiFetch<ChannelAccount[]>("/api/channels"),
  // A new account never writes to its channel until that is switched on for it.
  create: (data: { channel: ChannelKind; name: string; sellerId: string | null }) =>
    apiFetch<ChannelAccount>("/api/channels", {
      method: "POST",
      body: JSON.stringify({
        ...data,
        environment: 0,
        isEnabled: true,
        liveWritesEnabled: false,
        inventorySyncEnabled: false,
        orderImportEnabled: false,
        priceConflictPolicy: 2,
      }),
    }),
  addMarket: (accountId: string, marketplaceCode: string) =>
    apiFetch<ChannelMarket>(`/api/channels/${accountId}/markets`, { method: "POST", body: JSON.stringify({ marketplaceCode }) }),
  categoryMappings: () => apiFetch<CategoryMapping[]>("/api/channels/category-mappings"),
  // Saving the same internal category again for the marketplace replaces where it goes.
  saveCategoryMapping: (channelMarketId: string, internalCategory: string, externalCategoryId: string) =>
    apiFetch<CategoryMapping>("/api/channels/category-mappings", {
      method: "PUT",
      body: JSON.stringify({ channelMarketId, internalCategory, externalCategoryId }),
    }),
  update: (accountId: string, data: ChannelAccountUpdate) =>
    apiFetch<ChannelAccount>(`/api/channels/${accountId}`, { method: "PUT", body: JSON.stringify(data) }),
  removeCategoryMapping: (mappingId: string) => apiFetch<void>(`/api/channels/category-mappings/${mappingId}`, { method: "DELETE" }),
  // Write-only: what is saved is never sent back.
  setCredentials: (accountId: string, credentials: Record<string, string>) =>
    apiFetch<void>(`/api/channels/${accountId}/credentials`, { method: "PUT", body: JSON.stringify({ credentials }) }),
  // Changes only the ones given and keeps the rest.
  editCredentials: (accountId: string, credentials: Record<string, string>) =>
    apiFetch<void>(`/api/channels/${accountId}/credentials/edit`, { method: "POST", body: JSON.stringify({ credentials }) }),
  removeCredentials: (accountId: string) => apiFetch<void>(`/api/channels/${accountId}/credentials`, { method: "DELETE" }),
  // Asks the marketplace itself, so it needs live access to be on for the account.
  catalogSearch: (accountId: string, query: string) =>
    apiFetch<CatalogSearchResult[]>(`/api/channels/${accountId}/catalog-search?q=${encodeURIComponent(query)}`),
};

export type BulkJobType = 0 | 1 | 2 | 3 | 4 | 5 | 6; // PublishDrafts, TakeOffSale, SendAgain, CheckListings, ReadStore, SendEverythingAgain, ImportFromAmazon
export type BulkJobStatus = 0 | 1 | 2 | 3 | 4 | 5; // Queued, Running, Succeeded, CompletedWithErrors, Failed, Cancelled

// What each kind of job is called, and what it does, in the order they are offered.
export const BULK_JOB_TYPES: Record<BulkJobType, { label: string; help: string }> = {
  0: { label: "Publish every draft", help: "Checks each draft and publishes the ones that pass. The ones held back stay drafts, with the reason." },
  1: { label: "Take everything off sale", help: "Asks for every listing that is on sale, or on its way there, to come off sale. Nothing is deleted." },
  2: { label: "Send again what did not arrive", help: "Sends again every listing that was published but never reached the sales channel, or was turned down." },
  3: { label: "Check every listing", help: "Checks each listing again and records what stops it being published. Nothing is sent." },
  4: { label: "Read the store's listings", help: "Reads what eBay or the Magento store itself has, into Listings. Products new to your catalog are added to it." },
  5: { label: "Send every published listing again", help: "Sends everything that is on sale again, whether or not it arrived before: for a change the listings do not show by themselves, such as a category now going elsewhere." },
  6: { label: "Import from Amazon", help: "Makes products out of a list of items in Amazon's catalog. It is started from Import, where the list is pasted." },
};

// A job that needs more than a sales channel to start (a list of items) is started from its own page.
export const STARTED_ELSEWHERE: BulkJobType[] = [6];

// A large piece of work on one sales channel's listings, carried out in the background.
export type BulkJob = {
  id: string;
  type: BulkJobType;
  status: BulkJobStatus;
  channelAccountId: string;
  // Null when the sales channel has since been removed.
  accountName: string | null;
  channel: ChannelKind | null;
  // 0 for work that is not counted in items (reading a store).
  total: number;
  processed: number;
  succeeded: number;
  failed: number;
  cancelRequested: boolean;
  summary: string | null;
  lastError: string | null;
  // The first items held back, and why.
  errors: { item: string; message: string }[];
  createdByEmail: string;
  createdAtUtc: string;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
};

export const BulkJobsApi = {
  list: () => apiFetch<BulkJob[]>("/api/bulk-jobs"),
  get: (id: string) => apiFetch<BulkJob>(`/api/bulk-jobs/${id}`),
  start: (type: BulkJobType, channelAccountId: string) =>
    apiFetch<BulkJob>("/api/bulk-jobs", { method: "POST", body: JSON.stringify({ type, channelAccountId }) }),
  // A waiting job is cancelled at once; a running one stops after the items it is on.
  cancel: (id: string) => apiFetch<BulkJob>(`/api/bulk-jobs/${id}/cancel`, { method: "POST" }),
  // A new job for the same work, which picks up whatever is left to do.
  runAgain: (id: string) => apiFetch<BulkJob>(`/api/bulk-jobs/${id}/run-again`, { method: "POST" }),
  remove: (id: string) => apiFetch<void>(`/api/bulk-jobs/${id}`, { method: "DELETE" }),
};

// A category of the Magento store. `path` is its name with its parents', below the store's root category.
export type MagentoStoreCategory = { id: number; parentId: number; name: string; path: string; level: number; isActive: boolean; productCount: number };

// One of the company's own product categories, and where it goes in the store.
export type MagentoCategoryRow = {
  category: string;
  products: number;
  mappingId: string | null;
  storeCategoryId: string | null;
  storeCategoryPath: string | null;
  // Mapped to a number the store no longer has.
  storeCategoryMissing: boolean;
};

export type MagentoCategories = {
  storeReachable: boolean;
  storeError: string | null;
  storeCategories: MagentoStoreCategory[];
  categories: MagentoCategoryRow[];
  // Where a product goes whose own category is not mapped.
  defaultCategoryId: string | null;
  // Whether anything may be created in the store from here.
  liveWrites: boolean;
};

// What matching or creating did (or, in a dry run, would do) for each category.
export type MagentoCategoryBulk = {
  dryRun: boolean;
  mapped: number;
  created: number;
  items: { category: string; storeCategoryId: string | null; storeCategoryPath: string; created: number; error: string | null }[];
};

export const MagentoCategoriesApi = {
  get: () => apiFetch<MagentoCategories>("/api/magento/categories"),
  create: (data: { name: string; parentId: number | null; isActive: boolean; includeInMenu: boolean }) =>
    apiFetch<MagentoStoreCategory>("/api/magento/categories/create", { method: "POST", body: JSON.stringify(data) }),
  // With dryRun, nothing is created or mapped: the answer says what would be.
  createMissing: (data: { isActive: boolean; includeInMenu: boolean; dryRun: boolean }) =>
    apiFetch<MagentoCategoryBulk>("/api/magento/categories/create-missing", { method: "POST", body: JSON.stringify(data) }),
  match: () => apiFetch<MagentoCategoryBulk>("/api/magento/categories/match", { method: "POST" }),
  setDefault: (categoryId: string | null) => apiFetch<void>("/api/magento/categories/default", { method: "PUT", body: JSON.stringify({ categoryId }) }),
  removeUnused: () => apiFetch<{ removed: number }>("/api/magento/categories/remove-unused", { method: "POST" }),
  // Mappings to store categories that are gone: each goes to its namesake in the store, or is removed.
  // `unresolved` are the categories with products that were left with no store category.
  repair: () => apiFetch<{ repointed: number; removed: number; unresolved: string[] }>("/api/magento/categories/repair", { method: "POST" }),
};

// Whether Amazon's catalog can be read, and through which account; `problem` says why not.
export type AmazonImportStatus = {
  ready: boolean;
  accountName: string | null;
  marketplace: string | null;
  problem: string | null;
  // What an imported product's SKU starts with when none is chosen: the prefix, then the ASIN.
  skuPrefix: string;
  maxItems: number;
};

// An item of Amazon's catalog as it would become a product.
export type AmazonItem = {
  asin: string;
  title: string | null;
  brand: string | null;
  // The description and the bullet points, as one text.
  description: string | null;
  category: string | null;
  productType: string | null;
  listPrice: number | null;
  currency: string | null;
  imageUrls: string[];
  identifiers: { type: ProductIdentifierType; value: string }[];
  weightValue: number | null;
  weightUnit: string | null;
  length: number | null;
  width: number | null;
  height: number | null;
  dimensionUnit: string | null;
  suggestedSku: string;
  // The product already under the suggested SKU, when there is one.
  existingProductId: string | null;
};

// outcome: 0 created, 1 brought up to date.
export type AmazonImported = { outcome: 0 | 1 | 2; productId: string; sku: string };

export const AmazonImportApi = {
  status: () => apiFetch<AmazonImportStatus>("/api/amazon/import/status"),
  // By ASIN, the address of the item's page on Amazon, or a barcode. Nothing is saved.
  lookup: (query: string) => apiFetch<AmazonItem>("/api/amazon/import/lookup", { method: "POST", body: JSON.stringify({ query }) }),
  // By any of those, or by words of the item's name: the one item, or the ten Amazon puts first.
  find: (query: string) => apiFetch<AmazonItem[]>("/api/amazon/import/find", { method: "POST", body: JSON.stringify({ query }) }),
  importItem: (data: { asin: string; sku: string; price: number | null; stockQuantity: number; updateExisting: boolean }) =>
    apiFetch<AmazonImported>("/api/amazon/import/item", { method: "POST", body: JSON.stringify(data) }),
  // One item to a line, optionally followed by a comma and the SKU to give it. Answers with the background job.
  importBulk: (data: { lines: string; skuPrefix: string; updateExisting: boolean }) =>
    apiFetch<{ jobId: string; total: number }>("/api/amazon/import/bulk", { method: "POST", body: JSON.stringify(data) }),
};

export type MagentoStore = { storeAddress: string | null; storeViews: string[]; currency: string | null };

// listings: how many products the store reported; created: how many of them were new to the catalog here.
export type MagentoImport = { created: number; listings: number };

export const MagentoApi = {
  // Reaches the store with the saved address and token, and says which store answered.
  test: () => apiFetch<MagentoStore>("/api/magento/test", { method: "POST" }),
  // Reads the store's catalog as the listings on Magento.
  importListings: () => apiFetch<MagentoImport>("/api/magento/import/listings", { method: "POST" }),
};

export type InventoryItem = {
  variantId: string;
  productId: string;
  sku: string;
  productName: string;
  variantName: string | null;
  price: number;
  onHand: number;
  reserved: number;
  safetyStock: number;
  availableToSell: number;
  updatedAtUtc: string | null;
};

// accountingEnabled false: orders leave stock alone, so nothing is ever reserved.
export type InventoryOverview = { accountingEnabled: boolean; items: InventoryItem[] };

export type InventoryMovementType = 0 | 1 | 2 | 3 | 4; // Adjustment, Reserve, Release, Ship, ReturnReceipt

export type InventoryMovement = {
  id: string;
  variantId: string;
  sku: string;
  type: InventoryMovementType;
  onHandDelta: number;
  reservedDelta: number;
  reference: string | null;
  occurredAtUtc: string;
};

export const InventoryApi = {
  overview: () => apiFetch<InventoryOverview>("/api/catalog/inventory"),
  movements: (variantId?: string) =>
    apiFetch<InventoryMovement[]>(`/api/catalog/inventory/movements${variantId ? `?variantId=${variantId}` : ""}`),
  adjust: (variantId: string, onHand: number, safetyStock: number) =>
    apiFetch<void>(`/api/catalog/variants/${variantId}/inventory`, { method: "PUT", body: JSON.stringify({ onHand, safetyStock }) }),
  // The receipt reference makes it safe to send twice: the same one is counted once.
  receiveReturn: (variantId: string, quantity: number, receiptId: string) =>
    apiFetch<{ recorded: boolean }>("/api/catalog/returns", { method: "POST", body: JSON.stringify({ variantId, quantity, receiptId }) }),
};

export type SyncOperation = 0 | 1 | 2 | 3 | 4 | 5; // Content, Price, Inventory, Deactivate, OrderImport, Reconcile
export const SYNC_OPERATION_LABELS: Record<SyncOperation, string> = {
  0: "Listing content",
  1: "Price",
  2: "Stock",
  3: "Take off sale",
  4: "Order import",
  5: "Reconcile",
};

// Pending, Running, AwaitingRemote, Succeeded, Failed, NeedsCorrection, DryRunCompleted, Cancelled
export type SyncJobStatus = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7;
export type SyncErrorClass = 0 | 1 | 2 | 3 | 4; // None, Transient, Authorization, DataCorrection, Permanent

export type SyncAttempt = {
  number: number;
  startedAtUtc: string;
  finishedAtUtc: string;
  outcome: SyncJobStatus;
  errorClass: SyncErrorClass;
  httpStatus: number | null;
  externalRequestId: string | null;
  detail: string | null;
};

export type SyncJob = {
  id: string;
  channelAccountId: string;
  // Null for account-wide work such as an order import.
  channelListingId: string | null;
  operation: SyncOperation;
  status: SyncJobStatus;
  attempts: number;
  maxAttempts: number;
  nextAttemptAtUtc: string;
  externalSubmissionId: string | null;
  errorClass: SyncErrorClass;
  lastError: string | null;
  dryRun: boolean;
  createdAtUtc: string;
  completedAtUtc: string | null;
  // Only on a single job, not in the list.
  attemptHistory: SyncAttempt[] | null;
};

export type AccountHealth = {
  id: string;
  channel: ChannelKind;
  name: string;
  liveWrites: boolean;
  lastOrderImportAtUtc: string | null;
  // Orders have not been read recently enough for stock sent to this channel to be trusted.
  ordersStale: boolean;
  lastError: string | null;
  openOrderIssues: number;
};

export type SyncHealth = {
  undispatchedEvents: number;
  pendingJobs: number;
  runningJobs: number;
  awaitingRemoteJobs: number;
  failedJobs: number;
  needsCorrectionJobs: number;
  expiredLeases: number;
  retriedJobs: number;
  oldestPendingAtUtc: string | null;
  lastSuccessAtUtc: string | null;
  accounts: AccountHealth[];
};

export type OrderLineIssueReason = 0 | 1 | 2; // UnknownSku, AmbiguousSku, InventoryShortfall

export type OrderLineIssue = {
  id: string;
  channelAccountId: string | null;
  externalOrderId: string;
  externalLineId: string;
  sellerSku: string | null;
  quantity: number;
  reason: OrderLineIssueReason;
  orderId: string | null;
  createdAtUtc: string;
  resolvedAtUtc: string | null;
};

export const SyncApi = {
  health: () => apiFetch<SyncHealth>("/api/channels/sync/health"),
  jobs: (take = 200) => apiFetch<SyncJob[]>(`/api/channels/sync/jobs?take=${take}`),
  job: (id: string) => apiFetch<SyncJob>(`/api/channels/sync/jobs/${id}`),
  orderIssues: () => apiFetch<OrderLineIssue[]>("/api/channels/order-issues"),
  resolveOrderIssue: (id: string) => apiFetch<void>(`/api/channels/order-issues/${id}/resolve`, { method: "POST" }),
  importOrders: (accountId: string) => apiFetch<{ jobId: string }>(`/api/channels/${accountId}/import-orders`, { method: "POST" }),
};

export const CatalogApi = {
  product: (id: string) => apiFetch<CatalogProduct>(`/api/catalog/products/${id}`),
  updateContent: (id: string, data: { brand: string | null; description: string | null; category: string | null }) =>
    apiFetch<CatalogProduct>(`/api/catalog/products/${id}/content`, { method: "PUT", body: JSON.stringify(data) }),
  // An empty value removes the identifier.
  setIdentifier: (id: string, type: ProductIdentifierType, value: string) =>
    apiFetch<CatalogProduct>(`/api/catalog/products/${id}/identifiers`, { method: "PUT", body: JSON.stringify({ type, value }) }),
  // The first image is the main one; the rest are its gallery, in the order they were added.
  addImage: (id: string, url: string, position: number) =>
    apiFetch<CatalogProduct>(`/api/catalog/products/${id}/media`, {
      method: "POST",
      body: JSON.stringify({ url, purpose: position === 0 ? 0 : 1, position }),
    }),
  removeImage: (mediaId: string) => apiFetch<void>(`/api/catalog/media/${mediaId}`, { method: "DELETE" }),
};

export const ChannelListingsApi = {
  list: (accountId: string) => apiFetch<ChannelListing[]>(`/api/channel-listings?accountId=${accountId}`),
  // Every marketplace's listing of one product.
  ofProduct: (productId: string) => apiFetch<ChannelListing[]>(`/api/channel-listings?productId=${productId}`),
  all: () => apiFetch<ChannelListing[]>("/api/channel-listings"),
  // Sends the listing's current state again after a failure or a correction.
  retry: (id: string) => apiFetch<ListingQueued>(`/api/channel-listings/${id}/retry`, { method: "POST" }),
  // Creates the variant's listing on the marketplace as a draft; saving never publishes.
  // existingCatalogItemId: the marketplace's own item (an ASIN) to make the offer on, when there is one.
  add: (channelMarketId: string, variantId: string, existingCatalogItemId?: string) =>
    apiFetch<ChannelListing>("/api/channel-listings", {
      method: "PUT",
      body: JSON.stringify({ channelMarketId, variantId, fulfillmentMode: 0, existingCatalogItemId }),
    }),
  // Everything not in `edit` (seller SKU, fulfillment, attributes, other overrides) is sent back as it is.
  save: (listing: ChannelListing, edit: ListingEdit) =>
    apiFetch<ChannelListing>("/api/channel-listings", {
      method: "PUT",
      body: JSON.stringify({
        channelMarketId: listing.channelMarketId,
        variantId: listing.variantId,
        sellerSku: listing.sellerSku,
        fulfillmentMode: listing.fulfillmentMode,
        externalCategoryId: edit.externalCategoryId,
        content: { title: edit.title === null ? null : { value: edit.title } },
        priceOverride: edit.priceOverride,
        quantityCap: edit.quantityCap,
        existingCatalogItemId: edit.existingCatalogItemId,
        imageIds: edit.imageIds,
      }),
    }),
  preview: (id: string) => apiFetch<ListingPreview>(`/api/channel-listings/${id}/preview`, { method: "POST" }),
  validate: (id: string) => apiFetch<ListingValidation>(`/api/channel-listings/${id}/validate`, { method: "POST" }),
  publish: (id: string) => apiFetch<ListingQueued>(`/api/channel-listings/${id}/publish`, { method: "POST" }),
  deactivate: (id: string) => apiFetch<ListingQueued>(`/api/channel-listings/${id}/deactivate`, { method: "POST" }),
};
