using HR.Contracts.Domain.Entities;

namespace HR.Contracts.Domain.Services;

public static class SeparationWorkflowRules
{
    public static bool CanComplete(SeparationRecord record, IEnumerable<SeparationChecklist> steps)
    {
        if (record.SeparationEventPublished) return false;
        if (record.IsCancelled) return false;
        return AreAllRequiredStepsCompleted(steps);
    }

    /// <summary>FUN-002: cancel before publish always; after publish only while EffectiveDate is still in the future.</summary>
    public static bool CanCancel(SeparationRecord record)
    {
        if (record.IsCancelled) return false;
        if (!record.SeparationEventPublished) return true;
        return record.EffectiveDate > DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>FUN-002-D: do not start saga until effective date is today or past.</summary>
    public static bool IsEffectiveDateReached(SeparationRecord record, DateOnly? todayUtc = null)
    {
        var today = todayUtc ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return record.EffectiveDate <= today;
    }

    public static bool AreAllStepsCompleted(IEnumerable<SeparationChecklist> steps) =>
        AreAllRequiredStepsCompleted(steps);

    public static bool AreAllRequiredStepsCompleted(IEnumerable<SeparationChecklist> steps)
    {
        var required = steps.Where(s => s.IsRequired).ToList();
        if (required.Count == 0) return false;
        return required.All(s => s.IsCompleted);
    }

    public static IReadOnlyList<SeparationChecklist> GetIncompleteRequiredSteps(IEnumerable<SeparationChecklist> steps) =>
        steps.Where(s => s.IsRequired && !s.IsCompleted)
             .OrderBy(s => s.Step)
             .ToList();

    public static SeparationChecklist? GetNextPendingStep(IEnumerable<SeparationChecklist> steps) =>
        steps.Where(s => s.IsRequired && !s.IsCompleted)
             .OrderBy(s => s.Step)
             .FirstOrDefault();

    public static void SoftCancel(SeparationRecord record, string? reason)
    {
        record.IsCancelled = true;
        record.CancelledAt = DateTime.UtcNow;
        record.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء قبل النفاذ" : reason.Trim();
    }
}
