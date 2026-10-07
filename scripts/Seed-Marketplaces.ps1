#Requires -Version 7.0
<#
.SYNOPSIS
    Seeds one company's workspace with demo products offered on eBay, Amazon
    and Walmart, through the same API the workspace pages use.

.DESCRIPTION
    Creates six demo products (SKUs starting DEMO-) with brand, category,
    description, a barcode and pictures; adds eBay, Amazon and Walmart as
    sales channels where they are missing; maps the demo categories on each;
    and adds every product to every marketplace as a draft listing.

    Nothing is published and nothing is sent to a marketplace: new channel
    accounts are created with live writes off, and listings stay drafts.
    Safe to run again: whatever already exists is left as it is.

    The barcodes, pictures and marketplace category ids are made-up demo
    values. Replace them before sending anything to a real marketplace.

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

$picture = { param($text, $fill) "https://placehold.co/1000x1000/$fill/FFFFFF/png?text=$([uri]::EscapeDataString($text))" }

$products = @(
    @{ sku = "DEMO-MUG-BLUE"; name = "Stoneware Mug - Deep Blue 12oz"; price = 14.00; stock = 40; brand = "Hearth & Kiln"; category = "Mugs"; upc = "000010000011"
       description = "A hand-glazed stoneware mug in deep blue. Holds 12 oz; dishwasher and microwave safe."
       pictures = @((& $picture "Blue mug" "3B6695"), (& $picture "Blue mug - side" "728FAD")) }
    @{ sku = "DEMO-MUG-CORAL"; name = "Stoneware Mug - Coral 12oz"; price = 14.00; stock = 25; brand = "Hearth & Kiln"; category = "Mugs"; upc = "000010000028"
       description = "A hand-glazed stoneware mug in coral. Holds 12 oz; dishwasher and microwave safe."
       pictures = @((& $picture "Coral mug" "F36152"), (& $picture "Coral mug - side" "F58577")) }
    @{ sku = "DEMO-CANDLE-LAV"; name = "Soy Candle - Lavender 8oz"; price = 18.50; stock = 60; brand = "Hearth & Kiln"; category = "Candles"; upc = "000010000035"
       description = "A soy wax candle scented with lavender. Burns for about 45 hours; cotton wick."
       pictures = @((& $picture "Lavender candle" "996E9D"), (& $picture "Lavender candle - lit" "E4D9ED")) }
    @{ sku = "DEMO-CANDLE-AMB"; name = "Soy Candle - Amber 8oz"; price = 18.50; stock = 4; brand = "Hearth & Kiln"; category = "Candles"; upc = "000010000042"
       description = "A soy wax candle scented with amber and cedar. Burns for about 45 hours; cotton wick."
       pictures = @((& $picture "Amber candle" "D98A2B")) }
    @{ sku = "DEMO-TOTE-NAVY"; name = "Canvas Tote Bag - Navy"; price = 22.00; stock = 35; brand = "Field Goods"; category = "Bags"; upc = "000010000059"
       description = "A heavy cotton canvas tote in navy with an inside pocket. 15 x 16 inches; machine washable."
       pictures = @((& $picture "Navy tote" "192755"), (& $picture "Navy tote - inside" "33426F"), (& $picture "Navy tote - carried" "546B88")) }
    @{ sku = "DEMO-NOTEBOOK-A5"; name = "Dotted Notebook A5 - Sand"; price = 9.75; stock = 120; brand = "Field Goods"; category = "Stationery"; upc = "000010000066"
       description = "An A5 notebook with 160 dotted pages and a sand-coloured cloth cover. Lies flat when open."
       pictures = @((& $picture "Sand notebook" "B89B84"), (& $picture "Sand notebook - open" "E7C5B5")) }
)

# channel: the API's number for it. categories: demo ids, not checked against the marketplace.
$marketplaces = @(
    @{ name = "eBay"; channel = 0; market = "EBAY_US"; sellerId = $null
       categories = @{ Mugs = "20696"; Candles = "46782"; Bags = "169291"; Stationery = "102950" } }
    @{ name = "Amazon"; channel = 1; market = "ATVPDKIKX0DER"; sellerId = "DEMOSELLER"
       categories = @{ Mugs = "DRINKING_CUP"; Candles = "CANDLE"; Bags = "TOTE_BAG"; Stationery = "NOTEBOOK" } }
    @{ name = "Walmart"; channel = 2; market = "WALMART_US"; sellerId = $null
       categories = @{ Mugs = "Drinkware"; Candles = "Candles"; Bags = "Tote Bags"; Stationery = "Notebooks" } }
)

