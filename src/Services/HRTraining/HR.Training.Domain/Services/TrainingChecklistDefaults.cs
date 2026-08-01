using HR.Training.Domain.Entities;
using HR.Training.Domain.Enums;

namespace HR.Training.Domain.Services;

public static class TrainingChecklistDefaults
{
    public static (string OwnerRole, int SlaHours, bool Required) GovernanceFor(TrainingChecklistStep step) => step switch
    {
        TrainingChecklistStep.Enrolled => ("hr-training", 24, true),
        TrainingChecklistStep.Completed => ("hr-training", 720, true),
        TrainingChecklistStep.CertificateIssued => ("hr-training", 48, true),
        TrainingChecklistStep.Verified => ("hr-training", 24, false),
        _ => ("hr-training", 48, false)
    };

    public static IEnumerable<TrainingChecklist> CreateFor(Guid enrollmentId) =>
        Enum.GetValues<TrainingChecklistStep>().Select(step =>
        {
            var (owner, sla, required) = GovernanceFor(step);
            return new TrainingChecklist
            {
                EnrollmentId = enrollmentId,
                Step = step,
                IsRequired = required,
                OwnerRole = owner,
                SlaHours = sla
            };
        });

    public static bool PreCompleteGate(IEnumerable<TrainingChecklist> items)
    {
        var enrolled = items.FirstOrDefault(i => i.Step == TrainingChecklistStep.Enrolled);
        return enrolled is { IsCompleted: true } && !string.IsNullOrWhiteSpace(enrolled.EvidenceRef);
    }

    public static void Mark(TrainingChecklist item, Guid? by, string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            throw new InvalidOperationException("EvidenceRef مطلوب.");
        item.IsCompleted = true;
        item.CompletedAt = DateTime.UtcNow;
        item.CompletedBy = by;
        item.EvidenceRef = evidence.Trim();
    }
}

public static class CertificateVerificationRules
{
    public static bool IsValidFormat(string? certificateNo) =>
        !string.IsNullOrWhiteSpace(certificateNo)
        && certificateNo.Trim().StartsWith("CERT-", StringComparison.OrdinalIgnoreCase);
}
