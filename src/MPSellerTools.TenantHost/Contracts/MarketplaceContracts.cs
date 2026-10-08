using System.Text.Json;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.TenantHost.Contracts;

public record VariantResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string? Name,
    IReadOnlyDictionary<string, string> Options,
    ItemCondition Condition,
    decimal Price,
    string Currency,
    decimal? WeightValue,
    string? WeightUnit,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? DimensionUnit,
    bool IsDefault,
    bool IsArchived,
    int OnHand,
    int Reserved,
    int SafetyStock,
    int AvailableToSell,
    string RowVersion);

public record IdentifierResponse(Guid Id, ProductIdentifierType Type, string Value, Guid? VariantId);

public record MediaResponse(Guid Id, string Url, string? AltText, Guid? VariantId, MediaPurpose Purpose, int Position);

public record CatalogProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Brand,
    string? Description,
    string? Category,
    IReadOnlyList<VariantResponse> Variants,
    IReadOnlyList<IdentifierResponse> Identifiers,
    IReadOnlyList<MediaResponse> Media);

public record UpdateProductContentRequest(string? Brand, string? Description, string? Category);

/// <summary><see cref="Sku"/> is used when creating; a variant's SKU does not change afterwards.</summary>
public record SaveVariantRequest(
    string? Sku,
    string? Name,
    Dictionary<string, string>? Options,
    ItemCondition Condition,
    decimal Price,
    decimal? WeightValue,
    string? WeightUnit,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? DimensionUnit,
    string? RowVersion);

/// <summary>With <see cref="VariantId"/> the identifier is the variant's; without, the product's. An empty value removes it.</summary>
public record SetIdentifierRequest(ProductIdentifierType Type, string? Value, Guid? VariantId);

public record AddMediaRequest(string Url, string? AltText, Guid? VariantId, MediaPurpose Purpose, int Position);

public record AdjustInventoryRequest(int? OnHand, int? SafetyStock);

/// <summary><see cref="ReceiptId"/> is the warehouse's own reference for the receipt; the same one is counted once.</summary>
public record ReturnReceiptRequest(Guid VariantId, int Quantity, string ReceiptId);

public record InventoryItemResponse(
    Guid VariantId, Guid ProductId, string Sku, string ProductName, string? VariantName, decimal Price,
    int OnHand, int Reserved, int SafetyStock, int AvailableToSell, DateTime? UpdatedAtUtc);

/// <summary><see cref="AccountingEnabled"/> false: orders leave stock alone, so nothing is ever reserved.</summary>
public record InventoryOverviewResponse(bool AccountingEnabled, IReadOnlyList<InventoryItemResponse> Items);

public record InventoryMovementResponse(
    Guid Id, Guid VariantId, string Sku, InventoryMovementType Type, int OnHandDelta, int ReservedDelta, string? Reference, DateTime OccurredAtUtc);

public record ChannelMarketResponse(Guid Id, string MarketplaceCode, string Language, string Currency);

/// <summary>
/// A channel account as the API shows it. Credentials are never part of it,
/// only whether any are saved. <see cref="EffectiveLiveWrites"/> is what
/// actually applies: the account's switch and the host's together.
/// </summary>
public record ChannelAccountResponse(
    Guid Id,
    SalesChannel Channel,
    string Name,
    ChannelEnvironment Environment,
    string? SellerId,
    JsonElement? Settings,
    bool HasCredentials,
    bool IsEnabled,
    bool LiveWritesEnabled,
    bool EffectiveLiveWrites,
    bool InventorySyncEnabled,
    bool OrderImportEnabled,
    PriceConflictPolicy PriceConflictPolicy,
    DateTime? LastOrderImportAtUtc,
    string? LastError,
    IReadOnlyList<ChannelMarketResponse> Markets);

public record SaveChannelAccountRequest(
    SalesChannel Channel,
    string Name,
    ChannelEnvironment Environment,
    string? SellerId,
    JsonElement? Settings,
    bool IsEnabled,
    bool LiveWritesEnabled,
    bool InventorySyncEnabled,
    bool OrderImportEnabled,
    PriceConflictPolicy PriceConflictPolicy);

/// <summary>Amazon: clientId, clientSecret, refreshToken. Walmart: clientId, clientSecret. Magento: accessToken. Write-only.</summary>
public record SetCredentialsRequest(Dictionary<string, string> Credentials);

public record SaveMarketRequest(string MarketplaceCode, string? Language, string? Currency);

public record CategoryMappingResponse(
    Guid Id, Guid ChannelMarketId, string InternalCategory, string ExternalCategoryId, JsonElement? Requirements,
    string? RequirementsSource, string? RequirementsVersion, DateTime? RequirementsRetrievedAtUtc);

public record SaveCategoryMappingRequest(
    Guid ChannelMarketId, string InternalCategory, string ExternalCategoryId, JsonElement? Requirements,
    string? RequirementsSource, string? RequirementsVersion);

