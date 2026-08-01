using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

public static class SeparationChecklistDefaults
{
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
        _ => ("hr-contracts", 48)
    };

    public static IEnumerable<SeparationChecklist> CreateForRecord(Guid separationRecordId) =>
        DefaultSteps.Select(step =>
        {
            var (owner, sla) = GovernanceFor(step);
            return new SeparationChecklist
            {
                SeparationRecordId = separationRecordId,
                Step = step,
                IsCompleted = false,
                IsRequired = true,
                SourceSystem = "HRContracts",
                OwnerRole = owner,
                SlaHours = sla
            };
        });
}
