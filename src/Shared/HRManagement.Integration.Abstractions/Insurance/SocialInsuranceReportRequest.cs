namespace HRManagement.Integration.Abstractions.Insurance;

public sealed record SocialInsuranceReportRequest(
    Guid PayrollRunId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<SocialInsuranceContributionLine> Lines);

/// <summary>سطر اشتراك تأمينات — أساس الاحتساب وفق القانون 50/2004 و92/1959.</summary>
public sealed record SocialInsuranceContributionLine(
    Guid EmployeeId,
    string? NationalId,
    string? EmployeeName,
    decimal InsurableWageBase,
    decimal EmployeeContribution,
    decimal EmployerContribution);
