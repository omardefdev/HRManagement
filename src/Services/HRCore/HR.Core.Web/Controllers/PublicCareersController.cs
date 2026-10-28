using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Middleware;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Web.Controllers;

[AllowAnonymous]
[Route("")]
public class PublicCareersController(
    HRCoreDbContext db,
    ICodeGenerator codeGenerator,
    RecruitmentApplicationIntakeService intake,
    UnifiedIntakeService unifiedIntake,
    CandidatePublicStatusService publicStatus,
    CandidateDocumentService candidateDocuments,
    IOptions<RecruitmentOptions> recruitmentOptions) : Controller
{
    [HttpGet("")]
    [HttpGet("index")]
    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var postings = await PublicPostings()
            .Include(p => p.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .Where(p => p.IsPublished && (p.ClosingDate == null || p.ClosingDate >= today))
            .Where(p => p.RequisitionId == null || (p.Requisition!.Status == RequisitionStatus.Published))
            .OrderByDescending(p => p.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();
        return View(postings);
    }

    // Guid-constrained: an unconstrained "{id}" captured every single-segment HR.Core URL (e.g. /WorkInbox, /RecruitmentAppeals).
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var posting = await PublicPostings()
            .Include(p => p.Requisition)
                .ThenInclude(r => r!.FundedPosition)
                    .ThenInclude(p => p!.OrgUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
        // HCM-026: a posting whose intake was closed by the job stays viewable (with a notice) while selection continues.
        var intakeClosed = posting?.Requisition?.IntakeClosedAt is not null
            || posting?.ClosingDate < DateOnly.FromDateTime(DateTime.UtcNow);
        if (posting is null || (!posting.IsPublished && !intakeClosed) || !IsCareersVisible(posting))
            return NotFound();

        await PopulateDetailsAsync(posting, new CitizenApplicationApplyViewModel { PublicJobPostingId = id }, intakeClosed);
        return View(posting);
    }

    [HttpPost("apply")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicCareersRateLimitExtensions.PolicyName)]
    public async Task<IActionResult> Apply(CitizenApplicationApplyViewModel vm)
    {
        if (!string.IsNullOrWhiteSpace(vm.Website))
        {
            TempData["Error"] = "تعذر معالجة الطلب.";
            return RedirectToAction(nameof(Index));
        }

        var posting = await PublicPostings()
            .Include(p => p.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .FirstOrDefaultAsync(p => p.Id == vm.PublicJobPostingId);
        if (posting is null || !posting.IsPublished || !IsCareersVisible(posting))
        {
            TempData["Error"] = "الإعلان غير متاح للتقديم";
            return RedirectToAction(nameof(Index));
        }

        var nationalIdHash = PiiMask.Sha256Hex(vm.NationalId.Trim());

        if (posting.RequisitionId is Guid requisitionId)
        {
            var validationError = await intake.ValidatePublishedRequisitionAsync(
                requisitionId, posting.ClosingDate, forPublicPortal: true);
            if (validationError is not null)
            {
                TempData["Error"] = validationError;
                return RedirectToAction(nameof(Index));
            }

            // HCM-030 (A2): also refuse when the person already has an open application on the requisition through
            // another channel (HR-entered or internal).
            if (await intake.HasDuplicateCitizenApplicationAsync(
                    vm.NationalId, requisitionId, nationalIdHash, forPublicPortal: true)
                || await unifiedIntake.HasOpenApplicationAsync(
                    requisitionId, vm.NationalId, nationalIdHash, forPublicPortal: true))
            {
                TempData["Error"] = "تم تقديم طلب سابق لنفس الوظيفة بهذا الرقم الوطني.";
                return RedirectToAction(nameof(Details), new { id = posting.Id });
            }
        }

        // HCM-030 (D1): profile checks (all fields optional).
        var qualifications = await unifiedIntake.GetQualificationOptionsAsync(posting.TenantId);
        vm.EducationLevelCode = string.IsNullOrWhiteSpace(vm.EducationLevelCode) ? null : vm.EducationLevelCode.Trim();
        if (vm.EducationLevelCode is not null
            && !qualifications.Any(q => string.Equals(q.Code, vm.EducationLevelCode, StringComparison.OrdinalIgnoreCase)))
            ModelState.AddModelError(nameof(vm.EducationLevelCode), "المؤهل العلمي غير صالح.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (vm.GraduationYear > today.Year)
            ModelState.AddModelError(nameof(vm.GraduationYear), "سنة التخرج لا يمكن أن تكون في المستقبل.");
        if (vm.BirthDate is DateOnly birthDate
            && RecruitmentEligibilityRules.AgeOn(birthDate, today) is < CitizenApplicationApplyViewModel.MinApplicantAge
                or > CitizenApplicationApplyViewModel.MaxApplicantAge)
            ModelState.AddModelError(nameof(vm.BirthDate), "تاريخ الميلاد غير صالح.");

        // HCM-030 (E5): attachments, requisition-linked postings only (a legacy posting has no recruitment application to
        // hold them, so its form shows no file inputs). Checked locally before anything is relayed.
        IReadOnlyList<(string DocumentType, IFormFile File, string FieldName)> attached =
            posting.Requisition is null ? [] : vm.AttachedFiles();
        IReadOnlyList<PreparedCandidateFile> prepared = [];
        if (attached.Count > 0)
        {
            var outcome = await CandidateDocumentFileValidator.PrepareAsync(
                attached.Select(a => new CandidateDocumentFileValidator.Input(a.DocumentType, a.File, a.FieldName)).ToList(),
                recruitmentOptions.Value.CandidateDocuments);
            foreach (var (field, error) in outcome.Errors)
                ModelState.AddModelError(field, error);
            prepared = outcome.Files;
        }

        if (!ModelState.IsValid)
        {
            await PopulateDetailsAsync(posting, vm, intakeClosed: false);
            return View("Details", posting);
        }

        // Upload before anything is staged (the code generator below saves this context): if File.Storage cannot take
        // every file, the apply fails as a whole and nothing is saved. Files stored before the failure stay unreferenced
        // (Pending) in File.Storage.
        IReadOnlyList<(PreparedCandidateFile File, CandidateDocumentUploadResult.Uploaded Upload)> uploads = [];
        if (prepared.Count > 0)
        {
            var uploaded = await candidateDocuments.UploadAllAsync(posting.TenantId, prepared);
            if (uploaded is null)
            {
                ModelState.AddModelError(string.Empty, CandidateDocumentService.UploadFailedMessage);
                await PopulateDetailsAsync(posting, vm, intakeClosed: false);
                return View("Details", posting);
            }
            uploads = uploaded;
        }

        var model = new CitizenApplication
        {
            TenantId = posting.TenantId,
            PublicJobPostingId = vm.PublicJobPostingId,
            RequisitionId = posting.RequisitionId,
            FullName = vm.FullName,
            NationalId = vm.NationalId.Trim(),
            NationalIdHash = nationalIdHash,
            Phone = vm.Phone,
            Email = vm.Email,
            CoverNote = vm.CoverNote,
            EducationLevelCode = vm.EducationLevelCode?.ToUpperInvariant(),
            Specialization = string.IsNullOrWhiteSpace(vm.Specialization) ? null : vm.Specialization.Trim(),
            GraduationYear = vm.GraduationYear,
            YearsOfExperience = vm.YearsOfExperience,
            BirthDate = vm.BirthDate,
            ReferenceNumber = (await codeGenerator.GenerateAsync(CodeGenerationRequests.CitizenReference())).Code,
            Status = CitizenApplicationStatus.Received,
            SubmittedAt = DateTime.UtcNow
        };
        db.CitizenApplications.Add(model);

        // PLAN-30 S2: the channel consent.
        // HCM-030 (A2): a requisition-linked application enters the recruitment path at Submitted in the same save.
        // Legacy postings (no requisition) keep the citizen row only.
        if (posting.Requisition is not null)
        {
            var application = unifiedIntake.CreateFromCitizen(
                model, posting.Requisition, uploads.Select(u => u.File.DocumentType));
            // HCM-030 (E3/E5): documents and their File.Storage reference registrations, in the same save.
            candidateDocuments.Stage(
                posting.TenantId, application.Id, model.Id, uploads, CandidateDocument.UploadedByCandidate);
        }
        await db.SaveChangesAsync();

        TempData["Reference"] = model.ReferenceNumber;
        return RedirectToAction(nameof(Submitted));
    }

    [HttpGet("submitted")]
    public IActionResult Submitted()
    {
        ViewBag.Reference = TempData["Reference"];
        return View();
    }

    [HttpGet("track")]
    public IActionResult Track() => View();

    [HttpPost("track")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicCareersRateLimitExtensions.PolicyName)]
    public async Task<IActionResult> Track(string referenceNumber, string nationalId)
    {
        var hash = PiiMask.Sha256Hex(nationalId.Trim());
        var application = await db.CitizenApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .Include(a => a.PublicJobPosting)
            .Include(a => a.Requisition)
            .AsNoTracking()
            .FirstOrDefaultAsync(a =>
                a.ReferenceNumber == referenceNumber
                && (a.NationalIdHash == hash || a.NationalId == nationalId.Trim()));

        if (application is null)
        {
            ViewBag.NotFound = true;
            return View();
        }

        // HCM-030 (B3): a linked application shows its real public status; legacy rows keep the citizen status.
        if (application.PromotedRecruitmentApplicationId is Guid appId)
            ViewBag.PublicView = await publicStatus.GetViewAsync(appId, forPublicPortal: true);
        else
            ViewBag.Timeline = BuildLegacyTimeline(application);
        // PLAN-30 S3: /careers/my (OTP) adds the candidate actions (offer reply, withdraw, appeal) on CandidatePublicView.
        return View("TrackResult", application);
    }

    [HttpGet("privacy")]
    public IActionResult Privacy() => View();

    /// <summary>Details view data: apply form, intake state and (HCM-030 D1/D2) qualifications and criteria labels.</summary>
    private async Task PopulateDetailsAsync(PublicJobPosting posting, CitizenApplicationApplyViewModel applyModel, bool intakeClosed)
    {
        ViewBag.IntakeClosed = intakeClosed;
        ViewBag.ApplyModel = applyModel;
        ViewBag.IsLegacyPosting = posting.RequisitionId is null;
        ViewBag.Qualifications = await unifiedIntake.GetQualificationOptionsAsync(posting.TenantId);
        ViewBag.Criteria = posting.Requisition is null ? null : unifiedIntake.CriteriaFor(posting.Requisition);
        // HCM-030 (E4/E5): required document types and the attachment limits (help text).
        ViewBag.RequiredDocumentTypes = CandidateDocumentTypes.Parse(posting.Requisition?.RequiredDocumentTypes);
        ViewBag.DocumentLimits = recruitmentOptions.Value.CandidateDocuments;
    }

    private IQueryable<PublicJobPosting> PublicPostings() =>
        db.PublicJobPostings
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal);

    private static bool IsCareersVisible(PublicJobPosting posting)
    {
        if (posting.RequisitionId is null)
            return true;

        return posting.Requisition is { Status: RequisitionStatus.Published };
    }

    /// <summary>
    /// Citizen-status timeline of legacy rows (no requisition, or not yet linked). Linked rows use
    /// <see cref="CandidatePublicStatusService"/>; the interview and hire steps, which only meant "promoted", are gone.
    /// </summary>
    private static IReadOnlyList<(string Label, bool Reached, string? Detail)> BuildLegacyTimeline(CitizenApplication app)
    {
        var status = app.Status;
        bool reached(CitizenApplicationStatus min) => (int)status >= (int)min;

        return
        [
            ("تم استلام الطلب", true, app.SubmittedAt.ToString("yyyy-MM-dd")),
            ("قيد المراجعة", reached(CitizenApplicationStatus.UnderReview), null),
            ("القائمة المختصرة", reached(CitizenApplicationStatus.Shortlisted), null),
            ("القرار", status is CitizenApplicationStatus.Accepted or CitizenApplicationStatus.Rejected,
                status switch
                {
                    CitizenApplicationStatus.Accepted => "مقبول",
                    CitizenApplicationStatus.Rejected => "مرفوض — يمكن الاستئناف عبر الجهة",
                    _ => null
                })
        ];
    }
}
