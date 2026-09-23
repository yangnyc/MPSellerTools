#Requires -Version 7.0
<#
.SYNOPSIS
    Runs every automated check available for MPSellerTools (brief §13):
    restore/build, frontend lint/typecheck/build, backend tests. Prints a
    clear pass/fail summary and explicitly lists anything that could not be
    run automatically (e.g. the interactive Visual Studio F5 workflow).
#>

$ErrorActionPreference = "Continue"
$repoRoot = Split-Path -Parent $PSScriptRoot
$results = [ordered]@{}

# On ARM64 Windows, LocalDB's native components are x64-only — an ARM64 dotnet
# process fails every LocalDB connection with a SqlUserInstance.dll load error
# (see README.md's "ARM64 Windows" section). Prefer the side-by-side x64 SDK
# so the backend test step (which hits real LocalDB) doesn't produce false
# failures just because of which dotnet happened to be first on PATH.
if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") {
    $x64Dotnet = "$HOME\.dotnet-x64"
    if (Test-Path "$x64Dotnet\dotnet.exe") {
        Write-Host "ARM64 Windows detected — using the x64 .NET SDK at $x64Dotnet for LocalDB compatibility." -ForegroundColor Yellow
        $env:PATH = "$x64Dotnet;$env:PATH"
        $env:DOTNET_ROOT = $x64Dotnet
    } else {
        Write-Host "WARNING: ARM64 Windows detected but no x64 .NET SDK found at $x64Dotnet. The backend test step will fail to reach LocalDB — see README.md's 'ARM64 Windows' section." -ForegroundColor Red
    }
}

function Invoke-Check($name, [scriptblock]$action) {
    Write-Host ""
    Write-Host "==> $name" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $action
    $results[$name] = ($LASTEXITCODE -eq 0)
}

Push-Location $repoRoot
try {
    Invoke-Check ".NET solution restore" { dotnet restore MPSellerTools.sln }
    Invoke-Check ".NET solution build" { dotnet build MPSellerTools.sln -c Release }

    Push-Location "frontend/apps/platform"
    Invoke-Check "Platform frontend: typecheck" { npm run typecheck }
    Invoke-Check "Platform frontend: lint" { npm run lint }
    Invoke-Check "Platform frontend: build" { npm run build }
    Pop-Location

    Push-Location "frontend/apps/workspace"
    Invoke-Check "Workspace frontend: typecheck" { npm run typecheck }
    Invoke-Check "Workspace frontend: lint" { npm run lint }
    Invoke-Check "Workspace frontend: build" { npm run build }
    Pop-Location

    Invoke-Check "Backend tests (xUnit, real LocalDB)" { dotnet test tests/MPSellerTools.Tests/MPSellerTools.Tests.csproj -c Release }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "=================== Verify.ps1 summary ===================" -ForegroundColor Cyan
foreach ($key in $results.Keys) {
    $status = if ($results[$key]) { "PASS" } else { "FAIL" }
    $color = if ($results[$key]) { "Green" } else { "Red" }
    Write-Host ("{0,-45} {1}" -f $key, $status) -ForegroundColor $color
}

Write-Host ""
Write-Host "NOT covered by this script (brief §13 — require manual/GUI verification):" -ForegroundColor Yellow
Write-Host "  - Opening the solution in Visual Studio, selecting DevHost as the startup"
Write-Host "    project, and pressing F5 (brief §13 check #1). Automated equivalent:"
Write-Host "    .\scripts\Start-Dev.ps1 exercises the same DevHost startup path headlessly."
Write-Host "  - Playwright browser smoke tests (tests/e2e) — run separately with:"
Write-Host "      cd tests/e2e; npm install; npx playwright install; npm test"
Write-Host "    (requires the dev stack to be running — see Start-Dev.ps1)."
Write-Host "  - Manual cross-isolation checks that need two ports/sessions open at once"
Write-Host "    side by side (submitting one instance's cookie to another) — covered by"
Write-Host "    the xUnit integration tests above, but worth eyeballing once live."

$failed = $results.Values | Where-Object { -not $_ }
if ($failed) {
    exit 1
}
exit 0
