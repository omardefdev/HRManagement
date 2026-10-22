using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

/// <summary>
/// FUN-029 (I2): optional hire checklist steps (<c>HireChecklist</c> section). Applied when a transaction's checklist
/// is seeded; open transactions keep the checklist they were created with (no back-fill).
/// </summary>
public sealed class HireChecklistOptions
{
    public const string SectionName = "HireChecklist";

    /// <summary><c>HireChecklist:RequireAppointmentDecision</c> (default true, D-6).</summary>
    public bool RequireAppointmentDecision { get; set; } = true;

    /// <summary><c>HireChecklist:RequireMedicalFitness</c> (default false).</summary>
    public bool RequireMedicalFitness { get; set; }
}

public static class HireChecklistDefaults
{
    /// <summary>Steps every hire checklist has (pre-FUN-029 set).</summary>
    public static IReadOnlyList<HireStep> BaseSteps { get; } =
    [
        HireStep.CandidateReceived,
        HireStep.ContractClassified,
        HireStep.PersonalFileOpened,
        HireStep.PayrollLinked,
        HireStep.CareerRecordApproved,
        HireStep.IdentityIssued,
        HireStep.HireArchived
    ];

    /// <summary>Steps with the default <see cref="HireChecklistOptions"/> (appointment decision on, medical off).</summary>
    public static IReadOnlyList<HireStep> DefaultSteps { get; } = StepsFor(new HireChecklistOptions());

    public static IReadOnlyList<HireStep> StepsFor(HireChecklistOptions? options)
    {
        var o = options ?? new HireChecklistOptions();
        var steps = BaseSteps.ToList();
        if (o.RequireAppointmentDecision)
            steps.Add(HireStep.AppointmentDecisionIssued);
        if (o.RequireMedicalFitness)
            steps.Add(HireStep.PreEmploymentMedical);
        return steps;
    }

    public static (string OwnerRole, int SlaHours, bool RequiresEvidence) GovernanceFor(HireStep step) => step switch
    {
        HireStep.CandidateReceived => ("hr-recruiter", 24, false),
        HireStep.ContractClassified => ("hr-contracts", 48, true),
        HireStep.PersonalFileOpened => ("hr-core", 8, false),
        HireStep.PayrollLinked => ("payroll", 24, true),
        HireStep.CareerRecordApproved => ("hr-core", 120, false),
        HireStep.IdentityIssued => ("idcard", 72, true),
        HireStep.HireArchived => ("records-archive", 24, true),
        HireStep.AppointmentDecisionIssued => ("hr-contracts", 48, true),
        HireStep.PreEmploymentMedical => ("hr-health", 120, true),
        _ => ("hr-contracts", 48, false)
    };

    public static IEnumerable<HireChecklist> CreateForTransaction(Guid serviceTransactionId, HireChecklistOptions? options = null) =>
        StepsFor(options).Select(step =>
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
        HireStep.AppointmentDecisionIssued => "إصدار قرار التعيين (D-6)",
        HireStep.PreEmploymentMedical => "الفحص الطبي قبل التعيين",
        _ => step.ToString()
    };
}
