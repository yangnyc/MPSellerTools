namespace MPSellerTools.TenantHost.Contracts;

public record ProductResponse(
    Guid Id, string Sku, string Name, decimal Price, int StockQuantity, bool IsArchived, string RowVersion);

public record CreateProductRequest(string Sku, string Name, decimal Price, int StockQuantity);

public record UpdateProductRequest(string Name, decimal Price, int StockQuantity, string RowVersion);
