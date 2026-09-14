using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;

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

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Product>(entity =>
        {
            entity.HasIndex(p => p.Sku).IsUnique();
            entity.Property(p => p.Price).HasColumnType("decimal(18,2)");
            entity.Property(p => p.RowVersion).IsRowVersion();
        });

        builder.Entity<Order>(entity =>
        {
            entity.HasIndex(o => o.OrderNumber).IsUnique();
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
            // Products are archived, never hard-deleted while referenced (brief §9),
            // so a normal restricting FK is safe here.
            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
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
    }
}
