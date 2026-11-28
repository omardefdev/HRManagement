using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Application.Features.SeparationRecords;

/// <summary>
/// PLAN-60: the <c>Separation</c> section of <c>HR.Contracts.Web/appsettings.json</c> (every environment reads it). Each default is the
/// owner's decision of 2026-10-31.
/// </summary>
public sealed class SeparationOptions
{
    public const string SectionName = "Separation";

    public SeparationApprovalOptions Approval { get; set; } = new();

    public SeparationDecisionOptions Decision { get; set; } = new();

    public SeparationChecklistOptions Checklist { get; set; } = new();
}

public sealed class SeparationApprovalOptions
{
    /// <summary>The types routed to <c>SEPARATION-APPROVAL-SENSITIVE</c> (D-SEP-4).</summary>
    public static IReadOnlyList<SeparationType> DefaultSensitiveTypes { get; } = [SeparationType.Disciplinary, SeparationType.Judicial];

    /// <summary>
    /// Null means <see cref="DefaultSensitiveTypes"/>. A list, not an array with a default: the configuration binder writes over an array's
    /// items by index, so a shorter configured list would keep a default it meant to drop.
    /// </summary>
    public List<string>? SensitiveTypes { get; set; }
}

public sealed class SeparationDecisionOptions
{
    /// <summary>The decision's issuer is any holder of <c>HrContractsApprove</c> but the record's creator.</summary>
    public bool IssuerMustDifferFromCreator { get; set; } = true;
}

public sealed class SeparationChecklistOptions
{
    /// <summary>The employee's notification is a manual item with evidence, required before the execution.</summary>
    public bool RequireEmployeeNotification { get; set; } = true;
}
