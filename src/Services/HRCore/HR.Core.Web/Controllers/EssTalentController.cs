using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleEss)]
[Route("[controller]")]
public class EssTalentController(
    HRCoreDbContext db,
    RecruitmentApplicationIntakeService intake,
    IConfiguration configuration,
    UnifiedIntakeService unifiedIntake,
    CandidatePublicStatusService publicStatus,
    CompetitionIntakeService competitionIntake) : Controller
{
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId.Value);
        if (employee is null || !IsEligibleApplicant(employee))
        {
            TempData["Error"] = "حسابك غير مؤهل للترشّح الداخلي.";
            return View(Array.Empty<InternalVacancy>());
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = db.InternalVacancies
            .Include(v => v.OrgUnit)
            .Include(v => v.Requisition!)
                .ThenInclude(r => r.FundedPosition)
            .Where(v => v.IsOpen && (v.ClosingDate == null || v.ClosingDate >= today))
            .Where(v => v.RequisitionId != null
                && v.Requisition!.Status == RequisitionStatus.Published
                && v.Requisition.ClosedDate == null
                && (v.Requisition.PublishingChannel == RecruitmentPublishingChannel.InternalOnly
                    || v.Requisition.PublishingChannel == RecruitmentPublishingChannel.Both));

        query = await ApplyVisibilityScopeAsync(query, employee);

        var vacancies = await query
            .OrderByDescending(v => v.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();

        // HCM-031 (PLAN-31): announced competitions open to internal candidates (apply with line preferences).
        ViewBag.Competitions = await competitionIntake.GetInternalOpenAsync();
        return View(vacancies);
    }

    /// <summary>HCM-031 (PLAN-31, P2): internal apply to a competition with ordered line preferences.</summary>
    [HttpPost("ApplyCompetition")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCompetition(Guid competitionId, List<Guid?> preferences, string? motivation)
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId.Value);
        if (employee is null || !IsEligibleApplicant(employee))
        {
            TempData["Error"] = "حسابك غير مؤهل للترشّح الداخلي.";
            return RedirectToAction(nameof(Index));
        }

        var competition = await competitionIntake.LoadForIntakeAsync(competitionId, forPublicPortal: false);
        if (competition?.InternalVacancyId is not Guid vacancyId
            || !HR.Core.Domain.Services.RequisitionWorkflowRules.AllowsInternalChannel(competition.PublishingChannel))
        {
            TempData["Error"] = CompetitionIntakeService.NotAvailableMessage;
            return RedirectToAction(nameof(Index));
        }
        var intakeError = CompetitionIntakeService.IntakeError(competition, DateOnly.FromDateTime(DateTime.UtcNow));
        var preferenceError = CompetitionIntakeService.ValidatePreferences(
            competition, competition.Lines.Select(l => l.Id).ToList(), preferences ?? [], out var orderedLines);
        if ((intakeError ?? preferenceError) is string error)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }

        var hash = employee.NationalIdHash
            ?? (string.IsNullOrWhiteSpace(employee.NationalId) ? null : HRManagement.SharedKernel.PiiMask.Sha256Hex(employee.NationalId.Trim()));
        if (hash is null)
        {
            TempData["Error"] = "لا يمكن الترشّح دون رقم وطني مسجل في ملفك.";
            return RedirectToAction(nameof(Index));
        }
        if (await competitionIntake.HasApplicationAsync(competition.TenantId, competition.Id, hash, forPublicPortal: false))
        {
            TempData["Error"] = CompetitionIntakeService.DuplicateMessage;
            return RedirectToAction(nameof(MyApplications));
        }

        var internalApp = new InternalApplication
        {
            TenantId = employee.TenantId,
            InternalVacancyId = vacancyId,
            EmployeeId = employeeId.Value,
            RequisitionId = null,
            Motivation = motivation,
            Status = InternalApplicationStatus.Submitted,
            AppliedAt = DateTime.UtcNow
        };
        try
        {
            await competitionIntake.SubmitInternalAsync(internalApp, employee, competition, orderedLines);
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = CompetitionIntakeService.DuplicateMessage;
            return RedirectToAction(nameof(MyApplications));
        }
        TempData["Success"] = "تم تقديم ترشّحك على المسابقة بنجاح";
        return RedirectToAction(nameof(MyApplications));
    }

    [HttpPost("Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(Guid vacancyId, string? motivation)
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId.Value);
        if (employee is null || !IsEligibleApplicant(employee))
        {
            TempData["Error"] = "حسابك غير مؤهل للترشّح الداخلي.";
            return RedirectToAction(nameof(Index));
        }

        var vacancy = await db.InternalVacancies
            .Include(v => v.Requisition)
            .FirstOrDefaultAsync(v => v.Id == vacancyId);

        if (vacancy is null)
        {
            TempData["Error"] = "الشاغر غير موجود.";
            return RedirectToAction(nameof(Index));
        }

        var validationError = await intake.ValidateInternalVacancyAsync(vacancyId);
        if (validationError is not null)
        {
            TempData["Error"] = validationError;
            return RedirectToAction(nameof(Index));
        }

        if (vacancy.RequisitionId is not Guid requisitionId)
        {
            TempData["Error"] = "الشاغر غير مرتبط بطلب شاغر منشور.";
            return RedirectToAction(nameof(Index));
        }

        // HCM-030 (A2): also refuse when the employee already has an open application on the requisition through
        // another channel (public or HR-entered).
        if (await intake.HasDuplicateInternalApplicationAsync(employeeId.Value, requisitionId)
            || await unifiedIntake.HasOpenApplicationAsync(requisitionId, employee.NationalId, employee.NationalIdHash))
        {
            TempData["Error"] = "سبق أن ترشّحت لهذا الشاغر.";
            return RedirectToAction(nameof(MyApplications));
        }

        var internalApp = new InternalApplication
        {
            TenantId = employee.TenantId,
            InternalVacancyId = vacancyId,
            EmployeeId = employeeId.Value,
            RequisitionId = requisitionId,
            Motivation = motivation,
            Status = InternalApplicationStatus.Submitted,
            AppliedAt = DateTime.UtcNow
        };

        // HCM-030 (A2): the application enters the recruitment path at Submitted in the same transaction.
        await unifiedIntake.SubmitInternalAsync(internalApp, employee, vacancy.Requisition!);
        TempData["Success"] = "تم تقديم ترشّحك بنجاح";
        return RedirectToAction(nameof(MyApplications));
    }

    [HttpGet("MyApplications")]
    public async Task<IActionResult> MyApplications()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var applications = await db.InternalApplications
            .Include(a => a.InternalVacancy)
                .ThenInclude(v => v!.Requisition)
                    .ThenInclude(r => r!.FundedPosition)
            .Where(a => a.EmployeeId == employeeId.Value)
            .OrderByDescending(a => a.AppliedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();

        // HCM-030 (B3): the public status of the linked recruitment application, never the raw internal stage.
        ViewBag.PublicStatuses = await publicStatus.GetViewsAsync(applications
            .Where(a => a.PromotedRecruitmentApplicationId.HasValue)
            .Select(a => a.PromotedRecruitmentApplicationId!.Value)
            .ToList());
        return View(applications);
    }

    private static bool IsEligibleApplicant(Employee employee) =>
        employee.CurrentStatus is EmploymentStatus.Active
            or EmploymentStatus.OnLeave
            or EmploymentStatus.Seconded;

    private async Task<IQueryable<InternalVacancy>> ApplyVisibilityScopeAsync(
        IQueryable<InternalVacancy> query,
        Employee employee)
    {
        var scope = configuration.GetValue("Recruitment:InternalVisibilityScope", "OrgUnit");
        if (!string.Equals(scope, "OrgUnit", StringComparison.OrdinalIgnoreCase))
            return query;

        if (employee.PrimaryOrgUnitId is not Guid orgUnitId)
            return query.Where(_ => false);

        var employeePath = await db.OrgUnits.AsNoTracking()
            .Where(o => o.Id == orgUnitId)
            .Select(o => o.MaterializedPath)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(employeePath))
            return query.Where(v => v.OrgUnitId == orgUnitId);

        return query.Where(v => v.OrgUnitId == null
            || db.OrgUnits.Any(o => o.Id == v.OrgUnitId
                && (o.Id == orgUnitId || o.MaterializedPath.StartsWith(employeePath))));
    }
}
