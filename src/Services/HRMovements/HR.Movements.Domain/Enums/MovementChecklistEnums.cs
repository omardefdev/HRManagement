namespace HR.Movements.Domain.Enums;

public enum MovementKind
{
    Transfer = 0,
    Secondment = 1,
    Loan = 2
}

public enum MovementChecklistStep
{
    RequestRecorded = 0,
    Approved = 1,
    FinancialReviewed = 2,
    OrgSynced = 3,
    DirectoryUpdated = 4,
    AssetsNoted = 5,
    Executed = 6,
    Ended = 7
}
