using HR.Contracts.Application.Features.Lookup;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using HR.Contracts.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using MediatR;
using HR.Contracts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.Controllers;

// PLAN-51 (§4, P-6): reads name the module policy (the management approver reads HR.Contracts); every write names HrContracts.Write or HrContracts.Approve.
[Authorize(Policy = HrManagementPolicies.ModuleHrContracts)]
[OrgScoped(typeof(HR.Contracts.Domain.Entities.SeparationRecord), OutOfScope = OrgScopeAnswer.Forbid)]
public class SeparationRecordsController : Controller
{
    private readonly IMediator _mediator;
    private readonly IHrCoreLookupClient _hrCoreLookup;
    private readonly ContractsDbContext _db;

    public SeparationRecordsController(IMediator mediator, IHrCoreLookupClient hrCoreLookup, ContractsDbContext db)
    {
        _mediator = mediator;
        _hrCoreLookup = hrCoreLookup;
        _db = db;
    }

    public async Task<IActionResult> Index(SeparationType? separationType, Guid? employeeId, SeparationStatus? status)
    {
        // QA-034 (IAM-002): branch scope. A restricted user gets 403 for an employee outside the scope and lists only
        // employees inside it (no snapshot = outside, fail-closed); an unrestricted user is unchanged.
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;
        if (employeeId is { } scopedEmployeeId
            && await this.ForbidIfEmployeeOutOfScopeAsync(
                User, scopedEmployeeId, _db.EmployeeOrgUnitSnapshots, _db.OrgUnitSnapshots) is { } employeeDenied)
            return employeeDenied;

        // The in-scope employees go into the query, so the page limit applies to the branch's own rows (review E-1).
        var inScope = scope.MustFilter
            ? await _db.EmployeeOrgUnitSnapshots.EmployeeIdsInScope(scope).ToListAsync()
            : null;
        var items = await _mediator.Send(new GetSeparationRecordsQuery(inScope));
        if (separationType.HasValue)
            items = items.Where(x => x.SeparationType == separationType.Value).ToList();
        if (employeeId.HasValue)
            items = items.Where(x => x.EmployeeId == employeeId.Value).ToList();
        if (status.HasValue)
            items = items.Where(x => x.Status == status.Value).ToList();

        ViewBag.SeparationTypeFilter = separationType;
        ViewBag.EmployeeId = employeeId;
        ViewBag.StatusFilter = status;
        var nameIds = items.Select(x => x.EmployeeId).ToList();
        if (employeeId.HasValue)
            nameIds.Add(employeeId.Value);
        ViewBag.EmployeeNames = await ResolveEmployeeNamesAsync(nameIds);
        return View(items);
    }

    /// <summary>PLAN-60 (P-11): the record's status, approval, decision, items by mode and the actions its status allows.</summary>
    public async Task<IActionResult> Details(Guid id, [FromServices] SeparationPanelBuilder panels)
    {
        var panel = await panels.BuildAsync(id, User, HttpContext.RequestAborted);
        return panel is null ? NotFound() : View(panel);
    }

    private async Task<Dictionary<Guid, string>> ResolveEmployeeNamesAsync(IEnumerable<Guid> ids)
    {
        var result = ids.Distinct().ToDictionary(id => id, _ => "غير متاح");
        try
        {
            foreach (var row in await _hrCoreLookup.GetEmployeesByIdsAsync(result.Keys.ToList()))
                result[row.Id] = row.Label;
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<SeparationRecordsController>>()
                .LogWarning(ex, "Employee name lookup failed for separation records.");
        }
        return result;
    }

    private async Task PopulateContractsAsync(SeparationRecordCreateViewModel vm)
    {
        // PLAN-60 (§6): the chosen employee's own active or pending contracts, inside PLAN-52's scope.
        var contracts = await _mediator.Send(new GetEmployeeContractsLookupQuery(vm.EmployeeId, await ContractPickList.InScopeAsync(HttpContext)));
        var items = contracts.Select(c => new { Id = c.Id, Text = c.Label });
        vm.Contracts = new SelectList(items, "Id", "Text", vm.ContractId);
    }

    /// <summary>PLAN-60: the form reloads its contract list when the employee changes (<c>wwwroot/js/separation-contracts.js</c>).</summary>
    [HttpGet]
    public async Task<IActionResult> EmployeeContracts(Guid employeeId)
    {
        var contracts = await _mediator.Send(new GetEmployeeContractsLookupQuery(employeeId, await ContractPickList.InScopeAsync(HttpContext)));
        return Json(contracts.Select(c => new { id = c.Id, label = c.Label }));
    }

    public async Task<IActionResult> Create()
    {
        var vm = new SeparationRecordCreateViewModel();
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SeparationRecordCreateViewModel vm)
    {
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);
        // PLAN-60 (§6): the one factory checks the employee (PLAN-42's gate with his status), his open records and his contract.
        var created = await _mediator.Send(new CreateSeparationRecordCommand(MapCreate(vm)));
        if (!created.Success)
        {
            ModelState.AddModelError(string.Empty, created.Error ?? "تعذّر إنشاء سجل الفصل.");
            return View(vm);
        }

        TempData["Success"] = "تم تسجيل سجل الفصل";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var r = await _mediator.Send(new GetSeparationRecordByIdQuery(id));
        if (r == null) return NotFound();

