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

.PARAMETER ForceInstall
    Runs the frontend `npm install` even when the manifests are unchanged
    since the last install.
#>
param(
    [string]$Configuration = "Release",
    [switch]$ForceInstall
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Write-Step($message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

# Installed once at the workspace root (frontend/package.json lists the
# workspaces): shared dependencies are hoisted into frontend/node_modules
# instead of being unpacked once per package, which cuts the install — and
# every later walk of the tree — to about a third of the files. npm links
# each workspace into frontend/node_modules, so the checkout has to sit on a
# filesystem that can hold links (NTFS junctions are fine; a VM shared folder
# is not).
#
# The install is skipped when the root package-lock.json and every
# package.json are unchanged since the last successful install: it rewrites
# tens of thousands of small files, which takes minutes on slow storage. The
# stamp lives inside node_modules so that deleting node_modules forces a
# reinstall; -ForceInstall does the same without deleting anything.
$frontendDir = Join-Path $repoRoot "frontend"
$frontendPackages = @("apps/platform", "apps/workspace", "packages/ui")

function Get-ManifestHash {
    $manifests = @("package.json", "package-lock.json") +
        ($frontendPackages | ForEach-Object { "$_/package.json" })
    $hashes = foreach ($name in $manifests) {
        $path = Join-Path $frontendDir $name
        if (Test-Path $path) { (Get-FileHash $path -Algorithm SHA256).Hash }
    }
    return ($hashes -join ":")
}

$stampPath = Join-Path $frontendDir "node_modules/.install-stamp"
if (-not $ForceInstall -and (Test-Path $stampPath) -and
    (Get-Content $stampPath -Raw).Trim() -eq (Get-ManifestHash)) {
    Write-Step "Frontend dependencies up to date"
} else {
    Write-Step "Installing frontend dependencies"
    Push-Location $frontendDir
    try {
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    } finally {
        Pop-Location
    }

    # Hashed after the install, since npm may rewrite package-lock.json.
    Set-Content -Path $stampPath -Value (Get-ManifestHash)
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

    dotnet build MPSellerTools.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
} finally {
    Pop-Location
}

# Published — not just built — because a `dotnet build` output directory
# does not contain wwwroot (only `dotnet publish` copies it), and the
# provisioning worker/DevHost launch these as standalone "prebuilt, approved
# executables" (brief §10), not via `dotnet run` from their project folders.
# --no-build reuses the solution build just above instead of re-checking every
# project three more times; it publishes exactly what that build produced.
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
    dotnet publish $projectPath -c $Configuration -o $outputPath --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($component.Name)" }
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "Published components: $buildRoot"
