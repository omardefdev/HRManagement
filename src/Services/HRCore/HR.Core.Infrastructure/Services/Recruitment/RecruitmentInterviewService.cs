using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentInterviewService(HRCoreDbContext db)
{
    public async Task<(RecruitmentInterviewSlot? Slot, string? Error)> ScheduleAsync(
        Guid recruitmentApplicationId,
        DateTime scheduledAt,
        string? location,
        string? panelNotes,
        Guid? citizenApplicationId = null,
        CancellationToken ct = default)
    {
        var app = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == recruitmentApplicationId, ct);
        if (app is null) return (null, "الطلب غير موجود.");

        var slot = new RecruitmentInterviewSlot
        {
            RecruitmentApplicationId = app.Id,
            CitizenApplicationId = citizenApplicationId,
            ScheduledAt = scheduledAt,
            Location = location,
            PanelNotes = panelNotes,
            Status = InterviewSlotStatus.Scheduled
        };
        db.RecruitmentInterviewSlots.Add(slot);

        if (app.Status is RecruitmentApplicationStatus.Shortlisted
            or RecruitmentApplicationStatus.Ranked
            or RecruitmentApplicationStatus.Assessed)
        {
            app.Status = RecruitmentApplicationStatus.InterviewScheduled;
            app.ReviewedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return (slot, null);
    }

    public async Task<IReadOnlyList<RecruitmentInterviewSlot>> GetForApplicationAsync(
        Guid recruitmentApplicationId,
        CancellationToken ct = default) =>
        await db.RecruitmentInterviewSlots
            .AsNoTracking()
            .Where(s => s.RecruitmentApplicationId == recruitmentApplicationId)
            .OrderBy(s => s.ScheduledAt)
            .ToListAsync(ct);
}
