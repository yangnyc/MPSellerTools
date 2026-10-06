# MPSellerTools

A Windows/Visual Studio solution (React + ASP.NET Core) implementing a
**Multiple Application, Multiple Database** multi-tenant architecture: one
platform console manages a registry of companies, and each company runs its
own isolated ASP.NET Core process against its own SQL Server database.

See `MPSellerTools-Claude-Instructions-EN.md` (in the parent directory) for
the full brief this project implements, and `docs/architecture.md` for a
diagram of how the pieces fit together.

## Prerequisites

Detected in the environment this project was built and verified in:

| Tool | Version found | Notes |
| --- | --- | --- |
| Git | 2.51.2.windows.1 | |
| Node.js | v24.11.1 | |
| npm | 11.6.2 | |
| .NET SDK | 10.0.401 | Primary target — pinned in `global.json`. 8.0.425 and 9.0.318 were also present but unused. |
| Visual Studio | Community 2026, 18.10.12201.205 | Satisfies the VS 2026 18.x+ requirement for .NET 10; the ASP.NET/web workload must be installed. |
| SQL Server LocalDB | `MSSQLLocalDB`, engine 17.0.4025.3 (SQL Server 2025) | |
| PowerShell | 7.5.4 required (**not** the default `pwsh` on this machine — see below) | |

Install/verify these yourself if starting from scratch:
- **.NET 10 SDK**: https://dotnet.microsoft.com/download — `dotnet --list-sdks` should include a `10.x` entry.
- **Visual Studio 2026** with the **ASP.NET and web development** workload.
- **SQL Server Express LocalDB**: installed with Visual Studio's "SQL Server Data Tools" component, or standalone from Microsoft.
- **Node.js 20+** and npm.
- **PowerShell 7+**: https://aka.ms/powershell — see the ARM64/PowerShell note below if `pwsh --version` reports 6.x.

### ⚠️ ARM64 Windows

If you're on an ARM64 Windows machine (as this project was built and tested
on), two things need attention that don't apply on ordinary x64 Windows:

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
2. **Multiple PowerShell versions can be installed side by side** (this
   machine had both 6.1.3 and 7.5.4, with 6.1.3 first on `PATH`). All
   scripts below require PowerShell 7+ (`#Requires -Version 7.0`) and will
   refuse to run under 6.x. If `pwsh --version` shows a 6.x, invoke the
   scripts with the full path instead: `& "C:\Program Files\PowerShell\7\pwsh.exe" -File .\scripts\Setup-Dev.ps1`.

## Quick start

```powershell
.\scripts\Setup-Dev.ps1     # one-time: prerequisites, migrations, initial PlatformAdmin, demo Company A/B
.\scripts\Start-Dev.ps1     # starts the full dev environment (DevHost -> PlatformHost + Worker -> tenants)
```

Then open https://localhost:7100/login in a browser (see login URLs below).
When you're done:

```powershell
.\scripts\Stop-Dev.ps1
```

To rebuild after changing code (both scripts also do this, but standalone):

```powershell
.\scripts\Build.ps1
```

To run everything the automated checks can cover:

```powershell
.\scripts\Verify.ps1
```

UI regression checks for table search/pagination and sidebar styling run with
`npm run test:ui` from `tests/e2e`. They start Vite and mock API responses, so
they do not require the backend or demo accounts. Install Playwright's Chromium
with `npx playwright install chromium`, or use the installed Edge browser in
PowerShell with `$env:PLAYWRIGHT_CHANNEL = "msedge"` before running the checks.

## Login URLs

| User | Local login URL | Notes |
| --- | --- | --- |
| PlatformAdmin | https://localhost:7100/login | Fixed port. |
| TenantAdmin / Employee, Company A | Shown in the PlatformAdmin console's `/tenants/:id` page, or `.local/tenants/company-a/demo-credentials.txt` after Setup-Dev.ps1 | Port is assigned dynamically starting at 7201; on a fresh setup with no prior companies, Company A lands on exactly 7201 and Company B on 7202 (brief §6's example table), but ports increment for every company ever created, including ones from earlier test runs. |
| Any company created via the console | Shown on that company's `/tenants/:id` page once it reaches Active | |

A company's TenantAdmin and Employees share the same login URL — their
landing page and menu depend on their role after signing in, not on the URL.

## How to obtain development accounts

- **PlatformAdmin**: `scripts/Setup-Dev.ps1` generates a random password on
  first run and writes it to `.local/platform/dev-admin-credentials.txt`
  (git-ignored, never logged, never a hardcoded default). Re-running the
  script does not reset it.
- **Demo Company A / Company B**: `Setup-Dev.ps1` creates both through the
  real provisioning pipeline (not by inserting database rows), accepts their
  TenantAdmin and Employee invitations itself, and writes the resulting
  credentials to `.local/tenants/<slug>/demo-credentials.txt`.
- **A company you create yourself** via the PlatformAdmin console: the
  initial administrator's invitation link is written to that tenant's local
  dev outbox at `.local/tenants/<slug>/outbox/*.json` (there is no real
  email — see "Local isolation limitations" below). Open the newest file
  there and follow the `token=...` link on the company's login URL to set a
  password.

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
   `scripts/Build.ps1` / DevHost, not through Visual Studio's own Node
   tooling).

Frontend source (`frontend/apps/platform`, `frontend/apps/workspace`,
`frontend/packages/ui`) is not part of the `.sln` (Visual Studio doesn't
manage npm-based projects natively) — open it directly in VS Code or your
editor of choice, or add it to the solution as a **Solution Folder** with
"Add > Existing Item" for convenient browsing from within Visual Studio.

