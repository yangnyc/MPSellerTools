#Requires -Version 7.0
<#
.SYNOPSIS
    Stops only this project's managed processes (brief §12): DevHost,
    PlatformHost, and the Provisioning Worker (read from a verified
    PID + start-time state file, never a bare PID and never a blanket
    `dotnet` process kill), then asks the worker binary to gracefully stop
    every tenant process it can independently verify is genuinely running.
    Safe to run repeatedly, including when nothing is running.
#>

$ErrorActionPreference = "Continue"
$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot ".local/devhost-state.json"

# On ARM64 Windows, LocalDB's native components are x64-only — an ARM64 dotnet
# process fails every LocalDB connection with a SqlUserInstance.dll load error
# (see README.md's "ARM64 Windows" section). Prefer the side-by-side x64 SDK
# so the `--stop-all` worker invocation below can actually query the platform
# DB for tenant processes instead of crashing before it stops any of them.
if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") {
    $x64Dotnet = "$HOME\.dotnet-x64"
    if (Test-Path "$x64Dotnet\dotnet.exe") {
        Write-Host "ARM64 Windows detected — using the x64 .NET SDK at $x64Dotnet for LocalDB compatibility." -ForegroundColor Yellow
        $env:PATH = "$x64Dotnet;$env:PATH"
        $env:DOTNET_ROOT = $x64Dotnet
    } else {
        Write-Host "WARNING: ARM64 Windows detected but no x64 .NET SDK found at $x64Dotnet. Tenant process cleanup will fail — see README.md's 'ARM64 Windows' section." -ForegroundColor Red
    }
}

function Stop-VerifiedProcess($processId, $expectedStartTimeUtc, $label) {
    if (-not $processId) { return }
    $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $proc) {
        Write-Host "$label (PID $processId) is not running." -ForegroundColor DarkGray
        return
    }
    $actualStartTimeUtc = $proc.StartTime.ToUniversalTime()
    # $expectedStartTimeUtc arrives already parsed into a Kind=Utc [DateTime]
    # (ConvertFrom-Json converts ISO 8601 "Z" strings automatically) — a
    # further [DateTime]::Parse() here would stringify and reparse it as
    # Kind=Unspecified, so ToUniversalTime() below would add the local UTC
    # offset on top of an already-UTC value.
    $expected = ([DateTime]$expectedStartTimeUtc).ToUniversalTime()
    if ([Math]::Abs(($actualStartTimeUtc - $expected).TotalSeconds) -gt 5) {
        Write-Host "$label (PID $processId) start time does not match — the OS has reused this PID for a different process. Not touching it." -ForegroundColor Yellow
        return
    }
    Write-Host "Stopping $label (PID $processId)..." -ForegroundColor Cyan
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

if (Test-Path $statePath) {
    $state = Get-Content $statePath -Raw | ConvertFrom-Json
    if ($state.DevHostPid) {
        # DevHost has no separate recorded start time (it's the process
        # running this orchestration itself); a plain existence check is
        # enough since we don't hand its PID off anywhere else.
        Stop-Process -Id $state.DevHostPid -Force -ErrorAction SilentlyContinue
    }
    Stop-VerifiedProcess -processId $state.PlatformHostPid -expectedStartTimeUtc $state.PlatformHostStartTimeUtc -label "PlatformHost"
    Stop-VerifiedProcess -processId $state.WorkerPid -expectedStartTimeUtc $state.WorkerStartTimeUtc -label "Provisioning Worker"
} else {
    Write-Host "No .local/devhost-state.json found — nothing recorded as started by Start-Dev.ps1." -ForegroundColor DarkGray
}

$workerDll = Join-Path $repoRoot ".local/build/Worker/MPSellerTools.Provisioning.Worker.dll"
if (Test-Path $workerDll) {
    Write-Host "Stopping any verified-running tenant processes..." -ForegroundColor Cyan
    dotnet $workerDll --stop-all
} else {
    Write-Host "No published worker found at $workerDll — skipping tenant process cleanup." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Stop-Dev complete." -ForegroundColor Green
