using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;

namespace HR.Movements.Domain.Services;

public static class MovementChecklistDefaults
{
    public static (string OwnerRole, int SlaHours, bool Required) GovernanceFor(
        MovementKind kind, MovementChecklistStep step) => (kind, step) switch
    {
        (_, MovementChecklistStep.RequestRecorded) => ("hr-movements", 24, true),
        (_, MovementChecklistStep.Approved) => ("hr-management-approver", 48, true),
        (MovementKind.Transfer, MovementChecklistStep.FinancialReviewed) => ("payroll", 48, true),
        (MovementKind.Transfer, MovementChecklistStep.OrgSynced) => ("hr-core", 24, true),
        (MovementKind.Transfer, MovementChecklistStep.DirectoryUpdated) => ("auth-directory", 24, true),
        (MovementKind.Transfer, MovementChecklistStep.AssetsNoted) => ("admin-logistics", 72, false),
        (MovementKind.Transfer, MovementChecklistStep.Executed) => ("hr-movements", 1, true),
        (MovementKind.Secondment, MovementChecklistStep.FinancialReviewed) => ("payroll", 48, true),
        (MovementKind.Secondment, MovementChecklistStep.DirectoryUpdated) => ("auth-directory", 24, true),
        (MovementKind.Secondment, MovementChecklistStep.Executed) => ("hr-movements", 1, true),
        (MovementKind.Secondment, MovementChecklistStep.Ended) => ("hr-movements", 24, true),
        (MovementKind.Loan, MovementChecklistStep.Executed) => ("hr-movements", 24, true),
        (MovementKind.Loan, MovementChecklistStep.Ended) => ("hr-movements", 24, true),
        _ => ("hr-movements", 48, false)
    };

    public static IReadOnlyList<MovementChecklistStep> StepsFor(MovementKind kind) => kind switch
    {
        MovementKind.Transfer =>
        [
            MovementChecklistStep.RequestRecorded,
            MovementChecklistStep.Approved,
            MovementChecklistStep.FinancialReviewed,
            MovementChecklistStep.OrgSynced,
            MovementChecklistStep.DirectoryUpdated,
            MovementChecklistStep.AssetsNoted,
            MovementChecklistStep.Executed
        ],
        MovementKind.Secondment =>
        [
            MovementChecklistStep.RequestRecorded,
            MovementChecklistStep.Approved,
            MovementChecklistStep.FinancialReviewed,
            MovementChecklistStep.DirectoryUpdated,
            MovementChecklistStep.Executed,
            MovementChecklistStep.Ended
        ],
        MovementKind.Loan =>
        [
            MovementChecklistStep.RequestRecorded,
            MovementChecklistStep.Approved,
            MovementChecklistStep.Executed,
            MovementChecklistStep.Ended
        ],
        _ => [MovementChecklistStep.RequestRecorded]
    };

    public static IEnumerable<MovementChecklist> CreateFor(MovementKind kind, Guid correlationId) =>
        StepsFor(kind).Select(step =>
        {
            var (owner, sla, required) = GovernanceFor(kind, step);
            return new MovementChecklist
            {
                MovementKind = kind,
                CorrelationId = correlationId,
                Step = step,
                IsRequired = required,
                OwnerRole = owner,
                SlaHours = sla
            };
        });
}
