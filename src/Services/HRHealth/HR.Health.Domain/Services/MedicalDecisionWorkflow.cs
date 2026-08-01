using HR.Health.Domain.Entities;
using HR.Health.Domain.Enums;

namespace HR.Health.Domain.Services;

public static class MedicalDecisionWorkflow
{
    public static bool RequiresRetirement(MedicalCommitteeRecommendation recommendation) =>
        recommendation == MedicalCommitteeRecommendation.Retirement;

    public static bool RequiresStatusUpdate(MedicalCommitteeRecommendation recommendation) =>
        recommendation is MedicalCommitteeRecommendation.LimitedDuties
            or MedicalCommitteeRecommendation.ReturnToWork;

    public static bool ShouldPublishDecision(MedicalCommitteeRecommendation recommendation) =>
        recommendation is MedicalCommitteeRecommendation.Retirement
            or MedicalCommitteeRecommendation.LimitedDuties;

    public static HealthRetirementRecord CreateRetirementRecord(
        Guid employeeId, DateOnly effectiveDate, string? reason)
    {
        return new HealthRetirementRecord
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            EffectiveDate = effectiveDate,
            Reason = reason ?? "قرار لجنة طبية — تقاعد صحي"
        };
    }

    public static HealthStatusEntry CreateStatusEntry(
        Guid employeeId, MedicalCommitteeRecommendation recommendation)
    {
        var statusCode = recommendation switch
        {
            MedicalCommitteeRecommendation.LimitedDuties => "LIMITED_DUTIES",
            MedicalCommitteeRecommendation.ReturnToWork => "FIT_FOR_DUTY",
            _ => "OTHER"
        };

        return new HealthStatusEntry
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            EntryDate = DateTime.UtcNow,
            StatusCode = statusCode,
            Notes = $"بناء على توصية اللجنة الطبية: {recommendation}"
        };
    }

    public static string MapRecommendationToMedicalRef(MedicalCommitteeRecommendation recommendation) =>
        recommendation switch
        {
            MedicalCommitteeRecommendation.Retirement => "MEDICAL_RETIREMENT",
            MedicalCommitteeRecommendation.LimitedDuties => "LIMITED_DUTIES",
            MedicalCommitteeRecommendation.ReturnToWork => "RETURN_TO_WORK",
            _ => "OTHER"
        };
}
