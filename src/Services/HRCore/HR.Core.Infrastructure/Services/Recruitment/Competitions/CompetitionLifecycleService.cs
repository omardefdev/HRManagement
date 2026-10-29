using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: create/edit input of a competition (legal inputs from the approved reference, D-2).</summary>
public sealed record CompetitionInput(
    string? Title,
    string? LegalSourceId,
    string? ApplicationConditions,
    RecruitmentPublishingChannel PublishingChannel,
    DateOnly? AnnouncementDate,
    DateOnly? ApplicationClosingDate,
    CompetitionLegalInputs LegalInputs);

/// <summary>HCM-031: input of a competition line.</summary>
public sealed record CompetitionLineInput(
    string? Title,
    string? GradeCode,
    Guid? JobCatalogEntryId,
    Guid? OrgUnitId,
    decimal? LinePassMark);

/// <summary>HCM-031: an Approved, unpublished requisition that can be attached to a line.</summary>
public sealed record AttachableRequisition(Guid Id, string RequisitionNumber, string JobTitle, string GradeCode, string? OrgUnitName);

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31a): competition lifecycle — create and edit (Draft), lines, attach/detach requisitions,
/// submit for approval (<c>RECRUITMENT-COMPETITION-APPROVAL</c>), announce (one posting for the competition; attached
/// requisitions move to Published without postings of their own), close intake and cancel.
/// </summary>
public sealed class CompetitionLifecycleService(
    HRCoreDbContext db,
    ICodeGenerator codeGenerator,
    RecruitmentLegalSourceValidator legalSourceValidator,
    IWorkflowStarter workflowStarter,
    IOptions<RecruitmentOptions> options)
{
    public const string ApprovalWorkflowCode = "RECRUITMENT-COMPETITION-APPROVAL";
    public const string EntityType = nameof(RecruitmentCompetition);
    public const string NotFoundMessage = "المسابقة غير موجودة.";

    public async Task<(RecruitmentCompetition? Competition, string? Error)> CreateAsync(
        CompetitionInput input,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var error = await ValidateInputAsync(input, ct);
        if (error is not null)
            return (null, error);

        var competition = new RecruitmentCompetition
        {
            Number = (await codeGenerator.GenerateAsync(CodeGenerationRequests.RecruitmentCompetition(), ct)).Code,
            Status = RecruitmentCompetitionStatus.Draft
        };
        Apply(competition, input);
        db.RecruitmentCompetitions.Add(competition);
        Audit(competition, "CreateCompetition", actorUserId, new { competition.Number });
        await db.SaveChangesAsync(ct);
        return (competition, null);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        Guid competitionId,
        CompetitionInput input,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (false, NotFoundMessage);
        if (!CompetitionWorkflowRules.CanEdit(competition.Status))
            return (false, CompetitionWorkflowRules.NotEditableMessage);

        var error = await ValidateInputAsync(input, ct);
        if (error is not null)
            return (false, error);

        Apply(competition, input);
        Audit(competition, "UpdateCompetition", actorUserId, null);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(CompetitionLine? Line, string? Error)> AddLineAsync(
        Guid competitionId,
        CompetitionLineInput input,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (null, NotFoundMessage);
        if (!CompetitionWorkflowRules.CanEdit(competition.Status))
            return (null, CompetitionWorkflowRules.NotEditableMessage);

        var error = await ValidateLineAsync(input, ct);
        if (error is not null)
            return (null, error);

        var lineNo = (await db.CompetitionLines.Where(l => l.CompetitionId == competitionId)
            .Select(l => (int?)l.LineNo).MaxAsync(ct) ?? 0) + 1;
        var line = new CompetitionLine
        {
            TenantId = competition.TenantId,
            CompetitionId = competitionId,
            LineNo = lineNo
        };
        ApplyLine(line, input);
        db.CompetitionLines.Add(line);
        Audit(competition, "AddCompetitionLine", actorUserId, new { lineNo, line.GradeCode });
        await db.SaveChangesAsync(ct);
        return (line, null);
    }

    public async Task<(bool Success, string? Error)> UpdateLineAsync(
        Guid lineId,
        CompetitionLineInput input,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var line = await db.CompetitionLines.Include(l => l.Competition).Include(l => l.Requisitions)
            .FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line?.Competition is null)
            return (false, NotFoundMessage);
        if (!CompetitionWorkflowRules.CanEdit(line.Competition.Status))
            return (false, CompetitionWorkflowRules.NotEditableMessage);

        var error = await ValidateLineAsync(input, ct);
        if (error is not null)
            return (false, error);
        if (line.Requisitions.Count > 0
            && (!string.Equals(line.GradeCode, input.GradeCode?.Trim(), StringComparison.OrdinalIgnoreCase)
                || line.JobCatalogEntryId != input.JobCatalogEntryId))
            return (false, "افصل طلبات الشواغر عن السطر قبل تغيير درجته أو وظيفته.");

        ApplyLine(line, input);
        Audit(line.Competition, "UpdateCompetitionLine", actorUserId, new { line.LineNo });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RemoveLineAsync(Guid lineId, Guid? actorUserId, CancellationToken ct = default)
    {
        var line = await db.CompetitionLines.Include(l => l.Competition).Include(l => l.Requisitions)
            .FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line?.Competition is null)
            return (false, NotFoundMessage);
        if (!CompetitionWorkflowRules.CanEdit(line.Competition.Status))
            return (false, CompetitionWorkflowRules.NotEditableMessage);

        foreach (var requisition in line.Requisitions)
            requisition.CompetitionLineId = null;
        db.CompetitionLines.Remove(line);

        // Keep the line numbers dense (1..n) for the candidate form.
        var later = await db.CompetitionLines
            .Where(l => l.CompetitionId == line.CompetitionId && l.LineNo > line.LineNo)
            .OrderBy(l => l.LineNo)
            .ToListAsync(ct);
        Audit(line.Competition, "RemoveCompetitionLine", actorUserId, new { line.LineNo });
        await db.SaveChangesAsync(ct);
        foreach (var next in later)
        {
            next.LineNo--;
            await db.SaveChangesAsync(ct);
        }
        return (true, null);
    }

    /// <summary>Approved, unpublished, unattached requisitions whose funded position grade (and job, when set) matches the line.</summary>
    public async Task<IReadOnlyList<AttachableRequisition>> GetAttachableRequisitionsAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await db.CompetitionLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null)
            return [];

        var grade = line.GradeCode.Trim();
        var query = db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.Status == RequisitionStatus.Approved
                && r.CompetitionLineId == null
                && r.PublishedAt == null
                && r.PublicJobPostingId == null
                && r.InternalVacancyId == null
                && r.FundedPosition != null
                && r.FundedPosition.GradeCode == grade);
        if (line.JobCatalogEntryId is Guid jobId)
            query = query.Where(r => r.FundedPosition!.JobCatalogEntryId == jobId);

        return await query
            .OrderBy(r => r.RequisitionNumber)
            .Select(r => new AttachableRequisition(
                r.Id,
                r.RequisitionNumber,
                r.JobTitle,
                r.FundedPosition!.GradeCode,
                r.FundedPosition.OrgUnit != null ? r.FundedPosition.OrgUnit.NameAr : null))
            .ToListAsync(ct);
    }

    public async Task<(bool Success, string? Error)> AttachRequisitionAsync(
        Guid lineId,
        Guid requisitionId,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var line = await db.CompetitionLines.Include(l => l.Competition).FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line?.Competition is null)
            return (false, NotFoundMessage);
        if (!CompetitionWorkflowRules.CanEdit(line.Competition.Status))
            return (false, CompetitionWorkflowRules.NotEditableMessage);

        var requisition = await db.RecruitmentRequisitions.Include(r => r.FundedPosition)
            .FirstOrDefaultAsync(r => r.Id == requisitionId, ct);
        if (requisition is null)
            return (false, "طلب الشاغر غير موجود.");

        var error = CompetitionWorkflowRules.ValidateAttach(
            new CompetitionAttachCandidate(
                requisition.Status,
                requisition.PublishedAt,
                requisition.PublicJobPostingId,
                requisition.InternalVacancyId,
                requisition.CompetitionLineId,
                requisition.FundedPosition?.GradeCode,
                requisition.FundedPosition?.JobCatalogEntryId),
            line);
        if (error is not null)
            return (false, error);

        requisition.CompetitionLineId = line.Id;
        line.RequiredCount = await db.RecruitmentRequisitions.CountAsync(r => r.CompetitionLineId == line.Id, ct) + 1;
        Audit(line.Competition, "AttachRequisition", actorUserId, new { line.LineNo, requisition.RequisitionNumber });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DetachRequisitionAsync(
        Guid requisitionId,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var requisition = await db.RecruitmentRequisitions.FirstOrDefaultAsync(r => r.Id == requisitionId, ct);
        if (requisition?.CompetitionLineId is not Guid lineId)
            return (false, "طلب الشاغر غير مربوط بمسابقة.");
        var line = await db.CompetitionLines.Include(l => l.Competition).FirstAsync(l => l.Id == lineId, ct);
        if (!CompetitionWorkflowRules.CanEdit(line.Competition!.Status))
            return (false, CompetitionWorkflowRules.NotEditableMessage);

        requisition.CompetitionLineId = null;
        line.RequiredCount = Math.Max(0, await db.RecruitmentRequisitions.CountAsync(r => r.CompetitionLineId == line.Id, ct) - 1);
        Audit(line.Competition, "DetachRequisition", actorUserId, new { line.LineNo, requisition.RequisitionNumber });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Draft → PendingApproval: every line has a requisition and every attached requisition an active criteria version with
    /// an Exam item, one criteria shape for the whole competition (J2). Freezes each line's criteria version and starts
    /// <see cref="ApprovalWorkflowCode"/>.
    /// </summary>
    public async Task<(bool Success, string? Error)> SubmitForApprovalAsync(
        Guid competitionId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions
            .Include(c => c.Lines).ThenInclude(l => l.Requisitions)
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (false, NotFoundMessage);
        if (competition.Status != RecruitmentCompetitionStatus.Draft)
            return (false, "يمكن تقديم المسابقة للاعتماد وهي مسودة فقط.");

        var legalError = await legalSourceValidator.ValidateRequiredAsync(competition.LegalSourceId, ct);
        if (legalError is not null)
            return (false, legalError);

        var requisitionIds = competition.Lines.SelectMany(l => l.Requisitions).Select(r => r.Id).ToList();
        var activeCriteria = await db.RecruitmentAssessmentCriteria
            .Where(c => requisitionIds.Contains(c.RequisitionId) && c.IsActive)
            .ToListAsync(ct);
        var criteriaByRequisition = activeCriteria.ToDictionary(c => c.RequisitionId);

        var views = competition.Lines
            .OrderBy(l => l.LineNo)
            .Select(l => new CompetitionLineSubmitView(
                l.LineNo,
                l.Title,
                l.Requisitions.Count,
                l.Requisitions
                    .OrderBy(r => r.RequisitionNumber)
                    .Select(r => criteriaByRequisition.TryGetValue(r.Id, out var c)
                        ? (IReadOnlyList<AssessmentCriteriaItem>?)AssessmentCriteriaSerializer.Deserialize(c.CriteriaJson)
                        : null)
                    .ToList()))
            .ToList();
        var error = CompetitionWorkflowRules.ValidateSubmit(views, LegalInputsOf(competition));
        if (error is not null)
            return (false, error);

        foreach (var line in competition.Lines)
        {
            line.RequiredCount = line.Requisitions.Count;
            var first = line.Requisitions.OrderBy(r => r.RequisitionNumber).First();
            line.CriteriaVersionId = criteriaByRequisition[first.Id].Id;
        }

        competition.Status = RecruitmentCompetitionStatus.PendingApproval;
        competition.ReturnReason = null;
        var contextJson = JsonSerializer.Serialize(new { competitionId = competition.Id, competition.Number });
        competition.ApprovalWorkflowInstanceId = await workflowStarter.StartByWorkflowCodeAsync(
            ApprovalWorkflowCode,
            EntityType,
            competition.Id,
            competition.TenantId,
            actorUserId,
            actorUserName,
            contextJson,
            PolicyDomains.CoreRecruitment,
            ct);
        if (competition.ApprovalWorkflowInstanceId is null)
            return (false, "تعذّر بدء مسار اعتماد المسابقة: تعريف المسار غير منشور.");

        Audit(competition, "SubmitCompetition", actorUserId, new { lines = competition.Lines.Count, vacancies = requisitionIds.Count });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Approved → Announced: one public posting for the competition (and an internal vacancy when the channel includes
    /// internal); attached requisitions move to Published with the competition's closing date but no posting of their own.
    /// </summary>
    public async Task<(bool Success, string? Error)> AnnounceAsync(Guid competitionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions
            .Include(c => c.Lines).ThenInclude(l => l.Requisitions).ThenInclude(r => r.FundedPosition)
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (false, NotFoundMessage);
        if (!CompetitionWorkflowRules.CanTransition(competition.Status, RecruitmentCompetitionStatus.Announced))
            return (false, "يمكن إعلان المسابقة بعد اعتمادها فقط.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dateError = RequisitionWorkflowRules.ValidateClosingDateForPublish(
            competition.ApplicationClosingDate, isFirstPublish: true, today, options.Value.MinPostingDays);
        if (dateError is not null)
            return (false, dateError);

        var requisitions = competition.Lines.SelectMany(l => l.Requisitions).ToList();
        var notReady = requisitions.FirstOrDefault(r =>
            r.Status != RequisitionStatus.Approved || r.PublicJobPostingId is not null || r.InternalVacancyId is not null
            || r.FundedPosition?.Status != FundedPositionStatus.Vacant);
        if (notReady is not null)
            return (false, $"طلب الشاغر {notReady.RequisitionNumber} لم يعد معتمداً على منصب شاغر دون إعلان.");

        var now = DateTime.UtcNow;
        if (RequisitionWorkflowRules.AllowsPublicChannel(competition.PublishingChannel))
        {
            var posting = new PublicJobPosting
            {
                TenantId = competition.TenantId,
                Title = competition.Title,
                Description = DescribeLines(competition),
                Requirements = competition.ApplicationConditions,
                ClosingDate = competition.ApplicationClosingDate,
                IsPublished = true,
                RequisitionId = null
            };
            db.PublicJobPostings.Add(posting);
            competition.PublicJobPosting = posting;
            competition.PublicJobPostingId = posting.Id;
        }
        if (RequisitionWorkflowRules.AllowsInternalChannel(competition.PublishingChannel))
        {
            var vacancy = new InternalVacancy
            {
                TenantId = competition.TenantId,
                Title = competition.Title,
                Description = DescribeLines(competition),
                RequiredQualifications = competition.ApplicationConditions,
                ClosingDate = competition.ApplicationClosingDate,
                IsOpen = true,
                RequisitionId = null,
                IsDerivedFromRequisition = false
            };
            db.InternalVacancies.Add(vacancy);
            competition.InternalVacancy = vacancy;
            competition.InternalVacancyId = vacancy.Id;
        }

        foreach (var requisition in requisitions)
        {
            requisition.Status = RequisitionStatus.Published;
            requisition.PublishedAt ??= now;
            requisition.ApplicationClosingDate = competition.ApplicationClosingDate;
        }

        competition.Status = RecruitmentCompetitionStatus.Announced;
        competition.AnnouncedAt = now;
        competition.AnnouncementDate ??= today;
        Audit(competition, "AnnounceCompetition", actorUserId, new { vacancies = requisitions.Count, channel = competition.PublishingChannel.ToString() });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Announced → IntakeClosed (manual, or by the intake-close job after the closing date). Idempotent.</summary>
    public async Task<(bool Success, string? Error)> CloseIntakeAsync(Guid competitionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (false, NotFoundMessage);
        if (competition.Status != RecruitmentCompetitionStatus.Announced)
            return (false, "يمكن إغلاق التقديم على مسابقة معلنة فقط.");

        await StageIntakeCloseAsync(db, competition, DateTime.UtcNow, ct);
        Audit(competition, "CloseCompetitionIntake", actorUserId, null);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Stages (no save) the intake close of an announced competition: posting and vacancy closed, requisitions marked.</summary>
    internal static async Task StageIntakeCloseAsync(HRCoreDbContext db, RecruitmentCompetition competition, DateTime nowUtc, CancellationToken ct)
    {
        await CloseChannelsAsync(db, competition, ct);
        var lineIds = await db.CompetitionLines.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(l => l.TenantId == competition.TenantId && l.CompetitionId == competition.Id).Select(l => l.Id).ToListAsync(ct);
        var requisitions = await db.RecruitmentRequisitions.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(r => r.TenantId == competition.TenantId && r.CompetitionLineId != null && lineIds.Contains(r.CompetitionLineId.Value) && r.IntakeClosedAt == null)
            .ToListAsync(ct);
        foreach (var requisition in requisitions)
            requisition.IntakeClosedAt = nowUtc;
        competition.Status = RecruitmentCompetitionStatus.IntakeClosed;
        competition.IntakeClosedAt = nowUtc;
    }

    /// <summary>
    /// Cancels a competition before it is finalized (reason ≥ 10 characters). The posting and vacancy are closed, open
    /// competition applications are rejected (<c>RequisitionCancelled</c>, «إلغاء المسابقة»), and the attached requisitions
    /// stay attached for history but are released: they can be closed or cancelled on their own.
    /// </summary>
    public async Task<(bool Success, string? Error)> CancelAsync(
        Guid competitionId,
        string? reason,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (false, NotFoundMessage);
        if (!CompetitionWorkflowRules.CanCancel(competition.Status))
            return (false, "لا يمكن إلغاء مسابقة منتهية أو ملغاة.");
        var trimmed = reason?.Trim();
        if (trimmed is null || trimmed.Length < CompetitionWorkflowRules.MinCancellationReasonLength)
            return (false, $"سبب الإلغاء مطلوب ({CompetitionWorkflowRules.MinCancellationReasonLength} أحرف على الأقل).");
        if (trimmed.Length > 500)
            return (false, "سبب الإلغاء يجب ألا يتجاوز 500 حرف.");

        var now = DateTime.UtcNow;
        await CloseChannelsAsync(db, competition, ct);
        var terminal = SelectionWorkflowRules.TerminalStatuses.ToList();
        var open = await db.RecruitmentApplications
            .Where(a => a.CompetitionId == competition.Id && !terminal.Contains(a.Status))
            .ToListAsync(ct);
        foreach (var application in open)
            ApplicationRejection.Apply(application, RecruitmentRejectionKind.RequisitionCancelled, "إلغاء المسابقة", now);

        competition.Status = RecruitmentCompetitionStatus.Cancelled;
        competition.CancelledAt = now;
        competition.CancellationReason = trimmed;
        Audit(competition, "CancelCompetition", actorUserId, new { reason = trimmed, rejectedApplications = open.Count });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// The requisition guard used by the requisition lifecycle (publish, close, cancel): an error while the requisition
    /// is attached to a competition that still holds it, else null.
    /// </summary>
    public static async Task<string?> AttachedRequisitionBlockAsync(HRCoreDbContext db, Guid requisitionId, CancellationToken ct)
    {
        var status = await db.RecruitmentRequisitions
            .Where(r => r.Id == requisitionId && r.CompetitionLineId != null)
            .Select(r => (RecruitmentCompetitionStatus?)r.CompetitionLine!.Competition!.Status)
            .FirstOrDefaultAsync(ct);
        return status is RecruitmentCompetitionStatus s && CompetitionWorkflowRules.HoldsRequisitions(s)
            ? CompetitionWorkflowRules.AttachedRequisitionChangeMessage
            : null;
    }

    public static CompetitionLegalInputs LegalInputsOf(RecruitmentCompetition competition) => new(
        competition.ObjectionWindowDays,
        competition.ReserveValidityMonths,
        competition.MaxLinesPerCandidate,
        competition.PassMark,
        competition.ReconcileThreshold,
        competition.ReconcileRule,
        CompetitionWorkflowRules.ParseTieBreakOrder(competition.TieBreakOrder));

    private static async Task CloseChannelsAsync(HRCoreDbContext db, RecruitmentCompetition competition, CancellationToken ct)
    {
        if (competition.PublicJobPostingId is Guid postingId)
        {
            var posting = await db.PublicJobPostings.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
                .FirstOrDefaultAsync(p => p.TenantId == competition.TenantId && p.Id == postingId, ct);
            if (posting is not null)
                posting.IsPublished = false;
        }
        if (competition.InternalVacancyId is Guid vacancyId)
        {
            var vacancy = await db.InternalVacancies.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
                .FirstOrDefaultAsync(v => v.TenantId == competition.TenantId && v.Id == vacancyId, ct);
            if (vacancy is not null)
                vacancy.IsOpen = false;
        }
    }

    private async Task<string?> ValidateInputAsync(CompetitionInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return "عنوان المسابقة مطلوب.";
        if (input.Title.Trim().Length > 200)
            return "عنوان المسابقة يجب ألا يتجاوز 200 حرف.";
        if (input.ApplicationConditions is { Length: > 2000 })
            return "شروط التقديم يجب ألا تتجاوز 2000 حرف.";
        var legalError = await legalSourceValidator.ValidateRequiredAsync(
            input.LegalSourceId, "المرجع القانوني للمسابقة مطلوب (يُختار من سجل التشريعات).", ct);
        if (legalError is not null)
            return legalError;
        if (input.ApplicationClosingDate is null)
            return "تاريخ إغلاق التقديم مطلوب.";
        if (input.AnnouncementDate is DateOnly announced && announced > input.ApplicationClosingDate)
            return "تاريخ الإعلان يجب أن يسبق تاريخ إغلاق التقديم.";
        if (!Enum.IsDefined(input.PublishingChannel))
            return "قناة النشر غير معروفة.";
        return CompetitionWorkflowRules.ValidateLegalInputs(input.LegalInputs);
    }

    private async Task<string?> ValidateLineAsync(CompetitionLineInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return "عنوان السطر مطلوب.";
        if (input.Title.Trim().Length > 200)
            return "عنوان السطر يجب ألا يتجاوز 200 حرف.";
        if (string.IsNullOrWhiteSpace(input.GradeCode))
            return "درجة السطر مطلوبة.";
        if (input.GradeCode.Trim().Length > 20)
            return "رمز الدرجة يجب ألا يتجاوز 20 حرفاً.";
        if (input.JobCatalogEntryId is Guid jobId
            && !await db.JobCatalogEntries.AnyAsync(j => j.Id == jobId, ct))
            return "الوظيفة المختارة غير موجودة في الدليل.";
        if (input.OrgUnitId is Guid orgId && !await db.OrgUnits.AnyAsync(o => o.Id == orgId, ct))
            return "الوحدة التنظيمية غير موجودة.";
        return CompetitionWorkflowRules.ValidateLinePassMark(input.LinePassMark);
    }

    private static void Apply(RecruitmentCompetition competition, CompetitionInput input)
    {
        var legal = input.LegalInputs;
        competition.Title = input.Title!.Trim();
        competition.LegalSourceId = input.LegalSourceId!.Trim();
        competition.ApplicationConditions = string.IsNullOrWhiteSpace(input.ApplicationConditions) ? null : input.ApplicationConditions.Trim();
        competition.PublishingChannel = input.PublishingChannel;
        competition.AnnouncementDate = input.AnnouncementDate;
        competition.ApplicationClosingDate = input.ApplicationClosingDate!.Value;
        competition.ObjectionWindowDays = legal.ObjectionWindowDays!.Value;
        competition.ReserveValidityMonths = legal.ReserveValidityMonths!.Value;
        competition.MaxLinesPerCandidate = legal.MaxLinesPerCandidate!.Value;
        competition.PassMark = legal.PassMark!.Value;
        competition.ReconcileThreshold = legal.ReconcileThreshold!.Value;
        competition.ReconcileRule = legal.ReconcileRule!.Value;
        competition.TieBreakOrder = CompetitionWorkflowRules.FormatTieBreakOrder(legal.TieBreakOrder!);
    }

    private static void ApplyLine(CompetitionLine line, CompetitionLineInput input)
    {
        line.Title = input.Title!.Trim();
        line.GradeCode = input.GradeCode!.Trim();
        line.JobCatalogEntryId = input.JobCatalogEntryId;
        line.OrgUnitId = input.OrgUnitId;
        line.LinePassMark = input.LinePassMark;
    }

    private static string DescribeLines(RecruitmentCompetition competition)
    {
        var text = new StringBuilder();
        foreach (var line in competition.Lines.OrderBy(l => l.LineNo))
            text.AppendLine($"السطر {line.LineNo}: {line.Title} — الدرجة {line.GradeCode} — عدد الشواغر {line.Requisitions.Count}");
        return text.ToString().TrimEnd();
    }

    private void Audit(RecruitmentCompetition competition, string action, Guid? actorUserId, object? details) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = competition.TenantId,
            EntityType = EntityType,
            EntityId = competition.Id,
            Action = action,
            UserId = actorUserId,
            ChangedFields = details is null ? null : JsonSerializer.Serialize(details)
        });
}
