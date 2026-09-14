using MPSellerTools.Core.Business;

namespace MPSellerTools.Tests.Unit;

public class WorkItemStatusTransitionsTests
{
    [Theory]
    [InlineData(WorkItemStatus.Open, WorkItemStatus.InProgress, true)]
    [InlineData(WorkItemStatus.Open, WorkItemStatus.Cancelled, true)]
    [InlineData(WorkItemStatus.Open, WorkItemStatus.Done, false)]
    [InlineData(WorkItemStatus.InProgress, WorkItemStatus.Done, true)]
    [InlineData(WorkItemStatus.InProgress, WorkItemStatus.Open, false)]
    [InlineData(WorkItemStatus.Done, WorkItemStatus.Open, false)]
    [InlineData(WorkItemStatus.Cancelled, WorkItemStatus.InProgress, false)]
    public void IsValid_matches_the_documented_state_machine(WorkItemStatus from, WorkItemStatus to, bool expected)
    {
        Assert.Equal(expected, WorkItemStatusTransitions.IsValid(from, to));
    }
}
