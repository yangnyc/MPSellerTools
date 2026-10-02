#Requires -Version 7.0
# Commits and pushes any pending changes in this repo to GitHub. Run on a schedule via Task Scheduler.

$ErrorActionPreference = "Stop"
$git = "C:\Program Files\Git\cmd\git.exe"
$repo = Split-Path -Parent $PSScriptRoot
$logFile = Join-Path $repo ".local\auto-push.log"

New-Item -ItemType Directory -Force -Path (Split-Path $logFile) | Out-Null

function Log($msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg
    Add-Content -Path $logFile -Value $line
}

try {
    & $git -C $repo add -A

    $statusOutput = & $git -C $repo status --porcelain
    if (-not $statusOutput) {
        Log "No changes to commit."
        exit 0
    }

    & $git -C $repo commit -m ("Auto-push {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
    & $git -C $repo push origin master

    Log "Committed and pushed changes."
}
catch {
    Log "ERROR: $_"
    exit 1
}
