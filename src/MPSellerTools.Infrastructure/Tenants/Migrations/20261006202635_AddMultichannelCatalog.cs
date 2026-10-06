using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPSellerTools.Infrastructure.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class AddMultichannelCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Products",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelAccountId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalLineId",
                table: "OrderItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerSku",
                table: "OrderItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "OrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CredentialsProtected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LiveWritesEnabled = table.Column<bool>(type: "bit", nullable: false),
                    InventorySyncEnabled = table.Column<bool>(type: "bit", nullable: false),
                    OrderImportEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PriceConflictPolicy = table.Column<int>(type: "int", nullable: false),
                    LastOrderImportAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsExternallyOwned = table.Column<bool>(type: "bit", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryLocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    OnHandDelta = table.Column<int>(type: "int", nullable: false),
                    ReservedDelta = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderLineIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalLineId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SellerSku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLineIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    WeightValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    WeightUnit = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    Length = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Width = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Height = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DimensionUnit = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.CheckConstraint("CK_ProductVariants_Price", "[Price] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChannelMarkets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarketplaceCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMarkets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelMarkets_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelMarketId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsTestOnly = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalReferences_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InboxEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxEvents_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnHand = table.Column<int>(type: "int", nullable: false),
                    Reserved = table.Column<int>(type: "int", nullable: false),
                    SafetyStock = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBalances", x => x.Id);
                    table.CheckConstraint("CK_InventoryBalances_NonNegative", "[OnHand] >= 0 AND [Reserved] >= 0 AND [SafetyStock] >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryBalances_InventoryLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "InventoryLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryBalances_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservations", x => x.Id);
                    table.CheckConstraint("CK_InventoryReservations_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_InventoryReservations_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductIdentifiers", x => x.Id);
                    table.CheckConstraint("CK_ProductIdentifiers_Owner", "([ProductId] IS NOT NULL AND [VariantId] IS NULL) OR ([ProductId] IS NULL AND [VariantId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ProductIdentifiers_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductIdentifiers_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductMedia_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductMedia_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductMedia_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CategoryMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelMarketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InternalCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalCategoryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequirementsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequirementsSource = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequirementsVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequirementsRetrievedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryMappings_ChannelMarkets_ChannelMarketId",
                        column: x => x.ChannelMarketId,
                        principalTable: "ChannelMarkets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChannelListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelMarketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerSku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalCategoryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContentOverridesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PriceOverride = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    FulfillmentMode = table.Column<int>(type: "int", nullable: false),
                    QuantityCap = table.Column<int>(type: "int", nullable: true),
                    DesiredState = table.Column<int>(type: "int", nullable: false),
                    ContentVersion = table.Column<long>(type: "bigint", nullable: false),
                    PriceVersion = table.Column<long>(type: "bigint", nullable: false),
                    InventoryVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedContentVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedPriceVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedInventoryVersion = table.Column<long>(type: "bigint", nullable: false),
                    ObservedStatus = table.Column<int>(type: "int", nullable: false),
                    ObservedPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ObservedQuantity = table.Column<int>(type: "int", nullable: true),
                    IssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HasPriceConflict = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelListings", x => x.Id);
                    table.CheckConstraint("CK_ChannelListings_PriceOverride", "[PriceOverride] IS NULL OR [PriceOverride] >= 0");
                    table.CheckConstraint("CK_ChannelListings_QuantityCap", "[QuantityCap] IS NULL OR [QuantityCap] >= 0");
                    table.ForeignKey(
                        name: "FK_ChannelListings_ChannelMarkets_ChannelMarketId",
                        column: x => x.ChannelMarketId,
                        principalTable: "ChannelMarkets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelListings_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ListingGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelMarketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VariationAttributesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListingGroups_ChannelMarkets_ChannelMarketId",
                        column: x => x.ChannelMarketId,
                        principalTable: "ChannelMarkets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingGroups_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TargetVersion = table.Column<long>(type: "bigint", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExternalSubmissionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ErrorClass = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DryRun = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncJobs_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncJobs_ChannelListings_ChannelListingId",
                        column: x => x.ChannelListingId,
                        principalTable: "ChannelListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ListingGroupMembers",
                columns: table => new
                {
                    ListingGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingGroupMembers", x => new { x.ListingGroupId, x.ChannelListingId });
                    table.ForeignKey(
                        name: "FK_ListingGroupMembers_ChannelListings_ChannelListingId",
                        column: x => x.ChannelListingId,
                        principalTable: "ChannelListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ListingGroupMembers_ListingGroups_ListingGroupId",
                        column: x => x.ListingGroupId,
                        principalTable: "ListingGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ErrorClass = table.Column<int>(type: "int", nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    ExternalRequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncAttempts_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "InventoryLocations",
                columns: new[] { "Id", "ChannelAccountId", "Code", "IsExternallyOwned", "Kind", "Name" },
                values: new object[] { new Guid("0a000000-0000-0000-0000-000000000001"), null, "MAIN", false, 0, "Main warehouse" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ChannelAccountId_ExternalOrderId",
                table: "Orders",
                columns: new[] { "ChannelAccountId", "ExternalOrderId" },
                unique: true,
                filter: "[ChannelAccountId] IS NOT NULL AND [ExternalOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_VariantId",
                table: "OrderItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryMappings_ChannelMarketId_InternalCategory",
                table: "CategoryMappings",
                columns: new[] { "ChannelMarketId", "InternalCategory" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAccounts_Channel_Name",
                table: "ChannelAccounts",
                columns: new[] { "Channel", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelListings_ChannelMarketId_SellerSku",
                table: "ChannelListings",
                columns: new[] { "ChannelMarketId", "SellerSku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelListings_ChannelMarketId_VariantId",
                table: "ChannelListings",
                columns: new[] { "ChannelMarketId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelListings_VariantId",
                table: "ChannelListings",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMarkets_ChannelAccountId_MarketplaceCode",
                table: "ChannelMarkets",
                columns: new[] { "ChannelAccountId", "MarketplaceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalReferences_ChannelAccountId_ResourceType_Value",
                table: "ExternalReferences",
                columns: new[] { "ChannelAccountId", "ResourceType", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalReferences_OwnerType_OwnerId_ResourceType",
                table: "ExternalReferences",
                columns: new[] { "OwnerType", "OwnerId", "ResourceType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxEvents_ChannelAccountId_EventKey",
                table: "InboxEvents",
                columns: new[] { "ChannelAccountId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_LocationId_VariantId",
                table: "InventoryBalances",
                columns: new[] { "LocationId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_VariantId",
                table: "InventoryBalances",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocations_Code",
                table: "InventoryLocations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_IdempotencyKey",
                table: "InventoryMovements",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_VariantId_OccurredAtUtc",
                table: "InventoryMovements",
                columns: new[] { "VariantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_IdempotencyKey",
                table: "InventoryReservations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_OrderId",
                table: "InventoryReservations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_Status_ExpiresAtUtc",
                table: "InventoryReservations",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_VariantId",
                table: "InventoryReservations",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingGroupMembers_ChannelListingId",
                table: "ListingGroupMembers",
                column: "ChannelListingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListingGroups_ChannelMarketId_GroupKey",
                table: "ListingGroups",
                columns: new[] { "ChannelMarketId", "GroupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListingGroups_ProductId",
                table: "ListingGroups",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineIssues_ChannelAccountId_ExternalOrderId_ExternalLineId_Reason",
                table: "OrderLineIssues",
                columns: new[] { "ChannelAccountId", "ExternalOrderId", "ExternalLineId", "Reason" },
                unique: true,
                filter: "[ChannelAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineIssues_ResolvedAtUtc",
                table: "OrderLineIssues",
                column: "ResolvedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_DispatchedAtUtc",
                table: "OutboxEvents",
                column: "DispatchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentifiers_ProductId_Type",
                table: "ProductIdentifiers",
                columns: new[] { "ProductId", "Type" },
                unique: true,
                filter: "[ProductId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentifiers_Type_Value",
                table: "ProductIdentifiers",
                columns: new[] { "Type", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentifiers_VariantId_Type",
                table: "ProductIdentifiers",
                columns: new[] { "VariantId", "Type" },
                unique: true,
                filter: "[VariantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_MediaAssetId",
                table: "ProductMedia",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_ProductId_Position",
                table: "ProductMedia",
                columns: new[] { "ProductId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_VariantId",
                table: "ProductMedia",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_Default",
                table: "ProductVariants",
                column: "ProductId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_IsArchived",
                table: "ProductVariants",
                columns: new[] { "ProductId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Sku",
                table: "ProductVariants",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncAttempts_SyncJobId",
                table: "SyncAttempts",
                column: "SyncJobId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_PendingPerAccount",
                table: "SyncJobs",
                columns: new[] { "ChannelAccountId", "Operation" },
                unique: true,
                filter: "[Status] = 0 AND [ChannelListingId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_PendingPerListing",
                table: "SyncJobs",
                columns: new[] { "ChannelListingId", "Operation" },
                unique: true,
                filter: "[Status] = 0 AND [ChannelListingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_Status_NextAttemptAtUtc",
                table: "SyncJobs",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_ProductVariants_VariantId",
                table: "OrderItems",
                column: "VariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_ChannelAccounts_ChannelAccountId",
                table: "Orders",
                column: "ChannelAccountId",
                principalTable: "ChannelAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_ProductVariants_VariantId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_ChannelAccounts_ChannelAccountId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "CategoryMappings");

            migrationBuilder.DropTable(
                name: "ExternalReferences");

            migrationBuilder.DropTable(
                name: "InboxEvents");

            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "InventoryReservations");

            migrationBuilder.DropTable(
                name: "ListingGroupMembers");

            migrationBuilder.DropTable(
                name: "OrderLineIssues");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropTable(
                name: "ProductIdentifiers");

            migrationBuilder.DropTable(
                name: "ProductMedia");

            migrationBuilder.DropTable(
                name: "SyncAttempts");

            migrationBuilder.DropTable(
                name: "InventoryLocations");

            migrationBuilder.DropTable(
                name: "ListingGroups");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "SyncJobs");

            migrationBuilder.DropTable(
                name: "ChannelListings");

            migrationBuilder.DropTable(
                name: "ChannelMarkets");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "ChannelAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ChannelAccountId_ExternalOrderId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_VariantId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ChannelAccountId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalLineId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SellerSku",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "OrderItems");
        }
    }
}