/// <summary>
/// In <see cref="Content"/> a field left out keeps its current override, a
/// field given as null goes back to inheriting from the product, and
/// {"value":...} or {"cleared":true} sets an override.
/// <see cref="ExistingCatalogItemId"/> is an id the seller already holds for
/// the channel's catalog item (an ASIN), for offering on an existing item.
/// <see cref="ImageIds"/> left out keeps the listing's choice of pictures, an
/// empty list goes back to sending all of the product's, and a list of the
/// product's picture ids sends those, in that order.
/// </summary>
public record SaveChannelListingRequest(
    Guid ChannelMarketId,
    Guid VariantId,
    string? SellerSku,
    string? ExternalCategoryId,
    Dictionary<string, FieldOverride?>? Content,
    Dictionary<string, string>? Attributes,
    decimal? PriceOverride,
    FulfillmentMode FulfillmentMode,
    int? QuantityCap,
    string? ExistingCatalogItemId,
    List<Guid>? ImageIds = null);

public record ListingVersions(long Desired, long Confirmed);

public record ChannelListingResponse(
    Guid Id,
    Guid ChannelMarketId,
    Guid ChannelAccountId,
    SalesChannel Channel,
    string MarketplaceCode,
    Guid VariantId,
    string SellerSku,
    string? ExternalCategoryId,
    IReadOnlyDictionary<string, FieldOverride> ContentOverrides,
    IReadOnlyDictionary<string, string> Attributes,
    decimal? PriceOverride,
    FulfillmentMode FulfillmentMode,
    int? QuantityCap,
    string? EffectiveTitle,
    string? EffectiveDescription,
    string? EffectiveBrand,
    decimal EffectivePrice,
    int EffectiveQuantity,
    ListingDesiredState DesiredState,
    ListingObservedStatus ObservedStatus,
    decimal? ObservedPrice,
    int? ObservedQuantity,
    DateTime? ObservedAtUtc,
    bool HasPriceConflict,
    ListingVersions Content,
    ListingVersions Price,
    ListingVersions Inventory,
    JsonElement? Issues,
    IReadOnlyDictionary<ExternalResourceType, string> References,
    IReadOnlyList<ListingImage> AvailableImages,
    IReadOnlyList<Guid>? ImageIds,
    IReadOnlyList<string> EffectiveImageUrls,
    ImageRules ImageRules,
    Guid ProductId,
    // The listing's own page on the marketplace or store, once it has a number there; null before that,
    // in a sandbox, and on a site whose address is not known here.
    string? StoreUrl = null);

public record ListingValidationResponse(bool Valid, IReadOnlyList<ValidationIssue> Issues, string? RequirementsSource, string? RequirementsVersion);

/// <summary>What would be sent, built without sending. <see cref="LiveWrites"/> says whether a real run would reach the channel.</summary>
public record ListingPreviewResponse(
    SyncOperation Operation, bool LiveWrites, IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<ChannelRequest> Requests);

/// <summary><see cref="LiveWrites"/> false means the queued job will be carried out as a dry run.</summary>
public record ListingQueuedResponse(Guid ListingId, ListingDesiredState DesiredState, bool LiveWrites);

public record SaveListingGroupRequest(Guid ChannelMarketId, Guid ProductId, string GroupKey, List<string> VariationAttributes, List<Guid> ListingIds);

public record ListingGroupResponse(Guid Id, Guid ChannelMarketId, Guid ProductId, string GroupKey, IReadOnlyList<string> VariationAttributes, IReadOnlyList<Guid> ListingIds, string? ExternalListingId);

public record SyncAttemptResponse(
    int Number, DateTime StartedAtUtc, DateTime FinishedAtUtc, SyncJobStatus Outcome, SyncErrorClass ErrorClass, int? HttpStatus, string? ExternalRequestId, string? Detail);

public record SyncJobResponse(
    Guid Id,
    Guid ChannelAccountId,
    Guid? ChannelListingId,
    SyncOperation Operation,
    SyncJobStatus Status,
    long TargetVersion,
    int Attempts,
    int MaxAttempts,
    DateTime NextAttemptAtUtc,
    string? ExternalSubmissionId,
    SyncErrorClass ErrorClass,
    string? LastError,
    bool DryRun,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<SyncAttemptResponse>? AttemptHistory);

public record OrderLineIssueResponse(
    Guid Id, Guid? ChannelAccountId, string ExternalOrderId, string ExternalLineId, string? SellerSku, int Quantity,
    OrderLineIssueReason Reason, Guid? OrderId, DateTime CreatedAtUtc, DateTime? ResolvedAtUtc);

public record StorefrontProductResponse(
    string Sku, Guid ProductId, Guid VariantId, string? Title, string? Description, string? Brand, decimal Price, string Currency,
    int AvailableQuantity, IReadOnlyDictionary<string, string> Options, IReadOnlyList<string> ImageUrls);
