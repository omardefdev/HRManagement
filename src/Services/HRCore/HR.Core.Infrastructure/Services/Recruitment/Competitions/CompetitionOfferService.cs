using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: result of one item of a bulk offer action (failures are listed, never silent).</summary>
public sealed record CompetitionOfferItemResult(Guid ApplicationId, string ApplicantFullName, string? RequisitionNumber, bool Success, string? Error, Guid? OfferId);

/// <summary>
/// HCM-031 (PLAN-31, P5, decision 7): bulk offers for the allocated candidates of a finalized competition. Each draft goes
/// through <see cref="RecruitmentOfferService.CreateDraftAsync"/> (eligibility, merit order, Payroll grade bounds) with the
/// line grade and the salary of <c>Recruitment:Competition:DefaultOfferSalary</c> (grade minimum or base, from the Payroll
/// grade lookup); each submission enters the existing per-offer approval. The optional batch approval workflow
/// (<c>RECRUITMENT-OFFER-BATCH-APPROVAL</c>) is a documented follow-up.
/// </summary>
public sealed class CompetitionOfferService(
    HRCoreDbContext db,
    RecruitmentOfferService offers,
    IPayrollGradeLookup gradeLookup,
    IOptions<RecruitmentOptions> options)
{
    public async Task<IReadOnlyList<CompetitionOfferItemResult>> CreateDraftsAsync(
        Guid competitionId,
        Guid? lineId,
        DateTime proposedStartDate,
        int? validityDays,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition?.Status != RecruitmentCompetitionStatus.Finalized)
            return [new(Guid.Empty, "—", null, false, "تُنشأ العروض بعد إنهاء المسابقة.", null)];

        var candidates = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.CompetitionId == competitionId
                && a.Status == RecruitmentApplicationStatus.Ranked
                && a.RequisitionId != null
                && a.ActiveOfferLetterId == null
                && (lineId == null || a.CompetitionLineId == lineId))
            .OrderBy(a => a.Rank)
            .Select(a => new
            {
                a.Id,
                a.ApplicantFullName,
                a.Requisition!.RequisitionNumber,
                Grade = a.CompetitionLine!.GradeCode
            })
            .ToListAsync(ct);

        var rule = options.Value.Competition.DefaultOfferSalary;
        var asOf = DateOnly.FromDateTime(proposedStartDate);
        var salaries = new Dictionary<string, (decimal? Salary, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var results = new List<CompetitionOfferItemResult>();
        foreach (var candidate in candidates)
        {
            if (!salaries.TryGetValue(candidate.Grade, out var salary))
            {
                salary = await gradeLookup.GetAsync(candidate.Grade, asOf, ct) switch
                {
                    PayrollGradeLookupResult.Found found => (rule == CompetitionOfferSalaryRule.GradeBase ? found.BaseSalary : found.MinSalary, null),
                    PayrollGradeLookupResult.NotFound => (null, $"الدرجة {candidate.Grade} غير معرّفة في الرواتب."),
                    PayrollGradeLookupResult.Unavailable unavailable => (null, $"تعذّر التحقق من سلم الرواتب: {unavailable.Reason}"),
                    _ => (null, "تعذّر تحديد الراتب.")
                };
                salaries[candidate.Grade] = salary;
            }
            if (salary.Salary is not decimal amount)
            {
                results.Add(new(candidate.Id, candidate.ApplicantFullName, candidate.RequisitionNumber, false, salary.Error, null));
                continue;
            }

            try
            {
                var (offer, error) = await offers.CreateDraftAsync(candidate.Id, amount, candidate.Grade, proposedStartDate, validityDays, actorUserId, ct: ct);
                results.Add(new(candidate.Id, candidate.ApplicantFullName, candidate.RequisitionNumber, offer is not null, error, offer?.Id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                results.Add(new(candidate.Id, candidate.ApplicantFullName, candidate.RequisitionNumber, false, "تعذّر إنشاء مسودة العرض.", null));
            }
        }

        Audit(competition.TenantId, competitionId, "BulkCreateOfferDrafts", actorUserId,
            new { lineId, rule = rule.ToString(), created = results.Count(r => r.Success), failed = results.Count(r => !r.Success) });
        await db.SaveChangesAsync(ct);
        return results;
    }

    /// <summary>Submits every draft offer of the competition (or line) into the existing per-offer approval.</summary>
    public async Task<IReadOnlyList<CompetitionOfferItemResult>> SubmitDraftsAsync(
        Guid competitionId, Guid? lineId, Guid? actorUserId, string? actorUserName, CancellationToken ct = default)
    {
        var drafts = await db.OfferLetters.AsNoTracking()
            .Where(o => o.Status == OfferStatus.Draft
                && o.RecruitmentApplication!.CompetitionId == competitionId
                && (lineId == null || o.RecruitmentApplication.CompetitionLineId == lineId))
            .OrderBy(o => o.RecruitmentApplication!.Rank)
            .Select(o => new { o.Id, o.RecruitmentApplicationId, o.RecruitmentApplication!.ApplicantFullName, o.RecruitmentApplication.Requisition!.RequisitionNumber, o.TenantId })
            .ToListAsync(ct);
        var results = new List<CompetitionOfferItemResult>();
        foreach (var draft in drafts)
        {
            var (ok, error) = await offers.SubmitForApprovalAsync(draft.Id, actorUserId, actorUserName, ct);
            results.Add(new(draft.RecruitmentApplicationId, draft.ApplicantFullName, draft.RequisitionNumber, ok, error, draft.Id));
        }
        if (drafts.Count > 0)
        {
            Audit(drafts[0].TenantId, competitionId, "BulkSubmitOffers", actorUserId,
                new { lineId, submitted = results.Count(r => r.Success), failed = results.Count(r => !r.Success) });
            await db.SaveChangesAsync(ct);
        }
        return results;
    }

    private void Audit(Guid tenantId, Guid competitionId, string action, Guid? actorUserId, object details) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = tenantId,
            EntityType = CompetitionLifecycleService.EntityType,
            EntityId = competitionId,
            Action = action,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(details)
        });
}
