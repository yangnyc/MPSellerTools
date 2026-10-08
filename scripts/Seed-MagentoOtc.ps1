#Requires -Version 7.0
<#
.SYNOPSIS
    Seeds one company's workspace with a large demo catalog of over-the-counter
    pharmacy products and puts every one of them on Magento as a draft.

.DESCRIPTION
    Makes up a catalog of OTC products (pain relief, cold and flu, allergy,
    digestive health, vitamins, first aid, skin care and so on) from generic
    ingredients, strengths, forms and pack sizes under made-up store brands,
    and creates the first -Count of them (SKUs starting OTC-) with a brand,
    category, description and barcode. It adds Magento as a sales channel
    where it is missing, maps the categories to demo Magento category
    numbers, and adds every product to Magento as a draft listing.

    Nothing is published and nothing is sent to a store: the Magento account
    is created with live writes off and a placeholder store address, and the
    listings stay drafts. Safe to run again: the same -Count always makes the
    same products, and whatever already exists is left as it is. A larger
    -Count adds to what an earlier run made.

    The brands, barcodes, prices and Magento category numbers are made-up demo
    values, and the descriptions are not product labelling.

.PARAMETER Url
    The company's workspace address, e.g. https://localhost:7201.

.PARAMETER Email
    A TenantAdmin of that company.

.PARAMETER Password
    That user's password.

.PARAMETER Count
    How many products to seed. Defaults to 2500.
#>
param(
    [Parameter(Mandatory)] [string]$Url,
    [Parameter(Mandatory)] [string]$Email,
    [Parameter(Mandatory)] [string]$Password,
    [ValidateRange(1, 5000)] [int]$Count = 2500
)

$ErrorActionPreference = "Stop"
$base = $Url.TrimEnd("/")

# One client for everything, so the many requests below share connections and the session cookie.
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(100)

# Sends one API request and returns the parsed answer. The token is the antiforgery one, needed for anything but a GET.
function Invoke-Api([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$base$Path")
    if ($Token) { $request.Headers.Add("X-CSRF-TOKEN", $Token) }
    if ($null -ne $Body) {
        $request.Content = [System.Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 8 -Compress), [System.Text.Encoding]::UTF8, "application/json")
    }
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) { throw "$Method $Path failed ($([int]$response.StatusCode)): $text" }
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json
}

$token = (Invoke-Api GET "/api/antiforgery/token").token
Invoke-Api POST "/api/auth/login" @{ email = $Email; password = $Password } $token | Out-Null
# The token belongs to whoever is signed in, so it is asked for again after signing in.
$token = (Invoke-Api GET "/api/antiforgery/token").token
Write-Host "Signed in to $base as $Email" -ForegroundColor Cyan

# --- The catalog ----------------------------------------------------------------

# Made-up store brands; factor scales the price.
$brands = @(
    @{ name = "CareWell"; factor = 1.00 }
    @{ name = "Northfield Health"; factor = 1.10 }
    @{ name = "Harbor Pharmacy"; factor = 0.90 }
    @{ name = "Meridian Wellness"; factor = 1.20 }
    @{ name = "ValuMed"; factor = 0.80 }
    @{ name = "Brightside Care"; factor = 1.05 }
)

# category: the company's own; magento: the demo category number it maps to.
$categories = [ordered]@{
    "Pain Relief" = "21"; "Cold & Flu" = "22"; "Allergy" = "23"; "Digestive Health" = "24"; "Vitamins & Supplements" = "25"
    "First Aid" = "26"; "Skin Care" = "27"; "Eye & Ear Care" = "28"; "Oral Care" = "29"; "Sleep & Stress" = "30"
    "Children's Health" = "31"; "Foot Care" = "32"; "Smoking Cessation" = "33"
}

# One line per product family: what it is, what it is for, the forms it comes in, and its pack sizes with the
# price of each for the plainest brand. Every brand sells every form in every size.
function Line([string]$Category, [string]$Name, [string]$Use, [string[]]$Forms, [object[]]$Sizes) {
    @{ category = $Category; name = $Name; use = $Use; forms = $Forms; sizes = $Sizes }
}
$tablets = @(@("24 Count", 4.49), @("50 Count", 6.99), @("100 Count", 9.99), @("200 Count", 15.99), @("500 Count", 24.99))
$daily = @(@("14 Count", 8.99), @("30 Count", 13.99), @("45 Count", 17.99), @("90 Count", 26.99))
$vitamins = @(@("60 Count", 7.49), @("100 Count", 9.99), @("180 Count", 14.99), @("250 Count", 18.99), @("365 Count", 23.99))
$liquids = @(@("4 fl oz", 5.99), @("8 fl oz", 8.99), @("12 fl oz", 11.49), @("16 fl oz", 13.99))
$tubes = @(@("0.5 oz", 3.99), @("1 oz", 5.49), @("2 oz", 8.49), @("4 oz", 12.99))
$sprays = @(@("0.5 fl oz", 6.99), @("1 fl oz", 9.99), @("Twin Pack", 16.99))
$drops = @(@("0.33 fl oz", 5.99), @("0.5 fl oz", 7.99), @("1 fl oz", 11.99), @("Twin Pack", 13.99))
$patches = @(@("5 Count", 6.99), @("15 Count", 14.99), @("30 Count", 24.99))
$nicotine = @(@("20 Count", 9.99), @("100 Count", 34.99), @("160 Count", 49.99))

