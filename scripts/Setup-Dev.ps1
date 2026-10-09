#Requires -Version 7.0
<#
.SYNOPSIS
    One-time (idempotent, safe to re-run) local environment setup for
    MPSellerTools (brief §12): checks prerequisites, trusts the HTTPS dev
    certificate, applies the platform database migration, creates the
    initial PlatformAdmin, and seeds demo Company A / Company B through the
    same real provisioning pipeline a PlatformAdmin would use from the UI —
    never by inserting rows directly.
#>
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDir = Join-Path $repoRoot ".local"
New-Item -ItemType Directory -Force -Path $localDir | Out-Null

function Write-Step($message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}
function Write-Note($message) {
    Write-Host "    $message" -ForegroundColor DarkGray
}
function Write-Warn2($message) {
    Write-Host "    WARNING: $message" -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 1. Prerequisite check (informational — real versions, never guessed)
# ---------------------------------------------------------------------------
Write-Step "Checking prerequisites"

function Get-VersionSafe($command, $argList) {
    try { (& $command @argList 2>$null | Select-Object -First 1) } catch { $null }
}

$gitVersion = Get-VersionSafe git @("--version")
$nodeVersion = Get-VersionSafe node @("--version")
$npmVersion = Get-VersionSafe npm @("--version")
Write-Note "Git: $gitVersion"
Write-Note "Node: $nodeVersion"
Write-Note "npm: $npmVersion"

$sdks = & dotnet --list-sdks 2>$null
Write-Note "Installed .NET SDKs:"
$sdks | ForEach-Object { Write-Note "  $_" }
if (-not ($sdks -match "^10\.")) {
    Write-Warn2 ".NET 10 SDK not found. This project targets net10.0 (see global.json)."
}

$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($arch -eq "Arm64") {
    Write-Warn2 "Running on ARM64 Windows. SQL Server LocalDB's native components are x64-only:"
    Write-Warn2 "an ARM64 .NET process cannot open a LocalDB connection at all. This environment"
    Write-Warn2 "needed a side-by-side x64 .NET SDK on PATH before dotnet-ef or any host could"
    Write-Warn2 "reach LocalDB — see README.md's 'ARM64 Windows' section if commands below fail"
    Write-Warn2 "with a SqlUserInstance.dll / hostfxr.dll load error."
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsInfo = & $vswhere -latest -format json | ConvertFrom-Json
    if ($vsInfo) {
        Write-Note "Visual Studio: $($vsInfo.displayName) $($vsInfo.installationVersion)"
    } else {
        Write-Warn2 "No Visual Studio installation detected via vswhere."
    }
} else {
    Write-Warn2 "vswhere.exe not found — cannot detect Visual Studio version."
}

try {
    $sqlService = Get-Service MSSQLSERVER
    if ($sqlService.Status -ne "Running") { Start-Service MSSQLSERVER }
    Write-Note "SQL Server (default instance, service MSSQLSERVER) is running."
} catch {
    Write-Warn2 "Could not start SQL Server (service MSSQLSERVER): $_"
}

# ---------------------------------------------------------------------------
# 2. HTTPS dev certificate
# ---------------------------------------------------------------------------
Write-Step "Trusting the ASP.NET Core HTTPS development certificate"
dotnet dev-certs https --trust
if ($LASTEXITCODE -ne 0) { Write-Warn2 "dotnet dev-certs https --trust reported an error." }

# ---------------------------------------------------------------------------
# 3. dotnet-ef tool + platform database migration
# ---------------------------------------------------------------------------
Write-Step "Ensuring dotnet-ef is installed"
$efVersion = (dotnet tool list --global | Select-String "dotnet-ef")
if (-not $efVersion) {
    dotnet tool install --global dotnet-ef
} else {
    Write-Note "dotnet-ef already installed: $efVersion"
}

Write-Step "Applying platform database migration"
Push-Location $repoRoot
try {
    dotnet ef database update `
        --project src/MPSellerTools.Infrastructure/MPSellerTools.Infrastructure.csproj `
        --context PlatformDbContext
    if ($LASTEXITCODE -ne 0) { throw "Platform database migration failed." }
} finally {
    Pop-Location
}

# ---------------------------------------------------------------------------
# 4. Build (unless skipped) — required before we can launch anything below
# ---------------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Step "Building and publishing (this also builds the frontends)"
    & (Join-Path $PSScriptRoot "Build.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Build.ps1 failed." }
} else {
    Write-Note "Skipping build (-SkipBuild). Assuming .local/build/* is already up to date."
}

