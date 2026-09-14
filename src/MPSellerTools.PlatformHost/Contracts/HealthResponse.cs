namespace MPSellerTools.PlatformHost.Contracts;

public record HealthResponse(bool DatabaseReachable, bool MigrationsApplied);
