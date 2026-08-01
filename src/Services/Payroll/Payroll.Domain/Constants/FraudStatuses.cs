namespace Payroll.Domain.Constants;

/// <summary>FRD-001 fraud alert lifecycle statuses.</summary>
public static class FraudAlertStatus
{
    public const string Open = "Open";
    public const string UnderReview = "UnderReview";
    public const string Resolved = "Resolved";
    public const string Dismissed = "Dismissed";
}

/// <summary>FRD-001 fraud alert severity levels.</summary>
public static class FraudAlertSeverity
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
}

/// <summary>FRD-001 payment hold statuses.</summary>
public static class PaymentHoldStatus
{
    public const string Active = "Active";
    public const string Released = "Released";
}

/// <summary>FRD-001 payment hold types.</summary>
public static class PaymentHoldType
{
    public const string Fraud = "Fraud";
    public const string ProofOfLife = "ProofOfLife";
    public const string Manual = "Manual";
}

/// <summary>FRD-001 proof-of-life record statuses.</summary>
public static class ProofOfLifeStatus
{
    public const string Valid = "Valid";
    public const string Expired = "Expired";
    public const string Exception = "Exception";
}

/// <summary>FRD-001 anomaly rule identifiers (FR-01..FR-08).</summary>
public static class FraudRuleIds
{
    public const string GhostEmployee = "FR-01";
    public const string DeceasedBeneficiary = "FR-02";
    public const string DuplicateEmployment = "FR-03";
    public const string SharedIban = "FR-04";
    public const string AnomalousCompensation = "FR-05";
    public const string SingleApproverVolume = "FR-06";
    public const string SameDayHireAndPay = "FR-07";
    public const string MissingSourceRecord = "FR-08";
    public const string ProofOfLifeExpiry = "FR-POL";
}
