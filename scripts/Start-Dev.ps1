#Requires -Version 7.0
<#
.SYNOPSIS
    Starts the local dev environment (brief §12): checks for port conflicts,
    then launches DevHost, which publishes and starts PlatformHost and the
    Provisioning Worker (the worker in turn starts/reconciles any tenants).
#>

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Test-PortInUse($port) {
    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    return $null -ne $listener
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

Push-Location $repoRoot
try {
    dotnet run --project src/MPSellerTools.DevHost/MPSellerTools.DevHost.csproj -c Debug
} finally {
    Pop-Location
}
