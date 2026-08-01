namespace Compliance.Domain.Enums;

public enum DsarRequestType
{
    Access = 0,
    Rectify = 1,
    Deletion = 2
}

public enum DsarRequestStatus
{
    Open = 0,
    InProgress = 1,
    Fulfilled = 2,
    Rejected = 3
}
