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
    RecruitmentAppealWorkflowUiService appealWorkflowUiService,
    RecruitmentInterviewService interviewService,
    IWorkflowInstanceActionService workflowActions,
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
            .Include(a => a.CriteriaVersion)
            .Include(a => a.HiredEmployee)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (app is null) return NotFound();

        var activeAppeal = await db.RecruitmentAppeals
            .AsNoTracking()
            .Where(a => a.RecruitmentApplicationId == id)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        var interviews = await interviewService.GetForApplicationAsync(id);

        IReadOnlyList<RecruitmentApplication> rankBoard = [];
        SelectList? criteriaOptions = null;

        if (app.RequisitionId is Guid reqId)
        {
            rankBoard = await selection.GetRankedCandidatesAsync(reqId);
            var criteria = await db.RecruitmentAssessmentCriteria
                .AsNoTracking()
                .Where(c => c.RequisitionId == reqId)
                .OrderByDescending(c => c.VersionNumber)
                .ToListAsync();
            criteriaOptions = new SelectList(
                criteria.Select(c => new { c.Id, Label = $"v{c.VersionNumber} — {c.Title}" }),
                "Id", "Label", app.CriteriaVersionId);
        }

        if (!string.IsNullOrWhiteSpace(app.Requisition?.LegalSourceId))
        {
            var legalSource = await legislationProvider.GetByIdAsync(app.Requisition.LegalSourceId);
            ViewBag.RequisitionLegalSourceLabel = legalSource?.DisplayLabel ?? app.Requisition.LegalSourceId;
        }
        else
        {
            ViewBag.RequisitionLegalSourceLabel = null;
        }

        var appealWorkflow = await appealWorkflowUiService.BuildPanelAsync(activeAppeal, User);
        var showDirectDecide = activeAppeal is not null
            && activeAppeal.Status == RecruitmentAppealStatus.Pending
            && activeAppeal.WorkflowInstanceId is null;

        return View(new RecruitmentApplicationDetailsViewModel
        {
            Application = app,
            RankBoard = rankBoard,
            AllowedStatusTransitions = SelectionWorkflowRules.GetAllowedTargets(app, legacyFlag),
            CriteriaOptions = criteriaOptions,
            LegacyHireWithoutOffer = legacyFlag,
            CanHire = RecruitmentWorkflowRules.CanHire(app, legacyFlag),
            AppealPending = RecruitmentAppealWorkflowRules.HasPendingAppeal(app),
            CanRank = RecruitmentAppealWorkflowRules.CanRank(app),
            CanOffer = RecruitmentAppealWorkflowRules.CanOffer(app),
            CanAppeal = RecruitmentAppealWorkflowRules.CanAppeal(app, DateTime.UtcNow),
            ActiveAppeal = activeAppeal,
            AppealWorkflow = appealWorkflow,
            ShowDirectAppealDecide = showDirectDecide,
            InterviewSlots = interviews
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RecordAssessment(RecordAssessmentForm form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "يرجى التحقق من بيانات التقييم.";
            return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
        }

        var (success, error) = await selection.RecordAssessmentAsync(
            form.ApplicationId, form.CriteriaVersionId, form.Score);
        TempData[success ? "Success" : "Error"] = success ? "تم تسجيل التقييم" : error;
        return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
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

        var (success, error) = await selection.AssignRankAsync(form.ApplicationId, form.Rank);
        TempData[success ? "Success" : "Error"] = success ? "تم تعيين الترتيب" : error;
        return RedirectToAction(nameof(Details), new { id = form.ApplicationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SetStatus(Guid id, RecruitmentApplicationStatus status)
    {
        var legacyFlag = ResolveLegacyHireWithoutOffer();
        var (success, error) = await selection.TransitionStatusAsync(id, status, legacyFlag);
        TempData[success ? "Success" : "Error"] = success ? "تم تحديث حالة الطلب" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ReopenAppeal(Guid id, string appealNotes)
    {
        var (success, error) = await selection.ReopenAppealAsync(id, appealNotes, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تم إعادة فتح الاستئناف" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> FileAppeal(Guid id, string reason, Guid independentReviewerId)
    {
        var (appeal, error) = await appealService.FileAppealAsync(
            id, reason, independentReviewerId, ParseUserId(User), User.Identity?.Name);
        TempData[appeal is null ? "Error" : "Success"] = error ?? "تم تقديم الاستئناف";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> ApproveAppealWorkflow(Guid id, RecruitmentAppealWorkflowActionViewModel vm)
    {
        if (id != vm.ApplicationId) return BadRequest();

        var appeal = await db.RecruitmentAppeals.FindAsync(vm.AppealId);
        if (appeal is null) return NotFound();
        if (appeal.WorkflowInstanceId is not Guid workflowId)
        {
            TempData["Error"] = "لا يوجد مسار مراجعة مرتبط بهذا الاستئناف.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await workflowActions.ApproveAsync(
            workflowId,
            CreateWorkflowActor(User),
            vm.Comments,
            targetStepLevel: null);

        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "تم اعتماد مراجعة الاستئناف"
            : result.Error ?? "تعذّر اعتماد الاستئناف.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> RejectAppealWorkflow(Guid id, RecruitmentAppealWorkflowActionViewModel vm)
    {
        if (id != vm.ApplicationId) return BadRequest();

        var appeal = await db.RecruitmentAppeals.FindAsync(vm.AppealId);
        if (appeal is null) return NotFound();
        if (appeal.WorkflowInstanceId is not Guid workflowId)
        {
            TempData["Error"] = "لا يوجد مسار مراجعة مرتبط بهذا الاستئناف.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await workflowActions.RejectAsync(
            workflowId,
            CreateWorkflowActor(User),
            vm.Comments);

        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "تم رفض الاستئناف"
            : result.Error ?? "تعذّر رفض الاستئناف.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> DecideAppeal(Guid appealId, Guid applicationId, bool upheld, string decision)
    {
        var actorId = ParseUserId(User) ?? Guid.Empty;
        var (success, error) = await appealService.DecideAppealAsync(appealId, upheld, decision, actorId);
        TempData[success ? "Success" : "Error"] = success ? "تم البت في الاستئناف" : error;
        return RedirectToAction(nameof(Details), new { id = applicationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ScheduleInterview(
        Guid applicationId,
        DateTime scheduledAt,
        string? location,
        string? panelNotes)
    {
        var (slot, error) = await interviewService.ScheduleAsync(applicationId, scheduledAt, location, panelNotes);
        TempData[slot is null ? "Error" : "Success"] = error ?? "تم جدولة المقابلة";
        return RedirectToAction(nameof(Details), new { id = applicationId });
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
            form.ExpiryDate,
            ParseUserId(User));

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

    private PolicyEnforcementMode ResolvePolicyMode()
    {
        var raw = configuration["PolicyEngine:Core.Recruitment"]
            ?? configuration["PolicyEngine:DefaultMode"]
            ?? nameof(PolicyEnforcementMode.Off);
        return Enum.TryParse<PolicyEnforcementMode>(raw, true, out var mode)
            ? mode
            : PolicyEnforcementMode.Off;
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

    private static WorkflowActionActor CreateWorkflowActor(ClaimsPrincipal user)
    {
        var userId = ParseUserId(user);
        var userName = user.FindFirstValue("preferred_username") ?? user.Identity?.Name;
        var actorRole = user.FindFirstValue(ClaimTypes.Role) ?? user.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
        return new WorkflowActionActor(userId, userName, actorRole);
    }
}
