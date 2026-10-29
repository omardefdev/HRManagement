using System.Security.Claims;

using HR.Core.Domain.Entities;

using HR.Core.Domain.Enums;

using HR.Core.Domain.Services;

using HR.Core.Infrastructure.Data;

using HR.Core.Infrastructure.Services.Recruitment;

using HR.Core.Infrastructure.Services.Workflow;

using HR.Core.Web.Services;

using HR.Core.Web.ViewModels;

using HRManagement.ServiceDefaults;

using HRManagement.SharedKernel.Legal;

using HRManagement.SharedKernel.ViewModels;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;



namespace HR.Core.Web.Controllers;



[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]

public class OfferLettersController(

    HRCoreDbContext db,

    RecruitmentOfferService offerService,

    OfferLetterWorkflowUiService workflowUiService,

    IWorkflowInstanceActionService workflowActions,

    ILegislationRegisterProvider legislationProvider,

    EstablishmentScopeService establishmentScope,

    ReserveListService reserveList,
    OfferLetterDocumentService documents) : Controller

{

    public async Task<IActionResult> Index(OfferStatus? status, string? searchTerm, bool meritOverride = false)

    {

        var query = db.OfferLetters

            .AsNoTracking()

            .Include(o => o.RecruitmentApplication)

            .Include(o => o.FundedPosition)

            .AsQueryable();



        if (status is OfferStatus s)

            query = query.Where(o => o.Status == s);

        if (meritOverride)
            query = query.Where(o => o.IsMeritOverride || o.GradeDeviationReason != null || o.SalaryUnverified);



        if (!string.IsNullOrWhiteSpace(searchTerm))

        {

            var term = searchTerm.Trim();

            query = query.Where(o =>

                o.OfferNumber.Contains(term)

                || (o.RecruitmentApplication != null && o.RecruitmentApplication.ApplicantFullName.Contains(term)));

        }



        var offers = await query

            .OrderByDescending(o => o.CreatedAt)

            .Take(SearchFilterBase.MaxPageSize)

            .ToListAsync();



        return View(new OfferLettersIndexViewModel

        {

            Offers = offers,

            Status = status,

            SearchTerm = searchTerm,

            MeritOverrideOnly = meritOverride

        });

    }



    public async Task<IActionResult> Details(Guid id)

    {

        var offer = await db.OfferLetters

            .AsNoTracking()

            .Include(o => o.RecruitmentApplication)

                .ThenInclude(a => a!.Requisition)

            .Include(o => o.FundedPosition)

            .FirstOrDefaultAsync(o => o.Id == id);



        if (offer is null) return NotFound();



        if (!string.IsNullOrWhiteSpace(offer.LegalSourceId))

        {

            var legalSource = await legislationProvider.GetByIdAsync(offer.LegalSourceId);

            ViewBag.LegalSourceLabel = legalSource?.DisplayLabel ?? offer.LegalSourceId;

        }

        else

        {

            ViewBag.LegalSourceLabel = "—";

        }



        var vm = await workflowUiService.BuildDetailsAsync(offer, User);

        // HCM-027 (F3): after a decline/expiry/revoke, suggest the next candidate by merit.

        if (offer.Status is OfferStatus.Declined or OfferStatus.Expired or OfferStatus.Revoked

            && offer.RecruitmentApplication?.RequisitionId is Guid requisitionId)

            vm.NextCandidate = await reserveList.GetNextCandidateSuggestionAsync(requisitionId);

        // HCM-029 (D2): download link + integrity check against the hash stored at issue.
        vm.Document = await documents.GetAsync(offer.Id);
        ViewData["OfferId"] = offer.Id;

        return View(vm);

    }



    /// <summary>PLAN-25 (G-02): gives an offer left PendingApproval without a workflow its approval workflow.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RestartApproval(Guid id)
    {
        var (success, error) = await offerService.RestartApprovalAsync(id, ParseUserId(User), User.Identity?.Name);
        TempData[success ? "Success" : "Error"] = success ? "تمت إعادة بدء مسار اعتماد العرض" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> SubmitOffer(Guid id)

    {

        var (success, error) = await offerService.SubmitForApprovalAsync(

            id, ParseUserId(User), User.Identity?.Name);

        TempData[success ? "Success" : "Error"] = success ? "تم تقديم العرض للاعتماد" : error;

        return RedirectToAction(nameof(Details), new { id });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]

    public async Task<IActionResult> ApproveWorkflow(Guid id, OfferLetterWorkflowActionViewModel vm)

    {

        if (id != vm.OfferId) return BadRequest();



        var offer = await db.OfferLetters
            .Include(o => o.FundedPosition)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (offer is null) return NotFound();

        var orgBlock = WorkflowInboxOrgScope.GetBlockReason(
            await establishmentScope.GetScopeAsync(User),
            offer.FundedPosition?.OrgUnitId);
        if (orgBlock is not null)
        {
            TempData["Error"] = orgBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (offer.WorkflowInstanceId is not Guid workflowId)

        {

            TempData["Error"] = "لا يوجد مسار اعتماد مرتبط بهذا العرض.";

            return RedirectToAction(nameof(Details), new { id });

        }



        var result = await workflowActions.ApproveAsync(

            workflowId,

            CreateWorkflowActor(User),

            vm.Comments,

            targetStepLevel: null);



        TempData[result.Success ? "Success" : "Error"] = result.Success

            ? "تم اعتماد خطوة مسار خطاب العرض"

            : result.Error ?? "تعذّر اعتماد العرض.";



        return RedirectToAction(nameof(Details), new { id });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]

    public async Task<IActionResult> RejectWorkflow(Guid id, OfferLetterWorkflowActionViewModel vm)

    {

        if (id != vm.OfferId) return BadRequest();



        var offer = await db.OfferLetters
            .Include(o => o.FundedPosition)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (offer is null) return NotFound();

        var orgBlock = WorkflowInboxOrgScope.GetBlockReason(
            await establishmentScope.GetScopeAsync(User),
            offer.FundedPosition?.OrgUnitId);
        if (orgBlock is not null)
        {
            TempData["Error"] = orgBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (offer.WorkflowInstanceId is not Guid workflowId)

        {

            TempData["Error"] = "لا يوجد مسار اعتماد مرتبط بهذا العرض.";

            return RedirectToAction(nameof(Details), new { id });

        }



        var result = await workflowActions.RejectAsync(

            workflowId,

            CreateWorkflowActor(User),

            vm.Comments);



        TempData[result.Success ? "Success" : "Error"] = result.Success

            ? "تم رفض خطاب العرض وإرجاعه إلى المسودة"

            : result.Error ?? "تعذّر رفض العرض.";



        return RedirectToAction(nameof(Details), new { id });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AcceptOffer(
        Guid id,
        AcceptOfferOnBehalfForm form,
        [FromServices] OfferOnBehalfService onBehalf,
        CancellationToken ct)
    {
        // HCM-030 (PLAN-30 J): «قبول نيابةً عن المرشح» — a paper acceptance recorded by HR, restricted by a mandatory reason
        // (≥ 20 characters), the signed evidence and the AcceptOfferOnBehalf audit row; the candidate accepts on /careers/my.
        // Policy stays HrCoreWrite: HrCoreApprove is not narrower (it adds HrManagementApprover), so it would widen access.
        var (success, error) = await onBehalf.AcceptAsync(id, form.Reason, form.EvidenceFile, ParseUserId(User), ct);
        TempData[success ? "Success" : "Error"] = success ? "تم تسجيل قبول خطاب العرض نيابةً عن المرشح" : error;
        return RedirectToAction(nameof(Details), new { id });
    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> DeclineOffer(
        Guid id,
        DeclineOfferForm form,
        [FromServices] OfferOnBehalfService onBehalf,
        CancellationToken ct)
    {
        // HCM-027: HR records a paper decline on the candidate's behalf (HrOnBehalf); the candidate declines on /careers/my
        // (CandidatePortalService, Candidate). Both use DeclineAndDraftNextAsync, so the next-candidate draft is the same.
        // HCM-030 (J): the signed paper decline is optional evidence.
        var result = await onBehalf.DeclineAsync(id, form.Reason, form.ReasonCategory, form.EvidenceFile, ParseUserId(User), ct);

        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "تم رفض خطاب العرض" + NextOfferSuffix(result.NextDraft, result.NextDraftError)
            : result.Error;

        return RedirectToAction(nameof(Details), new { id });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Revoke(Guid id, RevokeOfferForm form)

    {

        var (success, error) = await offerService.RevokeAsync(id, form.Reason, form.RevertToRanked, ParseUserId(User));

        var suffix = string.Empty;

        if (success)
        {
            var (draft, draftError) = await offerService.DraftNextOfferAfterSettlementAsync(id, ParseUserId(User));
            suffix = NextOfferSuffix(draft, draftError);
        }

        TempData[success ? "Success" : "Error"] = success ? "تم إلغاء خطاب العرض" + suffix : error;

        return RedirectToAction(nameof(Details), new { id });

    }



    /// <summary>
    /// HCM-029 (D3): official offer letter PDF, generated on demand, only for Issued or Accepted offers.
    /// HCM-030 (PLAN-30 I): the candidate downloads the same PDF from /careers/my (CandidatePortalService.GetOfferLetterAsync).
    /// PLAN-29 follow-up: archive in File.Storage
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var document = await documents.GetAsync(id, ct);
        if (document is null) return NotFound();

        if (!document.CanDownload)
        {
            TempData["Error"] = OfferLetterDocumentService.NotDownloadableMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "OfferLetters/Document");
        var pdf = OfferLetterDocument.Generate(document);
        return File(pdf, "application/pdf", $"offer-letter-{document.Data.OfferNumber}.pdf");
    }

    /// <summary>
    /// HCM-030 (PLAN-30 J): streams the signed paper acceptance/decline HR attached, through HR.Core's service identity
    /// (HR users lack ModuleFileStorage), like RecruitmentApplications/CandidateDocument. Clean files only.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Evidence(
        Guid id,
        OfferEvidenceKind kind,
        [FromServices] OfferOnBehalfService onBehalf,
        CancellationToken ct)
    {
        var (offer, result) = await onBehalf.OpenEvidenceAsync(id, kind, ct);
        switch (result)
        {
            case CandidateFileContentResult.Found found:
                return File(found.Content, found.ContentType,
                    found.FileName ?? $"offer-{offer!.OfferNumber}-{kind.ToString().ToLowerInvariant()}-evidence");
            case CandidateFileContentResult.Unavailable when offer is not null:
                TempData["Error"] = CandidateDocumentService.DownloadUnavailableMessage;
                return RedirectToAction(nameof(Details), new { id });
            default:
                return NotFound();
        }
    }

    /// <summary>HCM-027 (F3): Recruitment:AutoDraftNextOffer — suffix for the success message.</summary>

    private static string NextOfferSuffix(OfferLetter? draft, string? error)

    {

        if (draft is not null)

            return $" — أُنشئت مسودة عرض للمرشح التالي بالجدارة ({draft.OfferNumber}).";

        return error is null ? string.Empty : $" — تعذّر إنشاء مسودة للمرشح التالي: {error}";

    }

    private static WorkflowActionActor CreateWorkflowActor(ClaimsPrincipal user)

    {

        var userId = ParseUserId(user);

        var userName = user.FindFirstValue("preferred_username") ?? user.Identity?.Name;

        var actorRole = user.FindFirstValue(ClaimTypes.Role) ?? user.Claims.FirstOrDefault(c => c.Type == "role")?.Value;

        return new WorkflowActionActor(userId, userName, actorRole);

    }



    private static Guid? ParseUserId(ClaimsPrincipal user)

    {

        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        return Guid.TryParse(v, out var g) ? g : null;

    }

}


