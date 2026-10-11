# SellerTool

Repository: https://github.com/yangnyc/MPSellerTools

MPSellerTools ("SellerTool") is a multi-tenant seller workspace: a platform
console, one ASP.NET Core host and one SQL Server database per company, and a
provisioning worker that creates and supervises them. `README.md` and
`docs/architecture.md` describe it in full.

## Development rules

- Treat this repository as the SellerTool project.
- Do not commit directly to `master`; work on a branch and open a pull request.
- Analyze existing architecture before making structural changes.
- Follow existing coding conventions.
- Do not introduce dependencies unless necessary.
- Do not modify unrelated code.
- Run relevant tests before considering a task complete.
- Never commit credentials, API keys, tokens, or secrets. Runtime configuration,
  keys, logs and credentials live in the git-ignored `.local/` directory.
- Never select a database from a tenant id in a URL, header or request, and
  never serve two companies from one tenant host. One company, one process,
  one database.
- Nothing may reach a real marketplace unless `Marketplace:LiveWritesEnabled`
  and the account's own switch are both on. They are off by default.

## Layout

| Path | Contents |
| --- | --- |
| `src/MPSellerTools.PlatformHost` | Platform console API and its static frontend |
| `src/MPSellerTools.TenantHost` | Company workspace API and its static frontend |
| `src/MPSellerTools.Provisioning.Worker` | Creates databases, starts and supervises tenant hosts |
| `src/MPSellerTools.DevHost` | Local launcher for the platform and the worker |
| `src/MPSellerTools.Core`, `.Infrastructure` | Domain model; EF Core, Identity, migrations |
| `frontend/apps/platform`, `frontend/apps/workspace` | React apps (Vite, TypeScript) |
| `frontend/packages/ui` | Shared UI kit adapted from Material Dashboard React |
| `tests/MPSellerTools.Tests` | xUnit unit and integration tests (real LocalDB) |
| `tests/e2e` | Playwright: `ui-tests` use a mocked API, `tests` need running hosts |
| `scripts` | PowerShell setup, build, start, stop, verify and seed scripts |

## Commands

The full application runs natively on Windows (PowerShell 7, .NET 10 SDK,
SQL Server LocalDB, Node.js), from the repository root:

```powershell
.\scripts\Setup-Dev.ps1    # prerequisites, HTTPS dev certificate, platform database, first admin
.\scripts\Build.ps1        # both frontends, the solution, and the published hosts
.\scripts\Start-Dev.ps1    # platform on https://localhost:7100; tenants from 7201
.\scripts\Verify.ps1       # frontend checks and backend tests
.\scripts\Stop-Dev.ps1
```

Checks that need no database and run on any OS, per frontend app
(`frontend/apps/platform`, `frontend/apps/workspace`):

```bash
npm install --no-workspaces
npm run typecheck
npm run lint
npm run build
```

Mocked browser tests, from `tests/e2e`:

```bash
npm install
npx playwright install chromium
npx playwright test --config playwright.ui.config.ts
```

Backend tests (Windows, LocalDB): `dotnet test tests/MPSellerTools.Tests`.

Install frontend dependencies per package with `--no-workspaces`; each package
has its own lockfile.

## GitHub Codespaces

A Codespace is a Linux container and the application does not run in one yet:
it depends on SQL Server LocalDB, Windows process supervision in the worker,
and Windows-specific PowerShell scripts. In a Codespace today, use the
frontend checks and the mocked browser tests above; do not expect the hosts or
the backend integration tests to start.

When a web server is run in a Codespace (a Vite dev server, for example), bind
it to `0.0.0.0` so the forwarded port can reach it. Do not change the hosts'
own bindings, allowed hosts or cookie settings to do so without discussing it:
they are part of the tenant isolation.
