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

public class SeparationWizardController : Controller
{
    private readonly IContractsRepository _repo;
    private readonly IMediator _mediator;
    private readonly ICodeGenerationClient _codeClient;
    private readonly IHrCoreLookupClient _hrCoreLookup;

    public SeparationWizardController(
        IContractsRepository repo,
        IMediator mediator,
        ICodeGenerationClient codeClient,
        IHrCoreLookupClient hrCoreLookup)
    {
        _repo = repo;
        _mediator = mediator;
        _codeClient = codeClient;
        _hrCoreLookup = hrCoreLookup;
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Step1(SeparationRecordCreateViewModel vm)
    {
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);

        var model = new SeparationRecord
        {
            EmployeeId = vm.EmployeeId,
            ContractId = vm.ContractId,
            SeparationType = vm.SeparationType,
            EffectiveDate = vm.EffectiveDate,
            Reason = vm.Reason,
            Documents = vm.Documents,
            ApprovedBy = ParseUserId()
        };
        await _repo.AddAsync(model, default);
        var transaction = new ServiceTransaction
        {
            EmployeeId = model.EmployeeId,
            TransactionType = ServiceTransactionType.Separation,
            Notes = "معالج الفصل — تسجيل"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, _codeClient, default);
        await _repo.AddAsync(transaction, default);
        await _repo.SaveChangesAsync(default);

        foreach (var item in SeparationChecklistDefaults.CreateForRecord(model.Id))
            await _repo.AddAsync(item, default);
        await _repo.SaveChangesAsync(default);

        TempData["Success"] = "تم إنشاء سجل الفصل";
        return RedirectToAction(nameof(Step2), new { id = model.Id });
    }

    public async Task<IActionResult> Step2(Guid id)
    {
        var record = await _repo.SeparationRecords.FirstOrDefaultAsync(r => r.Id == id);
        if (record is null) return NotFound();

        var checklist = await _repo.SeparationChecklists
            .Where(c => c.SeparationRecordId == id)
            .OrderBy(c => c.Step)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        ViewBag.Checklist = checklist;
        ViewBag.EmployeeName = await ResolveEmployeeNameAsync(record.EmployeeId);
        return View(record);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Step2CompleteStep(Guid id, HR.Contracts.Domain.Enums.SeparationStep step, string? note)
    {
        var (success, error) = await _mediator.Send(new CompleteSeparationChecklistStepCommand(
            id,
            step,
            note,
            CompletedBy: ParseUserId(),
            EvidenceRef: string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            SourceSystem: "HRContracts"));
        if (!success)
        {
            TempData["Error"] = error ?? "تعذر إكمال الخطوة";
            return RedirectToAction(nameof(Step2), new { id });
        }
        TempData["Success"] = "تم إكمال خطوة قائمة الفصل";
        return RedirectToAction(nameof(Step2), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Step2Confirm(Guid id)
    {
        var record = await _repo.SeparationRecords.FirstOrDefaultAsync(r => r.Id == id);
        if (record is null) return NotFound();

        var checklist = await _repo.SeparationChecklists
            .Where(c => c.SeparationRecordId == id && c.IsRequired)
            .OrderBy(c => c.Step)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();
        var incomplete = checklist.Where(c => !c.IsCompleted).ToList();
        if (incomplete.Count > 0)
        {
            TempData["Warning"] = $"لا يمكن المتابعة: {incomplete.Count} خطوة إلزامية لم تُكمل";
            return RedirectToAction(nameof(Step2), new { id });
        }

        return RedirectToAction(nameof(Step3), new { id });
    }

    public async Task<IActionResult> Step3(Guid id)
    {
        var record = await _repo.SeparationRecords.FirstOrDefaultAsync(r => r.Id == id);
        if (record is null) return NotFound();
        ViewBag.EmployeeName = await ResolveEmployeeNameAsync(record.EmployeeId);
        return View(record);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Step3Execute(Guid id)
    {
        var (success, error) = await _mediator.Send(new PublishSeparationCommand(id));
        if (!success)
        {
            TempData["Error"] = error ?? "تعذر تنفيذ الفصل";
            return RedirectToAction(nameof(Step3), new { id });
        }

        TempData["Success"] = "تم تنفيذ الفصل ونشر الأحداث";
        return RedirectToAction(nameof(Step4), new { id });
    }

    public async Task<IActionResult> Step4(Guid id)
    {
        var record = await _repo.SeparationRecords.FirstOrDefaultAsync(r => r.Id == id);
        if (record is null) return NotFound();

        var checklist = await _repo.SeparationChecklists
            .Where(c => c.SeparationRecordId == id)
            .OrderBy(c => c.Step)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        ViewBag.Checklist = checklist;
        ViewBag.EmployeeName = await ResolveEmployeeNameAsync(record.EmployeeId);
        return View(record);
    }

    private async Task<string> ResolveEmployeeNameAsync(Guid employeeId)
    {
        try
        {
            var result = await _hrCoreLookup.GetEmployeesByIdsAsync([employeeId], HttpContext.RequestAborted);
            return result.FirstOrDefault()?.Label ?? "غير متاح";
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<SeparationWizardController>>()
                .LogWarning(ex, "Employee name lookup failed for separation wizard.");
            return "غير متاح";
        }
    }

    private async Task PopulateContractsAsync(SeparationRecordCreateViewModel vm)
    {
        var contracts = await _mediator.Send(new GetContractsLookupQuery(vm.ContractId));
        var items = contracts.Select(c => new { c.Id, Text = c.Label });
        vm.Contracts = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(items, "Id", "Text", vm.ContractId);
    }
}
