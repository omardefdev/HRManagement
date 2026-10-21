using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-006 / HCM-026 (G-08): interview lifecycle — schedule, reschedule, cancel, complete.
/// Interviews never move an application backwards (no regression from Assessed/Ranked).
/// </summary>
public sealed class RecruitmentInterviewService(
    HRCoreDbContext db,
    IOptions<RecruitmentOptions> options)
{
    public const int MinRescheduleReasonLength = 5;
    public const int MaxReasonLength = 500;
    public const int MaxOutcomeNotesLength = 2000;
    public const int MaxLocationLength = 200;

    public async Task<(RecruitmentInterviewSlot? Slot, string? Error)> ScheduleAsync(
        Guid recruitmentApplicationId,
        DateTime scheduledAt,
        InterviewMode mode,
        string? location,
        IReadOnlyList<Guid> panelEmployeeIds,
        string? panelNotes,
        Guid? citizenApplicationId = null,
        CancellationToken ct = default)
    {
        var when = ToUtc(scheduledAt);
        if (when <= DateTime.UtcNow)
            return (null, "موعد المقابلة يجب أن يكون في المستقبل.");

        var trimmedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        if (trimmedLocation is { Length: > MaxLocationLength })
            return (null, $"المكان أو الرابط يجب ألا يتجاوز {MaxLocationLength} حرف.");

        var app = await db.RecruitmentApplications
            .Include(a => a.SourceInternalApplication)
            .FirstOrDefaultAsync(a => a.Id == recruitmentApplicationId, ct);
        if (app is null) return (null, "الطلب غير موجود.");

        // PLAN-26: Assessed/Ranked (and later) applications are never pulled back to InterviewScheduled.
        if (app.Status is not (RecruitmentApplicationStatus.Shortlisted or RecruitmentApplicationStatus.InterviewScheduled))
            return (null, "يمكن جدولة المقابلة فقط للطلبات في «القائمة المختصرة» أو «مقابلة مجدولة».");

        var hasScheduled = await db.RecruitmentInterviewSlots.AnyAsync(
            s => s.RecruitmentApplicationId == app.Id && s.Status == InterviewSlotStatus.Scheduled, ct);
        if (hasScheduled)
            return (null, "توجد مقابلة مجدولة لهذا المرشح؛ أعد جدولتها أو ألغها أولاً.");

        var (panel, panelError) = await ValidatePanelAsync(app, panelEmployeeIds, ct);
        if (panelError is not null)
            return (null, panelError);

        if (app.Status == RecruitmentApplicationStatus.Shortlisted
            && !SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.InterviewScheduled))
        {
            return (null, SelectionWorkflowRules.ValidateTransitionGate(app, RecruitmentApplicationStatus.InterviewScheduled)
                ?? "لا يمكن جدولة المقابلة للحالة الحالية.");
        }

        var slot = new RecruitmentInterviewSlot
        {
            TenantId = app.TenantId,
            RecruitmentApplicationId = app.Id,
            CitizenApplicationId = citizenApplicationId,
            ScheduledAt = when,
            Mode = mode,
            Location = trimmedLocation,
            PanelNotes = string.IsNullOrWhiteSpace(panelNotes) ? null : panelNotes.Trim(),
            Status = InterviewSlotStatus.Scheduled
        };
        AddPanel(slot, panel);
        db.RecruitmentInterviewSlots.Add(slot);

        if (app.Status == RecruitmentApplicationStatus.Shortlisted)
        {
            app.Status = RecruitmentApplicationStatus.InterviewScheduled;
            app.ReviewedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return (slot, null);
    }

    public async Task<(RecruitmentInterviewSlot? Slot, string? Error)> RescheduleAsync(
        Guid slotId,
        DateTime newTime,
        string? reason,
        CancellationToken ct = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason) || trimmedReason.Length < MinRescheduleReasonLength)
            return (null, $"سبب إعادة الجدولة مطلوب ({MinRescheduleReasonLength} أحرف على الأقل).");
        if (trimmedReason.Length > MaxReasonLength)
            return (null, $"السبب يجب ألا يتجاوز {MaxReasonLength} حرف.");

        var when = ToUtc(newTime);
        if (when <= DateTime.UtcNow)
            return (null, "الموعد الجديد يجب أن يكون في المستقبل.");

        var original = await db.RecruitmentInterviewSlots
            .Include(s => s.PanelMembers)
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (original is null) return (null, "المقابلة غير موجودة.");
        if (original.Status != InterviewSlotStatus.Scheduled)
            return (null, "يمكن إعادة جدولة المقابلات المجدولة فقط.");

        var previousReschedules = await CountRescheduleChainAsync(original, ct);
        var max = Math.Max(0, options.Value.MaxInterviewReschedules);
        if (previousReschedules >= max)
            return (null, $"تم بلوغ الحد الأقصى لإعادة جدولة المقابلة ({max}).");

        original.Status = InterviewSlotStatus.Rescheduled;
        original.CancelledReason = trimmedReason;

        var replacement = new RecruitmentInterviewSlot
        {
            TenantId = original.TenantId,
            RecruitmentApplicationId = original.RecruitmentApplicationId,
            CitizenApplicationId = original.CitizenApplicationId,
            ScheduledAt = when,
            Mode = original.Mode,
            Location = original.Location,
            PanelNotes = original.PanelNotes,
            Status = InterviewSlotStatus.Scheduled,
            RescheduledFromSlotId = original.Id
        };
        foreach (var member in original.PanelMembers)
        {
            replacement.PanelMembers.Add(new RecruitmentInterviewPanelMember
            {
                TenantId = member.TenantId,
                EmployeeId = member.EmployeeId,
                Role = member.Role,
                CoiDeclared = member.CoiDeclared
            });
        }

        db.RecruitmentInterviewSlots.Add(replacement);
        await db.SaveChangesAsync(ct);
        return (replacement, null);
    }

    public async Task<(bool Success, string? Error)> CancelAsync(
        Guid slotId,
        string? reason,
        CancellationToken ct = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason))
            return (false, "سبب الإلغاء مطلوب.");
        if (trimmedReason.Length > MaxReasonLength)
            return (false, $"السبب يجب ألا يتجاوز {MaxReasonLength} حرف.");

        var slot = await db.RecruitmentInterviewSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null) return (false, "المقابلة غير موجودة.");
        if (slot.Status != InterviewSlotStatus.Scheduled)
            return (false, "يمكن إلغاء المقابلات المجدولة فقط.");

        // The application status is intentionally left unchanged.
        slot.Status = InterviewSlotStatus.Cancelled;
        slot.CancelledReason = trimmedReason;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CompleteAsync(
        Guid slotId,
        InterviewOutcome outcome,
        string? notes,
        CancellationToken ct = default)
    {
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes is { Length: > MaxOutcomeNotesLength })
            return (false, $"الملاحظات يجب ألا تتجاوز {MaxOutcomeNotesLength} حرف.");

        var slot = await db.RecruitmentInterviewSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null) return (false, "المقابلة غير موجودة.");
        if (slot.Status != InterviewSlotStatus.Scheduled)
            return (false, "يمكن تسجيل نتيجة المقابلات المجدولة فقط.");
        if (ToUtc(slot.ScheduledAt) > DateTime.UtcNow)
            return (false, "لا يمكن تسجيل نتيجة مقابلة لم يحن موعدها بعد.");

        slot.Status = InterviewSlotStatus.Completed;
        slot.CompletedAt = DateTime.UtcNow;
        slot.Outcome = outcome;
        slot.OutcomeNotes = trimmedNotes;
        // NoShow leaves the application status unchanged — rejection or rescheduling is an HR decision.
        // PLAN-30: notify the candidate of the outcome.

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public Task<bool> HasAttendedInterviewAsync(Guid applicationId, CancellationToken ct = default) =>
        db.RecruitmentInterviewSlots.AnyAsync(
            s => s.RecruitmentApplicationId == applicationId
                && s.Status == InterviewSlotStatus.Completed
                && s.Outcome == InterviewOutcome.Attended,
            ct);

    public async Task<IReadOnlyList<RecruitmentInterviewSlot>> GetForApplicationAsync(
        Guid recruitmentApplicationId,
        CancellationToken ct = default) =>
        await db.RecruitmentInterviewSlots
            .AsNoTracking()
            .Include(s => s.PanelMembers)
                .ThenInclude(m => m.Employee)
            .Where(s => s.RecruitmentApplicationId == recruitmentApplicationId)
            .OrderBy(s => s.ScheduledAt)
            .ToListAsync(ct);

    private async Task<(IReadOnlyList<Guid> Panel, string? Error)> ValidatePanelAsync(
        RecruitmentApplication app,
        IReadOnlyList<Guid>? panelEmployeeIds,
        CancellationToken ct)
    {
        var panel = (panelEmployeeIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (panel.Count == 0)
            return ([], "يجب تحديد عضو واحد على الأقل في لجنة المقابلة.");

        var existing = await db.Employees
            .Where(e => panel.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(ct);
        if (existing.Count != panel.Count)
            return ([], "أحد أعضاء لجنة المقابلة غير موجود في سجل الموظفين.");

        if (app.SourceInternalApplication?.EmployeeId is Guid applicantEmployeeId && panel.Contains(applicantEmployeeId))
            return ([], "لا يجوز أن يكون المتقدم عضواً في لجنة مقابلته.");

        return (panel, null);
    }

    private static void AddPanel(RecruitmentInterviewSlot slot, IReadOnlyList<Guid> panel)
    {
        for (var i = 0; i < panel.Count; i++)
        {
            slot.PanelMembers.Add(new RecruitmentInterviewPanelMember
            {
                TenantId = slot.TenantId,
                EmployeeId = panel[i],
                Role = i == 0 ? RecruitmentInterviewPanelMember.ChairRole : RecruitmentInterviewPanelMember.MemberRole
            });
        }
    }

    private async Task<int> CountRescheduleChainAsync(RecruitmentInterviewSlot slot, CancellationToken ct)
    {
        var count = 0;
        var previousId = slot.RescheduledFromSlotId;
        while (previousId is Guid id && count < 100)
        {
            count++;
            previousId = await db.RecruitmentInterviewSlots
                .Where(s => s.Id == id)
                .Select(s => s.RescheduledFromSlotId)
                .FirstOrDefaultAsync(ct);
        }

        return count;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
