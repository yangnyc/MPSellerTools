using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Notifications;
using MPSellerTools.Core.Platform;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Hosting;
using MPSellerTools.Infrastructure.Notifications;
using MPSellerTools.Infrastructure.Tenants;
using Microsoft.AspNetCore.Identity;

namespace MPSellerTools.Provisioning.Worker;

/// <summary>
/// Implements the CreateTenant/Suspend/Resume/Restart/Delete job handlers (brief §10). Every
/// step is written to be safely re-run: a retried CreateTenant job that
/// failed partway through does not create a second database, a second
/// invitation, or a second OS process.
/// </summary>
public class TenantProvisioningService(
    ProvisioningOptions options,
    IConfiguration configuration,
    TenantProcessSupervisor supervisor,
    IHttpClientFactory httpClientFactory,
    string localDataDirectory,
    ILogger<TenantProvisioningService> logger)
{
    public async Task CreateTenantAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        logger.LogInformation("Provisioning tenant {Slug} ({TenantId})", tenant.Slug, tenant.Id);

        if (!SlugValidator.IsValid(tenant.Slug))
        {
            // Defense in depth: this should be unreachable, since the slug was
            // already validated by TenantsController before the Tenant row
            // was ever created, but a slug is about to be interpolated into a
            // database name and a set of file-system paths, so re-validate
            // its character set here too rather than trusting the stored value.
            throw new InvalidOperationException($"Tenant {tenant.Id} has an invalid stored slug '{tenant.Slug}'.");
        }

        var databaseName = tenant.DatabaseName ?? $"MPSellerTools_Tenant_{tenant.Slug.Replace('-', '_')}";
        var connectionString = TenantConnectionString(databaseName);

        await using var tenantDb = CreateTenantDbContext(connectionString);
        await tenantDb.Database.MigrateAsync(cancellationToken);

        await EnsureRolesAsync(tenantDb, cancellationToken);
        await EnsureCompanySettingsAsync(tenantDb, tenant, cancellationToken);
        var applicationInstanceId = tenant.ApplicationInstanceId ?? Guid.NewGuid();
        // The first administrator can sign in as soon as the company is up: with the password chosen when
        // it was asked for, or the one they already have in another company. Nobody is invited.
        if (!await EnsureInitialAdminAsync(tenantDb, tenant, cancellationToken))
        {
            throw new InvalidOperationException(
                $"No password was chosen for the administrator, and {tenant.InitialAdminEmail} has no account in another company to keep one from. "
                + "Delete this company and create it again with a password for the administrator.");
        }

        var url = PublicUrl(tenant);
        var instanceConfigPath = WriteInstanceConfig(tenant, applicationInstanceId, url, connectionString);

        if (!supervisor.IsRunning(tenant))
        {
            var launched = supervisor.Start(instanceConfigPath, LocalUrl(tenant), options.TenantHostPublishDirectory);
            tenant.ProcessId = launched.ProcessId;
            tenant.ProcessStartTimeUtc = launched.StartTimeUtc;
        }

        await WaitForReadinessAsync(InternalUrl(tenant), tenant.Id, applicationInstanceId, cancellationToken);

        tenant.DatabaseName = databaseName;
        tenant.ApplicationInstanceId = applicationInstanceId;
        tenant.Url = url;
        tenant.Status = TenantStatus.Active;
        tenant.FailureReason = null;
        tenant.UpdatedAtUtc = DateTime.UtcNow;
    }

    public async Task SuspendAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        supervisor.Stop(tenant);
        tenant.ProcessId = null;
        tenant.ProcessStartTimeUtc = null;
        tenant.Status = TenantStatus.Suspended;
        tenant.UpdatedAtUtc = DateTime.UtcNow;
        await Task.CompletedTask;
    }

    public async Task ResumeAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        if (tenant.DatabaseName is null || tenant.ApplicationInstanceId is null || tenant.Url is null)
        {
            throw new InvalidOperationException($"Tenant {tenant.Id} cannot be resumed: missing prior provisioning state.");
        }

        var connectionString = TenantConnectionString(tenant.DatabaseName);

        await using var tenantDb = CreateTenantDbContext(connectionString);
        if (!await tenantDb.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Tenant {tenant.Id}'s database '{tenant.DatabaseName}' is not reachable.");
        }

        // A tenant created by an earlier build may be behind on migrations, and its instance does
        // not report ready until none are pending. Applying them is this worker's job, as at
        // creation; migrations are additive, and this is the local database the worker itself made.
        await tenantDb.Database.MigrateAsync(cancellationToken);

        // A company still without anyone who can sign in (made before its first administrator was given an
        // account straight away) is given one now, when that person has an account in another company.
        if (!await tenantDb.Users.AnyAsync(cancellationToken))
        {
            await EnsureInitialAdminAsync(tenantDb, tenant, cancellationToken);
        }

        // Recomputed rather than reused, so the address follows the public host
        // setting when it is turned on or off after the tenant was created.
        tenant.Url = PublicUrl(tenant);
        var instanceConfigPath = WriteInstanceConfig(tenant, tenant.ApplicationInstanceId.Value, tenant.Url, connectionString);

        if (!supervisor.IsRunning(tenant))
        {
            var launched = supervisor.Start(instanceConfigPath, LocalUrl(tenant), options.TenantHostPublishDirectory);
            tenant.ProcessId = launched.ProcessId;
            tenant.ProcessStartTimeUtc = launched.StartTimeUtc;
        }

        await WaitForReadinessAsync(InternalUrl(tenant), tenant.Id, tenant.ApplicationInstanceId.Value, cancellationToken);

        tenant.Status = TenantStatus.Active;
        tenant.UpdatedAtUtc = DateTime.UtcNow;
    }

    public async Task RestartAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        supervisor.Stop(tenant);
        tenant.ProcessId = null;
        tenant.ProcessStartTimeUtc = null;
        await ResumeAsync(tenant, cancellationToken);
    }

    /// <summary>
    /// Stops the instance, drops its database and removes its local files. Safe
    /// to re-run: each step is a no-op when a previous attempt already did it.
    /// The caller removes the registry row once this returns.
    /// </summary>
    public async Task DeleteAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting tenant {Slug} ({TenantId})", tenant.Slug, tenant.Id);

        if (!SlugValidator.IsValid(tenant.Slug))
        {
            // Same defense in depth as CreateTenantAsync: the slug names the
            // database to drop and the directory to delete.
            throw new InvalidOperationException($"Tenant {tenant.Id} has an invalid stored slug '{tenant.Slug}'.");
        }

        supervisor.Stop(tenant);
        tenant.ProcessId = null;
        tenant.ProcessStartTimeUtc = null;

        // A provisioning run that failed partway may have created the database
        // without recording its name, so fall back to the name it would have used.
        var databaseName = tenant.DatabaseName ?? $"MPSellerTools_Tenant_{tenant.Slug.Replace('-', '_')}";
        if (!databaseName.StartsWith("MPSellerTools_Tenant_", StringComparison.Ordinal) ||
            !databaseName.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
        {
            throw new InvalidOperationException($"Tenant {tenant.Id} has an unexpected database name '{databaseName}'; refusing to drop it.");
        }

        var connectionString = TenantConnectionString(databaseName);
        await using (var tenantDb = CreateTenantDbContext(connectionString))
        {
            await tenantDb.Database.EnsureDeletedAsync(cancellationToken);
        }

        var instanceDirectory = Path.Combine(localDataDirectory, "tenants", tenant.Slug);
        // The just-stopped process can hold its log file open for a moment.
        for (var attempt = 1; Directory.Exists(instanceDirectory); attempt++)
        {
            try
            {
                Directory.Delete(instanceDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    /// <summary>The address people open: on the public host when one is configured, otherwise localhost.</summary>
    private string PublicUrl(Tenant tenant) =>
        $"https://{(string.IsNullOrWhiteSpace(options.PublicHost) ? "localhost" : options.PublicHost.Trim())}:{tenant.Port}";

    /// <summary>The address the instance itself listens on.</summary>
    private static string LocalUrl(Tenant tenant) => $"https://localhost:{tenant.Port}";

    /// <summary>
    /// The address this machine uses to reach the instance. Behind a proxy that
    /// is the public address, whose certificate is trusted everywhere; without
    /// one it is localhost, the only name the development certificate is valid for.
    /// </summary>
    private string InternalUrl(Tenant tenant) => options.BehindProxy ? PublicUrl(tenant) : LocalUrl(tenant);

    /// <summary>A company's database is on the same server as the registry, under its own name.</summary>
    private string TenantConnectionString(string databaseName) =>
        new SqlConnectionStringBuilder(configuration.GetConnectionString("PlatformDatabase")) { InitialCatalog = databaseName }.ConnectionString;

    private static TenantDbContext CreateTenantDbContext(string connectionString)
    {
        var dbOptions = new DbContextOptionsBuilder<TenantDbContext>().UseSqlServer(connectionString).Options;
        return new TenantDbContext(dbOptions);
    }

    private static async Task EnsureRolesAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        foreach (var role in new[] { Roles.TenantAdmin, Roles.Employee })
        {
            var normalized = role.ToUpperInvariant();
            if (!await db.Roles.AnyAsync(r => r.NormalizedName == normalized, cancellationToken))
            {
                db.Roles.Add(new IdentityRole<Guid>(role) { Id = Guid.NewGuid(), NormalizedName = normalized });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureCompanySettingsAsync(TenantDbContext db, Tenant tenant, CancellationToken cancellationToken)
    {
        if (!await db.CompanySettings.AnyAsync(cancellationToken))
        {
            db.CompanySettings.Add(new CompanySettings
            {
                Id = Guid.NewGuid(),
                CompanyName = tenant.Name,
                UpdatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Gives the company's first administrator an account in it: with the password chosen when the company
    /// was asked for, or, when none was, the one the same email already has in another company on this
    /// platform. One person may be in several companies; each keeps its own copy of the account, so a
    /// password changed later in one is changed there only. Returns whether the administrator can sign
    /// in; false means no password was chosen and nobody with this email was found.
    /// </summary>
    private async Task<bool> EnsureInitialAdminAsync(TenantDbContext db, Tenant tenant, CancellationToken cancellationToken)
    {
        var normalizedEmail = tenant.InitialAdminEmail.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return true;
        }

        var adminRole = await db.Roles.FirstAsync(r => r.NormalizedName == Roles.TenantAdmin.ToUpperInvariant(), cancellationToken);
        if (!string.IsNullOrEmpty(tenant.InitialAdminPasswordHash))
        {
            var email = tenant.InitialAdminEmail.Trim();
            var chosen = new TenantUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = normalizedEmail,
                Email = email,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
                PasswordHash = tenant.InitialAdminPasswordHash,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                DisplayName = email[..email.IndexOf('@')],
                LockoutEnabled = true,
            };
            db.Users.Add(chosen);
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = chosen.Id, RoleId = adminRole.Id });
            await db.SaveChangesAsync(cancellationToken);
            // The account has it now; the registry has no further use for it.
            tenant.InitialAdminPasswordHash = null;
            logger.LogInformation("Gave {Email} an administrator's account in {Slug}, with the password chosen for it", tenant.InitialAdminEmail, tenant.Slug);
            return true;
        }

        TenantUser? known = null;
        foreach (var database in await OtherTenantDatabasesAsync(tenant, cancellationToken))
        {
            try
            {
                await using var other = CreateTenantDbContext(TenantConnectionString(database));
                known = await other.Users.AsNoTracking()
                    .Where(u => u.NormalizedEmail == normalizedEmail && u.PasswordHash != null && !u.IsBlocked)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                // A company whose database cannot be read now is passed over; the invitation still gets the administrator in.
                logger.LogWarning("Could not look for {Email} in {Database}: {Reason}", tenant.InitialAdminEmail, database, ex.Message);
            }
            if (known is not null)
            {
                break;
            }
        }
        if (known is null)
        {
            return false;
        }

        var user = new TenantUser
        {
            Id = Guid.NewGuid(),
            UserName = known.UserName,
            NormalizedUserName = known.NormalizedUserName,
            Email = known.Email,
            NormalizedEmail = known.NormalizedEmail,
            EmailConfirmed = true,
            PasswordHash = known.PasswordHash,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            DisplayName = known.DisplayName,
            LockoutEnabled = true,
        };
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRole.Id });
        // An invitation still open for them has nothing left to do.
        db.Invitations.RemoveRange(await db.Invitations.Where(i => i.Email == tenant.InitialAdminEmail && i.AcceptedAtUtc == null).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Gave {Email} an administrator's account in {Slug}, as they have one in another company", tenant.InitialAdminEmail, tenant.Slug);
        return true;
    }

    /// <summary>The databases of the platform's other companies, oldest first, by the registry's own record.</summary>
    private async Task<List<string>> OtherTenantDatabasesAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await using var connection = new SqlConnection(configuration.GetConnectionString("PlatformDatabase"));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DatabaseName FROM Tenants WHERE Id <> @id AND DatabaseName IS NOT NULL ORDER BY CreatedAtUtc";
        command.Parameters.AddWithValue("@id", tenant.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            // Only a name this worker itself would have given a company's database is opened.
            if (name.StartsWith("MPSellerTools_Tenant_", StringComparison.Ordinal) && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            {
                names.Add(name);
            }
        }
        return names;
    }

    private string WriteInstanceConfig(Tenant tenant, Guid applicationInstanceId, string url, string connectionString)
    {
        var instanceDirectory = Path.Combine(localDataDirectory, "tenants", tenant.Slug);
        Directory.CreateDirectory(instanceDirectory);
        var path = Path.Combine(instanceDirectory, "instance-config.json");

        var config = new
        {
            ConnectionStrings = new { TenantDatabase = connectionString },
            Tenant = new
            {
                TenantId = tenant.Id,
                ApplicationInstanceId = applicationInstanceId,
                Slug = tenant.Slug,
                DisplayName = tenant.Name,
                Url = url,
            },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private async Task WaitForReadinessAsync(string url, Guid expectedTenantId, Guid expectedInstanceId, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(TenantProvisioningService));
        var deadline = DateTime.UtcNow.AddSeconds(options.ReadinessTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var health = await client.GetFromJsonAsync<HealthCheckResult>($"{url}/api/health", cancellationToken);
                if (health is { DatabaseReachable: true, MigrationsApplied: true } &&
                    health.TenantId == expectedTenantId && health.ApplicationInstanceId == expectedInstanceId)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Not up yet — keep polling until the deadline.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new InvalidOperationException($"Tenant instance at {url} did not become ready within {options.ReadinessTimeoutSeconds}s.");
    }

    private record HealthCheckResult(Guid TenantId, Guid ApplicationInstanceId, bool DatabaseReachable, bool MigrationsApplied);
}
