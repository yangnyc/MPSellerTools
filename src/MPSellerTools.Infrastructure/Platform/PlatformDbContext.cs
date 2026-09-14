using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Platform;

namespace MPSellerTools.Infrastructure.Platform;

/// <summary>
/// The single platform database (brief §4): platform administrators, the
/// company registry, provisioning jobs, and platform audit records. Never
/// business data, tenant users, or tenant passwords.
/// </summary>
public class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : IdentityDbContext<PlatformUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<ProvisioningJob> ProvisioningJobs => Set<ProvisioningJob>();

    public DbSet<PlatformAuditEntry> AuditEntries => Set<PlatformAuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(t => t.Slug).IsUnique();
            entity.Property(t => t.RowVersion).IsRowVersion();
        });

        builder.Entity<ProvisioningJob>(entity =>
        {
            entity.HasIndex(j => new { j.Status, j.JobType });
            entity.Property(j => j.RowVersion).IsRowVersion();
        });

        builder.Entity<PlatformAuditEntry>(entity =>
        {
            entity.HasIndex(a => a.OccurredAtUtc);
            entity.HasIndex(a => a.TenantId);
        });
    }
}
