#Requires -Version 7.0
<#
.SYNOPSIS
    Seeds one company's workspace with demo products offered on eBay, Amazon
    and Walmart, through the same API the workspace pages use.

.DESCRIPTION
    Creates six demo products (SKUs starting DEMO-) with brand, category,
    description, a barcode, a part number, pictures, and a shipping weight
    and size; adds eBay, Amazon and Walmart as sales channels where they are
    missing; maps the demo categories on each; and adds every product to
    every marketplace as a draft listing.

    The eBay listings are complete: each carries the item specifics eBay
    asks for in its category, and the eBay account gets the warehouse
    location and the shipping, payment and return policies a listing needs,
    so every one of them passes the check made before publishing.

    Nothing is published and nothing is sent to a marketplace: new channel
    accounts are created with live writes off, and listings stay drafts.
    Safe to run again: whatever already exists is left as it is.

    The barcodes, part numbers, pictures, marketplace category ids and the
    eBay location and policy ids are made-up demo values. Replace them
    before sending anything to a real marketplace.

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
       mpn = "HK-MUG-12-BLU"; weight = 14; weightUnit = "oz"; size = @(5, 4, 4)
       specifics = [ordered]@{ Type = "Mug"; Material = "Stoneware"; Color = "Blue"; Capacity = "12 oz"; Features = "Dishwasher Safe" }
       pictures = @((& $picture "Blue mug" "3B6695"), (& $picture "Blue mug - side" "728FAD")) }
    @{ sku = "DEMO-MUG-CORAL"; name = "Stoneware Mug - Coral 12oz"; price = 14.00; stock = 25; brand = "Hearth & Kiln"; category = "Mugs"; upc = "000010000028"
       description = "A hand-glazed stoneware mug in coral. Holds 12 oz; dishwasher and microwave safe."
       mpn = "HK-MUG-12-COR"; weight = 14; weightUnit = "oz"; size = @(5, 4, 4)
       specifics = [ordered]@{ Type = "Mug"; Material = "Stoneware"; Color = "Orange"; Capacity = "12 oz"; Features = "Dishwasher Safe" }
       pictures = @((& $picture "Coral mug" "F36152"), (& $picture "Coral mug - side" "F58577")) }
    @{ sku = "DEMO-CANDLE-LAV"; name = "Soy Candle - Lavender 8oz"; price = 18.50; stock = 60; brand = "Hearth & Kiln"; category = "Candles"; upc = "000010000035"
       description = "A soy wax candle scented with lavender. Burns for about 45 hours; cotton wick."
       mpn = "HK-CND-8-LAV"; weight = 12; weightUnit = "oz"; size = @(4, 4, 4)
       specifics = [ordered]@{ Type = "Jar Candle"; Scent = "Lavender"; Material = "Soy Wax"; Color = "Purple"; "Burn Time" = "45 hr" }
       pictures = @((& $picture "Lavender candle" "996E9D"), (& $picture "Lavender candle - lit" "E4D9ED")) }
    @{ sku = "DEMO-CANDLE-AMB"; name = "Soy Candle - Amber 8oz"; price = 18.50; stock = 4; brand = "Hearth & Kiln"; category = "Candles"; upc = "000010000042"
       description = "A soy wax candle scented with amber and cedar. Burns for about 45 hours; cotton wick."
       mpn = "HK-CND-8-AMB"; weight = 12; weightUnit = "oz"; size = @(4, 4, 4)
       specifics = [ordered]@{ Type = "Jar Candle"; Scent = "Amber"; Material = "Soy Wax"; Color = "Orange"; "Burn Time" = "45 hr" }
       pictures = @((& $picture "Amber candle" "D98A2B")) }
    @{ sku = "DEMO-TOTE-NAVY"; name = "Canvas Tote Bag - Navy"; price = 22.00; stock = 35; brand = "Field Goods"; category = "Bags"; upc = "000010000059"
       description = "A heavy cotton canvas tote in navy with an inside pocket. 15 x 16 inches; machine washable."
       mpn = "FG-TOTE-NVY"; weight = 9; weightUnit = "oz"; size = @(12, 9, 1)
       specifics = [ordered]@{ Style = "Tote"; "Exterior Material" = "Canvas"; "Exterior Color" = "Blue"; Department = "Unisex Adults"; Size = "Large"; Closure = "Open" }
       pictures = @((& $picture "Navy tote" "192755"), (& $picture "Navy tote - inside" "33426F"), (& $picture "Navy tote - carried" "546B88")) }
    @{ sku = "DEMO-NOTEBOOK-A5"; name = "Dotted Notebook A5 - Sand"; price = 9.75; stock = 120; brand = "Field Goods"; category = "Stationery"; upc = "000010000066"
       description = "An A5 notebook with 160 dotted pages and a sand-coloured cloth cover. Lies flat when open."
       mpn = "FG-NB-A5-SND"; weight = 11; weightUnit = "oz"; size = @(9, 6, 1)
       specifics = [ordered]@{ Type = "Notebook"; Size = "A5"; Ruling = "Dot Grid"; "Number of Pages" = "160"; Color = "Beige"; "Cover Material" = "Cloth" }
       pictures = @((& $picture "Sand notebook" "B89B84"), (& $picture "Sand notebook - open" "E7C5B5")) }
)

