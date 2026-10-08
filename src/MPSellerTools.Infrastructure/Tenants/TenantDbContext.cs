using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;

namespace MPSellerTools.Infrastructure.Tenants;

/// <summary>
/// One company's own database (brief §4): its Identity users/roles, company
/// settings, invitations, business data, and tenant audit records. The
/// connection string this context resolves to is fixed for the lifetime of
/// the hosting process — see MPSellerTools.TenantHost's startup configuration
/// (Increment 3), never chosen per-request.
/// </summary>
public class TenantDbContext(DbContextOptions<TenantDbContext> options)
    : IdentityDbContext<TenantUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<EbayConnection> EbayConnections => Set<EbayConnection>();

    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductIdentifier> ProductIdentifiers => Set<ProductIdentifier>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();

    public DbSet<ChannelAccount> ChannelAccounts => Set<ChannelAccount>();

    public DbSet<ChannelMarket> ChannelMarkets => Set<ChannelMarket>();

    public DbSet<CategoryMapping> CategoryMappings => Set<CategoryMapping>();

    public DbSet<ChannelListing> ChannelListings => Set<ChannelListing>();

    public DbSet<ListingGroup> ListingGroups => Set<ListingGroup>();

    public DbSet<ListingGroupMember> ListingGroupMembers => Set<ListingGroupMember>();

    public DbSet<ExternalReference> ExternalReferences => Set<ExternalReference>();

    public DbSet<InventoryLocation> InventoryLocations => Set<InventoryLocation>();

    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();

    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<OrderLineIssue> OrderLineIssues => Set<OrderLineIssue>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();

    public DbSet<SyncAttempt> SyncAttempts => Set<SyncAttempt>();

    public DbSet<InboxEvent> InboxEvents => Set<InboxEvent>();

    /// <summary>Large pieces of work carried out in the background, item by item.</summary>
    public DbSet<BulkJob> BulkJobs => Set<BulkJob>();

    // Added here rather than where the context is registered, so the host,
    // the provisioning worker and the tests all get it.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(new CatalogSaveChangesInterceptor());

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Product>(entity =>
        {
            entity.HasIndex(p => p.Sku).IsUnique();
            entity.Property(p => p.Price).HasColumnType("decimal(18,2)");
            entity.Property(p => p.RowVersion).IsRowVersion();
            entity.Property(p => p.Brand).HasMaxLength(200);
            entity.Property(p => p.Category).HasMaxLength(100);
        });

        builder.Entity<BulkJob>(entity =>
        {
            // How the worker finds the next one, and how the list is read.
            entity.HasIndex(j => new { j.Status, j.CreatedAtUtc });
            entity.HasOne<ChannelAccount>().WithMany().HasForeignKey(j => j.ChannelAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Order>(entity =>
        {
            entity.HasIndex(o => o.OrderNumber).IsUnique();
            entity.Property(o => o.EbayOrderId).HasMaxLength(64);
            // Filtered, so any number of orders created here can have none.
            entity.HasIndex(o => o.EbayOrderId).IsUnique().HasFilter("[EbayOrderId] IS NOT NULL");
            entity.Property(o => o.ExternalOrderId).HasMaxLength(64);
            entity.Property(o => o.Currency).HasMaxLength(3);
            // An order id is only unique within the account that issued it.
            entity.HasIndex(o => new { o.ChannelAccountId, o.ExternalOrderId }).IsUnique()
                .HasFilter("[ChannelAccountId] IS NOT NULL AND [ExternalOrderId] IS NOT NULL");
            entity.HasOne<ChannelAccount>().WithMany().HasForeignKey(o => o.ChannelAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(o => o.Total).HasColumnType("decimal(18,2)");
            entity.Property(o => o.RowVersion).IsRowVersion();
            entity.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderItem>(entity =>
        {
            entity.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(i => i.ExternalLineId).HasMaxLength(64);
            entity.Property(i => i.SellerSku).HasMaxLength(64);
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.VariantId).OnDelete(DeleteBehavior.Restrict);
            // Products are archived, never hard-deleted while referenced (brief §9),
            // so a normal restricting FK is safe here.
            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Listing>(entity =>
        {
            entity.HasIndex(l => new { l.Channel, l.ExternalId }).IsUnique();
            entity.Property(l => l.Price).HasColumnType("decimal(18,2)");
            // Same reasoning as OrderItem: products are archived, not deleted.
            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WorkItem>(entity =>
        {
            entity.Property(w => w.RowVersion).IsRowVersion();
        });

        builder.Entity<CompanySettings>(entity =>
        {
            entity.Property(c => c.RowVersion).IsRowVersion();
        });

        builder.Entity<Invitation>(entity =>
        {
            entity.HasIndex(i => i.TokenHash).IsUnique();
            entity.HasIndex(i => i.Email);
        });

        builder.Entity<AuditEntry>(entity =>
        {
            entity.HasIndex(a => a.OccurredAtUtc);
        });

        ConfigureMarketplace(builder);
    }

    private static void ConfigureMarketplace(ModelBuilder builder)
    {
        builder.Entity<ProductVariant>(entity =>
        {
            entity.HasIndex(v => v.Sku).IsUnique();
            // One default variant per product: the backfill and a concurrent product edit cannot both create it.
            entity.HasIndex(v => v.ProductId).IsUnique().HasFilter("[IsDefault] = 1").HasDatabaseName("IX_ProductVariants_ProductId_Default");
            entity.HasIndex(v => new { v.ProductId, v.IsArchived });
            entity.Property(v => v.Price).HasColumnType("decimal(18,2)");
            entity.Property(v => v.WeightValue).HasColumnType("decimal(18,4)");
            entity.Property(v => v.Length).HasColumnType("decimal(18,4)");
            entity.Property(v => v.Width).HasColumnType("decimal(18,4)");
            entity.Property(v => v.Height).HasColumnType("decimal(18,4)");
            entity.Property(v => v.RowVersion).IsRowVersion();
            entity.ToTable(t => t.HasCheckConstraint("CK_ProductVariants_Price", "[Price] >= 0"));
            entity.HasOne<Product>().WithMany().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductIdentifier>(entity =>
        {
            entity.HasIndex(i => new { i.VariantId, i.Type }).IsUnique().HasFilter("[VariantId] IS NOT NULL");
            entity.HasIndex(i => new { i.ProductId, i.Type }).IsUnique().HasFilter("[ProductId] IS NOT NULL");
            entity.HasIndex(i => new { i.Type, i.Value });
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_ProductIdentifiers_Owner",
                "([ProductId] IS NOT NULL AND [VariantId] IS NULL) OR ([ProductId] IS NULL AND [VariantId] IS NOT NULL)"));
            entity.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductMedia>(entity =>
        {
            entity.HasIndex(m => new { m.ProductId, m.Position });
            entity.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(m => m.VariantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MediaAsset>().WithMany().HasForeignKey(m => m.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChannelAccount>(entity =>
        {
            entity.HasIndex(a => new { a.Channel, a.Name }).IsUnique();
            entity.Property(a => a.RowVersion).IsRowVersion();
        });

        builder.Entity<ChannelMarket>(entity =>
        {
            entity.HasIndex(m => new { m.ChannelAccountId, m.MarketplaceCode }).IsUnique();
            entity.HasOne<ChannelAccount>().WithMany().HasForeignKey(m => m.ChannelAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CategoryMapping>(entity =>
        {
            entity.HasIndex(c => new { c.ChannelMarketId, c.InternalCategory }).IsUnique();
            entity.HasOne<ChannelMarket>().WithMany().HasForeignKey(c => c.ChannelMarketId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ChannelListing>(entity =>
        {
            entity.HasIndex(l => new { l.ChannelMarketId, l.VariantId }).IsUnique();
            // A seller SKU names one offer within a marketplace; the same SKU on another marketplace is a different offer.
            entity.HasIndex(l => new { l.ChannelMarketId, l.SellerSku }).IsUnique();
            entity.HasIndex(l => l.VariantId);
            entity.Property(l => l.PriceOverride).HasColumnType("decimal(18,2)");
            entity.Property(l => l.ObservedPrice).HasColumnType("decimal(18,2)");
            entity.Property(l => l.RowVersion).IsRowVersion();
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ChannelListings_PriceOverride", "[PriceOverride] IS NULL OR [PriceOverride] >= 0");
                t.HasCheckConstraint("CK_ChannelListings_QuantityCap", "[QuantityCap] IS NULL OR [QuantityCap] >= 0");
            });
            entity.HasOne<ChannelMarket>().WithMany().HasForeignKey(l => l.ChannelMarketId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(l => l.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ListingGroup>(entity =>
        {
            entity.HasIndex(g => new { g.ChannelMarketId, g.GroupKey }).IsUnique();
            entity.HasOne<ChannelMarket>().WithMany().HasForeignKey(g => g.ChannelMarketId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Product>().WithMany().HasForeignKey(g => g.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ListingGroupMember>(entity =>
        {
            entity.HasKey(m => new { m.ListingGroupId, m.ChannelListingId });
            // A listing sits in at most one group of its marketplace.
            entity.HasIndex(m => m.ChannelListingId).IsUnique();
            entity.HasOne<ListingGroup>().WithMany().HasForeignKey(m => m.ListingGroupId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ChannelListing>().WithMany().HasForeignKey(m => m.ChannelListingId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExternalReference>(entity =>
        {
            entity.HasIndex(r => new { r.OwnerType, r.OwnerId, r.ResourceType }).IsUnique();
            // Not unique: one eBay listing id is shared by every variant sold through that listing.
            entity.HasIndex(r => new { r.ChannelAccountId, r.ResourceType, r.Value });
            entity.HasOne<ChannelAccount>().WithMany().HasForeignKey(r => r.ChannelAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryLocation>(entity =>
        {
            entity.HasIndex(l => l.Code).IsUnique();
            entity.HasData(new InventoryLocation
            {
                Id = InventoryLocation.DefaultId,
                Code = "MAIN",
                Name = "Main warehouse",
                Kind = InventoryLocationKind.MerchantWarehouse,
            });
        });

        builder.Entity<InventoryBalance>(entity =>
        {
            entity.HasIndex(b => new { b.LocationId, b.VariantId }).IsUnique();
            entity.HasIndex(b => b.VariantId);
            entity.Ignore(b => b.AvailableToSell);
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_InventoryBalances_NonNegative", "[OnHand] >= 0 AND [Reserved] >= 0 AND [SafetyStock] >= 0"));
            entity.HasOne<InventoryLocation>().WithMany().HasForeignKey(b => b.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(b => b.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryReservation>(entity =>
        {
            entity.HasIndex(r => r.IdempotencyKey).IsUnique();
            entity.HasIndex(r => r.OrderId);
            entity.HasIndex(r => new { r.Status, r.ExpiresAtUtc });
            entity.ToTable(t => t.HasCheckConstraint("CK_InventoryReservations_Quantity", "[Quantity] > 0"));
            entity.HasOne<ProductVariant>().WithMany().HasForeignKey(r => r.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryMovement>(entity =>
        {
            entity.HasIndex(m => m.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            entity.HasIndex(m => new { m.VariantId, m.OccurredAtUtc });
        });

        builder.Entity<OrderLineIssue>(entity =>
        {
            entity.HasIndex(i => new { i.ChannelAccountId, i.ExternalOrderId, i.ExternalLineId, i.Reason }).IsUnique();
            entity.HasIndex(i => i.ResolvedAtUtc);
        });

        builder.Entity<OutboxEvent>(entity =>
        {
            entity.HasIndex(e => e.DispatchedAtUtc);
        });

        builder.Entity<SyncJob>(entity =>
        {
            // At most one waiting job per listing and operation: a further change raises its target instead of queueing another.
            entity.HasIndex(j => new { j.ChannelListingId, j.Operation }).IsUnique()
                .HasFilter("[Status] = 0 AND [ChannelListingId] IS NOT NULL").HasDatabaseName("IX_SyncJobs_PendingPerListing");
            entity.HasIndex(j => new { j.ChannelAccountId, j.Operation }).IsUnique()
                .HasFilter("[Status] = 0 AND [ChannelListingId] IS NULL").HasDatabaseName("IX_SyncJobs_PendingPerAccount");
            entity.HasIndex(j => new { j.Status, j.NextAttemptAtUtc });
            entity.HasOne<ChannelAccount>().WithMany().HasForeignKey(j => j.ChannelAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ChannelListing>().WithMany().HasForeignKey(j => j.ChannelListingId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SyncAttempt>(entity =>
        {
            entity.HasIndex(a => a.SyncJobId);
            entity.HasOne<SyncJob>().WithMany().HasForeignKey(a => a.SyncJobId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InboxEvent>(entity =>
        {
            entity.HasIndex(e => new { e.ChannelAccountId, e.EventKey }).IsUnique();
            entity.HasOne<ChannelAccount>().WithMany().HasForeignKey(e => e.ChannelAccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