# A few listings get settings of their own, so the pages have something to show for them.
$own = @{
    "Amazon|DEMO-MUG-BLUE"    = @{ priceOverride = 15.50; quantityCap = 10 }
    "Amazon|DEMO-TOTE-NAVY"   = @{ priceOverride = 24.00 }
    "eBay|DEMO-CANDLE-LAV"    = @{ quantityCap = 20 }
    "Walmart|DEMO-NOTEBOOK-A5" = @{ priceOverride = 9.25 }
}

# --- Products -----------------------------------------------------------------

$existing = @{}
foreach ($product in (Invoke-Api GET "/api/products")) { $existing[$product.sku] = $product.id }

$variants = @{}
foreach ($product in $products) {
    $id = $existing[$product.sku]
    if (-not $id) {
        $id = (Invoke-Api POST "/api/products" @{ sku = $product.sku; name = $product.name; price = $product.price; stockQuantity = $product.stock }).id
        Write-Host "  + product $($product.sku)"
    } else {
        Write-Host "  = product $($product.sku) already exists"
    }

    $catalog = Invoke-Api GET "/api/catalog/products/$id"
    if (-not $catalog.description) {
        $catalog = Invoke-Api PUT "/api/catalog/products/$id/content" @{ brand = $product.brand; description = $product.description; category = $product.category }
    }
    if (-not ($catalog.identifiers | Where-Object { $_.type -eq 1 })) {
        $catalog = Invoke-Api PUT "/api/catalog/products/$id/identifiers" @{ type = 1; value = $product.upc }
    }
    if (@($catalog.media).Count -eq 0) {
        for ($i = 0; $i -lt $product.pictures.Count; $i++) {
            $catalog = Invoke-Api POST "/api/catalog/products/$id/media" @{ url = $product.pictures[$i]; purpose = ($i -eq 0 ? 0 : 1); position = $i }
        }
    }
    $variants[$product.sku] = ($catalog.variants | Where-Object isDefault | Select-Object -First 1).id
    if (-not $variants[$product.sku]) { throw "Product $($product.sku) has no default variant; is the tenant's catalog migrated?" }
}

# --- Marketplaces, categories and listings -------------------------------------

$accounts = @(Invoke-Api GET "/api/channels")
$mappings = @(Invoke-Api GET "/api/channels/category-mappings")

foreach ($marketplace in $marketplaces) {
    $account = $accounts | Where-Object { $_.channel -eq $marketplace.channel } | Select-Object -First 1
    if (-not $account) {
        # Live writes off: everything here is prepared and checked, and nothing reaches the marketplace.
        $account = Invoke-Api POST "/api/channels" @{
            channel = $marketplace.channel; name = $marketplace.name; environment = 0; sellerId = $marketplace.sellerId
            isEnabled = $true; liveWritesEnabled = $false; inventorySyncEnabled = $false; orderImportEnabled = $false; priceConflictPolicy = 2
        }
        Write-Host "  + $($marketplace.name) added as a sales channel (sandbox, live writes off)"
    }
    $market = @($account.markets) | Select-Object -First 1
    if (-not $market) {
        $market = Invoke-Api POST "/api/channels/$($account.id)/markets" @{ marketplaceCode = $marketplace.market }
    }

    foreach ($category in $marketplace.categories.Keys) {
        if (-not ($mappings | Where-Object { $_.channelMarketId -eq $market.id -and $_.internalCategory -eq $category })) {
            Invoke-Api PUT "/api/channels/category-mappings" @{ channelMarketId = $market.id; internalCategory = $category; externalCategoryId = $marketplace.categories[$category] } | Out-Null
        }
    }

    $listed = @{}
    foreach ($listing in @(Invoke-Api GET "/api/channel-listings?accountId=$($account.id)")) { $listed[$listing.variantId] = $listing.id }

    $added = 0
    foreach ($product in $products) {
        $variantId = $variants[$product.sku]
        if ($listed[$variantId]) { continue }

        $body = @{ channelMarketId = $market.id; variantId = $variantId; fulfillmentMode = 0 }
        $settings = $own["$($marketplace.name)|$($product.sku)"]
        if ($settings) { foreach ($key in $settings.Keys) { $body[$key] = $settings[$key] } }
        $listing = Invoke-Api PUT "/api/channel-listings" $body
        # Records what still stops it being published, so the pages show it without anyone asking.
        Invoke-Api POST "/api/channel-listings/$($listing.id)/validate" | Out-Null
        $added++
    }
    Write-Host "  $($marketplace.name): $added draft listing(s) added, $($products.Count - $added) already there"
}

Write-Host ""
Write-Host "Done. See $base/ebay/products, $base/amazon and $base/walmart." -ForegroundColor Green
Write-Host "Barcodes, pictures and marketplace category ids are demo values; nothing was published." -ForegroundColor Yellow
