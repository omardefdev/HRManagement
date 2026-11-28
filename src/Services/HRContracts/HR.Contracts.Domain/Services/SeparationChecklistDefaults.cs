using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

/// <summary>
/// The clearance items of a new separation, and how each is completed (PLAN-60 §4). The party names are those of
/// <c>ProcessParties</c> (the saga's mandatory parties), pinned by a test: the domain does not reference the message contracts.
/// </summary>
public static class SeparationChecklistDefaults
{
    public const string PartyPayroll = "Payroll";
    public const string PartyHrCore = "HR.Core";
    public const string PartyIdCard = "IDCard";
    public const string PartyAuthDirectory = "Auth.Directory";
    public const string PartyAdminLogistics = "Admin.Logistics";
    public const string PartyItInfra = "IT.Infra";
    public const string PartyArchive = "Records.Archive";

    public static IReadOnlyList<SeparationStep> DefaultSteps { get; } =
        Enum.GetValues<SeparationStep>().ToList();

    public static (string OwnerRole, int SlaHours) GovernanceFor(SeparationStep step) => step switch
    {
        SeparationStep.ContractTerminated => ("hr-contracts", 48),
        SeparationStep.PayrollSettled => ("payroll", 72),
        SeparationStep.CardRevoked => ("idcard", 48),
        SeparationStep.AccountDisabled => ("auth-directory", 24),
        SeparationStep.AssetsReturned => ("admin-logistics", 72),
        SeparationStep.Archived => ("records-archive", 24),
        SeparationStep.DecisionIssued => ("hr-contracts", 24),
        SeparationStep.EmployeeNotified => ("hr-contracts", 48),
        SeparationStep.ItAccessRevoked => ("it-infra", 48),
        SeparationStep.HrCoreUpdated => ("hr-core", 24),
        _ => ("hr-contracts", 48)
    };

    /// <summary>
    /// Manual: HR, by hand, with evidence, before the execution. System: the service itself (the decision; the contract's end at the
    /// execution). PartyAck: the party's acknowledgement, after the execution, never by hand.
    /// </summary>
    public static SeparationStepCompletionMode ModeFor(SeparationStep step) => step switch
    {
        SeparationStep.DecisionIssued => SeparationStepCompletionMode.System,
        SeparationStep.ContractTerminated => SeparationStepCompletionMode.System,
        SeparationStep.EmployeeNotified => SeparationStepCompletionMode.Manual,
        _ => SeparationStepCompletionMode.PartyAck
    };

    /// <summary>The saga party whose acknowledgement completes a PartyAck item; null for the others.</summary>
    public static string? PartyFor(SeparationStep step) => step switch
    {
        SeparationStep.PayrollSettled => PartyPayroll,
        SeparationStep.CardRevoked => PartyIdCard,
        SeparationStep.AccountDisabled => PartyAuthDirectory,
        SeparationStep.AssetsReturned => PartyAdminLogistics,
        SeparationStep.Archived => PartyArchive,
        SeparationStep.ItAccessRevoked => PartyItInfra,
        SeparationStep.HrCoreUpdated => PartyHrCore,
        _ => null
    };

    /// <summary>The PartyAck item a party's acknowledgement completes, if any.</summary>
    public static SeparationStep? StepForParty(string? party) =>
        DefaultSteps.Where(s => string.Equals(PartyFor(s), party, StringComparison.OrdinalIgnoreCase))
            .Select(s => (SeparationStep?)s)
            .FirstOrDefault();

    /// <summary>
    /// The items the execution waits for (PLAN-60 §4, rule 1): every Manual item and every System item done before it — the decision and
    /// the employee's notification. The contract's end is System too, but the execution itself completes it.
    /// </summary>
    public static bool IsPreExecution(SeparationStep step) =>
        step is SeparationStep.DecisionIssued or SeparationStep.EmployeeNotified;

    /// <summary>
    /// The items of a new record. <paramref name="requireEmployeeNotification"/> is <c>Separation:Checklist:RequireEmployeeNotification</c>:
    /// false makes <see cref="SeparationStep.EmployeeNotified"/> not required on new records.
    /// </summary>
    public static IEnumerable<SeparationChecklist> CreateForRecord(
        Guid separationRecordId, bool requireEmployeeNotification = true, IEnumerable<SeparationStep>? steps = null) =>
        (steps ?? DefaultSteps).Select(step => Create(separationRecordId, step, requireEmployeeNotification));

    public static SeparationChecklist Create(Guid separationRecordId, SeparationStep step, bool requireEmployeeNotification = true)
    {
        var (owner, sla) = GovernanceFor(step);
        return new SeparationChecklist
        {
            SeparationRecordId = separationRecordId,
            Step = step,
            IsCompleted = false,
            IsRequired = step != SeparationStep.EmployeeNotified || requireEmployeeNotification,
            SourceSystem = "HRContracts",
            OwnerRole = owner,
            SlaHours = sla,
            CompletionMode = ModeFor(step),
            PartyName = PartyFor(step)
        };
    }
}
