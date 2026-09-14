# Windows Server Deployment Guidance

This document describes how MPSellerTools' architecture extends to a real
Windows Server deployment. **It is guidance, not an implemented automated
deployment** — the brief's required automated provisioning in this release
is local only (`MPSellerTools.Provisioning.Worker` launching local
processes). Nothing described here is wired up by the current codebase;
treat it as the plan for a future production rollout.

## Topology

- **SQL Server** (not LocalDB) running as a proper service, either on the
  same box as the application tier or a dedicated database server.
- **IIS** hosting each component as its own site/application:
  - One site for `MPSellerTools.PlatformHost`, bound to the platform's
    production hostname (e.g. `admin.mpsellertools.example.com`).
  - One IIS **Application Pool and site per tenant**, each running its own
    copy of the published `MPSellerTools.TenantHost` binary, bound to that
    tenant's own hostname (e.g. `company-a.mpsellertools.example.com`).
    Because TenantHost's tenant identity is fixed entirely by its
    per-instance configuration file (never by request routing), this maps
    directly onto IIS's existing per-site process isolation — each
    Application Pool is already a separate OS process with its own working
    directory, matching the local dev model exactly.
  - The provisioning worker would run as a **Windows Service** rather than
    a console process, using the Windows Service hosting extensions
    (`Microsoft.Extensions.Hosting.WindowsServices`) — not implemented in
    this codebase, but a drop-in addition to `Program.cs` since it's
    already a standard `IHost`.
- **DNS**: one A/CNAME record per tenant hostname, plus one for the platform
  console, all pointed at the IIS box(es). TLS certificates per hostname
  (or a wildcard) via IIS's normal binding configuration.

## Separate identities and database users (production only)

The brief explicitly calls out that local development runs everything under
a single Windows user, which is **not** an equivalent privilege boundary to
what production needs:

- **A separate, more-privileged deployment identity** for the provisioning
  process itself (creating databases, writing IIS site configuration,
  starting/stopping application pools) — distinct from any tenant
  application's own runtime identity.
- **A separate, least-privilege OS identity per tenant Application Pool**
  (an IIS Application Pool Identity or a dedicated `gMSA`/service account),
  so one tenant's compromised or buggy code cannot read another tenant's
  files, environment variables, or process memory.
- **A separate SQL Server login per tenant database**, granted access to
  *only* that one database (`db_owner` or a narrower custom role on that
  database alone — never `sysadmin`, never cross-database access). The
  platform database gets its own login, distinct from every tenant login,
  and no tenant login should be able to see the platform database exists.
- Connection strings for each tenant should be stored per-instance (e.g. in
  a protected configuration file or a secrets manager scoped to that
  tenant's Application Pool identity), exactly mirroring the local dev
  model's per-instance config file — just with real secrets management
  instead of a plaintext `.local/` file.

## Publishing

For each component:

```powershell
dotnet publish src/MPSellerTools.PlatformHost/MPSellerTools.PlatformHost.csproj -c Release -o <publish-dir>
dotnet publish src/MPSellerTools.TenantHost/MPSellerTools.TenantHost.csproj -c Release -o <publish-dir-per-tenant>
```

As in local dev, **always build the React frontends before publishing** —
`dotnet publish` copies whatever is currently in each host's `wwwroot`, and
`dotnet build` alone does not populate it. `scripts/Build.ps1` already
performs the frontends-then-publish sequence in the right order; a CI/CD
pipeline should do the same rather than relying on a developer's local
`wwwroot` being current.

Each TenantHost's published output should be deployed to its own directory
(one per tenant) so that upgrading Company A does not require touching
Company B's running files — again, matching the local model where each
TenantHost's working directory is already per-instance.

## Migrations

Apply migrations explicitly as part of the deployment/upgrade process
(`dotnet ef database update` against each database, or the equivalent via a
CI/CD release step), never via `EnsureCreated`/automatic migration on
application startup as this codebase does in `Development` for local
convenience. `Program.cs`'s auto-migrate block is explicitly guarded by
`app.Environment.IsDevelopment()` for exactly this reason.

## Backups

- Standard SQL Server backup/restore per database, on whatever schedule the
  business needs — since each tenant's data is a fully independent
  database, backup/restore/point-in-time-recovery for one company never
  touches another's.
- Back up the platform database (tenant registry, jobs, platform audit)
  with at least the same rigor as any single tenant database — losing it
  loses the ability to find or manage any tenant's instance, even though
  the tenant databases themselves would still be intact.
- Data Protection keys (per-instance key directories) and any per-instance
  configuration secrets should be backed up or reproducible independently
  of the database backups — losing them invalidates that instance's
  existing cookies/tokens (not a data-loss event, but a forced-relogin
  event for every active session on that tenant).