# channel: the API's number for it. categories: demo ids, not checked against the marketplace.
# settings: what the marketplace's account needs before a listing can be published.
# specifics: send each product's item specifics with its listing.
$marketplaces = @(
    @{ name = "eBay"; channel = 0; market = "EBAY_US"; sellerId = $null; specifics = $true
       settings = @{ merchantLocationKey = "DEMO-WAREHOUSE"; fulfillmentPolicyId = "6000000001"; paymentPolicyId = "6000000002"; returnPolicyId = "6000000003" }
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
    # type 4: the manufacturer's part number.
    if (-not ($catalog.identifiers | Where-Object { $_.type -eq 4 })) {
        $catalog = Invoke-Api PUT "/api/catalog/products/$id/identifiers" @{ type = 4; value = $product.mpn }
    }
    $variant = $catalog.variants | Where-Object isDefault | Select-Object -First 1
    if (-not $variant) { throw "Product $($product.sku) has no default variant; is the tenant's catalog migrated?" }
    $variants[$product.sku] = $variant.id
    # What it weighs and measures packed, which a marketplace works the postage out from.
    if ($null -eq $variant.weightValue) {
        Invoke-Api PUT "/api/catalog/variants/$($variant.id)" @{
            sku = $variant.sku; name = $variant.name; options = $variant.options; condition = $variant.condition; price = $variant.price
            weightValue = $product.weight; weightUnit = $product.weightUnit
            length = $product.size[0]; width = $product.size[1]; height = $product.size[2]; dimensionUnit = "in"
            rowVersion = $variant.rowVersion
        } | Out-Null
    }
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
    if ($marketplace.settings -and -not $account.settings) {
        $account = Invoke-Api PUT "/api/channels/$($account.id)" @{
            channel = $account.channel; name = $account.name; environment = $account.environment; sellerId = $account.sellerId
            settings = $marketplace.settings; isEnabled = $account.isEnabled; liveWritesEnabled = $account.liveWritesEnabled
            inventorySyncEnabled = $account.inventorySyncEnabled; orderImportEnabled = $account.orderImportEnabled
            priceConflictPolicy = $account.priceConflictPolicy
        }
        Write-Host "  + $($marketplace.name) account given its demo location and policies"
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
    foreach ($listing in @(Invoke-Api GET "/api/channel-listings?accountId=$($account.id)")) { $listed[$listing.variantId] = $listing }

    $added = 0; $completed = 0; $blocked = 0
    foreach ($product in $products) {
        $variantId = $variants[$product.sku]
        $existing = $listed[$variantId]
        $body = $null
        if (-not $existing) {
            $body = @{ channelMarketId = $market.id; variantId = $variantId; fulfillmentMode = 0 }
            $settings = $own["$($marketplace.name)|$($product.sku)"]
            if ($settings) { foreach ($key in $settings.Keys) { $body[$key] = $settings[$key] } }
            if ($marketplace.specifics) { $body.attributes = $product.specifics }
            $added++
        } elseif ($marketplace.specifics -and @($existing.attributes.PSObject.Properties).Count -eq 0) {
            # A listing from an earlier run, from before the item specifics were seeded.
            $body = @{
                channelMarketId = $market.id; variantId = $variantId; sellerSku = $existing.sellerSku; externalCategoryId = $existing.externalCategoryId
                priceOverride = $existing.priceOverride; fulfillmentMode = $existing.fulfillmentMode; quantityCap = $existing.quantityCap
                attributes = $product.specifics
            }
            $completed++
        }

        $listing = $body ? (Invoke-Api PUT "/api/channel-listings" $body) : $existing
        # Records what still stops it being published, so the pages show it without anyone asking.
        $check = Invoke-Api POST "/api/channel-listings/$($listing.id)/validate"
        if (-not $check.valid) { $blocked++ }
    }
    $summary = "$added draft listing(s) added, $($products.Count - $added) already there"
    if ($completed) { $summary += ", $completed given their item specifics" }
    Write-Host "  $($marketplace.name): $summary; $($products.Count - $blocked) of $($products.Count) ready to publish"
}

Write-Host ""
Write-Host "Done. See $base/ebay/products, $base/amazon and $base/walmart." -ForegroundColor Green
Write-Host "Barcodes, part numbers, pictures, category ids and the eBay location and policies are demo values; nothing was published." -ForegroundColor Yellow
