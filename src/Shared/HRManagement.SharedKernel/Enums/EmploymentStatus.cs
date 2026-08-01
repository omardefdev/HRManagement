namespace HRManagement.SharedKernel.Enums;

public enum EmploymentStatus
{
    Active,
    Suspended,
    Terminated,
    Retired,
    OnLeave,
    Seconded,
    Resigned,
    /// <summary>DATA-002: end of life while employed or after separation recording.</summary>
    Deceased,
    /// <summary>DATA-002: duplicate record closed; see MergedIntoEmployeeId.</summary>
    Merged,
    /// <summary>DATA-002: PII scrubbed under approved retention policy (not hard delete).</summary>
    Anonymized
}
