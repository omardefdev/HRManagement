using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

public static class HireChecklistDefaults
{
    public static IReadOnlyList<HireStep> DefaultSteps { get; } =
        Enum.GetValues<HireStep>().Cast<HireStep>().ToList();

    public static (string OwnerRole, int SlaHours, bool RequiresEvidence) GovernanceFor(HireStep step) => step switch
    {
        HireStep.CandidateReceived => ("hr-recruiter", 24, false),
        HireStep.ContractClassified => ("hr-contracts", 48, true),
        HireStep.PersonalFileOpened => ("hr-core", 8, false),
        HireStep.PayrollLinked => ("payroll", 24, true),
        HireStep.CareerRecordApproved => ("hr-core", 120, false),
        HireStep.IdentityIssued => ("idcard", 72, true),
        HireStep.HireArchived => ("records-archive", 24, true),
        _ => ("hr-contracts", 48, false)
    };

    public static IEnumerable<HireChecklist> CreateForTransaction(Guid serviceTransactionId) =>
        DefaultSteps.Select(step =>
        {
            var (owner, sla, _) = GovernanceFor(step);
            return new HireChecklist
            {
                ServiceTransactionId = serviceTransactionId,
                Step = step,
                IsCompleted = false,
                OwnerRole = owner,
                SlaHours = sla
            };
        });

    public static string StepLabel(HireStep step) => step switch
    {
        HireStep.CandidateReceived => "استلام المرشح (P1.1)",
        HireStep.ContractClassified => "تصنيف وعقد (P1.2)",
        HireStep.PersonalFileOpened => "الملف الشخصي (P2.1)",
        HireStep.PayrollLinked => "ربط الراتب (P6.1)",
        HireStep.CareerRecordApproved => "اعتماد السيرة (P5.1)",
        HireStep.IdentityIssued => "الهوية (P8.1)",
        HireStep.HireArchived => "أرشفة التعيين (P7.1)",
        _ => step.ToString()
    };
}
