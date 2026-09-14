using MPSellerTools.Core.Business;

namespace MPSellerTools.Tests.Unit;

public class OrderStatusTransitionsTests
{
    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.InProgress, true)]
    [InlineData(OrderStatus.New, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.New, OrderStatus.Completed, false)]
    [InlineData(OrderStatus.InProgress, OrderStatus.Completed, true)]
    [InlineData(OrderStatus.InProgress, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.InProgress, OrderStatus.New, false)]
    [InlineData(OrderStatus.Completed, OrderStatus.InProgress, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.New, false)]
    [InlineData(OrderStatus.New, OrderStatus.New, true)]
    public void IsValid_matches_the_documented_state_machine(OrderStatus from, OrderStatus to, bool expected)
    {
        Assert.Equal(expected, OrderStatusTransitions.IsValid(from, to));
    }
}
