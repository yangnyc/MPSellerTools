using MPSellerTools.Core.Business;

namespace MPSellerTools.Core.Marketplace;

/// <summary>
/// What a marketplace asks of a listing's pictures. Only <see cref="MaxImages"/>
/// and <see cref="MinImages"/> are enforced here: the pictures are addresses,
/// so their size, format and background cannot be measured and are shown to
/// the seller as guidance. The figures were written down from the
/// marketplaces' public documentation as remembered, not read from their
/// APIs; <see cref="Source"/> says where to check them.
/// </summary>
public record ImageRules(
    int MinImages,
    int MaxImages,
    string MainImage,
    string Formats,
    string Size,
    string Source);

public static class ChannelImageRules
{
    private static readonly ImageRules Website = new(0, 50, "The first picture is shown first.", "Any format a browser shows.", "No limit of its own.", "This workspace");

    public static ImageRules For(SalesChannel channel) => channel switch
    {
        SalesChannel.Ebay => new(
            1, 24,
            "The first picture is the gallery picture buyers see in search results.",
            "JPEG, PNG, GIF, TIFF, BMP or WebP, at an https address.",
            "At least 500 px on the longest side; 1600 px recommended.",
            "eBay Inventory API, createOrReplaceInventoryItem (product.imageUrls), and eBay's picture requirements"),
        SalesChannel.Amazon => new(
            0, 9,
            "The main picture shows only the product, on a pure white background.",
            "JPEG, PNG, TIFF or non-animated GIF.",
            "At least 1000 px on the longest side, so buyers can zoom.",
            "Amazon SP-API Listings Items (main_product_image_locator, other_product_image_locator_1 to 8) and Amazon's product image requirements"),
        SalesChannel.Walmart => new(
            1, 10,
            "The main picture shows the product on a white background.",
            "JPEG or PNG, up to 5 MB.",
            "Square, 1500 px or more; 2200 px recommended.",
            "Walmart Marketplace item spec (mainImageUrl, productSecondaryImageURL) and Walmart's image guidelines"),
        SalesChannel.Magento => new(
            0, 50,
            "Pictures are not sent to Magento from here; add them to the product in the store's admin.",
            "JPEG, PNG or GIF, uploaded in the store's admin.",
            "As the store's theme asks.",
            "Magento's catalog API takes a picture as file contents (media_gallery_entries), not as an address"),
        _ => Website,
    };
}
