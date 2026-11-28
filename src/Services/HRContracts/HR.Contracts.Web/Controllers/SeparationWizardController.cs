using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.Lookup;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Web.ViewModels;
using HR.Contracts.Application.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.Controllers;

// PLAN-51 (§4, P-6): reads name the module policy (the management approver reads HR.Contracts); every write names HrContracts.Write or HrContracts.Approve.
[Authorize(Policy = HrManagementPolicies.ModuleHrContracts)]
[OrgScoped(typeof(HR.Contracts.Domain.Entities.SeparationRecord), OutOfScope = OrgScopeAnswer.Forbid)]
public class SeparationWizardController : Controller
{
    private readonly IContractsRepository _repo;
    private readonly IMediator _mediator;
    public SeparationWizardController(
        IContractsRepository repo,
        IMediator mediator)
    {
        _repo = repo;
        _mediator = mediator;
    }

    private Guid? ParseUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Step1()
    {
        var vm = new SeparationRecordCreateViewModel
        {
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Step1(SeparationRecordCreateViewModel vm)
    {
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);

        // PLAN-60 (§6): the wizard creates through the one factory — the employee, his open records and his contract are checked there.
        var created = await _mediator.Send(new CreateSeparationRecordCommand(
            new HR.Contracts.Application.Models.SeparationRecordDto(
                null, vm.EmployeeId, vm.ContractId, vm.SeparationType, vm.EffectiveDate, vm.Reason, vm.Documents),
            "معالج الفصل — تسجيل"), HttpContext.RequestAborted);
        if (!created.Success)
        {
            ModelState.AddModelError(string.Empty, created.Error ?? "تعذّر إنشاء سجل الفصل.");
            return View(vm);
        }

        TempData["Success"] = "تم إنشاء سجل الفصل";
        return RedirectToAction(nameof(Step2), new { id = created.Record!.Id });
    }

    public async Task<IActionResult> Step2(Guid id, [FromServices] SeparationPanelBuilder panels)
    {
        var panel = await panels.BuildAsync(id, User, HttpContext.RequestAborted);
        return panel is null ? NotFound() : View(panel);
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Step2Confirm(Guid id)
    {
        var record = await _repo.SeparationRecords.FirstOrDefaultAsync(r => r.Id == id);
        if (record is null) return NotFound();

        // PLAN-60 (§4): the execution waits for the pre-execution items only (the decision, the notification); the parties' items follow it.
        var checklist = await _repo.SeparationChecklists
            .Where(c => c.SeparationRecordId == id)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();
        var incomplete = SeparationWorkflowRules.GetOpenPreExecutionSteps(checklist)
            .Where(c => c.Step != HR.Contracts.Domain.Enums.SeparationStep.DecisionIssued || record.Status != HR.Contracts.Domain.Enums.SeparationStatus.Approved)
            .ToList();
        if (record.Status is not (HR.Contracts.Domain.Enums.SeparationStatus.Approved or HR.Contracts.Domain.Enums.SeparationStatus.Executing
                or HR.Contracts.Domain.Enums.SeparationStatus.AwaitingIntervention or HR.Contracts.Domain.Enums.SeparationStatus.Completed))
        {
            TempData["Warning"] = "لا يمكن المتابعة إلى التنفيذ قبل اعتماد سجل الفصل في صندوق العمل.";
            return RedirectToAction(nameof(Step2), new { id });
        }
        if (incomplete.Count > 0)
        {
            TempData["Warning"] = $"لا يمكن المتابعة: بنود قبل التنفيذ لم تُكمل: {string.Join("، ", incomplete.Select(c => SeparationWorkflowRules.DisplayName(c.Step)))}";
            return RedirectToAction(nameof(Step2), new { id });
        }

        return RedirectToAction(nameof(Step3), new { id });
    }

    public async Task<IActionResult> Step3(Guid id, [FromServices] SeparationPanelBuilder panels)
    {
        var panel = await panels.BuildAsync(id, User, HttpContext.RequestAborted);
        return panel is null ? NotFound() : View(panel);
    }

    // PLAN-60 (P-10, D-51-2): the execution is a checker's act, as the publication was: HrContracts.Approve, not .Write.
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Step3Execute(Guid id)
    {
        var (success, error) = await _mediator.Send(new ExecuteSeparationCommand(id, ParseUserId()));
        if (!success)
        {
            TempData["Error"] = error ?? "تعذر تنفيذ الفصل";
            return RedirectToAction(nameof(Step3), new { id });
        }

        TempData["Success"] = "نُفِّذ الفصل: انتهى العقد بتاريخ النفاذ وبدأت رحلة الأطراف.";
        return RedirectToAction(nameof(Step4), new { id });
    }

    public async Task<IActionResult> Step4(Guid id, [FromServices] SeparationPanelBuilder panels)
    {
        var panel = await panels.BuildAsync(id, User, HttpContext.RequestAborted);
        return panel is null ? NotFound() : View(panel);
    }

    private async Task PopulateContractsAsync(SeparationRecordCreateViewModel vm)
    {
        // PLAN-60 (§6): the chosen employee's own active or pending contracts, inside PLAN-52's scope.
        var contracts = await _mediator.Send(new GetEmployeeContractsLookupQuery(vm.EmployeeId, await ContractPickList.InScopeAsync(HttpContext)));
        var items = contracts.Select(c => new { c.Id, Text = c.Label });
        vm.Contracts = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(items, "Id", "Text", vm.ContractId);
    }
}
