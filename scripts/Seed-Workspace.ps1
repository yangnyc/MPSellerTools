#Requires -Version 7.0
<#
.SYNOPSIS
    Seeds the rest of one company's workspace around its demo products:
    orders, tasks, stock settings, a return, invitations and the low-stock
    setting, through the same API the workspace pages use.

.DESCRIPTION
    Run scripts/Seed-Marketplaces.ps1 first: this works on the demo products
    (SKUs starting DEMO-) that script creates.

    Adds eight orders in every status, six tasks in every status, a safety
    stock on three products, one received return, two pending invitations,
    and a low-stock threshold in the company settings. Orders and tasks are
    shared out among the company's users, so add any employees before
    running it.

    Safe to run again: orders are only added to a company that has none, and
    whatever else already exists is left as it is. No email is sent; the
    invitations go to the company's dev outbox like any other.

.PARAMETER Url
    The company's workspace address, e.g. https://localhost:7201.

.PARAMETER Email
    A TenantAdmin of that company.

.PARAMETER Password
    That user's password.
#>
param(
    [Parameter(Mandatory)] [string]$Url,
    [Parameter(Mandatory)] [string]$Email,
    [Parameter(Mandatory)] [string]$Password
)

$ErrorActionPreference = "Stop"
$base = $Url.TrimEnd("/")
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

function Get-Token { (Invoke-RestMethod "$base/api/antiforgery/token" -WebSession $session -SkipCertificateCheck).token }

# Sends one API request. A status in $Accept is returned as $null instead of thrown.
function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [int[]]$Accept = @()) {
    $arguments = @{
        Uri = "$base$Path"; Method = $Method; WebSession = $session; SkipCertificateCheck = $true; SkipHttpErrorCheck = $true
    }
    if ($Method -ne "GET") { $arguments.Headers = @{ "X-CSRF-TOKEN" = (Get-Token) } }
    if ($null -ne $Body) { $arguments.ContentType = "application/json"; $arguments.Body = ($Body | ConvertTo-Json -Depth 8) }
    $response = Invoke-WebRequest @arguments
    if ($response.StatusCode -ge 400) {
        if ($Accept -contains $response.StatusCode) { return $null }
        $text = [System.Text.Encoding]::UTF8.GetString($response.Content)
        throw "$Method $Path failed ($($response.StatusCode)): $text"
    }
    $content = if ($response.Content -is [byte[]]) { [System.Text.Encoding]::UTF8.GetString($response.Content) } else { $response.Content }
    if ([string]::IsNullOrWhiteSpace($content)) { return $null }
    return $content | ConvertFrom-Json
}

Invoke-Api POST "/api/auth/login" @{ email = $Email; password = $Password } | Out-Null
Write-Host "Signed in to $base as $Email" -ForegroundColor Cyan

# --- What is seeded -----------------------------------------------------------

# status: the API's numbers. Orders: 0 New, 1 In progress, 2 Completed, 3 Cancelled.
$orders = @(
    @{ status = 2; items = @{ "DEMO-MUG-BLUE" = 2; "DEMO-MUG-CORAL" = 2 } }
    @{ status = 2; items = @{ "DEMO-CANDLE-LAV" = 3 } }
    @{ status = 2; items = @{ "DEMO-TOTE-NAVY" = 1; "DEMO-NOTEBOOK-A5" = 4 } }
    @{ status = 1; items = @{ "DEMO-NOTEBOOK-A5" = 10 } }
    @{ status = 1; items = @{ "DEMO-MUG-BLUE" = 1; "DEMO-CANDLE-LAV" = 1; "DEMO-TOTE-NAVY" = 1 } }
    @{ status = 0; items = @{ "DEMO-CANDLE-AMB" = 1 } }
    @{ status = 0; items = @{ "DEMO-TOTE-NAVY" = 2; "DEMO-MUG-CORAL" = 1 } }
    @{ status = 3; items = @{ "DEMO-NOTEBOOK-A5" = 25 } }
)

# Tasks: 0 Open, 1 In progress, 2 Done, 3 Cancelled. due: days from today.
$tasks = @(
    @{ title = "Photograph the amber candle from two more angles"; status = 0; due = 3
       description = "The listing has a single picture; eBay and Walmart both ask for at least two." }
    @{ title = "Reorder amber candles"; status = 1; due = 2
       description = "Four left on hand. The supplier ships in cases of 24." }
    @{ title = "Replace the demo barcodes with real UPCs"; status = 0; due = 10
       description = "Every DEMO- product carries a made-up barcode. Nothing can be published until these are real." }
    @{ title = "Count the notebook shelf"; status = 2; due = -2
       description = "Quarterly count of the A5 notebooks against what the inventory page shows." }
    @{ title = "Check the Amazon price of the navy tote"; status = 1; due = 1
       description = "It is listed at 24.00 on Amazon against 22.00 everywhere else." }
    @{ title = "Set up Walmart shipping templates"; status = 3; due = 7
       description = "Not needed while live writes are off." }
)

$safetyStock = @{ "DEMO-MUG-BLUE" = 5; "DEMO-CANDLE-LAV" = 10; "DEMO-NOTEBOOK-A5" = 20 }
$return = @{ sku = "DEMO-MUG-CORAL"; quantity = 1; receipt = "DEMO-RET-1001" }
$invitations = @(
    @{ email = "packing@demo.example"; role = "Employee" }
    @{ email = "manager@demo.example"; role = "TenantAdmin" }
)
$lowStockThreshold = 5

