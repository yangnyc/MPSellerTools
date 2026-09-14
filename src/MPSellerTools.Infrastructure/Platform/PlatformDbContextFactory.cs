using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MPSellerTools.Infrastructure.Platform;

/// <summary>
/// Used only by `dotnet ef migrations add` at design time. Runtime hosts
/// configure <see cref="PlatformDbContext"/> with their own connection
/// string from configuration (see MPSellerTools.PlatformHost's startup).
/// </summary>
public class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PlatformDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\MSSQLLocalDB;Database=MPSellerTools_Platform_DesignTime;Trusted_Connection=True;TrustServerCertificate=True");
        return new PlatformDbContext(optionsBuilder.Options);
    }
}
