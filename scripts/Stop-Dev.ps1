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

function Stop-VerifiedProcess($processId, $expectedStartTimeUtc, $label) {
    if (-not $processId) { return }
    $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $proc) {
        Write-Host "$label (PID $processId) is not running." -ForegroundColor DarkGray
        return
    }
    $actualStartTimeUtc = $proc.StartTime.ToUniversalTime()
    $expected = [DateTime]::Parse($expectedStartTimeUtc).ToUniversalTime()
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