        var vm = new SeparationRecordEditViewModel
        {
            Id = r.Id,
            EmployeeId = r.EmployeeId,
            ContractId = r.ContractId,
            SeparationType = r.SeparationType,
            EffectiveDate = r.EffectiveDate,
            Reason = r.Reason,
            Documents = r.Documents,
            Status = r.Status
        };
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SeparationRecordEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);
        var (updated, error) = await _mediator.Send(new UpdateSeparationRecordCommand(id, MapEdit(vm)));
        if (!updated)
        {
            if (error is null) return NotFound();
            ModelState.AddModelError(string.Empty, error);
            return View(vm);
        }
        TempData["Success"] = "تم تحديث سجل الفصل";
        return RedirectToAction(nameof(Details), new { id });
    }

    // PLAN-60 (§5): the execution, a second person's act (PLAN-51's HrContracts.Approve). A record is never deleted (FUN-002): the
    // list's delete became the cancellation, with a reason.

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Execute(Guid id)
    {
        var result = await _mediator.Send(new ExecuteSeparationCommand(id, ParseUserId()));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Warning"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = "نُفِّذ الفصل: انتهى العقد بتاريخ النفاذ وبدأت رحلة الأطراف.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var result = await _mediator.Send(new CancelSeparationCommand(id, reason, ParseUserId()));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Warning"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = "أُلغي سجل الفصل.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── PLAN-60 (§4): the clearance items. A manual item by hand with evidence (the maker); an exception by two people — the maker
    // asks, a second holder of HrContractsApprove approves. No form names an approver: the actor is always the session's.

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteManualStep(Guid id, HR.Contracts.Domain.Enums.SeparationStep step, string? evidenceRef, string? notes)
    {
        var (success, error) = await _mediator.Send(new CompleteManualStepCommand(
            id, step, evidenceRef, null, notes, ParseUserId(), User.Identity?.Name));
        if (!success && error is null) return NotFound();
        TempData[success ? "Success" : "Warning"] = success ? "أُكمل البند بدليله." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestStepException(Guid id, HR.Contracts.Domain.Enums.SeparationStep step, string? reason)
    {
        var (success, error) = await _mediator.Send(new RequestStepExceptionCommand(id, step, reason, ParseUserId()));
        if (!success && error is null) return NotFound();
        TempData[success ? "Success" : "Warning"] = success ? "سُجّل طلب الاستثناء، وينتظر اعتماد شخص ثانٍ." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveStepException(Guid id, HR.Contracts.Domain.Enums.SeparationStep step)
    {
        var (success, error) = await _mediator.Send(new ApproveStepExceptionCommand(id, step, ParseUserId()));
        if (!success && error is null) return NotFound();
        TempData[success ? "Success" : "Warning"] = success ? "اعتُمد الاستثناء: البند مستثنى." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── PLAN-60 (§3): the numbered decision, issued by a second person (never the creator).

    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IssueDecision(Guid id)
    {
        var result = await _mediator.Send(new IssueSeparationDecisionCommand(id, ParseUserId(), User.Identity?.Name));
        TempData[result.Success ? "Success" : "Warning"] = result.Success
            ? (result.AlreadyIssued ? $"القرار صادر مسبقاً: {result.Decision!.DecisionNumber}" : $"صدر القرار {result.Decision!.DecisionNumber}")
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── PLAN-60 (§2): the maker's acts on the approval. The approval itself is in HR.Core's inbox: HR.Contracts has no approval button.

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id)
    {
        var result = await _mediator.Send(new SubmitSeparationCommand(id, ParseUserId()));
        if (result.Success)
            TempData["Success"] = result.Finished ? "انتهى مسار الاعتماد وطُبّقت نتيجته." : "قُدِّم سجل الفصل للاعتماد في صندوق العمل.";
        else
            TempData["Warning"] = result.Error ?? "تعذّر تقديم سجل الفصل.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(Guid id, string? reason)
    {
        var result = await _mediator.Send(new WithdrawSeparationCommand(id, ParseUserId(), reason));
        if (result.Success)
            TempData["Success"] = "سُحب سجل الفصل من الاعتماد وعاد مسودة.";
        else if (result.Error is null && result.FinalOutcome is { } outcome)
            // AlreadyTerminal: the instance had ended; its outcome was applied, never a return to draft.
            TempData["Warning"] = $"انتهى مسار الاعتماد قبل السحب: طُبّقت نتيجته «{OutcomeLabel(outcome)}».";
        else
            TempData["Warning"] = result.Error ?? "تعذّر سحب سجل الفصل.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(Guid id)
    {
        var (success, error) = await _mediator.Send(new ReopenSeparationCommand(id, ParseUserId()));
        if (!success && error is null) return NotFound();
        TempData[success ? "Success" : "Warning"] = success ? "أُعيد فتح سجل الفصل مسودة." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private static string OutcomeLabel(string outcome) => outcome switch
    {
        HRManagement.ServiceDefaults.Workflow.CrossService.WorkflowApprovalOutcomes.Approved => "معتمد",
        HRManagement.ServiceDefaults.Workflow.CrossService.WorkflowApprovalOutcomes.Rejected => "مرفوض",
        _ => "مسحوب"
    };

    private Guid? ParseUserId()
    {
        var value = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static SeparationRecordDto MapCreate(SeparationRecordCreateViewModel vm) =>
        new(null, vm.EmployeeId, vm.ContractId, vm.SeparationType, vm.EffectiveDate, vm.Reason, vm.Documents);

    private static SeparationRecordDto MapEdit(SeparationRecordEditViewModel vm) =>
        new(vm.Id, vm.EmployeeId, vm.ContractId, vm.SeparationType, vm.EffectiveDate, vm.Reason, vm.Documents);
}
