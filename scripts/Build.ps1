#Requires -Version 7.0
<#
.SYNOPSIS
    Builds MPSellerTools: both frontends, the .NET solution, and publishes
    the three components the provisioning worker/DevHost launch as child
    processes (brief §12).

.PARAMETER Configuration
    .NET build/publish configuration. Defaults to Release, since the
    published output is what actually gets launched at runtime — Debug
    symbols aren't needed for that and Release is what a real deployment
    would use.
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Write-Step($message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

# Installed per package rather than once at the workspace root: a root
# install symlinks each workspace into frontend/node_modules, which fails
# with EISDIR on filesystems that can't hold symlinks (e.g. a VM shared
# folder). --no-workspaces stops npm from walking up to frontend/package.json
# and doing that root install anyway. Each package has its own
# package-lock.json, and the Vite configs already resolve packages/ui by
# path, so nothing needs the hoisted layout.
$frontendPackages = @("apps/platform", "apps/workspace", "packages/ui")
foreach ($package in $frontendPackages) {
    Write-Step "Installing frontend dependencies ($package)"
    Push-Location (Join-Path $repoRoot "frontend/$package")
    try {
        npm install --no-workspaces
        if ($LASTEXITCODE -ne 0) { throw "npm install failed for $package" }
    } finally {
        Pop-Location
    }
}

# Each app writes its build output directly into the corresponding host's
# wwwroot (see vite.config.ts in each app). `dotnet publish` below copies
# whatever is currently in wwwroot — building the frontends first, every
# time, is what keeps that from ever being a stale directory left over from
# an earlier run (brief §12).
Write-Step "Building platform frontend (-> src/MPSellerTools.PlatformHost/wwwroot)"
Push-Location (Join-Path $repoRoot "frontend/apps/platform")
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "platform frontend build failed" }
} finally {
    Pop-Location
}

Write-Step "Building workspace frontend (-> src/MPSellerTools.TenantHost/wwwroot)"
Push-Location (Join-Path $repoRoot "frontend/apps/workspace")
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "workspace frontend build failed" }
} finally {
    Pop-Location
}

Write-Step "Restoring and building the .NET solution ($Configuration)"
Push-Location $repoRoot
try {
    dotnet restore MPSellerTools.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

    dotnet build MPSellerTools.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
} finally {
    Pop-Location
}

# Published — not just built — because a `dotnet build` output directory
# does not contain wwwroot (only `dotnet publish` copies it), and the
# provisioning worker/DevHost launch these as standalone "prebuilt, approved
# executables" (brief §10), not via `dotnet run` from their project folders.
$buildRoot = Join-Path $repoRoot ".local/build"
$components = @(
    @{ Name = "MPSellerTools.PlatformHost"; Output = "PlatformHost" },
    @{ Name = "MPSellerTools.TenantHost"; Output = "TenantHost" },
    @{ Name = "MPSellerTools.Provisioning.Worker"; Output = "Worker" }
)

foreach ($component in $components) {
    Write-Step "Publishing $($component.Name) ($Configuration)"
    $projectPath = Join-Path $repoRoot "src/$($component.Name)/$($component.Name).csproj"
    $outputPath = Join-Path $buildRoot $component.Output
    dotnet publish $projectPath -c $Configuration -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($component.Name)" }
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "Published components: $buildRoot"