### Optional Vite/HMR dev mode

For fast frontend iteration without rebuilding the whole backend each time:

```powershell
cd frontend/apps/platform   # or apps/workspace
npm run dev                 # serves on localhost:5100 (platform) or :5201 (workspace) with hot reload
```

This proxies `/api/*` to the corresponding host (see each app's
`vite.config.ts`), so the real backend (`Start-Dev.ps1`) must already be
running. The primary, verified startup workflow is the built static assets
via DevHost/F5 — this mode is a convenience for UI work, not a replacement.

## Troubleshooting

- **LocalDB errors mentioning `SqlUserInstance.dll` or `hostfxr.dll`**: see
  the ARM64 section above — you're running under the wrong SDK architecture.
- **`sqllocaldb` says the instance isn't running**: `sqllocaldb start
  MSSQLLocalDB` (both `Setup-Dev.ps1` and `Start-Dev.ps1` do this
  automatically, but the instance can be stopped externally).
- **HTTPS certificate warnings in the browser**: run `dotnet dev-certs https
  --trust` (also done by `Setup-Dev.ps1`), then restart your browser.
- **"Port already in use" when starting**: `Start-Dev.ps1` checks port 7100
  up front and reports the conflicting process's PID and name. For a tenant
  port (7201+), check `Get-NetTCPConnection -LocalPort <port> -State
  Listen` yourself, or run `Stop-Dev.ps1` first.
- **`#Requires -Version 7.0` error running a script**: your `pwsh` on `PATH`
  is PowerShell 6.x — see the ARM64/PowerShell note above.
- **A tenant shows Active in the console but its URL doesn't load**: the
  provisioning worker reconciles this automatically on its next startup (or
  restart it via `Stop-Dev.ps1` + `Start-Dev.ps1`) — it checks every Active
  tenant's process on boot and relaunches any that aren't actually running.

## Local isolation limitations

This is a **local development** setup, explicitly not production-hardened:

- All companies' databases live on the **same shared LocalDB engine
  instance** on this machine — application and database separation is real
  (separate databases, separate connection strings, separate EF Core
  contexts), but there is no OS-level privilege boundary between them: the
  same Windows user account that runs one TenantHost could, in principle,
  open any of the other tenant databases directly with SQL tooling. See
  `docs/windows-deployment.md` for how production should separate this with
  distinct database users and OS identities.
- **No real email is ever sent.** Every invitation, password reset, etc. is
  written to a per-tenant "dev outbox" JSON file under
  `.local/tenants/<slug>/outbox/` instead. This is intentional (the brief
  explicitly forbids sending real email) and not something to "fix" — it's
  the permanent local-dev behavior, not a stand-in for a future mail
  integration that doesn't exist yet.
- **No graceful in-flight-request drain on Suspend.** Suspending a tenant
  terminates its process outright rather than waiting for active requests to
  finish. This satisfies the brief's requirement that Suspend "actually
  stops access" but is not a zero-downtime operation.
- **Local single-Windows-user execution does not provide the same privilege
  boundary a production deployment would.** The provisioning worker runs as
  whatever user started it and can create databases and start processes
  under that same identity — see `docs/windows-deployment.md` for the
  separate privileged deployment identity a real deployment needs.

## Multichannel catalog (Amazon, eBay, Walmart, website)

Products have variants with their own SKU, price and stock, and each variant
can be listed on Amazon, eBay, Walmart and the company's own website with
channel-specific content, price and quantity. Changes reach the channels
through an outbox and a background worker in each tenant's host.

**It is off by default and has never been run against a real marketplace.**
Out of the box every channel operation is a dry run and orders leave stock
alone, as before. The adapters are tested against in-process fakes only; no
credentials were available to try a sandbox.

- [`docs/multichannel-catalog.md`](docs/multichannel-catalog.md) — data model, ER diagram, data ownership, how sync works
- [`docs/marketplace-operations.md`](docs/marketplace-operations.md) — configuration, the admin API, dry runs, retries, rollback, and the checklist of what is and is not done
- [`docs/marketplace-integrations.md`](docs/marketplace-integrations.md) — which API operations were verified against official documentation, and which are assumptions

There is no workspace screen for it yet; it is driven through the tenant API.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/MPSellerTools.PlatformHost` | ASP.NET Core platform API + platform static frontend |
| `src/MPSellerTools.TenantHost` | ASP.NET Core tenant API + workspace static frontend |
| `src/MPSellerTools.Core` | Domain models and application contracts, no UI/EF dependency |
| `src/MPSellerTools.Infrastructure` | EF Core contexts (Platform + Tenant), Identity, migrations |
| `src/MPSellerTools.Provisioning.Worker` | Job queue processing and local tenant process management |
| `src/MPSellerTools.DevHost` | Local launcher used as the Visual Studio F5 startup project |
| `frontend/apps/platform` | React platform console |
| `frontend/apps/workspace` | React tenant workspace (TenantAdmin + Employee) |
| `frontend/packages/ui` | Shared UI components adapted from Creative Tim's Material Dashboard React (see `docs/template-adaptation.md`) |
| `tests/MPSellerTools.Tests` | xUnit unit + integration tests |
| `tests/e2e` | Playwright browser smoke tests |
| `scripts` | PowerShell setup/build/start/stop/verify scripts |
| `docs` | Architecture, template adaptation, and deployment documentation |
