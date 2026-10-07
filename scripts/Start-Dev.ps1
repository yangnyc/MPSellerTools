#Requires -Version 7.0
<#
.SYNOPSIS
    Starts the local dev environment (brief §12): checks for port conflicts,
    then launches DevHost, which publishes and starts PlatformHost and the
    Provisioning Worker (the worker in turn starts/reconciles any tenants).

.PARAMETER PublicHost
    The host name or IP address other machines use to reach this one. When
    given, PlatformHost and every tenant instance listen on all interfaces
    instead of localhost only, and company links use this host. The firewall
    must separately allow inbound TCP 7100 and 7201-7299 (see README.md).

.PARAMETER BehindProxy
    Use with -PublicHost when a reverse proxy on this machine (scripts/Caddyfile)
    answers on the public host with its own certificate. The hosts then keep
    listening on localhost only and the proxy forwards to them.
#>
param(
    [string]$PublicHost,
    [switch]$BehindProxy
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

# On ARM64 Windows, LocalDB's native components are x64-only — an ARM64 dotnet
# process fails every LocalDB connection with a SqlUserInstance.dll load error
# (see README.md's "ARM64 Windows" section). Prefer the side-by-side x64 SDK
# so DevHost and the processes it spawns (PlatformHost/TenantHost/Worker) can
# actually reach LocalDB regardless of which dotnet is first on PATH.
if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") {
    $x64Dotnet = "$HOME\.dotnet-x64"
    if (Test-Path "$x64Dotnet\dotnet.exe") {
        Write-Host "ARM64 Windows detected — using the x64 .NET SDK at $x64Dotnet for LocalDB compatibility." -ForegroundColor Yellow
        $env:PATH = "$x64Dotnet;$env:PATH"
        $env:DOTNET_ROOT = $x64Dotnet
    } else {
        Write-Host "WARNING: ARM64 Windows detected but no x64 .NET SDK found at $x64Dotnet. LocalDB connections will fail — see README.md's 'ARM64 Windows' section." -ForegroundColor Red
    }
}

function Test-PortInUse($port) {
    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($BehindProxy) {
        # The proxy holds the same port on the public address; the hosts only need localhost.
        $listener = $listener | Where-Object { $_.LocalAddress -ne $PublicHost }
    }
    return [bool]$listener
}

Write-Host "Checking for port conflicts..." -ForegroundColor Cyan
$requiredPorts = @(7100)
$conflicts = $requiredPorts | Where-Object { Test-PortInUse $_ }
if ($conflicts) {
    Write-Host "The following required ports are already in use:" -ForegroundColor Red
    $conflicts | ForEach-Object {
        $owner = Get-NetTCPConnection -LocalPort $_ -State Listen | Select-Object -First 1 -ExpandProperty OwningProcess
        $procName = (Get-Process -Id $owner -ErrorAction SilentlyContinue).ProcessName
        Write-Host "  Port $_ is in use by PID $owner ($procName)" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Run .\scripts\Stop-Dev.ps1 first, or stop the conflicting process yourself." -ForegroundColor Yellow
    exit 1
}

try {
    $localDbInfo = & sqllocaldb info MSSQLLocalDB 2>$null
    & sqllocaldb start MSSQLLocalDB | Out-Null
} catch {
    Write-Host "Could not start SQL Server LocalDB: $_" -ForegroundColor Yellow
}

Write-Host "Starting DevHost (publishes PlatformHost/TenantHost/Worker, then launches them)..." -ForegroundColor Cyan
Write-Host ""

if ($PublicHost) {
    Write-Host "Public access is ON: reachable from other machines at https://${PublicHost}:7100" -ForegroundColor Yellow
    $env:MPST_PUBLIC_HOST = $PublicHost
    $env:MPST_BEHIND_PROXY = $BehindProxy ? "1" : $null
} else {
    $env:MPST_PUBLIC_HOST = $null
    $env:MPST_BEHIND_PROXY = $null
}

Push-Location $repoRoot
try {
    dotnet run --project src/MPSellerTools.DevHost/MPSellerTools.DevHost.csproj -c Debug
} finally {
    Pop-Location
}
