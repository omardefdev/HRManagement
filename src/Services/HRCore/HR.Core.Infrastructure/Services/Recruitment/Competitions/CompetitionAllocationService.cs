using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: outcome of a finalization.</summary>
public sealed record CompetitionFinalizeResult(bool Success, string? Error, int Allocated, int Reserve, int NotSuccessful, bool AlreadyFinalized = false);

/// <summary>
/// HCM-031 (PLAN-31, P5, AC-HCM-31f): finalization — allocation by merit and preference (<see cref="CompetitionAllocationRules"/>)
/// from the current published results, line reserve lists, and the rejection of the others (<c>CompetitionResult</c>).
/// Allowed once the objection window has passed with no pending objection and no unpublished version. Idempotent: a
/// second call (also a concurrent one — the competition row is locked on PostgreSQL) finds it Finalized and changes nothing.
/// </summary>
public sealed class CompetitionAllocationService(HRCoreDbContext db, CompetitionObjectionService objections)
{
    public const string NotSuccessfulNote = "غير ناجح في المسابقة";
    public const string NotAdmittedNote = "لم يُقبل للاختبار في المسابقة";
    public const string NoLineNote = "ناجح دون شاغر في الأسطر المفضلة ودون قائمة احتياط";

    public async Task<CompetitionFinalizeResult> FinalizeAsync(Guid competitionId, Guid? actorUserId, CancellationToken ct = default)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM \"RecruitmentCompetitions\" WHERE \"Id\" = {0} FOR UPDATE", [competitionId], ct);

        var competition = await db.RecruitmentCompetitions.Include(c => c.Lines).ThenInclude(l => l.Requisitions)
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return new(false, CompetitionLifecycleService.NotFoundMessage, 0, 0, 0);
        if (competition.Status == RecruitmentCompetitionStatus.Finalized)
            return new(true, null, 0, 0, 0, AlreadyFinalized: true);
        if (competition.Status != RecruitmentCompetitionStatus.ResultsPublished || competition.CurrentResultVersionId is not Guid versionId)
            return new(false, "يُتاح الإنهاء بعد نشر النتائج.", 0, 0, 0);
        var now = DateTime.UtcNow;
        var deadline = CompetitionWorkflowRules.ObjectionDeadlineUtc(competition.ResultsFirstPublishedAt, competition.ObjectionWindowDays)!.Value;
        if (now <= deadline)
            return new(false, $"لا يمكن الإنهاء قبل انتهاء مدة الاعتراض ({deadline:yyyy/MM/dd HH:mm} UTC).", 0, 0, 0);
        if (await objections.HasPendingAsync(competitionId, ct))
            return new(false, "يوجد اعتراض قيد المراجعة؛ لا يمكن إنهاء المسابقة قبل البت فيه.", 0, 0, 0);
        if (await db.CompetitionResultVersions.AnyAsync(v => v.CompetitionId == competitionId
                && v.Status != CompetitionResultVersionStatus.Published && v.Status != CompetitionResultVersionStatus.Superseded, ct))
            return new(false, "توجد نسخة نتائج لم تُنشر بعد؛ انشرها أو احذف مسودتها قبل الإنهاء.", 0, 0, 0);

        var results = await db.CompetitionResults.AsNoTracking()
            .Where(r => r.ResultVersionId == versionId)
            .Select(r => new { r.RecruitmentApplicationId, r.ExamNumber, r.FinalScore, r.OverallRank, r.Outcome })
            .ToListAsync(ct);
        var preferences = (await db.CompetitionPreferences.AsNoTracking()
                .Where(p => p.CompetitionLine!.CompetitionId == competitionId)
                .OrderBy(p => p.Order)
                .Select(p => new { p.RecruitmentApplicationId, p.CompetitionLineId })
                .ToListAsync(ct))
            .ToLookup(p => p.RecruitmentApplicationId, p => p.CompetitionLineId);
        var lines = competition.Lines.OrderBy(l => l.LineNo)
            .Select(l => new AllocationLine(
                l.Id,
                l.Requisitions.Where(r => r.Status == RequisitionStatus.Published).OrderBy(r => r.RequisitionNumber).Select(r => r.Id).ToList(),
                CompetitionWorkflowRules.EffectivePassMark(competition, l)))
            .ToList();
        var passed = results
            .Where(r => r.Outcome == CompetitionResultOutcome.Passed && r.FinalScore is not null && r.OverallRank is not null)
            .Select(r => new AllocationCandidate(r.RecruitmentApplicationId, r.OverallRank!.Value, r.ExamNumber, r.FinalScore!.Value,
                preferences[r.RecruitmentApplicationId].ToList()))
            .ToList();
        var plan = CompetitionAllocationRules.Plan(passed, lines);

        var resultByApp = results.ToDictionary(r => r.RecruitmentApplicationId);
        var placementByApp = plan.Placements.ToDictionary(p => p.ApplicationId);
        var reserveApps = plan.Reserves.Select(r => r.ApplicationId).ToHashSet();
        var criteriaByLine = competition.Lines.ToDictionary(l => l.Id, l => l.CriteriaVersionId);
        var reserveUntil = DateOnly.FromDateTime(now).AddMonths(competition.ReserveValidityMonths);
        var terminal = SelectionWorkflowRules.TerminalStatuses.ToList();

        var openIds = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.CompetitionId == competitionId && !terminal.Contains(a.Status))
            .Select(a => a.Id)
            .ToListAsync(ct);
        int allocated = 0, reserve = 0, notSuccessful = 0;
        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var batch in openIds.Chunk(1000))
            {
                var apps = await db.RecruitmentApplications.Where(a => batch.Contains(a.Id)).ToListAsync(ct);
                foreach (var app in apps)
                {
                    if (placementByApp.TryGetValue(app.Id, out var placement))
                    {
                        var result = resultByApp[app.Id];
                        app.RequisitionId = placement.RequisitionId;
                        app.CompetitionLineId = placement.LineId;
                        app.Status = RecruitmentApplicationStatus.Ranked;
                        app.Rank = result.OverallRank;
                        app.RankedAt = now;
                        app.AssessmentScore = result.FinalScore;
                        app.CriteriaVersionId = criteriaByLine[placement.LineId];
                        db.CompetitionAllocations.Add(new CompetitionAllocation
                        {
                            TenantId = competition.TenantId,
                            CompetitionId = competitionId,
                            RecruitmentApplicationId = app.Id,
                            AllocatedLineId = placement.LineId,
                            AllocatedRequisitionId = placement.RequisitionId,
                            PreferenceOrderSatisfied = placement.PreferenceOrder
                        });
                        db.RecruitmentApplicationEvents.Add(CompetitionEvents.New(competition.TenantId, app.Id,
                            RecruitmentApplicationEventTypes.CompetitionAllocated, placement.LineId, now));
                        allocated++;
                    }
                    else if (reserveApps.Contains(app.Id))
                    {
                        app.Status = RecruitmentApplicationStatus.ReserveListed;
                        app.ReserveUntil = reserveUntil;
                        app.ReserveListedAt = now;
                        app.AssessmentScore = resultByApp[app.Id].FinalScore;
                        db.RecruitmentApplicationEvents.Add(CompetitionEvents.New(competition.TenantId, app.Id,
                            RecruitmentApplicationEventTypes.CompetitionReserve, competitionId, now));
                        reserve++;
                    }
                    else
                    {
                        var note = resultByApp.TryGetValue(app.Id, out var r)
                            ? r.Outcome == CompetitionResultOutcome.Passed ? NoLineNote : NotSuccessfulNote
                            : NotAdmittedNote;
                        ApplicationRejection.Apply(app, RecruitmentRejectionKind.CompetitionResult, note, now);
                        db.RecruitmentApplicationEvents.Add(CompetitionEvents.New(competition.TenantId, app.Id,
                            RecruitmentApplicationEventTypes.CompetitionNotSuccessful, competitionId, now));
                        notSuccessful++;
                    }
                }
                db.ChangeTracker.DetectChanges();
                await db.SaveChangesAsync(ct);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is RecruitmentApplication or CompetitionAllocation or RecruitmentApplicationEvent).ToList())
                    entry.State = EntityState.Detached;
            }

            foreach (var batch in plan.Reserves.Chunk(1000))
            {
                db.CompetitionReserveEntries.AddRange(batch.Select(r => new CompetitionReserveEntry
                {
                    TenantId = competition.TenantId,
                    CompetitionLineId = r.LineId,
                    RecruitmentApplicationId = r.ApplicationId,
                    LineRank = r.LineRank,
                    ReserveUntil = reserveUntil
                }));
                db.ChangeTracker.DetectChanges();
                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        competition.Status = RecruitmentCompetitionStatus.Finalized;
        competition.FinalizedAt = now;
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = competition.TenantId,
            EntityType = CompetitionLifecycleService.EntityType,
            EntityId = competitionId,
            Action = "FinalizeCompetition",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new { allocated, reserve, notSuccessful, reserveUntil = reserveUntil.ToString("yyyy-MM-dd") })
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return new(true, null, allocated, reserve, notSuccessful);
    }
}
