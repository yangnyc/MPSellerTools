# MPSellerTools

Seller tooling for companies that sell on Amazon, eBay, Walmart and their own
website, built as a **Multiple Application, Multiple Database** multi-tenant
system: one platform console manages a registry of companies, and each company
runs its own isolated ASP.NET Core process against its own SQL Server database.

It is a Windows / Visual Studio solution: ASP.NET Core (.NET 10) hosts, React
frontends, SQL Server, and PowerShell scripts that set up, build, start
and stop the whole thing.

> **Status: local development setup, not production-hardened.** Read
> [Local isolation limitations](#local-isolation-limitations) and
> [Security notes](#security-notes) before exposing it to anyone.

## Contents

- [What it does](#what-it-does)
- [How it fits together](#how-it-fits-together)
- [Prerequisites](#prerequisites)
- [Quick start](#quick-start)
- [Scripts](#scripts)
- [Ports](#ports)
- [Login URLs and accounts](#login-urls-and-accounts)
- [Reaching it from another machine](#reaching-it-from-another-machine)
- [Configuration](#configuration)
- [Visual Studio F5 / attach workflow](#visual-studio-f5--attach-workflow)
- [Frontend development](#frontend-development)
- [Tests and checks](#tests-and-checks)
- [Multichannel catalog](#multichannel-catalog-amazon-ebay-walmart-website)
- [Local data](#local-data)
- [Troubleshooting](#troubleshooting)
- [Local isolation limitations](#local-isolation-limitations)
- [Security notes](#security-notes)
- [Repository layout](#repository-layout)
- [Further documentation](#further-documentation)
- [Third-party code](#third-party-code)

## What it does

**Platform console** (for the PlatformAdmin):

- Create, rename, suspend, resume, restart and delete companies. Each company
  gets its own database and its own running application instance.
- See every company's status, address, port, database and process.
- Manage the users of any company (invite, create, edit, block, reset
  password) through that company's own instance.
- Audit log of everything a platform administrator did; export of the company list.

**Company workspace** (for a company's TenantAdmin and Employees):

- Products with variants (own SKU, price and stock), pictures, brands and categories.
- Orders, tasks, company settings, users and invitations, audit log.
- Sales channels: Amazon, eBay, Walmart, a Magento store and the company's own website, with
  per-channel content, price, quantity and pictures for each listing.
- A sync queue that carries changes to the channels, inventory accounting and
  low-stock monitoring. **Off by default** — see
  [Multichannel catalog](#multichannel-catalog-amazon-ebay-walmart-website).

## How it fits together

```
DevHost (local launcher)
├── PlatformHost            https://localhost:7100   platform API + console
│     └── MPSellerTools_Platform database (registry of companies, jobs, audit)
└── Provisioning.Worker     no port                  runs the job queue
      ├── TenantHost: Company A   https://localhost:7201   own database
      ├── TenantHost: Company B   https://localhost:7202   own database
      └── …one process and one database per company
```

- **PlatformHost** never opens a company's database. To manage a company's
  users it calls that company's instance with a per-company access key.
- **Provisioning.Worker** takes jobs from the platform database (create,
  suspend, resume, restart, delete), creates and migrates the company's
  database, and starts or stops its **TenantHost** process. On startup it
  restarts any Active company whose process is not running.
- **TenantHost** is one compiled binary; each running copy is bound to exactly
  one company by a per-instance config file. Its database never changes for
  the lifetime of the process.
- Each host serves its own built React frontend from `wwwroot`.

[`docs/architecture.md`](docs/architecture.md) has the full diagrams and the
provisioning, suspend/resume and delete flows.

## Prerequisites

Detected in the environment this project was built and verified in:

| Tool | Version found | Notes |
| --- | --- | --- |
| Git | 2.51.2.windows.1 | |
| Node.js | v24.11.1 | |
| npm | 11.6.2 | |
| .NET SDK | 10.0.401 | Primary target — pinned in `global.json`. 8.0.425 and 9.0.318 were also present but unused. |
| Visual Studio | Community 2026, 18.10.12201.205 | Satisfies the VS 2026 18.x+ requirement for .NET 10; the ASP.NET/web workload must be installed. |
| SQL Server | 2025 Developer (17.0.1000.7), default instance `MSSQLSERVER` | The app's databases. |
| SQL Server LocalDB | `MSSQLLocalDB` | Only the backend integration tests use it. |
| PowerShell | 7.5.4 required (**not** the default `pwsh` on that machine — see below) | |

Install/verify these yourself if starting from scratch:

- **.NET 10 SDK**: https://dotnet.microsoft.com/download — `dotnet --list-sdks` should include a `10.x` entry.
- **Visual Studio 2026** with the **ASP.NET and web development** workload (only needed for the F5 workflow; the scripts run without it).
- **SQL Server** (Developer edition is free) as the default instance, listening on TCP 51433 with SQL Server and Windows authentication (mixed mode) enabled. The app connects to it at the server's public address, as the SQL login `mpsellertools` — see [Database connection](#database-connection).
- **SQL Server Express LocalDB**, for the backend tests only: installed with Visual Studio's "SQL Server Data Tools" component, or standalone from Microsoft.
- **Node.js 20+** and npm.
- **PowerShell 7+**: https://aka.ms/powershell — see the ARM64/PowerShell note below if `pwsh --version` reports 6.x.
- **Caddy 2.11+** — only for [a trusted certificate on a public address](#with-a-trusted-certificate-caddy).

### ⚠️ ARM64 Windows

If you're on an ARM64 Windows machine (as this project was first built and
tested on), two things need attention that don't apply on ordinary x64 Windows:

1. **SQL Server LocalDB is x64-only.** An ARM64-hosted .NET process cannot
   open a LocalDB connection at all — it fails with an opaque
   `SqlUserInstance.dll`/`hostfxr.dll` load error. You need an **x64 .NET 10
   SDK installed side-by-side** with the default ARM64 one, and it must be
   first on `PATH` (or referenced via `DOTNET_ROOT`) whenever you run
   anything that touches LocalDB — `dotnet ef`, the scripts below, or the
   hosts themselves. Install one with:
   ```powershell
   Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
   .\dotnet-install.ps1 -Channel 10.0 -Architecture x64 -InstallDir "$HOME\.dotnet-x64" -Version 10.0.401 -NoPath
   $env:PATH = "$HOME\.dotnet-x64;$env:PATH"   # do this in every new shell before running the scripts below
   ```
   If you've already run `dotnet tool install --global dotnet-ef` under the
   ARM64 SDK, reinstall it once the x64 SDK is on `PATH` so its generated
   apphost is x64 too: `dotnet tool uninstall --global dotnet-ef && dotnet
   tool install --global dotnet-ef`.
2. **Multiple PowerShell versions can be installed side by side** (that
   machine had both 6.1.3 and 7.5.4, with 6.1.3 first on `PATH`). All
   scripts below require PowerShell 7+ (`#Requires -Version 7.0`) and will
   refuse to run under 6.x. If `pwsh --version` shows a 6.x, invoke the
   scripts with the full path instead: `& "C:\Program Files\PowerShell\7\pwsh.exe" -File .\scripts\Setup-Dev.ps1`.

## Quick start

```powershell
.\scripts\Setup-Dev.ps1     # one-time: prerequisites, migrations, initial PlatformAdmin, demo Company A/B
.\scripts\Start-Dev.ps1     # starts the full dev environment (DevHost -> PlatformHost + Worker -> tenants)
```

Then open https://localhost:7100/login in a browser and sign in with the
account in `.local/platform/dev-admin-credentials.txt` (see
[Login URLs and accounts](#login-urls-and-accounts)). When you're done:

```powershell
.\scripts\Stop-Dev.ps1
```

`Start-Dev.ps1` keeps running in its window and prints the hosts' logs; press
Ctrl+C there, or run `Stop-Dev.ps1` from another window, to stop everything.

## Scripts

All in `scripts/`, all PowerShell 7+ unless noted.

| Script | What it does |
| --- | --- |
| `Setup-Dev.ps1` | One-time, safe to re-run. Checks prerequisites, trusts the HTTPS development certificate, migrates the platform database, builds, creates the initial PlatformAdmin, and seeds demo Company A and Company B through the real provisioning pipeline. `-SkipBuild` reuses an existing build. |
| `Start-Dev.ps1` | Checks port 7100 is free and SQL Server is running, then runs DevHost, which publishes and starts PlatformHost and the worker (and through it every Active company). `-PublicHost` and `-BehindProxy` open it to other machines — see [below](#reaching-it-from-another-machine). |
| `Stop-Dev.ps1` | Stops only this project's processes (verified by PID and start time, never a blanket `dotnet` kill), then asks the worker to stop every company instance. Safe to run when nothing is running. |
| `Build.ps1` | Installs frontend dependencies if the manifests changed, builds both frontends into each host's `wwwroot`, builds the solution and publishes the three components to `.local/build/`. `-Configuration Debug`, `-ForceInstall`. |
| `Verify.ps1` | Runs every automated check: restore/build, frontend lint/typecheck/build, backend tests, with a pass/fail summary. |
| `Seed-Marketplaces.ps1` | Fills one company's workspace with six demo products listed as drafts on eBay, Amazon and Walmart; the eBay drafts carry everything eBay asks for and pass the pre-publish check. Nothing is sent to a marketplace. Needs `-Url`, `-Email`, `-Password` of a TenantAdmin. |
| `Seed-Workspace.ps1` | After `Seed-Marketplaces.ps1`: adds orders and tasks in every status, safety stock, a return, two pending invitations and a low-stock threshold, shared out among the company's users. Same `-Url`, `-Email`, `-Password`. |
| `Seed-MagentoOtc.ps1` | Fills one company's workspace with a large demo catalog of over-the-counter pharmacy products (2,500 unless `-Count` says otherwise) and puts each on Magento as a draft. Nothing is sent to a store. Same `-Url`, `-Email`, `-Password`. |
| `Auto-Push.ps1` | Commits and pushes any pending changes to `origin master`; meant to be run on a schedule by Task Scheduler. PowerShell 5.1+. |
| `Caddyfile` | Not a script: the [Caddy](https://caddyserver.com) configuration for a trusted certificate on a public address. |

## Ports

| Port | Used by | Notes |
| --- | --- | --- |
| 7100 | PlatformHost | Fixed. |
| 7201–7299 | TenantHost, one per company | Each new company takes the port after the highest one in use: Company A is 7201 and Company B 7202 on a fresh setup. |
| 5100 / 5201 | Vite dev servers (platform / workspace) | Only in [hot-reload mode](#hot-reload-mode-vite). |
| 80, 443 | Caddy | Only with [a trusted certificate](#with-a-trusted-certificate-caddy): certificate validation, and a redirect from the bare address to port 7100. |

Everything is HTTPS. There is no plain-HTTP mode: the sign-in cookies are
marked Secure and would not be sent.

## Login URLs and accounts

| User | Login URL | Notes |
| --- | --- | --- |
| PlatformAdmin | https://localhost:7100/login | Fixed port. |
| TenantAdmin / Employee, Company A | Shown on the company's `/tenants/:id` page in the platform console, or in `.local/tenants/company-a/demo-credentials.txt` after `Setup-Dev.ps1` | See [Ports](#ports) for how the port is chosen. |
| Any company created via the console | Shown on that company's `/tenants/:id` page once it reaches Active | |

A company's TenantAdmin and Employees share the same login URL — their
landing page and menu depend on their role after signing in, not on the URL.

Where the accounts come from:

- **PlatformAdmin**: generated the first time PlatformHost starts in
  Development mode (so by `Setup-Dev.ps1` or the first `Start-Dev.ps1`). The
  email is `admin@mpsellertools.local`; the password is random and written to
  `.local/platform/dev-admin-credentials.txt` (git-ignored, never logged,
  never a hardcoded default). It is not regenerated on later runs.
- **Demo Company A / Company B**: `Setup-Dev.ps1` creates both through the
  real provisioning pipeline (not by inserting database rows), accepts their
  TenantAdmin and Employee invitations itself, and writes the resulting
  credentials to `.local/tenants/<slug>/demo-credentials.txt`. These demo
  passwords are fixed values written in the script — see
  [Security notes](#security-notes).
- **A company you create yourself** in the platform console: the initial
  administrator's invitation link is written to that company's local dev
  outbox at `.local/tenants/<slug>/outbox/*.json` (there is no real email).
  Open the newest file there and follow the link to set a password. A
  PlatformAdmin can also add users directly from the console's user pages.

Password rules everywhere: at least 12 characters; five failed sign-ins lock
the account for 15 minutes; sessions last 8 hours, sliding.

## Reaching it from another machine

By default everything listens on localhost only and nothing outside the
machine can reach it. There are two ways to open it up; both use the host
name or IP address other machines will type (`203.0.113.10` below is a
placeholder).

Either way, this exposes the login pages and APIs to anyone who can reach the
machine. Starting again without `-PublicHost` puts everything back on
localhost, and company links follow the setting the next time each company is
started.

### Directly (browser certificate warning)

```powershell
New-NetFirewallRule -DisplayName "MPSellerTools (dev)" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 7100,"7201-7299"   # once, elevated
.\scripts\Start-Dev.ps1 -PublicHost 203.0.113.10
```

PlatformHost and every company instance listen on all interfaces and accept
requests addressed to that host; company links in the console use it. Browsers
warn about the certificate, because the HTTPS development certificate is only
valid for `localhost`; visitors have to click through the warning.

### With a trusted certificate (Caddy)

[Caddy](https://caddyserver.com) 2.11 or later sits in front, answers on the
public address with a Let's Encrypt certificate it obtains and renews by
itself, and forwards each port to the host on the same port on localhost. The
hosts themselves stay on localhost. This works for a bare IP address — no
domain name needed.

```powershell
winget install --id CaddyServer.Caddy -e --source winget          # once
New-NetFirewallRule -DisplayName "MPSellerTools (dev)" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 7100,"7201-7299"                         # once, elevated
New-NetFirewallRule -DisplayName "MPSellerTools (dev) certificate validation" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80,443            # once, elevated

$env:MPST_PUBLIC_HOST = "203.0.113.10"; caddy run --config scripts/Caddyfile --adapter caddyfile   # leave running
.\scripts\Start-Dev.ps1 -PublicHost 203.0.113.10 -BehindProxy                                       # in another window
```

Then `https://203.0.113.10:7100/login` opens without a warning, and
`https://203.0.113.10` redirects there.

Things to know:

- Let's Encrypt validates the address over ports 80/443, so they must be
  reachable from the internet — check the hosting provider's network firewall
  as well as Windows Firewall.
- A certificate for a bare IP address only exists in Let's Encrypt's
  "shortlived" profile and lasts about six days. Caddy renews it
  automatically, but **only while it is running**: if Caddy is stopped for
  longer than that, the certificate expires and browsers warn again until it
  is started and renews.
- In this mode nothing uses `localhost` addresses: the hosts also reach each
  other through the public address (the worker's readiness check, the
  platform's calls to a company's instance), because that is the only name
  with a certificate every process trusts. The hosts still *listen* on
  localhost, since Caddy holds the same ports on the public address.
- `scripts/Caddyfile` forwards 7100 and the first forty company ports
  (7201–7240). A company on a port outside that list cannot be provisioned
  until the port is added to the list.
- A company port with nothing running behind it answers 502 Bad Gateway.
- Caddy keeps its certificates under `%AppData%\Caddy`.

## Configuration

Each host reads `appsettings.json`, then environment variables (`:` in a key
becomes `__`). The scripts set what is needed; these are the ones worth knowing.

| Setting | Where | Meaning |
| --- | --- | --- |
| `ConnectionStrings:PlatformDatabase` | PlatformHost, worker | The registry database. `MPSellerTools_Platform` on this machine's SQL Server — see [Database connection](#database-connection). The worker creates each company's database on the same server with the same login. |
| `ConnectionStrings:TenantDatabase` | TenantHost | That company's database; written into its instance config by the worker. |
| `Hosting:LocalDataDirectory` | all | Where keys, logs, outbox and credentials go. Default: `.local/` at the solution root. |
| `Hosting:DevSpaOrigins` | both hosts | Origins allowed to call the API cross-origin in Development (the Vite dev servers). |
| `AllowedHosts` | both hosts | Host names the host answers to. `localhost` unless `-PublicHost` adds one. |
| `MPST_INSTANCE_CONFIG_FILE` | TenantHost | Path of the per-company config file that binds the process to one company. |
| `MPST_PUBLIC_HOST`, `MPST_BEHIND_PROXY` | DevHost, Caddy | Set by `Start-Dev.ps1 -PublicHost … -BehindProxy`; `MPST_PUBLIC_HOST` is also what `scripts/Caddyfile` reads. |
| `Provisioning:TenantHostPublishDirectory` | worker | The published TenantHost the worker launches (`.local/build/TenantHost`). |
| `Provisioning:PublicHost`, `Provisioning:BehindProxy` | worker | The public address for company links, and whether a proxy answers on it. |
| `Hosting:BehindProxy` | PlatformHost | When true, the platform calls company instances at their public address instead of localhost. |
| `Provisioning:DotnetExecutablePath` | worker | The `dotnet` used to launch company instances; must be x64 on ARM64 Windows. |
| `Marketplace:*` | TenantHost | The multichannel switches — see [`docs/marketplace-operations.md`](docs/marketplace-operations.md). |

PlatformHost runs in the Development environment under the scripts (which is
what applies migrations and creates the first admin on startup, and serves the
OpenAPI document at `/openapi/v1.json`); company instances run as Production.

## Visual Studio F5 / attach workflow

1. Open `MPSellerTools.sln` in Visual Studio 2026.
2. Set **MPSellerTools.DevHost** as the startup project (right-click it in
   Solution Explorer → "Set as Startup Project").
3. Press F5. DevHost publishes PlatformHost, TenantHost, and
   Provisioning.Worker fresh (this takes a few seconds — `dotnet build`
   alone does not copy the frontend's `wwwroot` into the build output, only
   `dotnet publish` does), starts PlatformHost, waits for it to report
   healthy, then starts the worker. The worker in turn starts (or, on a
   second run, reconciles and restarts) any tenants already recorded as
   Active in the platform database.
4. To debug a specific child process (say, TenantHost for Company A):
   **Debug → Attach to Process...**, filter by name (`dotnet.exe` or
   `MPSellerTools.TenantHost.exe`), and match it up using the console output
   DevHost printed (it logs each child's PID) or Task Manager's command-line
   column, which shows the full path under `.local/build/TenantHost/`.
5. DevHost does not launch itself recursively — it only ever starts
   PlatformHost and the Worker, and the Worker only ever starts TenantHost.
6. Required Visual Studio workload: **ASP.NET and web development**. No
   other workload is required (no Node.js workload needed — the frontend
   build runs via plain `npm`/`node` on `PATH`, orchestrated by
   `scripts/Build.ps1`, not through Visual Studio's own Node tooling).

Frontend source (`frontend/apps/platform`, `frontend/apps/workspace`,
`frontend/packages/ui`) is not part of the `.sln` (Visual Studio doesn't
manage npm-based projects natively) — open it directly in VS Code or your
editor of choice, or add it to the solution as a **Solution Folder** with
"Add > Existing Item" for convenient browsing from within Visual Studio.

## Frontend development

The three packages under `frontend/` are one npm workspace: dependencies are
installed once at `frontend/` (`npm install` there, or let `Build.ps1` do it)
and shared, so there is a single `frontend/package-lock.json`.

| Package | What it is | Builds into |
| --- | --- | --- |
| `frontend/apps/platform` | React platform console (TypeScript, Vite) | `src/MPSellerTools.PlatformHost/wwwroot` |
| `frontend/apps/workspace` | React company workspace (TypeScript, Vite) | `src/MPSellerTools.TenantHost/wwwroot` |
| `frontend/packages/ui` | Shared UI components and theme | consumed by both apps as source |

After changing frontend code, run `.\scripts\Build.ps1` (or just restart with
`Start-Dev.ps1`, which republishes) to see it in the running hosts.

### Hot-reload mode (Vite)

For fast frontend iteration without rebuilding the whole backend each time:

```powershell
cd frontend/apps/platform   # or apps/workspace
npm run dev                 # serves on localhost:5100 (platform) or :5201 (workspace) with hot reload
```

This proxies `/api/*` to the corresponding host (see each app's
`vite.config.ts`), so the real backend (`Start-Dev.ps1`) must already be
running. The primary, verified startup workflow is the built static assets
via DevHost/F5 — this mode is a convenience for UI work, not a replacement.

## Tests and checks

```powershell
.\scripts\Verify.ps1                 # everything the automated checks can cover
dotnet test MPSellerTools.sln        # backend unit + integration tests only (needs LocalDB; takes a few minutes)
```

- **Backend** (`tests/MPSellerTools.Tests`, xUnit): unit tests plus
  integration tests that run the real host pipelines against real LocalDB
  databases, with in-process fakes standing in for the marketplaces.
- **Browser smoke tests** (`tests/e2e/tests`, Playwright): run against the
  started dev environment.
- **UI regression checks** for table search/pagination and sidebar styling:
  `npm run test:ui` from `tests/e2e`. They start Vite and mock API responses,
  so they do not require the backend or demo accounts. Install Playwright's
  Chromium with `npx playwright install chromium`, or use the installed Edge
  browser in PowerShell with `$env:PLAYWRIGHT_CHANNEL = "msedge"` before
  running the checks.

## Multichannel catalog (Amazon, eBay, Walmart, website)

Products have variants with their own SKU, price and stock, and each variant
can be listed on Amazon, eBay, Walmart and the company's own website with
channel-specific content, price, quantity and pictures. Changes reach the
channels through an outbox and a background worker in each tenant's host.

**It is off by default and has never been run against a real marketplace.**
Out of the box every channel operation is a dry run and orders leave stock
alone. The adapters are tested against in-process fakes only; no credentials
were available to try a sandbox.

To see it with data, seed a company's workspace with demo products and draft
listings (nothing is sent anywhere):

```powershell
.\scripts\Seed-Marketplaces.ps1 -Url https://localhost:7201 -Email admin@company-a.local -Password <that admin's password>
```

- [`docs/multichannel-catalog.md`](docs/multichannel-catalog.md) — data model, ER diagram, data ownership, how sync works
- [`docs/marketplace-operations.md`](docs/marketplace-operations.md) — configuration, the admin API, dry runs, retries, rollback, going live step by step, and the checklist of what is and is not done
- [`docs/marketplace-integrations.md`](docs/marketplace-integrations.md) — which API operations were verified against official documentation, and which are assumptions

## Local data

Everything the running system writes outside the databases goes under
`.local/` at the solution root, which is git-ignored:

| Path | Contents |
| --- | --- |
| `.local/build/` | The published PlatformHost, TenantHost and Worker that actually run. |
| `.local/platform/dev-admin-credentials.txt` | The PlatformAdmin email and password. |
| `.local/platform/keys/`, `.local/tenants/<slug>/keys/` | Data-protection keys (cookies, stored marketplace credentials). Deleting them signs everyone out and makes stored credentials unreadable. |
| `.local/platform/logs/`, `.local/worker/logs/`, `.local/tenants/<slug>/logs/` | Daily log files, 14 days kept. |
| `.local/tenants/<slug>/outbox/` | The "emails" that company would have sent (invitations, password resets). |
| `.local/tenants/<slug>/instance-config.json` | Binds that company's process to its database. |
| `.local/tenants/<slug>/demo-credentials.txt` | Demo companies only. |
| `.local/devhost-state.json` | PIDs of the running processes, for `Stop-Dev.ps1`. |

The databases are in the default SQL Server instance: `MPSellerTools_Platform`
and one `MPSellerTools_Tenant_<slug>` per company.

### Database connection

The app reaches SQL Server at the server's public address, not `localhost`,
and signs in with the SQL login `mpsellertools`. The address and the password
are not in the repository: each host reads them from an
`appsettings.Local.json` next to its `appsettings.json`, which git ignores.
The committed `appsettings.json` files hold only a placeholder without a
password.

On a new machine, create the three files (TenantHost's is only its `dotnet
run` fallback; a provisioned company gets its connection string from the
worker):

```jsonc
// src/MPSellerTools.PlatformHost/appsettings.Local.json
// src/MPSellerTools.Provisioning.Worker/appsettings.Local.json
{ "ConnectionStrings": { "PlatformDatabase": "Server=<address>,51433;Database=MPSellerTools_Platform;User Id=mpsellertools;Password=<password>;TrustServerCertificate=True" } }

// src/MPSellerTools.TenantHost/appsettings.Local.json
{ "ConnectionStrings": { "TenantDatabase": "Server=<address>,51433;Database=MPSellerTools_Tenant_dev;User Id=mpsellertools;Password=<password>;TrustServerCertificate=True" } }
```

The login is in the `dbcreator` server role and owns the app's databases; it
is not a sysadmin. The Windows firewall rule `MPSellerTools SQL Server (TCP
51433)` lets other machines connect with the same details (SSMS, `sqlcmd -S
<address>,51433 -U mpsellertools`).

## Troubleshooting

- **LocalDB errors mentioning `SqlUserInstance.dll` or `hostfxr.dll`**: see
  the ARM64 section above — you're running under the wrong SDK architecture.
- **The hosts cannot reach the database**: check the SQL Server service with
  `Get-Service MSSQLSERVER` (both `Setup-Dev.ps1` and `Start-Dev.ps1` start
  it when it is stopped).
- **HTTPS certificate warning on `localhost`**: run `dotnet dev-certs https
  --trust` (also done by `Setup-Dev.ps1`), then restart your browser.
- **HTTPS certificate warning on a public address**: expected with
  `-PublicHost` alone. Use [Caddy](#with-a-trusted-certificate-caddy); if it
  is already in use, check that Caddy is running and that ports 80/443 are
  reachable from the internet.
- **502 Bad Gateway on a public address**: Caddy is up but the host behind
  that port is not — run `Start-Dev.ps1 -PublicHost … -BehindProxy`, or the
  company on that port is suspended or does not exist.
- **The site doesn't load at all from another machine**: the firewall rule is
  missing, the hosting provider's own firewall blocks the port, or
  `Start-Dev.ps1` was started without `-PublicHost`.
- **"Port already in use" when starting**: `Start-Dev.ps1` checks port 7100
  up front and reports the conflicting process's PID and name. For a tenant
  port (7201+), check `Get-NetTCPConnection -LocalPort <port> -State
  Listen` yourself, or run `Stop-Dev.ps1` first. If Caddy holds the port,
  start with `-BehindProxy`.
- **`#Requires -Version 7.0` error running a script**: your `pwsh` on `PATH`
  is PowerShell 6.x — see the ARM64/PowerShell note above.
- **"Provisioning failed … did not become ready within 30s"**: the company's
  process started but the worker could not verify it over HTTPS. Without a
  proxy the worker calls `https://localhost:<port>`, which needs the HTTPS
  development certificate to be trusted on this machine — `Setup-Dev.ps1`
  does that (`dotnet dev-certs https --trust`). Behind Caddy it calls the
  public address instead, so check that Caddy is running and forwards that
  port. Then use **Retry** on the company's page.
- **A tenant shows Active in the console but its URL doesn't load**: the
  provisioning worker reconciles this automatically on its next startup (or
  restart it via `Stop-Dev.ps1` + `Start-Dev.ps1`) — it checks every Active
  tenant's process on boot and relaunches any that aren't actually running.

## Local isolation limitations

This is a **local development** setup, explicitly not production-hardened:

- All companies' databases live on the **same shared SQL Server engine
  instance** on this machine — application and database separation is real
  (separate databases, separate connection strings, separate EF Core
  contexts), but there is no privilege boundary between them: every
  TenantHost signs in with the same SQL login, which could, in principle,
  open any of the other tenant databases directly with SQL tooling. The SQL
  port is also open to other machines, so that login's password is all that
  protects every database. See
  `docs/windows-deployment.md` for how production should separate this with
  distinct database users and OS identities.
- **No real email is ever sent.** Every invitation, password reset, etc. is
  written to a per-tenant "dev outbox" JSON file under
  `.local/tenants/<slug>/outbox/` instead. This is intentional and not
  something to "fix" — it's the permanent local-dev behavior, not a stand-in
  for a future mail integration that doesn't exist yet.
- **No graceful in-flight-request drain on Suspend.** Suspending a tenant
  terminates its process outright rather than waiting for active requests to
  finish. Suspend really does stop access, but it is not a zero-downtime
  operation.
- **Local single-Windows-user execution does not provide the same privilege
  boundary a production deployment would.** The provisioning worker runs as
  whatever user started it and can create databases and start processes
  under that same identity — see `docs/windows-deployment.md` for the
  separate privileged deployment identity a real deployment needs.

## Security notes

Relevant as soon as the machine is reachable by other people:

- **The demo companies have fixed, published passwords.** `Setup-Dev.ps1`
  gives Company A and Company B's demo users passwords that are written in
  the script. Do not run it on a machine opened with `-PublicHost`, or change
  those passwords (or delete the demo companies) before opening it.
- **The PlatformAdmin password** is random and only in
  `.local/platform/dev-admin-credentials.txt`. Anyone with it controls every
  company; keep that file private.
- PlatformHost runs in Development mode under the scripts, so its OpenAPI
  document is readable without signing in.
- Nothing in `.local/` belongs in Git; `.gitignore` already excludes it.
- What is in place: HTTPS only, HttpOnly + Secure + SameSite=Strict cookies,
  antiforgery tokens on every state-changing API call, account lockout, and
  marketplace credentials encrypted at rest. What is not: rate limiting, a
  web application firewall, separate OS identities per company, or any
  external security review.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/MPSellerTools.PlatformHost` | ASP.NET Core platform API + platform static frontend |
| `src/MPSellerTools.TenantHost` | ASP.NET Core tenant API + workspace static frontend |
| `src/MPSellerTools.Core` | Domain models and application contracts, no UI/EF dependency |
| `src/MPSellerTools.Infrastructure` | EF Core contexts (Platform + Tenant), Identity, migrations |
| `src/MPSellerTools.Provisioning.Worker` | Job queue processing and local tenant process management |
| `src/MPSellerTools.DevHost` | Local launcher used by `Start-Dev.ps1` and as the Visual Studio F5 startup project |
| `frontend/apps/platform` | React platform console |
| `frontend/apps/workspace` | React tenant workspace (TenantAdmin + Employee) |
| `frontend/packages/ui` | Shared UI components adapted from Creative Tim's Material Dashboard React (see `docs/template-adaptation.md`) |
| `tests/MPSellerTools.Tests` | xUnit unit + integration tests |
| `tests/e2e` | Playwright browser smoke tests and UI regression checks |
| `scripts` | PowerShell setup/build/start/stop/verify scripts and the Caddy configuration |
| `docs` | Architecture, template adaptation, marketplace and deployment documentation |

## Further documentation

| Document | Covers |
| --- | --- |
| [`docs/architecture.md`](docs/architecture.md) | Component and database diagram, platform access to tenant users, eBay integration, process tree, provisioning / suspend / resume / delete flows |
| [`docs/windows-deployment.md`](docs/windows-deployment.md) | Guidance for a real Windows Server deployment: topology, identities, publishing, migrations, backups |
| [`docs/multichannel-catalog.md`](docs/multichannel-catalog.md) | Catalog and listing data model and how sync works |
| [`docs/marketplace-operations.md`](docs/marketplace-operations.md) | Running the multichannel catalog: configuration, going live, rollback |
| [`docs/marketplace-integrations.md`](docs/marketplace-integrations.md) | What was verified against each marketplace's documentation |
| [`docs/template-adaptation.md`](docs/template-adaptation.md) | What was taken from the UI template and what was changed |

## Third-party code

`frontend/packages/ui/` is adapted from Creative Tim's Material Dashboard 2
React (MIT License, Copyright (c) 2019 Creative Tim). See
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) and
`frontend/packages/ui/LICENSE-creative-tim.md`. The project itself has no
licence file, so no rights are granted to others beyond what GitHub's terms
give for a public repository.
