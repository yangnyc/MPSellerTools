# Architecture

MPSellerTools follows a **Multiple Application, Multiple Database**
multi-tenant architecture (see
[Frontegg's overview](https://frontegg.com/guides/multi-tenant-architecture#How_Does_Multi-Tenancy_Work_3_Types_of_Multi-tenant_Architecture)):
each company runs its own application process against its own database,
under a common platform that provisions and manages them.

## Component and database diagram

```mermaid
flowchart TB
    subgraph Platform["Platform (fixed, port 7100)"]
        PA[PlatformAdmin browser]
        PH[MPSellerTools.PlatformHost<br/>ASP.NET Core API + React console]
        PDB[(MPSellerTools_Platform<br/>Admins · Tenant registry · Jobs · Audit)]
        PA -->|https| PH
        PH -->|EF Core| PDB
    end

    subgraph Worker["Provisioning"]
        W[MPSellerTools.Provisioning.Worker]
    end

    PH -->|enqueue job| PDB
    W -->|lease + claim jobs| PDB
    W -->|create DB, migrate,<br/>seed, launch process| TA
    W -->|create DB, migrate,<br/>seed, launch process| TB

    subgraph CompanyA["Company A instance (dynamic port, e.g. 7201)"]
        UA[TenantAdmin / Employee browser]
        TA[MPSellerTools.TenantHost<br/>same compiled binary, instance config A]
        TADB[(MPSellerTools_Tenant_company_a<br/>Users · Products · Orders · Tasks · Audit)]
        UA -->|https| TA
        TA -->|EF Core| TADB
    end

    subgraph CompanyB["Company B instance (dynamic port, e.g. 7202)"]
        UB[TenantAdmin / Employee browser]
        TB[MPSellerTools.TenantHost<br/>same compiled binary, instance config B]
        TBDB[(MPSellerTools_Tenant_company_b<br/>Users · Products · Orders · Tasks · Audit)]
        UB -->|https| TB
        TB -->|EF Core| TBDB
    end

    style PDB fill:#e8f0fe
    style TADB fill:#fce8e6
    style TBDB fill:#e6f4ea
```

Key properties this diagram is meant to make visible:

- **PlatformHost and every TenantHost are separate OS processes**, each with
  its own connection string, cookie name, antiforgery cookie name, and Data
  Protection key directory — never a single process that switches database
  based on a request header, tenantId, or JWT claim.
- **TenantHost's own code is identical for every company** — Company A and
  Company B run the exact same compiled `MPSellerTools.TenantHost.dll`,
  differing only in the immutable per-instance configuration file each
  process is launched with (`TenantId`, `ApplicationInstanceId`, `Slug`,
  connection string, port).
- **The platform database never contains business data.** It holds platform
  administrators, the company registry (`Tenants`), the provisioning job
  queue, and platform-level audit entries only.
- **Each tenant database is fully self-contained**: its own ASP.NET Core
  Identity users/roles (so the same email address can exist independently in
  two different companies), its own business tables, its own audit log.

## Local dev-mode process tree

```mermaid
flowchart TD
    DH[DevHost<br/>Visual Studio F5 target] -->|Process.Start| PH2[PlatformHost]
    DH -->|Process.Start, after PlatformHost is healthy| W2[Provisioning.Worker]
    W2 -->|Process.Start, one per Active tenant| TH1[TenantHost — Company A]
    W2 -->|Process.Start, one per Active tenant| TH2[TenantHost — Company B]
    W2 -.->|reconciles on startup:<br/>verifies each Active tenant's<br/>process is really running| TH1
    W2 -.->|restarts it if not| TH1
```

DevHost, PlatformHost, and the Worker share DevHost's console, so a single
Ctrl+C in that console propagates via normal Windows console signal
delivery to all of them — and, because the worker launches TenantHost
without `CREATE_NEW_PROCESS_GROUP`, to every tenant process too.

## Provisioning flow (creating a company)

```mermaid
sequenceDiagram
    participant Admin as PlatformAdmin (browser)
    participant PH as PlatformHost API
    participant PDB as Platform DB
    participant Worker as Provisioning.Worker
    participant TDB as New Tenant DB
    participant TH as New TenantHost process

    Admin->>PH: POST /api/tenants {name, slug, adminEmail}
    PH->>PH: normalize + validate slug,<br/>allocate next free port
    PH->>PDB: insert Tenant (Provisioning) + Job (Pending), in one transaction
    PH-->>Admin: 202 Accepted {tenantId, jobId}
    Worker->>PDB: claim Pending job (lease + optimistic concurrency)
    Worker->>TDB: create database, apply migrations
    Worker->>TDB: seed roles, CompanySettings, TenantAdmin invitation
    Worker->>Worker: write per-instance config file<br/>(TenantId, connection string, port — outside source control)
    Worker->>TH: launch published TenantHost.dll as a child process
    Worker->>TH: GET /api/health (poll until TenantId/instance/DB/migrations all verified)
    Worker->>PDB: mark Tenant Active, Job Succeeded
    Admin->>PH: GET /api/tenants/:id (polls until Active)
    PH-->>Admin: Active, with the new login URL
```

## Suspend / Resume

Suspend verifies the tenant's recorded process (PID **and** start time — a
bare PID is not sufficient, since the OS reuses them) and terminates it,
which genuinely removes access (the port stops listening). Resume relaunches
the same published binary with the same instance config, re-verifies
readiness via `/api/health`, and marks the tenant Active again with the same
`ApplicationInstanceId`. Supervision state (`Tenant.ProcessId` /
`ProcessStartTimeUtc`) lives in the platform database, not in the worker's
memory, so a worker restart (or this whole machine rebooting) can always
tell which tenants it needs to bring back — see `ReconcileActiveTenantsAsync`
in `MPSellerTools.Provisioning.Worker`.