$lines = @(
    Line "Pain Relief" "Ibuprofen 200 mg" "Pain reliever and fever reducer (NSAID)" @("Tablets", "Caplets", "Softgels") $tablets
    Line "Pain Relief" "Acetaminophen 325 mg" "Pain reliever and fever reducer" @("Tablets", "Caplets") $tablets
    Line "Pain Relief" "Acetaminophen 500 mg" "Extra strength pain reliever and fever reducer" @("Tablets", "Caplets", "Gelcaps") $tablets
    Line "Pain Relief" "Acetaminophen 650 mg Extended Release" "Arthritis pain reliever, lasting up to 8 hours" @("Caplets") $tablets
    Line "Pain Relief" "Naproxen Sodium 220 mg" "All day pain reliever and fever reducer (NSAID)" @("Tablets", "Caplets", "Liquid Gels") $tablets
    Line "Pain Relief" "Aspirin 81 mg" "Low dose aspirin regimen" @("Enteric Coated Tablets", "Chewable Tablets") $tablets
    Line "Pain Relief" "Aspirin 325 mg" "Pain reliever and fever reducer" @("Tablets", "Enteric Coated Tablets") $tablets
    Line "Pain Relief" "Acetaminophen, Aspirin and Caffeine" "Migraine and headache relief" @("Caplets", "Geltabs") $tablets
    Line "Pain Relief" "Lidocaine 4%" "Topical pain relief for back, neck and shoulders" @("Patches") $patches
    Line "Pain Relief" "Lidocaine 4%" "Numbing pain relief" @("Cream", "Roll-On") $tubes
    Line "Pain Relief" "Menthol 5%" "Cooling relief for sore muscles and joints" @("Gel", "Roll-On", "Spray") $tubes
    Line "Pain Relief" "Diclofenac Sodium 1%" "Arthritis pain relief gel (NSAID)" @("Topical Gel") $tubes
    Line "Pain Relief" "Capsaicin 0.1%" "Warming relief for muscle and joint pain" @("Cream", "Patches") $tubes

    Line "Cold & Flu" "Daytime Cold & Flu" "Relieves congestion, cough, sore throat and fever without drowsiness" @("Softgels", "Caplets", "Liquid") $tablets
    Line "Cold & Flu" "Nighttime Cold & Flu" "Relieves cough, sneezing, sore throat and fever so you can rest" @("Softgels", "Caplets", "Liquid") $tablets
    Line "Cold & Flu" "Phenylephrine HCl 10 mg" "Nasal decongestant" @("Tablets") $tablets
    Line "Cold & Flu" "Guaifenesin 400 mg" "Expectorant that loosens chest congestion" @("Tablets", "Caplets") $tablets
    Line "Cold & Flu" "Guaifenesin 600 mg Extended Release" "12 hour chest congestion relief" @("Tablets") $daily
    Line "Cold & Flu" "Guaifenesin and Dextromethorphan" "Cough suppressant and expectorant" @("Syrup", "Extended Release Tablets") $liquids
    Line "Cold & Flu" "Dextromethorphan HBr" "12 hour cough suppressant" @("Syrup", "Softgels") $liquids
    Line "Cold & Flu" "Oxymetazoline HCl 0.05%" "12 hour nasal decongestant" @("Nasal Spray", "Nasal Mist") $sprays
    Line "Cold & Flu" "Saline 0.65%" "Moisturizes dry nasal passages" @("Nasal Spray", "Nasal Mist", "Nasal Gel") $sprays
    Line "Cold & Flu" "Menthol Cough Drops" "Soothes sore throats and quiets coughs" @("Honey Lemon", "Cherry", "Menthol Eucalyptus", "Sugar Free") @(@("30 Count", 2.49), @("80 Count", 4.99), @("160 Count", 8.49))
    Line "Cold & Flu" "Zinc Gluconate 13 mg" "Taken at the first sign of a cold" @("Lozenges", "Quick Dissolve Tablets") $daily
    Line "Cold & Flu" "Benzocaine and Menthol" "Sore throat relief" @("Lozenges", "Throat Spray") $daily
    Line "Cold & Flu" "Vapor Chest Rub" "Cough suppressant and topical analgesic" @("Ointment", "Children's Ointment") $tubes

    Line "Allergy" "Cetirizine HCl 10 mg" "24 hour relief of sneezing, runny nose and itchy eyes" @("Tablets", "Liquid Gels", "Dissolve Tablets") $daily
    Line "Allergy" "Loratadine 10 mg" "Non-drowsy 24 hour allergy relief" @("Tablets", "Dissolve Tablets", "Liquid Gels") $daily
    Line "Allergy" "Fexofenadine HCl 180 mg" "Non-drowsy 24 hour allergy relief" @("Tablets", "Gelcaps") $daily
    Line "Allergy" "Fexofenadine HCl 60 mg" "Non-drowsy 12 hour allergy relief" @("Tablets") $daily
    Line "Allergy" "Levocetirizine 5 mg" "24 hour allergy relief, taken in the evening" @("Tablets") $daily
    Line "Allergy" "Diphenhydramine HCl 25 mg" "Antihistamine for allergy symptoms" @("Tablets", "Capsules", "Liquid Gels") $tablets
    Line "Allergy" "Chlorpheniramine Maleate 4 mg" "4 hour allergy relief" @("Tablets") $tablets
    Line "Allergy" "Fluticasone Propionate 50 mcg" "24 hour nasal allergy relief" @("Nasal Spray") @(@("60 Sprays", 11.99), @("120 Sprays", 18.99), @("144 Sprays", 21.99), @("Twin Pack", 34.99))
    Line "Allergy" "Triamcinolone Acetonide 55 mcg" "24 hour nasal allergy relief" @("Nasal Spray") @(@("60 Sprays", 10.99), @("120 Sprays", 16.99), @("Twin Pack", 29.99))
    Line "Allergy" "Ketotifen Fumarate 0.025%" "Itchy eye relief lasting up to 12 hours" @("Eye Drops") $drops
    Line "Allergy" "Loratadine and Pseudoephedrine Alternative" "Allergy and congestion relief with phenylephrine" @("Tablets") $daily

    Line "Digestive Health" "Famotidine 10 mg" "Acid reducer that relieves and prevents heartburn" @("Tablets") $tablets
    Line "Digestive Health" "Famotidine 20 mg" "Maximum strength acid reducer" @("Tablets") $tablets
    Line "Digestive Health" "Omeprazole 20 mg Delayed Release" "Treats frequent heartburn, taken once a day for 14 days" @("Tablets", "Capsules") @(@("14 Count", 9.99), @("28 Count", 16.99), @("42 Count", 22.99))
    Line "Digestive Health" "Esomeprazole Magnesium 20 mg" "Treats frequent heartburn" @("Capsules", "Mini Capsules") @(@("14 Count", 10.99), @("28 Count", 18.99), @("42 Count", 24.99))
    Line "Digestive Health" "Lansoprazole 15 mg Delayed Release" "Treats frequent heartburn" @("Capsules") @(@("14 Count", 9.99), @("28 Count", 16.99), @("42 Count", 22.99))
    Line "Digestive Health" "Calcium Carbonate 750 mg" "Extra strength antacid" @("Chewable Tablets - Assorted Fruit", "Chewable Tablets - Peppermint", "Chewable Tablets - Berry") @(@("48 Count", 3.99), @("96 Count", 5.99), @("200 Count", 9.49))
    Line "Digestive Health" "Calcium Carbonate 1000 mg" "Ultra strength antacid" @("Chewable Tablets - Assorted Fruit", "Soft Chews") @(@("72 Count", 5.49), @("160 Count", 9.99))
    Line "Digestive Health" "Bismuth Subsalicylate" "Relieves upset stomach, nausea and diarrhea" @("Liquid", "Chewable Tablets", "Caplets") $liquids
    Line "Digestive Health" "Loperamide HCl 2 mg" "Controls the symptoms of diarrhea" @("Caplets", "Softgels") @(@("12 Count", 4.99), @("24 Count", 7.99), @("48 Count", 12.99), @("96 Count", 19.99))
    Line "Digestive Health" "Simethicone 125 mg" "Relieves gas pressure and bloating" @("Softgels", "Chewable Tablets") $tablets
    Line "Digestive Health" "Simethicone 180 mg" "Extra strength gas relief" @("Softgels") $tablets
    Line "Digestive Health" "Polyethylene Glycol 3350" "Osmotic laxative powder, relieves occasional constipation" @("Powder") @(@("7 Doses", 6.99), @("14 Doses", 10.99), @("30 Doses", 18.99), @("45 Doses", 24.99))
    Line "Digestive Health" "Docusate Sodium 100 mg" "Stool softener" @("Softgels") $tablets
    Line "Digestive Health" "Bisacodyl 5 mg" "Gentle overnight laxative" @("Enteric Coated Tablets") $tablets
    Line "Digestive Health" "Senna 8.6 mg" "Natural vegetable laxative" @("Tablets") $tablets
    Line "Digestive Health" "Psyllium Husk Fiber" "Daily fiber supplement" @("Powder - Orange", "Powder - Unflavored", "Capsules") @(@("48 Doses", 9.99), @("72 Doses", 13.99), @("114 Doses", 19.99), @("180 Doses", 26.99))
    Line "Digestive Health" "Lactase Enzyme 9000 FCC" "Helps digest dairy" @("Caplets", "Chewable Tablets") $daily
    Line "Digestive Health" "Meclizine HCl 25 mg" "Prevents and treats motion sickness" @("Tablets", "Chewable Tablets") $tablets

    Line "Vitamins & Supplements" "Vitamin C 500 mg" "Supports immune health" @("Tablets", "Chewable Tablets", "Gummies") $vitamins
    Line "Vitamins & Supplements" "Vitamin C 1000 mg" "High potency immune support" @("Tablets", "Caplets") $vitamins
    Line "Vitamins & Supplements" "Vitamin D3 1000 IU" "Supports bone and immune health" @("Softgels", "Tablets", "Gummies") $vitamins
    Line "Vitamins & Supplements" "Vitamin D3 2000 IU" "Supports bone and immune health" @("Softgels", "Tablets") $vitamins
    Line "Vitamins & Supplements" "Vitamin D3 5000 IU" "High potency vitamin D" @("Softgels") $vitamins
    Line "Vitamins & Supplements" "Vitamin B12 1000 mcg" "Supports energy metabolism" @("Tablets", "Quick Dissolve Tablets", "Gummies") $vitamins
    Line "Vitamins & Supplements" "Vitamin B Complex" "Eight B vitamins for energy metabolism" @("Tablets", "Capsules") $vitamins
    Line "Vitamins & Supplements" "Vitamin E 400 IU" "Antioxidant support" @("Softgels") $vitamins
    Line "Vitamins & Supplements" "Adult Multivitamin" "Complete daily multivitamin with minerals" @("Tablets", "Gummies") $vitamins
    Line "Vitamins & Supplements" "Adult 50+ Multivitamin" "Daily multivitamin made for adults over fifty" @("Tablets", "Gummies") $vitamins
    Line "Vitamins & Supplements" "Women's Multivitamin" "Daily multivitamin with iron and folic acid" @("Tablets", "Gummies") $vitamins
    Line "Vitamins & Supplements" "Men's Multivitamin" "Daily multivitamin for men" @("Tablets", "Gummies") $vitamins
    Line "Vitamins & Supplements" "Prenatal Multivitamin with DHA" "Daily support before and during pregnancy" @("Softgels", "Tablets") $vitamins
    Line "Vitamins & Supplements" "Calcium 600 mg with Vitamin D3" "Supports bone health" @("Tablets", "Soft Chews") $vitamins
    Line "Vitamins & Supplements" "Magnesium Oxide 250 mg" "Supports muscle and nerve function" @("Tablets") $vitamins
    Line "Vitamins & Supplements" "Magnesium Glycinate 200 mg" "Gentle, well absorbed magnesium" @("Capsules") $vitamins
    Line "Vitamins & Supplements" "Zinc 50 mg" "Supports immune health" @("Tablets", "Caplets") $vitamins
    Line "Vitamins & Supplements" "Iron 65 mg" "Ferrous sulfate iron supplement" @("Tablets") $vitamins
    Line "Vitamins & Supplements" "Fish Oil 1000 mg" "Omega-3 for heart health" @("Softgels", "Mini Softgels") $vitamins
    Line "Vitamins & Supplements" "Biotin 5000 mcg" "Supports hair, skin and nails" @("Softgels", "Gummies") $vitamins
    Line "Vitamins & Supplements" "Folic Acid 400 mcg" "Supports healthy cell growth" @("Tablets") $vitamins
    Line "Vitamins & Supplements" "Probiotic 10 Billion CFU" "Daily digestive and immune support" @("Capsules", "Gummies") $daily
    Line "Vitamins & Supplements" "Elderberry with Vitamin C and Zinc" "Immune support" @("Gummies", "Syrup") $daily
    Line "Vitamins & Supplements" "Glucosamine Chondroitin" "Supports joint comfort" @("Tablets", "Caplets") $vitamins
    Line "Vitamins & Supplements" "Turmeric Curcumin 500 mg" "Antioxidant support" @("Capsules") $vitamins
    Line "Vitamins & Supplements" "CoQ10 100 mg" "Supports heart health" @("Softgels") $daily
    Line "Vitamins & Supplements" "Potassium Gluconate 99 mg" "Mineral supplement" @("Tablets", "Caplets") $vitamins

    Line "First Aid" "Hydrocortisone 1%" "Maximum strength anti-itch" @("Cream", "Ointment", "Cream with Aloe") $tubes
    Line "First Aid" "Triple Antibiotic" "First aid antibiotic for minor cuts, scrapes and burns" @("Ointment", "Ointment with Pain Relief") $tubes
    Line "First Aid" "Bacitracin Zinc" "First aid antibiotic" @("Ointment") $tubes
    Line "First Aid" "Hydrogen Peroxide 3%" "First aid antiseptic" @("Solution", "Spray") $liquids
    Line "First Aid" "Isopropyl Alcohol 70%" "First aid antiseptic" @("Solution", "Spray") $liquids
    Line "First Aid" "Isopropyl Alcohol 91%" "First aid antiseptic" @("Solution") $liquids
    Line "First Aid" "Adhesive Bandages" "Sterile bandages for minor cuts and scrapes" @("Flexible Fabric", "Sheer", "Waterproof", "Assorted Sizes") @(@("30 Count", 2.99), @("60 Count", 4.99), @("100 Count", 6.99))
    Line "First Aid" "Sterile Gauze Pads" "Absorbent dressing for wounds" @("2 x 2 in", "3 x 3 in", "4 x 4 in") @(@("10 Count", 2.49), @("25 Count", 4.49), @("50 Count", 7.49))
    Line "First Aid" "Antiseptic Wipes" "Benzalkonium chloride wound cleansing wipes" @("Individually Wrapped") @(@("20 Count", 2.99), @("50 Count", 5.49), @("100 Count", 8.99))
    Line "First Aid" "Burn Relief with Lidocaine" "Cools and soothes minor burns" @("Gel", "Spray") $tubes
    Line "First Aid" "Elastic Bandage" "Compression wrap for sprains and strains" @("2 in", "3 in", "4 in", "6 in") @(@("Single", 3.49), @("Twin Pack", 5.99))
    Line "First Aid" "Liquid Bandage" "Waterproof seal for small cuts" @("Brush On", "Spray") @(@("0.3 fl oz", 6.49), @("0.5 fl oz", 8.99))
    Line "First Aid" "Instant Cold Pack" "Single use cold therapy" @("Standard", "Large") @(@("Single", 1.99), @("Twin Pack", 3.49), @("6 Count", 8.99))

    Line "Skin Care" "Clotrimazole 1%" "Antifungal cream for athlete's foot, jock itch and ringworm" @("Cream") $tubes
    Line "Skin Care" "Terbinafine HCl 1%" "Antifungal cream that cures most athlete's foot" @("Cream") $tubes
    Line "Skin Care" "Benzoyl Peroxide 10%" "Maximum strength acne treatment" @("Face Wash", "Spot Gel") $tubes
    Line "Skin Care" "Salicylic Acid 2%" "Acne treatment that clears pores" @("Cleanser", "Pads", "Gel") $tubes
    Line "Skin Care" "Adapalene 0.1%" "Once daily acne treatment gel" @("Gel") @(@("0.5 oz", 12.99), @("1.6 oz", 24.99))
    Line "Skin Care" "Petroleum Jelly" "Skin protectant for dry, chapped skin" @("Original", "Baby") @(@("3.75 oz", 2.99), @("7.5 oz", 4.49), @("13 oz", 6.49))
    Line "Skin Care" "Zinc Oxide 40%" "Maximum strength diaper rash paste" @("Paste", "Cream") $tubes
    Line "Skin Care" "Calamine" "Dries the oozing of poison ivy, oak and sumac" @("Lotion", "Lotion with Antihistamine") $liquids
    Line "Skin Care" "Aloe Vera" "Soothes sunburn and dry skin" @("Gel", "Gel with Lidocaine") $liquids
    Line "Skin Care" "Broad Spectrum SPF 30 Sunscreen" "Water resistant sun protection" @("Lotion", "Spray", "Stick") $liquids
    Line "Skin Care" "Broad Spectrum SPF 50 Sunscreen" "Water resistant sun protection" @("Lotion", "Spray", "Stick") $liquids
    Line "Skin Care" "Urea 20%" "Intensive moisturizer for rough, cracked skin" @("Cream") $tubes
    Line "Skin Care" "Colloidal Oatmeal 1%" "Daily moisturizer for dry, itchy skin" @("Lotion", "Cream") $liquids
    Line "Skin Care" "Pramoxine HCl 1%" "Anti-itch relief without steroids" @("Lotion", "Cream") $tubes

    Line "Eye & Ear Care" "Lubricant Eye Drops" "Relieves dry, irritated eyes" @("Original", "Preservative Free Vials", "Gel Drops") $drops
    Line "Eye & Ear Care" "Redness Relief Eye Drops" "Tetrahydrozoline HCl 0.05%, removes redness" @("Original", "Advanced") $drops
    Line "Eye & Ear Care" "Multi-Purpose Contact Lens Solution" "Cleans, rinses, disinfects and stores soft lenses" @("Solution") @(@("4 fl oz", 4.99), @("12 fl oz", 8.99), @("Twin Pack", 15.99))
    Line "Eye & Ear Care" "Sterile Eye Wash" "Flushes loose foreign material from the eye" @("Solution") $liquids
    Line "Eye & Ear Care" "Carbamide Peroxide 6.5%" "Ear wax removal drops" @("Drops", "Drops with Bulb Syringe") $drops
    Line "Eye & Ear Care" "Swimmer's Ear Drying Drops" "Dries water in the ears" @("Drops") $drops
    Line "Eye & Ear Care" "Nighttime Lubricant Eye Ointment" "Overnight dry eye relief" @("Ointment") @(@("0.125 oz", 9.99), @("Twin Pack", 16.99))

    Line "Oral Care" "Benzocaine 20%" "Maximum strength oral pain reliever" @("Gel", "Liquid") @(@("0.25 oz", 5.49), @("0.42 oz", 7.49))
    Line "Oral Care" "Anticavity Fluoride Mouth Rinse" "Strengthens enamel and helps prevent cavities" @("Mint", "Alcohol Free Mint", "Bubble Gum") @(@("8.4 fl oz", 3.99), @("16.9 fl oz", 5.49), @("33.8 fl oz", 7.99))
    Line "Oral Care" "Antiseptic Mouthwash" "Kills germs that cause bad breath, plaque and gingivitis" @("Blue Mint", "Spring Mint", "Citrus", "Alcohol Free") @(@("8.4 fl oz", 3.49), @("16.9 fl oz", 4.99), @("33.8 fl oz", 6.99), @("50.7 fl oz", 8.99))
    Line "Oral Care" "Sensitive Teeth Toothpaste" "Potassium nitrate 5%, with fluoride" @("Fresh Mint", "Whitening", "Extra Fresh") @(@("3.4 oz", 4.99), @("4 oz", 5.49), @("Twin Pack", 9.99))
    Line "Oral Care" "Denture Cleanser" "Effervescent tablets that clean and freshen dentures" @("Tablets") @(@("40 Count", 3.99), @("90 Count", 6.99), @("120 Count", 8.49))
    Line "Oral Care" "Dry Mouth Lozenges" "Moisturizing relief for dry mouth" @("Mint", "Citrus") @(@("40 Count", 7.99), @("99 Count", 16.99))
    Line "Oral Care" "Canker Sore Patch" "Covers and protects mouth sores" @("Patches") @(@("6 Count", 6.99), @("12 Count", 11.99))

    Line "Sleep & Stress" "Melatonin 3 mg" "Drug-free sleep support" @("Tablets", "Quick Dissolve Tablets", "Gummies") $vitamins
    Line "Sleep & Stress" "Melatonin 5 mg" "Drug-free sleep support" @("Tablets", "Quick Dissolve Tablets", "Gummies") $vitamins
    Line "Sleep & Stress" "Melatonin 10 mg" "Maximum strength sleep support" @("Tablets", "Quick Dissolve Tablets", "Gummies") $vitamins
    Line "Sleep & Stress" "Doxylamine Succinate 25 mg" "Nighttime sleep aid" @("Tablets") @(@("32 Count", 6.99), @("48 Count", 8.99), @("96 Count", 14.99))
    Line "Sleep & Stress" "Diphenhydramine HCl 50 mg" "Maximum strength nighttime sleep aid" @("Softgels", "Caplets") @(@("32 Count", 6.49), @("64 Count", 9.99), @("96 Count", 13.99))
    Line "Sleep & Stress" "Valerian Root 500 mg" "Herbal supplement for relaxation" @("Capsules") $vitamins
    Line "Sleep & Stress" "L-Theanine 200 mg" "Supports a calm, relaxed state" @("Capsules") $daily
    Line "Sleep & Stress" "Ashwagandha 600 mg" "Herbal supplement for stress support" @("Capsules", "Gummies") $daily

    Line "Children's Health" "Children's Acetaminophen 160 mg per 5 mL" "Pain reliever and fever reducer for ages 2 to 11" @("Suspension - Cherry", "Suspension - Grape", "Suspension - Dye Free", "Chewable Tablets") @(@("4 fl oz", 5.49), @("8 fl oz", 8.99), @("Twin Pack", 9.99))
    Line "Children's Health" "Children's Ibuprofen 100 mg per 5 mL" "Pain reliever and fever reducer for ages 2 to 11" @("Suspension - Berry", "Suspension - Grape", "Suspension - Dye Free", "Chewable Tablets") @(@("4 fl oz", 5.99), @("8 fl oz", 9.49), @("Twin Pack", 10.99))
    Line "Children's Health" "Infants' Acetaminophen 160 mg per 5 mL" "Pain reliever and fever reducer with dosing syringe" @("Suspension - Cherry", "Suspension - Dye Free") @(@("1 fl oz", 5.99), @("2 fl oz", 8.99))
    Line "Children's Health" "Children's Cetirizine 5 mg per 5 mL" "24 hour allergy relief for ages 2 and up" @("Solution - Grape", "Solution - Bubble Gum", "Chewable Tablets") @(@("4 fl oz", 7.99), @("8 fl oz", 12.99))
    Line "Children's Health" "Children's Loratadine 5 mg per 5 mL" "Non-drowsy 24 hour allergy relief" @("Solution - Grape", "Chewable Tablets") @(@("4 fl oz", 7.49), @("8 fl oz", 11.99))
    Line "Children's Health" "Children's Cough and Chest Congestion" "Dextromethorphan and guaifenesin for ages 4 and up" @("Liquid - Cherry", "Liquid - Grape") $liquids
    Line "Children's Health" "Children's Multivitamin" "Complete daily multivitamin for kids" @("Gummies", "Chewable Tablets") $vitamins
    Line "Children's Health" "Children's Vitamin D3 600 IU" "Supports growing bones" @("Gummies", "Drops") $daily
    Line "Children's Health" "Electrolyte Solution" "Replaces fluids and electrolytes" @("Unflavored", "Grape", "Strawberry", "Fruit Punch") @(@("33.8 fl oz", 4.99), @("4 Pack", 17.99))
    Line "Children's Health" "Infants' Simethicone 20 mg" "Relieves gas discomfort" @("Drops", "Dye Free Drops") @(@("0.5 fl oz", 5.99), @("1 fl oz", 8.99))
    Line "Children's Health" "Saline Nasal Drops" "Gentle relief for stuffy little noses" @("Drops", "Mist") @(@("0.5 fl oz", 3.99), @("1 fl oz", 5.49), @("Twin Pack", 8.99))

    Line "Foot Care" "Tolnaftate 1%" "Antifungal for athlete's foot" @("Spray Powder", "Cream", "Liquid Spray") $tubes
    Line "Foot Care" "Miconazole Nitrate 2%" "Antifungal for athlete's foot" @("Powder", "Spray Powder", "Cream") $tubes
    Line "Foot Care" "Salicylic Acid 17%" "Removes common and plantar warts" @("Liquid", "Gel") @(@("0.31 fl oz", 7.49), @("0.5 fl oz", 9.99))
    Line "Foot Care" "Salicylic Acid 40%" "Medicated corn and callus removers" @("Corn Pads", "Callus Pads", "Wart Pads") @(@("6 Count", 4.99), @("9 Count", 6.49), @("18 Count", 10.99))
    Line "Foot Care" "Moleskin Padding" "Prevents blisters and relieves shoe friction" @("Roll", "Strips") @(@("Single", 4.49), @("3 Pack", 10.99))
    Line "Foot Care" "Epsom Salt" "Magnesium sulfate soak for tired, aching feet" @("Unscented", "Lavender", "Eucalyptus") @(@("1 lb", 2.99), @("3 lb", 5.99), @("6 lb", 9.99))
    Line "Foot Care" "Gel Insoles" "Cushioning for all day comfort" @("Men's", "Women's") @(@("1 Pair", 9.99), @("2 Pairs", 17.99))

    Line "Smoking Cessation" "Nicotine Polacrilex Gum 2 mg" "Stop smoking aid" @("Original", "Mint", "Cinnamon", "Fruit") $nicotine
    Line "Smoking Cessation" "Nicotine Polacrilex Gum 4 mg" "Stop smoking aid" @("Original", "Mint", "Cinnamon", "Fruit") $nicotine
    Line "Smoking Cessation" "Nicotine Polacrilex Lozenge 2 mg" "Stop smoking aid" @("Mint", "Cherry") $nicotine
    Line "Smoking Cessation" "Nicotine Polacrilex Lozenge 4 mg" "Stop smoking aid" @("Mint", "Cherry") $nicotine
    Line "Smoking Cessation" "Nicotine Transdermal Patch 21 mg" "Stop smoking aid, step 1" @("Clear Patches") @(@("7 Count", 19.99), @("14 Count", 34.99), @("28 Count", 59.99))
    Line "Smoking Cessation" "Nicotine Transdermal Patch 14 mg" "Stop smoking aid, step 2" @("Clear Patches") @(@("7 Count", 19.99), @("14 Count", 34.99))
    Line "Smoking Cessation" "Nicotine Transdermal Patch 7 mg" "Stop smoking aid, step 3" @("Clear Patches") @(@("7 Count", 19.99), @("14 Count", 34.99))
)

