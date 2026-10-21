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

    EstablishmentScopeService establishmentScope) : Controller

{

    public async Task<IActionResult> Index(OfferStatus? status, string? searchTerm)

    {

        var query = db.OfferLetters

            .AsNoTracking()

            .Include(o => o.RecruitmentApplication)

            .Include(o => o.FundedPosition)

            .AsQueryable();



        if (status is OfferStatus s)

            query = query.Where(o => o.Status == s);



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

            SearchTerm = searchTerm

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

        ViewData["OfferId"] = offer.Id;

        return View(vm);

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

    public async Task<IActionResult> AcceptOffer(Guid id)

    {

        var (success, error) = await offerService.AcceptAsync(id);

        TempData[success ? "Success" : "Error"] = success ? "تم قبول خطاب العرض" : error;

        return RedirectToAction(nameof(Details), new { id });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> DeclineOffer(Guid id)

    {

        var (success, error) = await offerService.DeclineAsync(id);

        TempData[success ? "Success" : "Error"] = success ? "تم رفض خطاب العرض" : error;

        return RedirectToAction(nameof(Details), new { id });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Revoke(Guid id)

    {

        var (success, error) = await offerService.RevokeAsync(id);

        TempData[success ? "Success" : "Error"] = success ? "تم إلغاء خطاب العرض" : error;

        return RedirectToAction(nameof(Details), new { id });

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