# ---------------------------------------------------------------------------
# 5. Start PlatformHost briefly to trigger idempotent PlatformAdmin seeding
#    (see MPSellerTools.PlatformHost/Program.cs — never runs if a
#    PlatformAdmin already exists, generates a random password, never a
#    hardcoded default, brief §7/§12).
# ---------------------------------------------------------------------------
$platformHostDll = Join-Path $repoRoot ".local/build/PlatformHost/MPSellerTools.PlatformHost.dll"
$workerDll = Join-Path $repoRoot ".local/build/Worker/MPSellerTools.Provisioning.Worker.dll"
$tenantHostPublishDir = Join-Path $repoRoot ".local/build/TenantHost"
$credentialsPath = Join-Path $repoRoot ".local/platform/dev-admin-credentials.txt"

Write-Step "Starting PlatformHost and the Provisioning Worker to seed demo data"
$platformProcess = Start-Process -FilePath "dotnet" -ArgumentList $platformHostDll -PassThru -WindowStyle Hidden `
    -Environment @{ ASPNETCORE_URLS = "https://localhost:7100"; ASPNETCORE_ENVIRONMENT = "Development" }
$workerProcess = $null

try {
    Write-Note "Waiting for PlatformHost to become ready..."
    $deadline = (Get-Date).AddSeconds(60)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-RestMethod -Uri "https://localhost:7100/api/health" -SkipCertificateCheck -TimeoutSec 3
            if ($health.databaseReachable) { $ready = $true; break }
        } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw "PlatformHost did not become ready within 60 seconds." }
    Write-Note "PlatformHost is ready."

    if (-not (Test-Path $credentialsPath)) {
        throw "Expected $credentialsPath to exist after PlatformHost's first run — PlatformAdmin seeding may have failed."
    }
    $creds = Get-Content $credentialsPath -Raw
    $adminEmail = ([regex]::Match($creds, "Email:\s*(.+)")).Groups[1].Value.Trim()
    $adminPassword = ([regex]::Match($creds, "Password:\s*(.+)")).Groups[1].Value.Trim()
    Write-Note "PlatformAdmin: $adminEmail (see $credentialsPath for the password)"

    $workerProcess = Start-Process -FilePath "dotnet" -ArgumentList $workerDll -PassThru -WindowStyle Hidden `
        -Environment @{
            DOTNET_ENVIRONMENT = "Development"
            Provisioning__TenantHostPublishDirectory = $tenantHostPublishDir
        }
    Write-Note "Provisioning worker started (PID $($workerProcess.Id))."

    # --- Log in as PlatformAdmin ---
    $session = $null
    $tokenResponse = Invoke-RestMethod -Uri "https://localhost:7100/api/antiforgery/token" -SkipCertificateCheck -SessionVariable session
    Invoke-RestMethod -Uri "https://localhost:7100/api/auth/login" -Method Post -SkipCertificateCheck -WebSession $session `
        -Headers @{ "X-CSRF-TOKEN" = $tokenResponse.token } -ContentType "application/json" `
        -Body (@{ email = $adminEmail; password = $adminPassword } | ConvertTo-Json) | Out-Null
    Write-Note "Logged in as PlatformAdmin."

    function New-DemoCompany($name, $slug, $email) {
        # Deliberately no pipeline (|) here at all: an explicit loop with a
        # plain array index sidesteps a real, reproduced-but-unexplained
        # PowerShell 7.5.4 behavior where piping this particular API's array
        # response through Where-Object/Select-Object -First 1 still yielded
        # every element's values concatenated into $existing's properties
        # instead of a single object, even after wrapping the source in @().
        $allTenants = Invoke-RestMethod -Uri "https://localhost:7100/api/tenants" -SkipCertificateCheck -WebSession $session
        $existing = $null
        for ($i = 0; $i -lt $allTenants.Count; $i++) {
            if ($allTenants[$i].slug -eq $slug) { $existing = $allTenants[$i]; break }
        }
        if ($existing) {
            Write-Note "Company '$slug' already exists (status: $($existing.status)) — skipping creation."
            $existingId = [string]$existing.id
            [void][guid]::Parse($existingId)
            return $existingId
        }

        Write-Note "Creating company '$slug' through the real provisioning API..."
        $token = (Invoke-RestMethod -Uri "https://localhost:7100/api/antiforgery/token" -SkipCertificateCheck -WebSession $session).token
        $result = Invoke-RestMethod -Uri "https://localhost:7100/api/tenants" -Method Post -SkipCertificateCheck -WebSession $session `
            -Headers @{ "X-CSRF-TOKEN" = $token } -ContentType "application/json" `
            -Body (@{ name = $name; slug = $slug; initialAdminEmail = $email; initialAdminPassword = $demoAdminPassword } | ConvertTo-Json)
        $newId = [string]$result.tenantId
        [void][guid]::Parse($newId)
        return $newId
    }

    function Wait-TenantActive($tenantId) {
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $deadline) {
            $tenant = Invoke-RestMethod -Uri "https://localhost:7100/api/tenants/$tenantId" -SkipCertificateCheck -WebSession $session
            if ($tenant.status -eq 1) { return $tenant } # Active
            if ($tenant.status -eq 3) { throw "Tenant $tenantId failed to provision: $($tenant.failureReason)" } # Failed
            Start-Sleep -Seconds 2
        }
        throw "Tenant $tenantId did not become Active within 60 seconds."
    }

    # The password every demo company's administrator is given when the company is made.
    $demoAdminPassword = "DemoAdmin-Pass1!"

    function Set-DemoTenant($name, $slug, $adminEmail, $employeeEmail) {
        $tenantId = New-DemoCompany -name $name -slug $slug -email $adminEmail
        $tenant = Wait-TenantActive -tenantId $tenantId
        $tenantUrl = $tenant.url
        Write-Note "$name is Active at $tenantUrl"

        $demoCredsPath = Join-Path $localDir "tenants/$slug/demo-credentials.txt"
        New-Item -ItemType Directory -Force -Path (Split-Path $demoCredsPath) | Out-Null

        $tSession = $null
        Invoke-RestMethod -Uri "$tenantUrl/api/antiforgery/token" -SkipCertificateCheck -SessionVariable tSession | Out-Null

        # The TenantAdmin's account was made with the company, with this password.
        $adminPassword2 = $demoAdminPassword
        $employeePassword = "DemoEmployee-Pass1!"

        $alreadySeeded = Test-Path $demoCredsPath

        $loginToken = (Invoke-RestMethod -Uri "$tenantUrl/api/antiforgery/token" -SkipCertificateCheck -WebSession $tSession).token
        Invoke-RestMethod -Uri "$tenantUrl/api/auth/login" -Method Post -SkipCertificateCheck -WebSession $tSession `
            -Headers @{ "X-CSRF-TOKEN" = $loginToken } -ContentType "application/json" `
            -Body (@{ email = $adminEmail; password = $adminPassword2 } | ConvertTo-Json) | Out-Null

        # --- Employee: added once, with a password ---
        if (-not $alreadySeeded) {
            $createToken = (Invoke-RestMethod -Uri "$tenantUrl/api/antiforgery/token" -SkipCertificateCheck -WebSession $tSession).token
            Invoke-RestMethod -Uri "$tenantUrl/api/users" -Method Post -SkipCertificateCheck -WebSession $tSession `
                -Headers @{ "X-CSRF-TOKEN" = $createToken } -ContentType "application/json" `
                -Body (@{ email = $employeeEmail; role = "Employee"; displayName = "$name Employee"; password = $employeePassword } | ConvertTo-Json) | Out-Null
            Write-Note "Employee added to $slug."

            # --- Small, distinguishable seed dataset ---
            $productToken = (Invoke-RestMethod -Uri "$tenantUrl/api/antiforgery/token" -SkipCertificateCheck -WebSession $tSession).token
            $product = Invoke-RestMethod -Uri "$tenantUrl/api/products" -Method Post -SkipCertificateCheck -WebSession $tSession `
                -Headers @{ "X-CSRF-TOKEN" = $productToken } -ContentType "application/json" `
                -Body (@{ sku = "$($slug.ToUpper())-001"; name = "$name Sample Product"; price = 12.5; stockQuantity = 25 } | ConvertTo-Json)

            $orderToken = (Invoke-RestMethod -Uri "$tenantUrl/api/antiforgery/token" -SkipCertificateCheck -WebSession $tSession).token
            Invoke-RestMethod -Uri "$tenantUrl/api/orders" -Method Post -SkipCertificateCheck -WebSession $tSession `
                -Headers @{ "X-CSRF-TOKEN" = $orderToken } -ContentType "application/json" `
                -Body (@{ items = @(@{ productId = $product.id; quantity = 2 }); assignedUserId = $null } | ConvertTo-Json -Depth 5) | Out-Null

            $taskToken = (Invoke-RestMethod -Uri "$tenantUrl/api/antiforgery/token" -SkipCertificateCheck -WebSession $tSession).token
            $employees = Invoke-RestMethod -Uri "$tenantUrl/api/users" -SkipCertificateCheck -WebSession $tSession |
                Where-Object { $_.email -eq $employeeEmail }
            Invoke-RestMethod -Uri "$tenantUrl/api/tasks" -Method Post -SkipCertificateCheck -WebSession $tSession `
                -Headers @{ "X-CSRF-TOKEN" = $taskToken } -ContentType "application/json" `
                -Body (@{ title = "Welcome to $name"; description = "Sample task seeded by Setup-Dev.ps1"; assignedUserId = $employees[0].id; dueAtUtc = $null } | ConvertTo-Json) | Out-Null

            @"
Company: $name ($slug)
URL: $tenantUrl
TenantAdmin: $adminEmail / $adminPassword2
Employee: $employeeEmail / $employeePassword
Seeded: $(Get-Date -AsUTC -Format o)
"@ | Set-Content -Path $demoCredsPath
            Write-Note "Seed data created for $slug. Credentials: $demoCredsPath"
        } else {
            Write-Note "$slug already has seed data — skipping."
        }
    }

    Write-Step "Seeding demo Company A"
    Set-DemoTenant -name "Company A" -slug "company-a" -adminEmail "admin@company-a.local" -employeeEmail "employee@company-a.local"

    Write-Step "Seeding demo Company B"
    Set-DemoTenant -name "Company B" -slug "company-b" -adminEmail "admin@company-b.local" -employeeEmail "employee@company-b.local"

} finally {
    Write-Step "Stopping the temporary PlatformHost/Worker used for setup"
    if ($platformProcess -and -not $platformProcess.HasExited) { Stop-Process -Id $platformProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($workerProcess -and -not $workerProcess.HasExited) { Stop-Process -Id $workerProcess.Id -Force -ErrorAction SilentlyContinue }
    Write-Note "Run .\scripts\Start-Dev.ps1 to start the full dev environment."
}

Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
Write-Host "Platform admin credentials: $credentialsPath"
Write-Host "Demo company credentials:   .local/tenants/<slug>/demo-credentials.txt"