# Every brand, form and size of every line, numbered in a fixed order so a product keeps its SKU from run to run.
$catalog = [System.Collections.Generic.List[object]]::new()
foreach ($line in $lines) {
    foreach ($brand in $brands) {
        foreach ($form in $line.forms) {
            foreach ($size in $line.sizes) {
                $number = $catalog.Count + 1
                $price = [Math]::Max(0.99, [Math]::Floor([double]$size[1] * $brand.factor) + 0.99)
                $catalog.Add(@{
                    sku = "OTC-{0:D5}" -f $number
                    name = "$($brand.name) $($line.name) $form, $($size[0])"
                    brand = $brand.name
                    category = $line.category
                    description = "$($line.use). $($brand.name) $($line.name), $($form.ToLowerInvariant()), $($size[0].ToLowerInvariant()). Use only as directed on the label."
                    price = [decimal]$price
                    # Made up, with no valid check digit.
                    upc = "0004{0:D8}" -f $number
                })
            }
        }
    }
}
if ($Count -gt $catalog.Count) { throw "The catalog only has $($catalog.Count) products; ask for fewer." }

# The same shuffle every time, so the first -Count are always the same products, spread over every category.
$random = [System.Random]::new(20261008)
$order = 0..($catalog.Count - 1) | Sort-Object { $random.Next() }
$wanted = @($order | Select-Object -First $Count | ForEach-Object { $catalog[$_] })
# A few are out of stock and a few run low, so the stock pages have something to show.
foreach ($product in $wanted) {
    $roll = $random.Next(100)
    $product.stock = $roll -lt 4 ? 0 : $roll -lt 12 ? $random.Next(1, 6) : $random.Next(12, 400)
}
Write-Host "Catalog: $($catalog.Count) possible products; seeding $($wanted.Count) of them."

