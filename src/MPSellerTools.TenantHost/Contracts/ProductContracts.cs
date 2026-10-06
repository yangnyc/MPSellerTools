namespace MPSellerTools.TenantHost.Contracts;

public record ProductResponse(
    Guid Id, string Sku, string Name, decimal Price, int StockQuantity, bool IsArchived, string RowVersion);

public record CreateProductRequest(string Sku, string Name, decimal Price, int StockQuantity);

public record UpdateProductRequest(string Name, decimal Price, int StockQuantity, string RowVersion);

public record ImportProductRow(string? Sku, string? Name, decimal Price, int StockQuantity);

public record ImportProductsRequest(IReadOnlyList<ImportProductRow>? Rows);

/// <summary><see cref="Row"/> counts from 1, in the order the rows were sent.</summary>
public record ImportProductError(int Row, string? Sku, string Message);

/// <summary>With <see cref="DryRun"/> the counts say what would happen; nothing was saved.</summary>
public record ImportProductsResponse(bool DryRun, int Created, int Updated, int Unchanged, IReadOnlyList<ImportProductError> Errors);
