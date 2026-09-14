namespace MPSellerTools.Core.Business;

/// <summary>Permitted order status transitions (brief §9). Enforced server-side, not just in the UI.</summary>
public static class OrderStatusTransitions
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.New] = [OrderStatus.InProgress, OrderStatus.Cancelled],
        [OrderStatus.InProgress] = [OrderStatus.Completed, OrderStatus.Cancelled],
        [OrderStatus.Completed] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static bool IsValid(OrderStatus from, OrderStatus to) =>
        from == to || Allowed[from].Contains(to);
}
