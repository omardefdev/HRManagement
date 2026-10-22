using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class RecruitmentApplicationsController(
    HRCoreDbContext db,
    RecruitmentIntegrationService integration,
    RecruitmentApplicationIntakeService intake,
    RecruitmentSelectionService selection,
    RecruitmentOfferService offerService,
    RecruitmentAppealService appealService,
    RecruitmentInterviewService interviewService,
    RecruitmentScoringService scoringService,
    IAuthorizationService authorization,
    ILegislationRegisterProvider legislationProvider,
    IPolicyOrchestrator policyOrchestrator,
    IConfiguration configuration) : Controller
{
    public async Task<IActionResult> Index(RecruitmentApplicationsFilter filter, bool showCreate = false)
    {
        filter.Normalize();
        return View(await BuildIndexViewModelAsync(filter, showCreateModal: showCreate));
    }

    public IActionResult Create() =>
        RedirectToAction(nameof(Index), new { showCreate = true });

    public async Task<IActionResult> Details(Guid id)
    {
        var legacyFlag = ResolveLegacyHireWithoutOffer();
        var app = await db.RecruitmentApplications
            .AsNoTracking()
            .Include(a => a.Requisition)
            .Include(a => a.ActiveOfferLetter)
            .Include(a => a.OfferLetters)
            .Include(a => a.CriteriaVersion)
            .Include(a => a.HiredEmployee)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (app is null) return NotFound();

        var activeAppeal = await db.RecruitmentAppeals
            .AsNoTracking()
            .Where(a => a.RecruitmentApplicationId == id)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        var interviews = await interviewService.GetForApplicationAsync(id);

        IReadOnlyList<RecruitmentApplication> rankBoard = [];
        int? bestAvailableRank = null;
        CommitteeRankingGate? committeeGate = null;

        if (app.RequisitionId is Guid reqId)
        {
            rankBoard = await selection.GetRankedCandidatesAsync(reqId);
            bestAvailableRank = await offerService.GetBestAvailableRankAsync(reqId);
            committeeGate = await selection.GetCommitteeRankingGateAsync(reqId);
        }

        var scoringPanel = await BuildScoringPanelAsync(app);

        // HCM-027 (E5): grade pre-filled from the funded position + Payroll range hint (server-side lookup).
        var defaultStartDate = DateTime.UtcNow.Date.AddDays(30);
        string? positionGrade = null;
        PayrollGradeLookupResult? gradeLookup = null;
        if (app.RequisitionId.HasValue && app.ActiveOfferLetter is null
            && OfferEligibilityRules.OfferableStatuses.Contains(app.Status))
        {
            (positionGrade, gradeLookup) = await offerService.GetOfferTermsHintAsync(
                app.Id, DateOnly.FromDateTime(defaultStartDate));
        }
        var gradeFound = gradeLookup as PayrollGradeLookupResult.Found;

        if (!string.IsNullOrWhiteSpace(app.Requisition?.LegalSourceId))
        {
            var legalSource = await legislationProvider.GetByIdAsync(app.Requisition.LegalSourceId);
            ViewBag.RequisitionLegalSourceLabel = legalSource?.DisplayLabel ?? app.Requisition.LegalSourceId;
        }
        else
        {
            ViewBag.RequisitionLegalSourceLabel = null;
        }

        var appealFilingError = app.Status == RecruitmentApplicationStatus.Rejected
            ? appealService.GetFilingError(app, DateTime.UtcNow)
            : null;
        var hireBlockedReason = await RecruitmentHireAppealGuard.ValidateAsync(db, app.RequisitionId, BlockHireOnPendingAppeal());

        return View(new RecruitmentApplicationDetailsViewModel
        {
            Application = app,
            RankBoard = rankBoard,
            AllowedStatusTransitions = SelectionWorkflowRules.GetAllowedTargets(app, legacyFlag),
            ScoringPanel = scoringPanel,
            LegacyHireWithoutOffer = legacyFlag,
            CanHire = RecruitmentWorkflowRules.CanHire(app, legacyFlag) && hireBlockedReason is null,
            HireBlockedReason = RecruitmentWorkflowRules.CanHire(app, legacyFlag) ? hireBlockedReason : null,
            AppealPending = RecruitmentAppealWorkflowRules.HasPendingAppeal(app),
            CanRank = RecruitmentAppealWorkflowRules.CanRank(app),
            CanOffer = RecruitmentAppealWorkflowRules.CanOffer(app),
            CanAppeal = app.Status == RecruitmentApplicationStatus.Rejected && appealFilingError is null,
            AppealFilingError = appealFilingError,
            ActiveAppeal = activeAppeal,
            InterviewSlots = interviews,
            BestAvailableRank = bestAvailableRank,
            RequiresMeritOverride = app.Rank is null || app.Rank != bestAvailableRank,
            DefaultOfferValidityDays = offerService.DefaultValidityDays,
            PositionGradeCode = positionGrade,
            GradeMinSalary = gradeFound?.MinSalary,
            GradeMaxSalary = gradeFound?.MaxSalary,
            PayrollLookupUnavailable = gradeLookup is PayrollGradeLookupResult.Unavailable,
            PositionGradeNotInScale = gradeLookup is PayrollGradeLookupResult.NotFound,
            DefaultProposedStartDate = defaultStartDate,
            CommitteeGate = committeeGate
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(RecruitmentApplicationCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            var indexVm = await BuildIndexViewModelAsync(new RecruitmentApplicationsFilter(), vm, showCreateModal: true);
            return View("Index", indexVm);
        }

        var validationError = await intake.ValidatePublishedRequisitionAsync(vm.RequisitionId);
        if (validationError is not null)
        {
            ModelState.AddModelError(nameof(vm.RequisitionId), validationError);
            var indexVm = await BuildIndexViewModelAsync(new RecruitmentApplicationsFilter(), vm, showCreateModal: true);
            return View("Index", indexVm);
        }

        var requisition = await db.RecruitmentRequisitions
            .AsNoTracking()
            .FirstAsync(r => r.Id == vm.RequisitionId);

        db.RecruitmentApplications.Add(new RecruitmentApplication
        {
            ApplicantFullName = vm.ApplicantFullName.Trim(),
            NationalId = string.IsNullOrWhiteSpace(vm.NationalId) ? null : vm.NationalId.Trim(),
            RequisitionId = vm.RequisitionId,
            AppliedPosition = RecruitmentApplicationIntakeRules.DenormalizeJobTitle(requisition),
            Notes = vm.Notes,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Submitted
        });

        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل طلب التوظيف";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> DeclareCoi(Guid id)
    {
        var app = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == id);
        if (app is null) return NotFound();

        var (success, error) = await selection.DeclareCoiAsync(app, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تم تسجيل إقرار عدم تعارض المصالح" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>HCM-026: the current user submits structured scores as an assessor.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SubmitScores(SubmitScoresForm form)
    {
        var assessorUserId = ParseUserId(User);
        if (assessorUserId is null)
        {
            TempData["Error"] = "تعذّر تحديد هوية المقيّم.";
            return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
        }

        var scores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, raw) in form.Scores)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (!InvariantDecimal.TryParse(raw, out var value))
            {
                TempData["Error"] = $"قيمة الدرجة غير صالحة للبند «{key}».";
                return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
            }
            scores[key] = value;
        }

        var (success, error) = await scoringService.SubmitScoresAsync(
            form.ApplicationId,
            assessorUserId.Value,
            HrOrgScopeVisibility.GetEmployeeId(User),
            scores,
            form.Notes,
            form.NoConflictDeclared);

        TempData[success ? "Success" : "Error"] = success ? "تم حفظ درجات التقييم وإعادة احتساب النتيجة" : error;
        return RedirectToAction(nameof(Details), null, new { id = form.ApplicationId }, "assessment-scoring");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AssignRank(AssignRankForm form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "يرجى إدخال ترتيب صحيح.";
            return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
        }

        var (success, error) = await selection.AssignRankAsync(
            form.ApplicationId, form.Rank, form.RankOverrideReason, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تم تعيين الترتيب" : error;
        return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SetStatus(Guid id, RecruitmentApplicationStatus status, string? rejectionNote = null)
    {
        var legacyFlag = ResolveLegacyHireWithoutOffer();
        var (success, error) = await selection.TransitionStatusAsync(id, status, legacyFlag, rejectionNote);
        TempData[success ? "Success" : "Error"] = success ? "تم تحديث حالة الطلب" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>HCM-028 (H1): HR files on the candidate's behalf; the reviewer is assigned on RecruitmentAppeals/Details.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> FileAppeal(Guid id, string? reason)
    {
        var (appeal, error) = await appealService.FileAppealAsync(
            id, reason, RecruitmentAppealFiledBy.HrOnBehalf, ParseUserId(User), HrOrgScopeVisibility.GetEmployeeId(User));
        if (appeal is null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم تقديم الاستئناف — عيّن المراجع المستقل لبدء المراجعة.";
        return RedirectToAction("Details", "RecruitmentAppeals", new { id = appeal.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ScheduleInterview(ScheduleInterviewForm form)
    {
        var (slot, error) = await interviewService.ScheduleAsync(
            form.ApplicationId,
            LocalInputToUtc(form.ScheduledAt),
            form.Mode,
            form.Location,
            form.PanelEmployeeIds,
            form.PanelNotes);
        TempData[slot is null ? "Error" : "Success"] = error ?? "تم جدولة المقابلة";
        return RedirectToInterviews(form.ApplicationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RescheduleInterview(Guid applicationId, Guid slotId, DateTime newTime, string? reason)
    {
        var (slot, error) = await interviewService.RescheduleAsync(slotId, LocalInputToUtc(newTime), reason);
        TempData[slot is null ? "Error" : "Success"] = error ?? "تمت إعادة جدولة المقابلة";
        return RedirectToInterviews(applicationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CancelInterview(Guid applicationId, Guid slotId, string? reason)
    {
        var (success, error) = await interviewService.CancelAsync(slotId, reason);
        TempData[success ? "Success" : "Error"] = success ? "تم إلغاء المقابلة" : error;
        return RedirectToInterviews(applicationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CompleteInterview(Guid applicationId, Guid slotId, InterviewOutcome outcome, string? notes)
    {
        var (success, error) = await interviewService.CompleteAsync(slotId, outcome, notes);
        TempData[success ? "Success" : "Error"] = success ? "تم تسجيل نتيجة المقابلة" : error;
        return RedirectToInterviews(applicationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CreateOffer(CreateOfferForm form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "يرجى التحقق من بيانات العرض.";
            return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
        }

        var (offer, error) = await offerService.CreateDraftAsync(
            form.ApplicationId,
            form.OfferedSalary,
            form.GradeCode,
            form.ProposedStartDate,
            form.ValidityDays,
            ParseUserId(User),
            form.IsMeritOverride,
            form.MeritOverrideReason,
            form.GradeDeviationReason,
            form.SalaryJustification);

        if (offer is null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
        }

        TempData["Success"] = "تم إنشاء مسودة خطاب العرض";
        return RedirectToAction("Details", "OfferLetters", new { id = offer.Id });
    }

    public async Task<IActionResult> Hire(Guid id)
    {
        var legacyFlag = ResolveLegacyHireWithoutOffer();
        var app = await db.RecruitmentApplications
            .AsNoTracking()
            .Include(a => a.Requisition)
                .ThenInclude(r => r!.FundedPosition)
                    .ThenInclude(p => p!.OrgUnit)
            .Include(a => a.ActiveOfferLetter)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (app is null) return NotFound();

        if (!RecruitmentWorkflowRules.CanHire(app, legacyFlag))
        {
            TempData["Error"] = legacyFlag && app.RequisitionId is null
                ? "يمكن التعيين فقط للطلبات في حالة «القائمة المختصرة» أو «عرض مقبول»."
                : "يمكن التعيين فقط بعد قبول خطاب العرض.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var appealBlock = await RecruitmentHireAppealGuard.ValidateAsync(db, app.RequisitionId, BlockHireOnPendingAppeal());
        if (appealBlock is not null)
        {
            TempData["Error"] = appealBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        var vm = RecruitmentHireViewModel.FromApplication(app, app.Requisition, app.Requisition?.FundedPosition);
        await PopulateSelectListsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Hire(RecruitmentHireViewModel vm)
    {
        var legacyFlag = ResolveLegacyHireWithoutOffer();
        var app = await db.RecruitmentApplications
            .Include(a => a.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .Include(a => a.ActiveOfferLetter)
            .FirstOrDefaultAsync(a => a.Id == vm.ApplicationId);

        if (app is null) return NotFound();

        if (!RecruitmentWorkflowRules.CanHire(app, legacyFlag))
        {
            TempData["Error"] = "لا يمكن التعيين في الحالة الحالية.";
            return RedirectToAction(nameof(Details), new { id = app.Id });
        }

        var appealBlock = await RecruitmentHireAppealGuard.ValidateAsync(db, app.RequisitionId, BlockHireOnPendingAppeal());
        if (appealBlock is not null)
        {
            TempData["Error"] = appealBlock;
            return RedirectToAction(nameof(Details), new { id = app.Id });
        }

        if (vm.Mode == RecruitmentHireMode.LinkExisting)
        {
            if (!vm.ExistingEmployeeId.HasValue)
                ModelState.AddModelError(nameof(vm.ExistingEmployeeId), "يجب اختيار موظف موجود.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(vm.NationalId))
                ModelState.AddModelError(nameof(vm.NationalId), "الرقم الوطني مطلوب.");
            if (string.IsNullOrWhiteSpace(vm.FirstName))
                ModelState.AddModelError(nameof(vm.FirstName), "الاسم الأول مطلوب.");
            if (string.IsNullOrWhiteSpace(vm.LastName))
                ModelState.AddModelError(nameof(vm.LastName), "اسم العائلة مطلوب.");
        }

        if (!RecruitmentHireViewModel.EmploymentCategories.Any(x =>
                string.Equals(x.Code, vm.EmploymentCategory, StringComparison.OrdinalIgnoreCase)))
            ModelState.AddModelError(nameof(vm.EmploymentCategory), "يرجى اختيار فئة توظيف صحيحة.");

        if (!ModelState.IsValid)
        {
            vm = RecruitmentHireViewModel.FromApplication(app, app.Requisition, app.Requisition?.FundedPosition);
            await PopulateSelectListsAsync(vm);
            return View(vm);
        }

        var mode = ResolvePolicyMode();
        if (mode != PolicyEnforcementMode.Off)
        {
            var contextJson = JsonSerializer.Serialize(new
            {
                applicationId = app.Id,
                requisitionId = app.RequisitionId,
                applicantFullName = app.ApplicantFullName
            });

            var policyRequest = PolicyDomainFactsBuilders.ForRecruitmentApplication(
                app.Id,
                app.TenantId,
                "Hire",
                app.Status == RecruitmentApplicationStatus.OfferAccepted,
                app.ConflictOfInterestDeclared,
                app.Rank.HasValue,
                app.Requisition?.Status.ToString(),
                app.Requisition?.FundedPosition?.OrgUnitId != null,
                contextJson);
            policyRequest.Mode = mode;
            policyRequest.ActorUserId = ParseUserId(User);
            policyRequest.ActorUserName = User.Identity?.Name;

            var policyResult = await policyOrchestrator.EnforceAsync(policyRequest);
            if (!policyResult.Allowed)
            {
                ModelState.AddModelError(string.Empty, policyResult.Decision.DenyReason ?? "مرفوض بسياسة التوظيف.");
                vm = RecruitmentHireViewModel.FromApplication(app, app.Requisition, app.Requisition?.FundedPosition);
                await PopulateSelectListsAsync(vm);
                return View(vm);
            }
        }

        var start = vm.ExpectedStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        Employee employee;

        try
        {
            employee = vm.Mode == RecruitmentHireMode.LinkExisting
                ? await integration.HireExistingEmployeeAsync(
                    app, vm.ExistingEmployeeId!.Value, start, vm.EmploymentCategory)
                : await integration.HireAndCreateEmployeeAsync(
                    app,
                    vm.NationalId.Trim(),
                    vm.FirstName.Trim(),
                    string.IsNullOrWhiteSpace(vm.MiddleName) ? null : vm.MiddleName.Trim(),
                    vm.LastName.Trim(),
                    vm.BirthDate.AsUtc(),
                    vm.Gender,
                    vm.PrimaryOrgUnitId ?? app.Requisition?.FundedPosition?.OrgUnitId,
                    start,
                    vm.EmploymentCategory);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            vm = RecruitmentHireViewModel.FromApplication(app, app.Requisition, app.Requisition?.FundedPosition);
            await PopulateSelectListsAsync(vm);
            return View(vm);
        }

        TempData["Success"] = $"تم إنشاء ملف {app.ApplicantFullName} وفتح معالج استكمال التعيين.";
        var gateway = configuration["HrGateway:PublicBaseUrl"] ?? "http://localhost:5000";
        return Redirect($"{gateway.TrimEnd('/')}/ui/hr-contracts/HireWizard/Continue?employeeId={employee.Id}");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> PromoteFromCitizen(Guid citizenApplicationId)
    {
        var citizen = await db.CitizenApplications
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == citizenApplicationId);

        if (citizen is null) return NotFound();

        if (citizen.PromotedRecruitmentApplicationId.HasValue)
        {
            TempData["Info"] = "تم تحويل هذا الطلب مسبقاً إلى مسار التوظيف الداخلي.";
            return RedirectToAction(nameof(Index));
        }

        if (citizen.Status is not (CitizenApplicationStatus.Shortlisted or CitizenApplicationStatus.UnderReview))
        {
            TempData["Error"] = "يمكن التحويل فقط للطلبات قيد المراجعة أو في القائمة المختصرة.";
            return RedirectToAction("Details", "PublicJobPostings", new { id = citizen.PublicJobPostingId });
        }

        if (!citizen.RequisitionId.HasValue)
        {
            TempData["Error"] = "لا يمكن التحويل بدون طلب شاغر مرتبط.";
            return RedirectToAction("Details", "PublicJobPostings", new { id = citizen.PublicJobPostingId });
        }

        var validationError = await intake.ValidatePublishedRequisitionAsync(citizen.RequisitionId.Value);
        if (validationError is not null)
        {
            TempData["Error"] = validationError;
            return RedirectToAction("Details", "PublicJobPostings", new { id = citizen.PublicJobPostingId });
        }

        var requisition = citizen.Requisition
            ?? await db.RecruitmentRequisitions.AsNoTracking().FirstAsync(r => r.Id == citizen.RequisitionId);

        var recruitment = new RecruitmentApplication
        {
            ApplicantFullName = citizen.FullName,
            NationalId = citizen.NationalId,
            RequisitionId = citizen.RequisitionId,
            AppliedPosition = RecruitmentApplicationIntakeRules.DenormalizeJobTitle(requisition),
            Notes = $"محوّل من طلب مواطن {citizen.ReferenceNumber}",
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Shortlisted,
            ReviewedAt = DateTime.UtcNow,
            ConflictOfInterestDeclared = citizen.ConflictOfInterestDeclared,
            ConflictOfInterestDeclaredAt = citizen.ConflictOfInterestDeclaredAt
        };

        db.RecruitmentApplications.Add(recruitment);
        citizen.PromotedRecruitmentApplication = recruitment;
        await db.SaveChangesAsync();

        TempData["Success"] = "تم إضافة المرشح إلى مسار التوظيف الداخلي.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> PromoteFromInternal(Guid internalApplicationId)
    {
        var internalApp = await db.InternalApplications
            .Include(a => a.InternalVacancy)
            .Include(a => a.Requisition)
            .Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.Id == internalApplicationId);

        if (internalApp is null) return NotFound();

        if (internalApp.PromotedRecruitmentApplicationId.HasValue)
        {
            TempData["Info"] = "تم تحويل هذا الترشّح مسبقاً.";
            return RedirectToAction(nameof(Details), new { id = internalApp.PromotedRecruitmentApplicationId });
        }

        if (internalApp.Status is not (InternalApplicationStatus.Shortlisted or InternalApplicationStatus.UnderReview))
        {
            TempData["Error"] = "يمكن التحويل فقط للترشّحات قيد المراجعة أو في القائمة المختصرة.";
            return RedirectToAction("Details", "TalentMarketplace", new { id = internalApp.InternalVacancyId });
        }

        if (!internalApp.RequisitionId.HasValue)
        {
            TempData["Error"] = "لا يمكن التحويل بدون طلب شاغر مرتبط.";
            return RedirectToAction("Details", "TalentMarketplace", new { id = internalApp.InternalVacancyId });
        }

        var validationError = await intake.ValidatePublishedRequisitionAsync(internalApp.RequisitionId.Value);
        if (validationError is not null)
        {
            TempData["Error"] = validationError;
            return RedirectToAction("Details", "TalentMarketplace", new { id = internalApp.InternalVacancyId });
        }

        var requisition = internalApp.Requisition
            ?? await db.RecruitmentRequisitions.AsNoTracking().FirstAsync(r => r.Id == internalApp.RequisitionId);
        var employee = internalApp.Employee
            ?? await db.Employees.AsNoTracking().FirstAsync(e => e.Id == internalApp.EmployeeId);

        var recruitment = new RecruitmentApplication
        {
            ApplicantFullName = employee.FullName,
            NationalId = employee.NationalId,
            NationalIdHash = employee.NationalIdHash,
            RequisitionId = internalApp.RequisitionId,
            AppliedPosition = RecruitmentApplicationIntakeRules.DenormalizeJobTitle(requisition),
            Notes = $"محوّل من ترشّح داخلي {internalApp.Id:D}",
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Shortlisted,
            ReviewedAt = DateTime.UtcNow,
            SourceInternalApplicationId = internalApp.Id
        };

        db.RecruitmentApplications.Add(recruitment);
        internalApp.PromotedRecruitmentApplication = recruitment;
        await db.SaveChangesAsync();

        TempData["Success"] = "تم إضافة الموظف إلى مسار الاختيار.";
        return RedirectToAction(nameof(Details), new { id = recruitment.Id });
    }

    private static RecruitmentApplicationCreateViewModel NewCreateModel() => new();

    private bool ResolveLegacyHireWithoutOffer() =>
        configuration.GetValue("Recruitment:LegacyHireWithoutOffer", false);

    /// <summary>HCM-028 (I): <c>Recruitment:BlockHireOnPendingAppeal</c> (default true).</summary>
    private bool BlockHireOnPendingAppeal() =>
        configuration.GetValue("Recruitment:BlockHireOnPendingAppeal", true);

    private PolicyEnforcementMode ResolvePolicyMode()
    {
        var raw = configuration["PolicyEngine:Core.Recruitment"]
            ?? configuration["PolicyEngine:DefaultMode"]
            ?? nameof(PolicyEnforcementMode.Off);
        return Enum.TryParse<PolicyEnforcementMode>(raw, true, out var mode)
            ? mode
            : PolicyEnforcementMode.Off;
    }

    private RedirectToActionResult RedirectToInterviews(Guid applicationId) =>
        RedirectToAction(nameof(Details), null, new { id = applicationId }, "interviews");

    /// <summary>datetime-local inputs carry no zone; they are entered in the server's local time (as displayed).</summary>
    private static DateTime LocalInputToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
    };

    private async Task<AssessmentScoringPanelViewModel> BuildScoringPanelAsync(RecruitmentApplication app)
    {
        var summary = await scoringService.GetSummaryAsync(app.Id);
        var currentUserId = ParseUserId(User);
        var canWrite = (await authorization.AuthorizeAsync(User, HrManagementPolicies.HrCoreWrite)).Succeeded;

        var employeeIds = summary.Assessors
            .Where(a => a.AssessorEmployeeId.HasValue)
            .Select(a => a.AssessorEmployeeId!.Value)
            .Distinct()
            .ToList();
        var names = employeeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Employees
                .AsNoTracking()
                .Where(e => employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName);

        string? cannotScoreReason = null;
        if (!canWrite)
            cannotScoreReason = "التقييم متاح لحسابات الموارد البشرية ذات صلاحية الكتابة.";
        else if (currentUserId is null)
            cannotScoreReason = "تعذّر تحديد هوية المقيّم.";
        else if (app.RequisitionId is null)
            cannotScoreReason = "التقييم المنظم يتطلب طلب شاغر.";
        else if (summary.Criteria is null || !summary.Criteria.IsActive)
            cannotScoreReason = "لا توجد معايير تقييم نشطة لطلب الشاغر.";
        else if (app.Status is not (RecruitmentApplicationStatus.InterviewScheduled or RecruitmentApplicationStatus.Assessed))
            cannotScoreReason = app.Status is RecruitmentApplicationStatus.Ranked
                or RecruitmentApplicationStatus.OfferExtended
                or RecruitmentApplicationStatus.OfferAccepted
                or RecruitmentApplicationStatus.Hired
                ? "الدرجات مُقفلة بعد ترتيب المرشح."
                : "يبدأ التقييم بعد جدولة المقابلة.";

        string? interviewGateHint = null;
        if (summary.Items.Any(i => i.Stage == AssessmentStage.Interview)
            && !await interviewService.HasAttendedInterviewAsync(app.Id))
            interviewGateHint = "بنود مرحلة المقابلة تُفتح للتقييم بعد إكمال المقابلة بنتيجة «حضر».";

        var mine = summary.Assessors.FirstOrDefault(a => a.AssessorUserId == currentUserId);
        return new AssessmentScoringPanelViewModel
        {
            ApplicationId = app.Id,
            Summary = summary,
            CanScore = cannotScoreReason is null,
            CannotScoreReason = cannotScoreReason,
            MyScores = mine?.Scores ?? new Dictionary<string, decimal>(),
            MyNotes = mine?.Notes,
            CurrentUserId = currentUserId,
            AssessorNames = names,
            InterviewGateHint = interviewGateHint
        };
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    private async Task<RecruitmentApplicationsIndexViewModel> BuildIndexViewModelAsync(
        RecruitmentApplicationsFilter? filter = null,
        RecruitmentApplicationCreateViewModel? createModel = null,
        bool showCreateModal = false)
    {
        filter ??= new RecruitmentApplicationsFilter();
        filter.Normalize();
        var legacyFlag = ResolveLegacyHireWithoutOffer();

        var published = await intake.GetOpenPublishedRequisitionsAsync();
        var requisitionOptions = published
            .Select(r => new
            {
                r.Id,
                Label = $"{r.RequisitionNumber} — {r.JobTitle} ({r.OrgUnitName ?? "—"})"
            })
            .ToList();

        var query = db.RecruitmentApplications
            .AsNoTracking()
            .Include(a => a.HiredEmployee)
            .Include(a => a.Requisition)
            .Include(a => a.ActiveOfferLetter)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm;
            query = query.Where(x => x.ApplicantFullName.Contains(term)
                || x.AppliedPosition.Contains(term)
                || (x.Notes != null && x.Notes.Contains(term))
                || (x.Requisition != null && x.Requisition.RequisitionNumber.Contains(term)));
        }

        if (filter.RequisitionId is Guid reqId)
            query = query.Where(x => x.RequisitionId == reqId);

        if (filter.LegacyOnly)
            query = query.Where(x => x.RequisitionId == null);

        if (filter.HasOffer is true)
            query = query.Where(x => x.ActiveOfferLetterId != null);
        else if (filter.HasOffer is false)
            query = query.Where(x => x.ActiveOfferLetterId == null);

        if (Enum.TryParse<RecruitmentApplicationStatus>(filter.Status, true, out var status))
            query = query.Where(x => x.Status == status);

        var totalCount = await query.CountAsync();
        var applications = await query
            .OrderByDescending(x => x.AppliedAt)
            .ThenBy(x => x.ApplicantFullName)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        createModel ??= NewCreateModel();
        createModel.PublishedRequisitions = new SelectList(requisitionOptions, "Id", "Label", createModel.RequisitionId);

        ViewBag.LegacyHireWithoutOffer = legacyFlag;

        return new RecruitmentApplicationsIndexViewModel
        {
            Applications = applications,
            CreateModel = createModel,
            ShowCreateModal = showCreateModal,
            Filter = filter,
            TotalCount = totalCount,
            RequisitionFilterOptions = new SelectList(requisitionOptions, "Id", "Label", filter.RequisitionId)
        };
    }

    private async Task PopulateSelectListsAsync(RecruitmentHireViewModel vm)
    {
        var orgUnits = await db.OrgUnits.AsNoTracking().ToListAsync();
        vm.OrgUnits = new SelectList(
            OrgUnitDisplayService.BuildOptions(orgUnits, vm.PrimaryOrgUnitId),
            "Value", "Text", vm.PrimaryOrgUnitId?.ToString());

        var employeeRows = await db.Employees.AsNoTracking()
            .OrderBy(e => e.FullName)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .ToListAsync();

        var employees = employeeRows
            .Select(e => new
            {
                e.Id,
                Display = e.FullName + " (" + EmployeeNationalIdPresentation.ResolveDisplayNationalId(e, User) + ")"
            })
            .ToList();

        vm.Employees = new SelectList(employees, "Id", "Display", vm.ExistingEmployeeId);
    }
}