# --- Users and products -------------------------------------------------------

$users = @(Invoke-Api GET "/api/users" | Where-Object { -not $_.isBlocked })
$me = $users | Where-Object { $_.email -eq $Email } | Select-Object -First 1
if (-not $me) { throw "$Email is not among the company's active users." }
# Shared out in turn; a company with one user gets everything assigned to them.
$turn = 0
function Get-NextUser { $script:turn++; $users[$script:turn % $users.Count].id }

$productIds = @{}
foreach ($product in (Invoke-Api GET "/api/products")) { $productIds[$product.sku] = $product.id }
$wanted = @($orders | ForEach-Object { $_.items.Keys }) + $safetyStock.Keys + $return.sku | Select-Object -Unique
$missing = @($wanted | Where-Object { -not $productIds[$_] })
if ($missing) { throw "Missing demo products ($($missing -join ', ')). Run Seed-Marketplaces.ps1 first." }

$variants = @{}
foreach ($sku in (@($safetyStock.Keys) + $return.sku | Select-Object -Unique)) {
    $variants[$sku] = ((Invoke-Api GET "/api/catalog/products/$($productIds[$sku])").variants | Where-Object isDefault | Select-Object -First 1).id
}

# --- Settings and stock ---------------------------------------------------------

$settings = Invoke-Api GET "/api/settings"
if ($null -eq $settings.lowStockThreshold) {
    Invoke-Api PUT "/api/settings" @{
        companyName = $settings.companyName; lowStockThreshold = $lowStockThreshold; lowStockAssigneeId = $me.id; rowVersion = $settings.rowVersion
    } | Out-Null
    Write-Host "  + low-stock threshold set to $lowStockThreshold, alerts assigned to $Email"
} else {
    Write-Host "  = low-stock threshold already set"
}

foreach ($sku in $safetyStock.Keys) {
    Invoke-Api PUT "/api/catalog/variants/$($variants[$sku])/inventory" @{ safetyStock = $safetyStock[$sku] } | Out-Null
}
Write-Host "  safety stock set on $($safetyStock.Count) product(s)"

# A receipt is only ever recorded once, whatever its number of runs.
$received = Invoke-Api POST "/api/catalog/returns" @{ variantId = $variants[$return.sku]; quantity = $return.quantity; receiptId = $return.receipt }
Write-Host ($received.recorded ? "  + return $($return.receipt) received ($($return.quantity) x $($return.sku))" : "  = return $($return.receipt) already received")

# --- Orders -------------------------------------------------------------------

if (@(Invoke-Api GET "/api/orders").Count -gt 0) {
    Write-Host "  = the company already has orders; none added"
} else {
    foreach ($order in $orders) {
        $items = @($order.items.Keys | ForEach-Object { @{ productId = $productIds[$_]; quantity = $order.items[$_] } })
        $created = Invoke-Api POST "/api/orders" @{ items = $items; assignedUserId = (Get-NextUser) }
        # Completed is only reached through In progress.
        $steps = switch ($order.status) { 1 { @(1) } 2 { @(1, 2) } 3 { @(3) } default { @() } }
        foreach ($status in $steps) {
            $created = Invoke-Api POST "/api/orders/$($created.id)/status" @{ status = $status; rowVersion = $created.rowVersion }
        }
    }
    Write-Host "  + $($orders.Count) orders added"
}

# --- Tasks --------------------------------------------------------------------

$existingTitles = @(Invoke-Api GET "/api/tasks" | ForEach-Object { $_.title })
$added = 0
foreach ($task in $tasks) {
    if ($existingTitles -contains $task.title) { continue }
    $created = Invoke-Api POST "/api/tasks" @{
        title = $task.title; description = $task.description; assignedUserId = (Get-NextUser)
        dueAtUtc = (Get-Date).ToUniversalTime().Date.AddDays($task.due).ToString("o")
    }
    # Done is only reached through In progress.
    $steps = switch ($task.status) { 1 { @(1) } 2 { @(1, 2) } 3 { @(3) } default { @() } }
    foreach ($status in $steps) {
        $created = Invoke-Api POST "/api/tasks/$($created.id)/status" @{ status = $status; rowVersion = $created.rowVersion }
    }
    $added++
}
Write-Host "  tasks: $added added, $($tasks.Count - $added) already there"

# --- Invitations --------------------------------------------------------------

# Only where the host has invitations switched on (Features:InvitationsEnabled); they are off unless it does.
if ((Invoke-Api GET "/api/auth/me").invitationsEnabled) {
    $known = @($users | ForEach-Object { $_.email }) + @(Invoke-Api GET "/api/invitations" | ForEach-Object { $_.email })
    $added = 0
    foreach ($invitation in $invitations) {
        if ($known -contains $invitation.email) { continue }
        Invoke-Api POST "/api/invitations" @{ email = $invitation.email; role = $invitation.role } | Out-Null
        $added++
    }
    Write-Host "  invitations: $added sent, $($invitations.Count - $added) already there"
} else {
    Write-Host "  invitations: switched off on this host, none sent"
}

Write-Host ""
Write-Host "Done. See $base/orders, $base/tasks, $base/inventory and $base/users." -ForegroundColor Green
