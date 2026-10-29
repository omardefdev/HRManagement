using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Web.Controllers;

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31a): HR screens of multi-vacancy competitions — read with <c>HrCoreReadOnly</c>, change
/// with <c>HrCoreWrite</c>, approve through the workflow (<c>RECRUITMENT-COMPETITION-APPROVAL</c>).
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public sealed class CompetitionsController(
    HRCoreDbContext db,
    CompetitionLifecycleService lifecycle,
    CompetitionIntakeService intake,
    ExamSeatingService seating,
    ExamMarkingService marking,
    ExamAdmissionCardDocumentService cards,
    CompetitionResultsService results,
    CompetitionObjectionService objections,
    CompetitionAllocationService allocation,
    CompetitionOfferService competitionOffers,
    IWorkflowInstanceActionService workflowActions,
    ILegislationRegisterProvider legislationProvider,
    IAuthorizationService authorization,
    IOptions<RecruitmentOptions> options) : Controller
{
    public async Task<IActionResult> Index(RecruitmentCompetitionStatus? status, CancellationToken ct)
    {
        var query = db.RecruitmentCompetitions.AsNoTracking();
        if (status is not null)
            query = query.Where(c => c.Status == status);
        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CompetitionIndexRow(
                c.Id,
                c.Number,
                c.Title,
                c.Status,
                c.ApplicationClosingDate,
                c.Lines.Count,
                c.Lines.Sum(l => l.RequiredCount)))
            .Take(200)
            .ToListAsync(ct);
        return View(new CompetitionIndexViewModel
        {
            Rows = rows,
            Status = status,
            CanWrite = await CanAsync(HrManagementPolicies.HrCoreWrite)
        });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var defaults = options.Value.Competition.Defaults;
        var vm = new CompetitionFormViewModel
        {
            ApplicationClosingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Math.Max(7, options.Value.MinPostingDays) + 7),
            ObjectionWindowDays = defaults.ObjectionWindowDays,
            ReserveValidityMonths = defaults.ReserveValidityMonths,
            MaxLinesPerCandidate = defaults.MaxLinesPerCandidate,
            PassMark = defaults.PassMark,
            ReconcileThreshold = defaults.ReconcileThreshold,
            ReconcileRule = defaults.ReconcileRule,
            TieBreakOrder = defaults.TieBreakOrder?.Select(t => (CompetitionTieBreak?)t).ToList() ?? [],
            PrefilledFromDefaults = !defaults.IsEmpty
        };
        await PopulateFormListsAsync(ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(CompetitionFormViewModel vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var (competition, error) = await lifecycle.CreateAsync(vm.ToInput(), ParseUserId(User), ct);
            if (competition is not null)
            {
                TempData["Success"] = $"تم إنشاء المسابقة {competition.Number} كمسودة.";
                return RedirectToAction(nameof(Details), new { id = competition.Id });
            }
            ModelState.AddModelError(string.Empty, error!);
        }
        await PopulateFormListsAsync(ct);
        return View(vm);
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var competition = await db.RecruitmentCompetitions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null) return NotFound();
        if (!CompetitionWorkflowRules.CanEdit(competition.Status))
        {
            TempData["Error"] = CompetitionWorkflowRules.NotEditableMessage;
            return RedirectToAction(nameof(Details), new { id });
        }
        await PopulateFormListsAsync(ct);
        return View(CompetitionFormViewModel.From(competition));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, CompetitionFormViewModel vm, CancellationToken ct)
    {
        if (vm.Id != id) return BadRequest();
        if (ModelState.IsValid)
        {
            var (success, error) = await lifecycle.UpdateAsync(id, vm.ToInput(), ParseUserId(User), ct);
            if (success)
            {
                TempData["Success"] = "تم حفظ بيانات المسابقة.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(string.Empty, error!);
        }
        await PopulateFormListsAsync(ct);
        return View(vm);
    }

    public async Task<IActionResult> Details(Guid id, int page = 1, RecruitmentApplicationStatus? applicantStatus = null, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.AsNoTracking()
            .Include(c => c.Lines).ThenInclude(l => l.JobCatalogEntry)
            .Include(c => c.Lines).ThenInclude(l => l.OrgUnit)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null) return NotFound();

        var lineIds = competition.Lines.Select(l => l.Id).ToList();
        var requisitions = await db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.CompetitionLineId != null && lineIds.Contains(r.CompetitionLineId.Value))
            .OrderBy(r => r.RequisitionNumber)
            .Select(r => new
            {
                LineId = r.CompetitionLineId!.Value,
                Row = new CompetitionLineRequisitionRow(
                    r.Id, r.RequisitionNumber, r.JobTitle, r.Status,
                    r.FundedPosition != null && r.FundedPosition.OrgUnit != null ? r.FundedPosition.OrgUnit.NameAr : null)
            })
            .ToListAsync(ct);

        var editable = CompetitionWorkflowRules.CanEdit(competition.Status);
        var lines = new List<CompetitionLineRow>();
        foreach (var line in competition.Lines.OrderBy(l => l.LineNo))
        {
            lines.Add(new CompetitionLineRow(
                line,
                requisitions.Where(r => r.LineId == line.Id).Select(r => r.Row).ToList(),
                editable ? await lifecycle.GetAttachableRequisitionsAsync(line.Id, ct) : []));
        }

        var latest = await db.CompetitionResultVersions.AsNoTracking()
            .Where(v => v.CompetitionId == id)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);
        var legalSource = await legislationProvider.GetByIdAsync(competition.LegalSourceId, ct);
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, competition.ApprovalWorkflowInstanceId, ct);
        var workflow = WorkflowPanelBuilder.Build(
            instance,
            definition,
            User,
            WorkflowPanelBuilder.ParseUserId(User),
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = competition.Status == RecruitmentCompetitionStatus.PendingApproval,
                EntityCreatedBy = competition.CreatedBy,
                CreatorSoDMessage = "لا يمكن للمنشئ اعتماد مسابقته الخاصة."
            });

        return View(new CompetitionDetailsViewModel
        {
            Competition = competition,
            LegalSourceLabel = legalSource?.DisplayLabel ?? competition.LegalSourceId,
            Lines = lines,
            TieBreakOrder = CompetitionWorkflowRules.ParseTieBreakOrder(competition.TieBreakOrder),
            Workflow = workflow,
            CanWrite = await CanAsync(HrManagementPolicies.HrCoreWrite),
            CanApprove = await CanAsync(HrManagementPolicies.HrCoreApprove),
            ApplicationCount = await db.RecruitmentApplications.CountAsync(a => a.CompetitionId == id, ct),
            Applicants = await LoadApplicantsAsync(id, page, applicantStatus, ct),
            BulkResults = ReadBulkResults(),
            ExamSessions = await LoadExamSessionsAsync(id, ct),
            ResultVersions = await LoadResultVersionsAsync(id, ct),
            LatestVersion = latest,
            Results = latest is null ? [] : await LoadResultsAsync(latest.Id, ct),
            ResultsWorkflow = latest?.WorkflowInstanceId is null ? null : await BuildResultsWorkflowAsync(latest, ct),
            Objections = await objections.ListAsync(id, ct),
            Allocations = await LoadAllocationsAsync(id, ct),
            Reserves = await db.CompetitionReserveEntries.AsNoTracking()
                .Where(e => e.CompetitionLine!.CompetitionId == id)
                .OrderBy(e => e.CompetitionLine!.LineNo).ThenBy(e => e.LineRank)
                .Select(e => new ReserveRow(e.CompetitionLineId, e.CompetitionLine!.LineNo, e.LineRank, e.RecruitmentApplication!.ApplicantFullName, e.ReserveUntil, e.Status))
                .Take(2000)
                .ToListAsync(ct),
            RecallLog = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == CompetitionLifecycleService.EntityType && a.EntityId == id
                    && (a.Action == CompetitionReserveService.RecallAuditAction || a.Action == CompetitionReserveService.ExhaustedAuditAction))
                .OrderByDescending(a => a.CreatedAt)
                .Take(100)
                .Select(a => new RecallLogRow(a.CreatedAt, a.Action, a.ChangedFields))
                .ToListAsync(ct),
            OfferResults = TempData["OfferResults"] is string offerJson
                ? System.Text.Json.JsonSerializer.Deserialize<List<CompetitionOfferItemResult>>(offerJson) ?? []
                : [],
            CanFinalize = competition.Status == RecruitmentCompetitionStatus.ResultsPublished
                && CompetitionWorkflowRules.ObjectionDeadlineUtc(competition.ResultsFirstPublishedAt, competition.ObjectionWindowDays) is DateTime finalizeAfter
                && DateTime.UtcNow > finalizeAfter,
            ObjectionDeadlineUtc = CompetitionWorkflowRules.ObjectionDeadlineUtc(competition.ResultsFirstPublishedAt, competition.ObjectionWindowDays),
            MarkerCandidates = await db.Employees.AsNoTracking()
                .OrderBy(e => e.FullName)
                .Take(500)
                .Select(e => new SelectListItem(e.FullName, e.Id.ToString()))
                .ToListAsync(ct)
        });
    }

    // ---- HCM-031 (P3): exams ---------------------------------------------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AddExamSession(
        Guid id, ExamSessionType type, string? title, DateTime? scheduledAt, int durationMinutes, string? venue, int capacity, decimal maxScore,
        CancellationToken ct)
    {
        var (_, error) = await seating.CreateSessionAsync(
            id, new ExamSessionInput(type, title, scheduledAt?.ToUniversalTime(), durationMinutes, venue, capacity, maxScore), ParseUserId(User), ct);
        return Result((error is null, error), id, "تمت إضافة جلسة الامتحان.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RemoveExamSession(Guid id, Guid sessionId, CancellationToken ct) =>
        Result(await seating.RemoveSessionAsync(sessionId, ParseUserId(User), ct), id, "تم حذف جلسة الامتحان.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AssignMarker(Guid id, Guid sessionId, Guid? employeeId, ExamMarkerRole role, CancellationToken ct)
    {
        if (employeeId is not Guid employee)
            return Result((false, "اختر الموظف المصحح."), id, null);
        return Result(await seating.AssignMarkerAsync(sessionId, employee, role, ParseUserId(User), ct), id, "تم تكليف المصحح.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RemoveMarker(Guid id, Guid assignmentId, CancellationToken ct) =>
        Result(await seating.RemoveMarkerAsync(assignmentId, ParseUserId(User), ct), id, "تم إلغاء تكليف المصحح.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Seat(Guid id, CancellationToken ct)
    {
        var result = await seating.SeatAsync(id, ParseUserId(User), ct);
        return Result((result.Success, result.Error), id, $"تم توزيع {result.Seated} متقدماً على القاعات.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> IssueAdmissionCards(Guid id, CancellationToken ct)
    {
        var (ok, error, issued) = await seating.IssueAdmissionCardsAsync(id, ParseUserId(User), ct);
        return Result((ok, error), id, $"صدرت {issued} بطاقة امتحان.");
    }

    public async Task<IActionResult> ExamSession(Guid id, CancellationToken ct)
    {
        var session = await db.ExamSessions.AsNoTracking().Include(s => s.Competition).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return NotFound();
        var candidates = await db.ExamCandidates.AsNoTracking()
            .Where(c => c.ExamSessionId == id)
            .OrderBy(c => c.SeatNumber)
            .Select(c => new ExamCandidateRow(c.Id, c.ExamNumber, c.SeatNumber, c.RecruitmentApplication!.ApplicantFullName,
                c.Attendance, c.AdmissionCardIssuedAt != null, c.FinalMark))
            .ToListAsync(ct);
        return View(new ExamSessionDetailsViewModel
        {
            Session = session,
            Competition = session.Competition!,
            Candidates = candidates,
            CanWrite = await CanAsync(HrManagementPolicies.HrCoreWrite)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SetAttendance(Guid id, List<Guid>? present, List<Guid>? absent, CancellationToken ct)
    {
        var map = new Dictionary<Guid, ExamAttendance>();
        foreach (var candidateId in present ?? []) map[candidateId] = ExamAttendance.Present;
        foreach (var candidateId in absent ?? []) map[candidateId] = ExamAttendance.Absent;
        var (ok, error) = map.Count == 0 ? (false, "اختر الحضور أو الغياب لمتقدم واحد على الأقل.") : await seating.SetAttendanceAsync(id, map, ParseUserId(User), ct);
        TempData[ok ? "Success" : "Error"] = ok ? "تم تسجيل الحضور." : error;
        return RedirectToAction(nameof(ExamSession), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> MarkSessionHeld(Guid id, CancellationToken ct)
    {
        var (ok, error) = await seating.MarkHeldAsync(id, ParseUserId(User), ct);
        TempData[ok ? "Success" : "Error"] = ok ? "اعتُمد انعقاد الجلسة وفُتح التصحيح." : error;
        return RedirectToAction(nameof(ExamSession), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CloseMarking(Guid id, CancellationToken ct)
    {
        var (ok, error) = await marking.CloseMarkingAsync(id, ParseUserId(User), ct);
        TempData[ok ? "Success" : "Error"] = ok ? "أُغلق التصحيح وثُبّتت العلامات." : error;
        return RedirectToAction(nameof(ExamSession), new { id });
    }

    /// <summary>HCM-031 (P3): admission card PDF for HR (issued cards only).</summary>
    public async Task<IActionResult> AdmissionCard(Guid id, CancellationToken ct)
    {
        var card = await cards.GetAsync(id, ct);
        if (card is null) return NotFound();
        if (!card.IsIssued)
        {
            TempData["Error"] = ExamAdmissionCardDocumentService.NotIssuedMessage;
            return RedirectToAction(nameof(Index));
        }
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "Competitions/AdmissionCard");
        return File(HR.Core.Web.Services.ExamAdmissionCardDocument.Generate(card), "application/pdf", $"admission-card-{card.Data.ExamNumber}.pdf");
    }

    // ---- HCM-031 (P4): results and objections ------------------------------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ComputeResults(Guid id, string? reason, CancellationToken ct)
    {
        var (version, error) = await results.ComputeDraftAsync(id, reason, ParseUserId(User), ct);
        return Result((version is not null, error), id, $"تم احتساب مسودة النتائج (النسخة {version?.VersionNo}).");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ResolveTie(Guid id, Guid versionId, List<Guid>? applicationIds, List<int>? positions, string? reason, CancellationToken ct)
    {
        if (applicationIds is not { Count: > 1 } || positions is null || positions.Count != applicationIds.Count)
            return Result((false, "حدد ترتيب كل أفراد مجموعة التعادل."), id, null);
        var ordered = applicationIds.Zip(positions).OrderBy(p => p.Second).Select(p => p.First).ToList();
        return Result(await results.ResolveTieAsync(versionId, ordered, reason, ParseUserId(User), ct), id, "تم تسجيل قرار التعادل.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SubmitResults(Guid id, Guid versionId, CancellationToken ct) =>
        Result(await results.SubmitForApprovalAsync(versionId, ParseUserId(User), UserName(User), ct), id, "قُدّمت النتائج للاعتماد.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> ApproveResultsWorkflow(Guid id, Guid versionId, string? comments, CancellationToken ct)
    {
        var instanceId = await db.CompetitionResultVersions.Where(v => v.Id == versionId && v.CompetitionId == id)
            .Select(v => v.WorkflowInstanceId).FirstOrDefaultAsync(ct);
        if (instanceId is not Guid workflowId)
            return Result((false, "لا يوجد مسار اعتماد مرتبط بنسخة النتائج."), id, null);
        var result = await workflowActions.ApproveAsync(workflowId, CreateWorkflowActor(User), comments, targetStepLevel: null, ct);
        return Result((result.Success, result.Error ?? "تعذّر اعتماد الخطوة."), id, "تم اعتماد خطوة مسار النتائج.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> RejectResultsWorkflow(Guid id, Guid versionId, string? comments, CancellationToken ct)
    {
        var instanceId = await db.CompetitionResultVersions.Where(v => v.Id == versionId && v.CompetitionId == id)
            .Select(v => v.WorkflowInstanceId).FirstOrDefaultAsync(ct);
        if (instanceId is not Guid workflowId)
            return Result((false, "لا يوجد مسار اعتماد مرتبط بنسخة النتائج."), id, null);
        var result = await workflowActions.RejectAsync(workflowId, CreateWorkflowActor(User), comments, ct);
        return Result((result.Success, result.Error ?? "تعذّر رفض الخطوة."), id, "أُعيدت نسخة النتائج إلى المسودة.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> PublishResults(Guid id, Guid versionId, CancellationToken ct) =>
        Result(await results.PublishAsync(versionId, ParseUserId(User), ct), id, "نُشرت النتائج.");

    /// <summary>HR copy of the public results PDF (current published version).</summary>
    public async Task<IActionResult> ResultsDocument(Guid id, CancellationToken ct)
    {
        var view = await results.GetPublicAsync(id, 1, ct);
        if (view is null) return NotFound();
        var rows = await results.GetPublicRowsAsync(view.Competition.TenantId, view.Version.Id, ct);
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "Competitions/ResultsDocument");
        return File(HR.Core.Web.Services.CompetitionResultsDocument.Generate(view.Competition, view.Version, rows, view.ObjectionDeadlineUtc),
            "application/pdf", $"results-{view.Competition.Number}-v{view.Version.VersionNo}.pdf");
    }

    /// <summary>HR files a result objection on the candidate's behalf (by exam number).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> FileObjection(Guid id, string? examNumber, string? reason, CancellationToken ct)
    {
        var paper = await db.ExamCandidates.AsNoTracking()
            .Where(c => c.CompetitionId == id && c.ExamNumber == (examNumber ?? "").Trim())
            .Select(c => new { c.Id, c.RecruitmentApplicationId })
            .FirstOrDefaultAsync(ct);
        if (paper is null)
            return Result((false, "الرقم الامتحاني غير موجود في هذه المسابقة."), id, null);
        var (appeal, error) = await objections.FileAsync(paper.RecruitmentApplicationId, paper.Id, reason,
            HR.Core.Domain.Entities.RecruitmentAppealFiledBy.HrOnBehalf, ParseUserId(User), HrOrgScopeVisibility.GetEmployeeId(User), ct);
        if (appeal is null)
            return Result((false, error), id, null);
        TempData["Success"] = "سُجّل الاعتراض؛ عيّن المراجع المستقل من صفحة الاعتراض.";
        return RedirectToAction("Details", "RecruitmentAppeals", new { id = appeal.Id });
    }

    // ---- HCM-031 (P5): finalize, bulk offers ---------------------------------------------------------------------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Finalize(Guid id, CancellationToken ct)
    {
        var result = await allocation.FinalizeAsync(id, ParseUserId(User), ct);
        return Result((result.Success, result.Error), id, result.AlreadyFinalized
            ? "المسابقة منتهية مسبقاً."
            : $"أُنهيت المسابقة: {result.Allocated} مخصَّص، {result.Reserve} في الاحتياط، {result.NotSuccessful} غير ناجح.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CreateOfferDrafts(Guid id, Guid? lineId, DateTime? proposedStartDate, int? validityDays, CancellationToken ct)
    {
        if (proposedStartDate is not DateTime start)
            return Result((false, "تاريخ المباشرة المقترح مطلوب."), id, null);
        var items = await competitionOffers.CreateDraftsAsync(id, lineId, DateTime.SpecifyKind(start, DateTimeKind.Utc), validityDays, ParseUserId(User), ct);
        return OfferBatchResult(id, items, "مسودة عرض");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SubmitOfferDrafts(Guid id, Guid? lineId, CancellationToken ct)
    {
        var items = await competitionOffers.SubmitDraftsAsync(id, lineId, ParseUserId(User), UserName(User), ct);
        return OfferBatchResult(id, items, "عرض قُدّم للاعتماد");
    }

    private IActionResult OfferBatchResult(Guid id, IReadOnlyList<CompetitionOfferItemResult> items, string label)
    {
        var ok = items.Count(i => i.Success);
        TempData[ok == items.Count ? "Success" : "Error"] = $"{label}: {ok} من {items.Count}.";
        TempData["OfferResults"] = System.Text.Json.JsonSerializer.Serialize(items.Take(500).ToList());
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IReadOnlyList<AllocationRow>> LoadAllocationsAsync(Guid competitionId, CancellationToken ct) =>
        await db.CompetitionAllocations.AsNoTracking()
            .Where(a => a.CompetitionId == competitionId && a.AllocatedLineId != null)
            .OrderBy(a => a.AllocatedLine!.LineNo).ThenBy(a => a.RecruitmentApplication!.Rank)
            .Take(2000)
            .Select(a => new AllocationRow(
                a.AllocatedLineId!.Value,
                a.AllocatedLine!.LineNo,
                a.AllocatedLine.Title,
                a.RecruitmentApplication!.ApplicantFullName,
                a.RecruitmentApplication.Rank,
                a.AllocatedRequisition != null ? a.AllocatedRequisition.RequisitionNumber : null,
                a.PreferenceOrderSatisfied,
                a.FromReserve,
                a.RecruitmentApplication.Status,
                a.RecruitmentApplication.ActiveOfferLetter != null ? a.RecruitmentApplication.ActiveOfferLetter.Status : null))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<ResultVersionRow>> LoadResultVersionsAsync(Guid competitionId, CancellationToken ct)
    {
        var versions = await db.CompetitionResultVersions.AsNoTracking()
            .Where(v => v.CompetitionId == competitionId)
            .OrderByDescending(v => v.VersionNo)
            .ToListAsync(ct);
        var rows = new List<ResultVersionRow>();
        foreach (var v in versions)
        {
            var counts = await db.CompetitionResults.AsNoTracking()
                .Where(r => r.ResultVersionId == v.Id)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Passed = g.Count(r => r.Outcome == CompetitionResultOutcome.Passed),
                    Ties = g.Count(r => r.TieUnresolved)
                })
                .FirstOrDefaultAsync(ct);
            var changes = string.IsNullOrWhiteSpace(v.DiffJson) ? 0
                : System.Text.Json.JsonSerializer.Deserialize<List<CompetitionResultChange>>(v.DiffJson)?.Count ?? 0;
            rows.Add(new ResultVersionRow(v, counts?.Total ?? 0, counts?.Passed ?? 0, counts?.Ties ?? 0, changes));
        }
        return rows;
    }

    private async Task<IReadOnlyList<ResultRow>> LoadResultsAsync(Guid versionId, CancellationToken ct) =>
        await db.CompetitionResults.AsNoTracking()
            .Where(r => r.ResultVersionId == versionId && (r.TieUnresolved || r.OverallRank == null || r.OverallRank <= 300))
            .OrderBy(r => r.OverallRank == null).ThenBy(r => r.OverallRank).ThenBy(r => r.ExamNumber)
            .Take(1000)
            .Select(r => new ResultRow(r.RecruitmentApplicationId, r.ExamNumber, r.RecruitmentApplication!.ApplicantFullName,
                r.ExamScore, r.FinalScore, r.OverallRank, r.Outcome, r.TieUnresolved, r.TieDecisionReason))
            .ToListAsync(ct);

    private async Task<WorkflowPanelViewModel> BuildResultsWorkflowAsync(CompetitionResultVersion version, CancellationToken ct)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, version.WorkflowInstanceId, ct);
        var userId = WorkflowPanelBuilder.ParseUserId(User);
        return WorkflowPanelBuilder.Build(instance, definition, User, userId, new WorkflowPanelBuildOptions
        {
            IsAwaitingApproval = version.Status == CompetitionResultVersionStatus.PendingApproval,
            EntityCreatedBy = version.ComputedBy ?? version.CreatedBy,
            CreatorSoDMessage = "لا يجوز لمن احتسب النتائج اعتمادها."
        });
    }

    private async Task<IReadOnlyList<ExamSessionRow>> LoadExamSessionsAsync(Guid competitionId, CancellationToken ct)
    {
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.CompetitionId == competitionId)
            .OrderBy(s => s.ScheduledAt)
            .ToListAsync(ct);
        var rows = new List<ExamSessionRow>();
        foreach (var session in sessions)
        {
            var counts = await db.ExamCandidates.AsNoTracking()
                .Where(c => c.ExamSessionId == session.Id)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Seated = g.Count(),
                    Present = g.Count(c => c.Attendance == ExamAttendance.Present),
                    Absent = g.Count(c => c.Attendance == ExamAttendance.Absent),
                    Cards = g.Count(c => c.AdmissionCardIssuedAt != null)
                })
                .FirstOrDefaultAsync(ct);
            var (_, complete, awaiting) = session.Status is ExamSessionStatus.Held or ExamSessionStatus.MarkingClosed
                ? await marking.ProgressAsync(session.Id, ct)
                : (0, 0, 0);
            var markers = await db.ExamMarkerAssignments.AsNoTracking()
                .Where(m => m.ExamSessionId == session.Id)
                .OrderBy(m => m.Role)
                .Select(m => new ExamMarkerRow(m.Id, m.MarkerEmployee != null ? m.MarkerEmployee.FullName : "—", m.Role, m.MarkerUserId != null, m.HoldsHrReadAccess))
                .ToListAsync(ct);
            rows.Add(new ExamSessionRow(session, counts?.Seated ?? 0, counts?.Present ?? 0, counts?.Absent ?? 0, complete, awaiting, counts?.Cards ?? 0, markers));
        }
        return rows;
    }

    /// <summary>HCM-031 (P2): bulk screening on «المتقدمون» (UnderReview / Shortlisted = admitted to the exam / Rejected).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ScreenApplicants(
        Guid id,
        List<Guid>? applicationIds,
        RecruitmentApplicationStatus target,
        bool conflictOfInterestDeclared,
        string? eligibilityOverrideReason,
        string? rejectionNote,
        CancellationToken ct)
    {
        if (applicationIds is not { Count: > 0 })
        {
            TempData["Error"] = "اختر متقدماً واحداً على الأقل.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var results = await intake.BulkTransitionAsync(
            id, applicationIds, target, conflictOfInterestDeclared, eligibilityOverrideReason, rejectionNote, ParseUserId(User), ct);
        var ok = results.Count(r => r.Success);
        TempData[ok == results.Count ? "Success" : "Error"] = $"نُفّذ الإجراء على {ok} من {results.Count} طلب.";
        TempData["BulkResults"] = System.Text.Json.JsonSerializer.Serialize(results.Where(r => !r.Success).Take(100).ToList());
        return RedirectToAction(nameof(Details), new { id });
    }

    private IReadOnlyList<CompetitionBulkItemResult> ReadBulkResults() =>
        TempData["BulkResults"] is string json
            ? System.Text.Json.JsonSerializer.Deserialize<List<CompetitionBulkItemResult>>(json) ?? []
            : [];

    private async Task<CompetitionApplicantsViewModel> LoadApplicantsAsync(
        Guid competitionId, int page, RecruitmentApplicationStatus? status, CancellationToken ct)
    {
        var query = db.RecruitmentApplications.AsNoTracking().Where(a => a.CompetitionId == competitionId);
        if (status is not null)
            query = query.Where(a => a.Status == status);
        var total = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)CompetitionsPaging.PageSize)));
        var rows = await query
            .OrderBy(a => a.AppliedAt).ThenBy(a => a.Id)
            .Skip((page - 1) * CompetitionsPaging.PageSize)
            .Take(CompetitionsPaging.PageSize)
            .Select(a => new { a.Id, a.ApplicantFullName, a.IntakeChannel, a.Status, a.AppliedAt, a.EligibilityFlags })
            .ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var preferences = (await db.CompetitionPreferences.AsNoTracking()
                .Where(p => ids.Contains(p.RecruitmentApplicationId))
                .OrderBy(p => p.Order)
                .Select(p => new { p.RecruitmentApplicationId, p.Order, p.CompetitionLine!.LineNo, p.CompetitionLine.Title, p.EligibilityFlags })
                .ToListAsync(ct))
            .ToLookup(p => p.RecruitmentApplicationId);
        return new CompetitionApplicantsViewModel
        {
            Total = total,
            Page = page,
            Status = status,
            Rows = rows.Select(r => new CompetitionApplicantRow(
                r.Id,
                r.ApplicantFullName,
                r.IntakeChannel,
                r.Status,
                r.AppliedAt,
                preferences[r.Id].Select(p => $"{p.Order}. السطر {p.LineNo} — {p.Title}"
                    + (p.EligibilityFlags is null ? "" : $" ({string.Join("، ", RecruitmentEligibilityRules.Parse(p.EligibilityFlags).Select(RecruitmentEligibilityRules.LabelAr))})"))
                    .ToList(),
                RecruitmentEligibilityRules.Parse(r.EligibilityFlags).Select(RecruitmentEligibilityRules.LabelAr).ToList()))
                .ToList()
        };
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AddLine(Guid id, CancellationToken ct)
    {
        var competition = await db.RecruitmentCompetitions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null) return NotFound();
        await PopulateLineListsAsync(ct);
        return View("LineForm", new CompetitionLineFormViewModel { CompetitionId = id });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> EditLine(Guid id, CancellationToken ct)
    {
        var line = await db.CompetitionLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (line is null) return NotFound();
        await PopulateLineListsAsync(ct);
        return View("LineForm", new CompetitionLineFormViewModel
        {
            CompetitionId = line.CompetitionId,
            LineId = line.Id,
            Title = line.Title,
            GradeCode = line.GradeCode,
            JobCatalogEntryId = line.JobCatalogEntryId,
            OrgUnitId = line.OrgUnitId,
            LinePassMark = line.LinePassMark
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SaveLine(CompetitionLineFormViewModel vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var error = vm.LineId is Guid lineId
                ? (await lifecycle.UpdateLineAsync(lineId, vm.ToInput(), ParseUserId(User), ct)).Error
                : (await lifecycle.AddLineAsync(vm.CompetitionId, vm.ToInput(), ParseUserId(User), ct)).Error;
            if (error is null)
            {
                TempData["Success"] = "تم حفظ سطر المسابقة.";
                return RedirectToAction(nameof(Details), new { id = vm.CompetitionId });
            }
            ModelState.AddModelError(string.Empty, error);
        }
        await PopulateLineListsAsync(ct);
        return View("LineForm", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RemoveLine(Guid id, Guid lineId, CancellationToken ct) =>
        Result(await lifecycle.RemoveLineAsync(lineId, ParseUserId(User), ct), id, "تم حذف السطر.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AttachRequisition(Guid id, Guid lineId, Guid? requisitionId, CancellationToken ct)
    {
        if (requisitionId is not Guid reqId)
        {
            TempData["Error"] = "اختر طلب الشاغر المراد ربطه.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return Result(await lifecycle.AttachRequisitionAsync(lineId, reqId, ParseUserId(User), ct), id, "تم ربط طلب الشاغر بالسطر.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> DetachRequisition(Guid id, Guid requisitionId, CancellationToken ct) =>
        Result(await lifecycle.DetachRequisitionAsync(requisitionId, ParseUserId(User), ct), id, "تم فصل طلب الشاغر عن السطر.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) =>
        Result(await lifecycle.SubmitForApprovalAsync(id, ParseUserId(User), UserName(User), ct), id, "تم تقديم المسابقة للاعتماد.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> ApproveWorkflow(Guid id, string? comments, CancellationToken ct)
    {
        var instanceId = await db.RecruitmentCompetitions.Where(c => c.Id == id).Select(c => c.ApprovalWorkflowInstanceId).FirstOrDefaultAsync(ct);
        if (instanceId is not Guid workflowId)
            return Result((false, "لا يوجد مسار اعتماد مرتبط بهذه المسابقة."), id, null);
        var result = await workflowActions.ApproveAsync(workflowId, CreateWorkflowActor(User), comments, targetStepLevel: null, ct);
        return Result((result.Success, result.Error ?? "تعذّر اعتماد الخطوة."), id, "تم اعتماد خطوة مسار المسابقة.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> RejectWorkflow(Guid id, string? comments, CancellationToken ct)
    {
        var instanceId = await db.RecruitmentCompetitions.Where(c => c.Id == id).Select(c => c.ApprovalWorkflowInstanceId).FirstOrDefaultAsync(ct);
        if (instanceId is not Guid workflowId)
            return Result((false, "لا يوجد مسار اعتماد مرتبط بهذه المسابقة."), id, null);
        var result = await workflowActions.RejectAsync(workflowId, CreateWorkflowActor(User), comments, ct);
        return Result((result.Success, result.Error ?? "تعذّر رفض الخطوة."), id, "أُعيدت المسابقة إلى المسودة.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Announce(Guid id, CancellationToken ct) =>
        Result(await lifecycle.AnnounceAsync(id, ParseUserId(User), ct), id, "تم إعلان المسابقة.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CloseIntake(Guid id, CancellationToken ct) =>
        Result(await lifecycle.CloseIntakeAsync(id, ParseUserId(User), ct), id, "تم إغلاق التقديم على المسابقة.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Cancel(Guid id, string? reason, CancellationToken ct) =>
        Result(await lifecycle.CancelAsync(id, reason, ParseUserId(User), ct), id, "تم إلغاء المسابقة.");

    private IActionResult Result((bool Success, string? Error) result, Guid id, string? success)
    {
        TempData[result.Success ? "Success" : "Error"] = result.Success ? success : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanAsync(string policy) => (await authorization.AuthorizeAsync(User, policy)).Succeeded;

    private async Task PopulateFormListsAsync(CancellationToken ct)
    {
        var sources = await legislationProvider.ListActiveAsync(ct);
        ViewBag.LegalSources = sources.Select(s => new SelectListItem(s.DisplayLabel, s.LegalSourceId)).ToList();
    }

    private async Task PopulateLineListsAsync(CancellationToken ct)
    {
        ViewBag.JobCatalogEntries = await db.JobCatalogEntries.AsNoTracking()
            .Where(j => j.IsActive)
            .OrderBy(j => j.JobCode)
            .Select(j => new SelectListItem($"{j.JobCode} — {j.TitleAr} ({j.GradeCode})", j.Id.ToString()))
            .ToListAsync(ct);
        ViewBag.OrgUnits = await db.OrgUnits.AsNoTracking()
            .OrderBy(o => o.NameAr)
            .Select(o => new SelectListItem(o.NameAr, o.Id.ToString()))
            .ToListAsync(ct);
    }

    private static WorkflowActionActor CreateWorkflowActor(ClaimsPrincipal user)
    {
        var actorRole = user.FindFirstValue(ClaimTypes.Role) ?? user.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
        return new WorkflowActionActor(ParseUserId(user), UserName(user), actorRole);
    }

    private static string? UserName(ClaimsPrincipal user) => user.FindFirstValue("preferred_username") ?? user.Identity?.Name;

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
