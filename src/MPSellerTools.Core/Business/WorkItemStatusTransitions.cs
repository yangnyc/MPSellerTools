namespace MPSellerTools.Core.Business;

/// <summary>
/// Permitted task status transitions. An Employee may move their own task through
/// these (brief §5) but cannot reassign it or change anything else about it.
/// </summary>
public static class WorkItemStatusTransitions
{
    private static readonly Dictionary<WorkItemStatus, WorkItemStatus[]> Allowed = new()
    {
        [WorkItemStatus.Open] = [WorkItemStatus.InProgress, WorkItemStatus.Cancelled],
        [WorkItemStatus.InProgress] = [WorkItemStatus.Done, WorkItemStatus.Cancelled],
        [WorkItemStatus.Done] = [],
        [WorkItemStatus.Cancelled] = [],
    };

    public static bool IsValid(WorkItemStatus from, WorkItemStatus to) =>
        from == to || Allowed[from].Contains(to);
}