# --- Products -----------------------------------------------------------------

$have = @{}
foreach ($product in (Invoke-Api GET "/api/products")) { $have[$product.sku] = $product.id }

$new = @($wanted | Where-Object { -not $have.ContainsKey($_.sku) })
for ($i = 0; $i -lt $new.Count; $i += 500) {
    $rows = @($new[$i..([Math]::Min($i + 499, $new.Count - 1))] | ForEach-Object { @{ sku = $_.sku; name = $_.name; price = $_.price; stockQuantity = $_.stock } })
    $result = Invoke-Api POST "/api/products/import?dryRun=false" @{ rows = $rows } $token
    if ($result.errors.Count -gt 0) { throw "The import refused $($result.errors.Count) row(s); the first: $($result.errors[0].sku) $($result.errors[0].message)" }
}
Write-Host "  products: $($new.Count) added, $($wanted.Count - $new.Count) already there"

foreach ($product in (Invoke-Api GET "/api/products")) { $have[$product.sku] = $product.id }
# Each product's default variant, which is what a marketplace lists.
$variants = @{}
foreach ($item in (Invoke-Api GET "/api/catalog/inventory").items) { if (-not $variants.ContainsKey($item.sku)) { $variants[$item.sku] = $item.variantId } }

# --- Magento ------------------------------------------------------------------

