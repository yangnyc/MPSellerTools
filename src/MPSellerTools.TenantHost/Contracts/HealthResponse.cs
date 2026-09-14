namespace MPSellerTools.TenantHost.Contracts;

/// <summary>
/// Used by the provisioning worker to verify readiness before marking a
/// tenant Active (brief §10 step 7): the responding instance really is the
/// expected tenant/instance, and its database is reachable with all
/// migrations applied. Contains no secrets, so it is safe to leave anonymous.
/// </summary>
public record HealthResponse(Guid TenantId, Guid ApplicationInstanceId, bool DatabaseReachable, bool MigrationsApplied);
