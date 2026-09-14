using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MPSellerTools.Infrastructure.Tenants;

/// <summary>
/// Used only by `dotnet ef migrations add` at design time. Each running
/// TenantHost instance configures <see cref="TenantDbContext"/> with its own
/// fixed, per-instance connection string instead (brief §4) — never chosen
/// at request time.
/// </summary>
public class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=MPSellerTools_Tenant_DesignTime;Trusted_Connection=True;TrustServerCertificate=True");
        return new TenantDbContext(optionsBuilder.Options);
    }
}