$account = @(Invoke-Api GET "/api/channels") | Where-Object { $_.channel -eq 4 } | Select-Object -First 1
if (-not $account) {
    # Live writes off and a placeholder address: everything is prepared and checked, and nothing reaches a store.
    $account = Invoke-Api POST "/api/channels" @{
        channel = 4; name = "Magento"; environment = 0; sellerId = $null; settings = @{ baseUrl = "https://magento.example.com" }
        isEnabled = $true; liveWritesEnabled = $false; inventorySyncEnabled = $false; orderImportEnabled = $false; priceConflictPolicy = 2
    } $token
    Write-Host "  + Magento added as a sales channel (placeholder store address, live writes off)"
}
$market = @($account.markets) | Select-Object -First 1
if (-not $market) { $market = Invoke-Api POST "/api/channels/$($account.id)/markets" @{ marketplaceCode = "default" } $token }

$mappings = @(Invoke-Api GET "/api/channels/category-mappings")
foreach ($category in $categories.Keys) {
    if (-not ($mappings | Where-Object { $_.channelMarketId -eq $market.id -and $_.internalCategory -eq $category })) {
        Invoke-Api PUT "/api/channels/category-mappings" @{ channelMarketId = $market.id; internalCategory = $category; externalCategoryId = $categories[$category] } $token | Out-Null
    }
}

