namespace HR.Contracts.Domain.Enums;

public enum ServiceTransactionState
{
    Draft = 0,
    Submitted = 1,
    UnderReview = 2,
    NeedsInfo = 3,
    InProcess = 4,
    PendingApproval = 5,
    Approved = 6,
    Rejected = 7,
    Executing = 8,
    PostProcessing = 9,
    Completed = 10,
    Archived = 11,
    Cancelled = 12
}
