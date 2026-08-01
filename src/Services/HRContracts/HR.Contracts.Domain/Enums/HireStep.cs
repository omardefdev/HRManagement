namespace HR.Contracts.Domain.Enums;

/// <summary>خطوات معاملة التعيين (DFD Level 2 — موظف جديد).</summary>
public enum HireStep
{
    CandidateReceived = 0,
    ContractClassified = 1,
    PersonalFileOpened = 2,
    PayrollLinked = 3,
    CareerRecordApproved = 4,
    IdentityIssued = 5,
    HireArchived = 6
}