$listed = @{}
foreach ($listing in @(Invoke-Api GET "/api/channel-listings?accountId=$($account.id)")) { $listed[$listing.sellerSku] = $true }

# A product without its Magento listing is not finished: its details are (re)written, then it is listed.
# The listing comes last, so a run that was interrupted picks up exactly where it stopped.
$todo = @($wanted | Where-Object { -not $listed.ContainsKey($_.sku) } | ForEach-Object {
    if (-not $have[$_.sku] -or -not $variants[$_.sku]) { throw "Product $($_.sku) is missing after the import; is the tenant's catalog migrated?" }
    @{ sku = $_.sku; id = $have[$_.sku]; variantId = $variants[$_.sku]; brand = $_.brand; category = $_.category; description = $_.description; upc = $_.upc }
})
Write-Host "  listing $($todo.Count) product(s) on Magento; this takes a few minutes for a large catalog..."

$failures = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$marketId = $market.id
$todo | ForEach-Object -ThrottleLimit 8 -Parallel {
    $item = $_
    $client = $using:client
    $base = $using:base
    $token = $using:token
    $send = {
        param([string]$Method, [string]$Path, $Body)
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$base$Path")
        $request.Headers.Add("X-CSRF-TOKEN", $token)
        $request.Content = [System.Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 6 -Compress), [System.Text.Encoding]::UTF8, "application/json")
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "$Method $Path failed ($([int]$response.StatusCode)): $text" }
        if ($text) { $text | ConvertFrom-Json }
    }
    try {
        & $send PUT "/api/catalog/products/$($item.id)/content" @{ brand = $item.brand; description = $item.description; category = $item.category } | Out-Null
        # type 1: UPC.
        & $send PUT "/api/catalog/products/$($item.id)/identifiers" @{ type = 1; value = $item.upc } | Out-Null
        $listing = & $send PUT "/api/channel-listings" @{ channelMarketId = $using:marketId; variantId = $item.variantId; fulfillmentMode = 0 }
        # Records what still stops it being published, so the pages show it without anyone asking.
        & $send POST "/api/channel-listings/$($listing.id)/validate" @{} | Out-Null
    } catch {
        ($using:failures).Add("$($item.sku): $($_.Exception.Message)")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "  $($failures.Count) product(s) could not be finished; run the script again to retry them. The first:" -ForegroundColor Yellow
    $failures | Select-Object -First 3 | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
}
Write-Host "  Magento: $($todo.Count - $failures.Count) draft listing(s) added, $($wanted.Count - $todo.Count) already there"

Write-Host ""
Write-Host "Done. See $base/products and $base/magento." -ForegroundColor Green
Write-Host "Brands, barcodes, prices and Magento category numbers are demo values; nothing was published." -ForegroundColor Yellow
if ($failures.Count -gt 0) { exit 1 }
