using MPSellerTools.Core.Business;

namespace MPSellerTools.TenantHost.Contracts;

public record OrderItemLine(Guid ProductId, string ProductName, string ProductSku, int Quantity, decimal UnitPrice);

public record OrderResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    Guid? AssignedUserId,
    IReadOnlyList<OrderItemLine> Items,
    decimal Total,
    string RowVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public record OrderItemRequest(Guid ProductId, int Quantity);

public record CreateOrderRequest(IReadOnlyList<OrderItemRequest> Items, Guid? AssignedUserId);

public record UpdateOrderRequest(IReadOnlyList<OrderItemRequest> Items, Guid? AssignedUserId, string RowVersion);

public record ChangeOrderStatusRequest(OrderStatus Status, string RowVersion);
