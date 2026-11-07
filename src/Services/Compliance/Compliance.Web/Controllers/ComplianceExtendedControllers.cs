using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Domain.Services;
using Compliance.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.ViewModels;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Compliance.Web.Controllers;

public class LegalCasesController(
    ComplianceDbContext db,
    IPublishEndpoint publish,
    ICodeGenerationClient codeClient) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.LegalCases.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new LegalCase { Status = CaseStatus.Open });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LegalCase model)
    {
        if (!ModelState.IsValid) return View(model);

        var (ok, caseNumber, error) = await CodeGenerationGateway.GenerateOrErrorAsync(
            codeClient, CodeGenerationRequests.LegalCase());
        if (!ok)
        {
            ModelState.AddModelError(string.Empty, error ?? "تعذّر توليد رقم القضية");
            return View(model);
        }

        model.CaseNumber = caseNumber!;
        db.LegalCases.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل القضية";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var model = await db.LegalCases.FirstOrDefaultAsync(x => x.Id == id);
        if (model is null) return NotFound();
        return View(model);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await db.LegalCases.FindAsync(id);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, LegalCase model)
    {
        var entity = await db.LegalCases.FindAsync(id);
        if (entity is null) return NotFound();

        entity.Subject = model.Subject;
        entity.Parties = model.Parties;
        entity.Status = model.Status;
        // CaseNumber immutable (COD-001)
        entity.CourtDetails = model.CourtDetails;
        entity.AssignedTo = model.AssignedTo;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث القضية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(Guid id, string? assignedTo)
    {
        var entity = await db.LegalCases.FindAsync(id);
        if (entity is null) return NotFound();

        Guid? g = null;
        if (!string.IsNullOrWhiteSpace(assignedTo) && Guid.TryParse(assignedTo, out var parsed))
            g = parsed;
        entity.AssignedTo = g;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(Guid id, CaseStatus status)
    {
        var entity = await db.LegalCases.FindAsync(id);
        if (entity is null) return NotFound();

        entity.Status = status;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(Guid id)
    {
        var model = await db.LegalCases.FindAsync(id);
        if (model is null) return NotFound();

        model.Status = CaseStatus.Closed;

        await publish.Publish<ILegalJudgmentIssued>(new
        {
            EmployeeId = Guid.Empty,
            EffectiveDate = DateTime.UtcNow,
            JudgmentRef = model.CaseNumber,
            Notes = model.CourtDetails,
            SourceSystem = "Compliance",
            CorrelationId = id.ToString()
        });
        await db.SaveChangesAsync();

        TempData["Success"] = "تم إغلاق القضية";
        return RedirectToAction(nameof(Details), new { id });
    }
}

/// <summary>
/// PLAN-46 (section 4): an action is a draft until compliance, then the authority, approve it in HR.Core's engine; only that approval sends
/// it to payroll (<c>DisciplinaryApprovalOutcomeConsumer</c>). This controller publishes nothing, and binds no approval field.
/// </summary>
public class DisciplinaryActionsController(
    ComplianceDbContext db,
    IFileReferenceClient fileRefs,
    IHrCoreLookupClient hrCoreLookup,
    Compliance.Infrastructure.Services.DisciplinaryApprovalService approvals) : Controller
{
    public const string NotEditableMessage = "يُعدَّل الإجراء وهو مسودة أو بعد رفضه فقط.";
    public const string NotApprovedMessage = "يُستأنف الإجراء المعتمد فقط.";

    private static Guid? ParseUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public async Task<IActionResult> Index()
    {
        var items = await db.DisciplinaryActions.AsNoTracking()
            .OrderByDescending(x => x.IssuedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();
        ViewBag.EmployeeNames = await ResolveEmployeeNamesAsync(items.Select(x => x.EmployeeId));
        return View(items);
    }

    public IActionResult Create()
    {
        ViewBag.Complaints = new SelectList(db.Complaints.AsNoTracking().Where(c => !c.IsCancelled).OrderByDescending(c => c.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToList(), "Id", "Subject");
        return View(new DisciplinaryAction { IssuedAt = DateTime.UtcNow, AppealStatus = AppealStatus.None });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DisciplinaryAction model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Complaints = new SelectList(db.Complaints.AsNoTracking().Where(c => !c.IsCancelled).OrderByDescending(c => c.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToList(), "Id", "Subject");
            return View(model);
        }

        model.AppealStatus = AppealStatus.None;
        if (!DisciplinaryWorkflowRules.CanIssue(model.AppealStatus))
        {
            TempData["Error"] = "لا يمكن إصدار إجراء تأديبي في حالة الاستئناف الحالية";
            ViewBag.Complaints = new SelectList(db.Complaints.AsNoTracking().Where(c => !c.IsCancelled).OrderByDescending(c => c.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToList(), "Id", "Subject");
            return View(model);
        }

        if (model.EvidenceFileId.HasValue)
        {
            try
            {
                await fileRefs.RegisterReferenceAsync(
                    model.EvidenceFileId.Value,
                    FileReferenceClientExtensions.BuildHolderKey(
                        HrFileServices.Compliance, nameof(DisciplinaryAction), model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
                        nameof(DisciplinaryAction.EvidenceFileId)));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"مرفق مرفوض (FILE-002): {ex.Message}";
                ViewBag.Complaints = new SelectList(db.Complaints.AsNoTracking().Where(c => !c.IsCancelled).OrderByDescending(c => c.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToList(), "Id", "Subject");
                return View(model);
            }
        }

        // PLAN-46: a draft, with only what the form shows; nothing reaches payroll before the approval.
        var action = new DisciplinaryAction
        {
            Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
            EmployeeId = model.EmployeeId,
            ActionType = model.ActionType,
            IssuedAt = model.IssuedAt,
            Notes = model.Notes,
            ComplaintId = model.ComplaintId,
            EvidenceFileId = model.EvidenceFileId,
            AppealStatus = AppealStatus.None,
            ApprovalStatus = DisciplinaryApprovalStatus.Draft,
            ApprovedBeforeEngine = false,
            IssuedBy = ServerAssignedIdentity.ResolveActorEmployeeId(User)
        };
        db.DisciplinaryActions.Add(action);
        await db.SaveChangesAsync();

        // Re-register with final id if evidence was attached before id known (after the commit; the registration is idempotent).
        if (action.EvidenceFileId.HasValue)
        {
            await fileRefs.RegisterReferenceAsync(
                action.EvidenceFileId.Value,
                FileReferenceClientExtensions.BuildHolderKey(
                    HrFileServices.Compliance, nameof(DisciplinaryAction), action.Id, nameof(DisciplinaryAction.EvidenceFileId)));
        }

        // Then the submission. A failure leaves the draft, which Details submits again.
        if (ParseUserId(User) is Guid submitter)
        {
            var result = await approvals.SubmitAsync(action, submitter, await EmployeeNameAsync(action.EmployeeId), HttpContext.RequestAborted);
            if (result.Success)
                TempData["Success"] = "سُجّل الإجراء وقُدّم للاعتماد: لا أثر له قبل اعتماد الامتثال ثم صاحب الصلاحية.";
            else
                TempData["Error"] = $"سُجّل الإجراء مسودةً ولم يُقدَّم: {result.Error}";
        }
        else
        {
            TempData["Error"] = "سُجّل الإجراء مسودةً؛ تعذّر تحديد مقدّم الطلب.";
        }

        return RedirectToAction(nameof(Details), new { id = action.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id)
    {
        var action = await db.DisciplinaryActions.FindAsync(id);
        if (action is null) return NotFound();
        if (ParseUserId(User) is not Guid submitter) return Forbid();

        var result = await approvals.SubmitAsync(action, submitter, await EmployeeNameAsync(action.EmployeeId), HttpContext.RequestAborted);
        if (result.Success)
            TempData["Success"] = result.Finished ? "انتهى اعتماد هذا الإجراء سابقاً وطُبّقت نتيجته." : "قُدّم الإجراء للاعتماد.";
        else
            TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(Guid id, string? reason)
    {
        var action = await db.DisciplinaryActions.FindAsync(id);
        if (action is null) return NotFound();

        var result = await approvals.WithdrawAsync(
            action, ParseUserId(User), string.IsNullOrWhiteSpace(reason) ? "سحب الإجراء التأديبي" : reason.Trim(), HttpContext.RequestAborted);
        if (result.Success)
            TempData["Success"] = "سُحب الإجراء وعاد مسودة.";
        else
            TempData["Error"] = result.Error ?? "انتهى الاعتماد قبل السحب وطُبّقت نتيجته.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<string?> EmployeeNameAsync(Guid employeeId)
    {
        var name = (await ResolveEmployeeNamesAsync([employeeId])).GetValueOrDefault(employeeId);
        return name == "غير متاح" ? null : name;
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var model = await db.DisciplinaryActions
            .Include(x => x.Complaint)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (model is null) return NotFound();
        ViewBag.EmployeeName = (await ResolveEmployeeNamesAsync([model.EmployeeId]))
            .GetValueOrDefault(model.EmployeeId, "غير متاح");
        return View(model);
    }

    private async Task<Dictionary<Guid, string>> ResolveEmployeeNamesAsync(IEnumerable<Guid> ids)
    {
        var result = ids.Distinct().ToDictionary(id => id, _ => "غير متاح");
        try
        {
            foreach (var row in await hrCoreLookup.GetEmployeesByIdsAsync(result.Keys.ToList()))
                result[row.Id] = row.Label;
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<DisciplinaryActionsController>>()
                .LogWarning(ex, "Employee name lookup failed for disciplinary actions.");
        }
        return result;
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await db.DisciplinaryActions.FindAsync(id);
        if (model is null) return NotFound();
        if (model.AppealStatus != AppealStatus.None || !DisciplinaryWorkflowRules.CanEdit(model.ApprovalStatus))
        {
            TempData["Error"] = NotEditableMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        ViewBag.Complaints = new SelectList(db.Complaints.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToList(), "Id", "Subject");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, DisciplinaryAction model)
    {
        var entity = await db.DisciplinaryActions.FindAsync(id);
        if (entity is null) return NotFound();
        // PLAN-46: an action that is pending, approved or published keeps its employee and type.
        if (entity.AppealStatus != AppealStatus.None || !DisciplinaryWorkflowRules.CanEdit(entity.ApprovalStatus))
        {
            TempData["Error"] = NotEditableMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        entity.EmployeeId = model.EmployeeId;
        entity.ActionType = model.ActionType;
        entity.IssuedAt = model.IssuedAt;
        entity.Notes = model.Notes;
        entity.ComplaintId = model.ComplaintId;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الإجراء";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Appeal(Guid id)
    {
        var entity = await db.DisciplinaryActions.FindAsync(id);
        if (entity is null) return NotFound();

        if (DisciplinaryWorkflowRules.CanAppeal(entity.AppealStatus, entity.ApprovalStatus))
        {
            entity.AppealStatus = AppealStatus.Pending;
            await db.SaveChangesAsync();
        }
        else
        {
            TempData["Error"] = NotApprovedMessage;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideAppeal(Guid id, string decision)
    {
        var entity = await db.DisciplinaryActions.FindAsync(id);
        if (entity is null) return NotFound();

        if (DisciplinaryWorkflowRules.CanDecide(entity.AppealStatus, entity.ApprovalStatus))
        {
            entity.AppealStatus = AppealStatus.Decided;
            entity.Notes = (entity.Notes ?? "") + $"\n[قرار الاستئناف]: {decision}";
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class AuditFindingsController(ComplianceDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.AuditFindings.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new AuditFinding { Status = FindingStatus.Open, Severity = FindingSeverity.Medium });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AuditFinding model)
    {
        if (!ModelState.IsValid) return View(model);
        db.AuditFindings.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الملاحظة الرقابية";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var model = await db.AuditFindings
            .Include(x => x.Recommendations)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (model is null) return NotFound();
        return View(model);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await db.AuditFindings.FindAsync(id);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, AuditFinding model)
    {
        var entity = await db.AuditFindings.FindAsync(id);
        if (entity is null) return NotFound();

        entity.AuditId = model.AuditId;
        entity.Description = model.Description;
        entity.Severity = model.Severity;
        entity.Recommendation = model.Recommendation;
        entity.Status = model.Status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الملاحظة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Mitigate(Guid id)
    {
        var entity = await db.AuditFindings.FindAsync(id);
        if (entity is null) return NotFound();

        entity.Status = FindingStatus.Mitigated;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تخفيف الملاحظة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseItem(Guid id)
    {
        var entity = await db.AuditFindings.FindAsync(id);
        if (entity is null) return NotFound();

        entity.Status = FindingStatus.Closed;
        entity.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إغلاق الملاحظة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateReferral(Guid id)
    {
        var finding = await db.AuditFindings.FindAsync(id);
        if (finding is null) return NotFound();

        var referral = new LegalReferral
        {
            Id = Guid.NewGuid(),
            OriginatingUnit = null,
            ReferredTo = "الشؤون القانونية",
            ReferralDate = DateTime.UtcNow,
            Documents = finding.Description
        };
        db.LegalReferrals.Add(referral);
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت الإحالة القانونية";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class LegalReferralsController(ComplianceDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.LegalReferrals.AsNoTracking().OrderByDescending(x => x.ReferralDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new LegalReferral { ReferralDate = DateTime.UtcNow });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LegalReferral model)
    {
        if (!ModelState.IsValid) return View(model);
        model.Id = Guid.NewGuid();
        db.LegalReferrals.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الإحالة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var model = await db.LegalReferrals.FirstOrDefaultAsync(x => x.Id == id);
        if (model is null) return NotFound();
        return View(model);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await db.LegalReferrals.FindAsync(id);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, LegalReferral model)
    {
        var entity = await db.LegalReferrals.FindAsync(id);
        if (entity is null) return NotFound();

        entity.OriginatingUnit = model.OriginatingUnit;
        entity.ReferredTo = model.ReferredTo;
        entity.ReferralDate = model.ReferralDate;
        entity.Documents = model.Documents;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الإحالة";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class ComplianceRecommendationsController(ComplianceDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ComplianceRecommendations.AsNoTracking().OrderBy(x => x.DueDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create()
    {
        ViewBag.Findings = new SelectList(db.AuditFindings.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToList(), "Id", "Description");
        return View(new ComplianceRecommendation());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ComplianceRecommendation model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Findings = new SelectList(db.AuditFindings.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToList(), "Id", "Description");
            return View(model);
        }
        db.ComplianceRecommendations.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل التوصية";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var model = await db.ComplianceRecommendations
            .Include(x => x.Finding)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (model is null) return NotFound();
        return View(model);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await db.ComplianceRecommendations.FindAsync(id);
        if (model is null) return NotFound();
        ViewBag.Findings = new SelectList(db.AuditFindings.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToList(), "Id", "Description");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ComplianceRecommendation model)
    {
        var entity = await db.ComplianceRecommendations.FindAsync(id);
        if (entity is null) return NotFound();

        entity.FindingId = model.FindingId;
        entity.RecommendedAction = model.RecommendedAction;
        entity.DueDate = model.DueDate;
        entity.ResponsibleUnitId = model.ResponsibleUnitId;
        entity.Status = model.Status;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث التوصية";
        return RedirectToAction(nameof(Details), new { id });
    }
}
