using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using HRManagement.SharedKernel.Enums;
using DocumentType = HRManagement.SharedKernel.Enums.DocumentType;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleEss)]
[Route("[controller]")]
public class EssPortalController(HRCoreDbContext db, IPublishEndpoint publish) : Controller
{
    private async Task<Employee?> GetOwnEmployeeAsync()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null)
            return null;

        return await db.Employees
            .Include(e => e.PrimaryOrgUnit)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var employee = await GetOwnEmployeeAsync();
        if (employee is null)
            return View("NoProfile");

        ViewBag.AttendanceCount = await db.DailyAttendanceRecords
            .CountAsync(a => a.EmployeeId == employee.Id);
        ViewBag.DocumentCount = await db.EmployeeDocuments
            .CountAsync(d => d.EmployeeId == employee.Id);
        return View(employee);
    }

    [HttpGet("Attendance")]
    public async Task<IActionResult> Attendance()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var records = await db.DailyAttendanceRecords
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.AttendanceDate)
            .Take(90)
            .ToListAsync();

        return View(records);
    }

    [HttpGet("Documents")]
    public async Task<IActionResult> Documents()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var documents = await db.EmployeeDocuments
            .Where(d => d.EmployeeId == employeeId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();

        return View(documents);
    }

    [HttpGet("Leave")]
    public async Task<IActionResult> Leave()
    {
        if (await GetOwnEmployeeAsync() is null)
            return View("NoProfile");

        return View(new EssLeaveRequestModel { StartDate = DateOnly.FromDateTime(DateTime.UtcNow) });
    }

    [HttpPost("Leave")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Leave(EssLeaveRequestModel model)
    {
        var employee = await GetOwnEmployeeAsync();
        if (employee is null)
            return View("NoProfile");

        if (!ModelState.IsValid)
            return View(model);

        var duration = (model.EndDate.DayNumber - model.StartDate.DayNumber) + 1;
        if (duration <= 0)
        {
            ModelState.AddModelError(string.Empty, "تاريخ النهاية يجب أن يكون بعد البداية");
            return View(model);
        }

        await publish.Publish<ILeaveRequested>(new
        {
            RequestId = Guid.NewGuid(),
            EmployeeId = employee.Id,
            LeaveType = model.LeaveType.ToString(),
            StartDate = model.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            EndDate = model.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            DurationDays = duration,
            model.Notes,
            SourceSystem = "ESS",
            CorrelationId = HttpContext.TraceIdentifier,
            OrgUnitId = employee.PrimaryOrgUnitId
        });

        // HR Core publishes through the EF transactional outbox, which only dispatches on
        // SaveChanges. Without this the request was staged and discarded while the employee was
        // told it had been submitted.
        await db.SaveChangesAsync();

        TempData["Success"] = "تم تقديم طلب الإجازة";
        return RedirectToAction(nameof(Leave));
    }

    [HttpGet("Advance")]
    public async Task<IActionResult> Advance()
    {
        if (await GetOwnEmployeeAsync() is null)
            return View("NoProfile");

        return View(new EssAdvanceRequestModel());
    }

    [HttpPost("Advance")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Advance(EssAdvanceRequestModel model)
    {
        var employee = await GetOwnEmployeeAsync();
        if (employee is null)
            return View("NoProfile");

        if (!ModelState.IsValid)
            return View(model);

        await publish.Publish<IAdvanceRequested>(new
        {
            RequestId = Guid.NewGuid(),
            EmployeeId = employee.Id,
            model.Amount,
            model.Reason,
            SourceSystem = "ESS",
            CorrelationId = HttpContext.TraceIdentifier,
            OrgUnitId = employee.PrimaryOrgUnitId
        });

        // Flush the transactional outbox; see the leave action above.
        await db.SaveChangesAsync();

        TempData["Success"] = "تم تقديم طلب السلفة";
        return RedirectToAction(nameof(Advance));
    }

    [HttpGet("IdCard")]
    public async Task<IActionResult> IdCard()
    {
        if (await GetOwnEmployeeAsync() is null)
            return View("NoProfile");

        return View(new EssIdCardRequestModel());
    }

    [HttpPost("IdCard")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IdCard(EssIdCardRequestModel model)
    {
        var employee = await GetOwnEmployeeAsync();
        if (employee is null)
            return View("NoProfile");

        db.EmployeeDocuments.Add(new EmployeeDocument
        {
            EmployeeId = employee.Id,
            DocType = DocumentType.Other,
            DocTitle = string.IsNullOrWhiteSpace(model.Notes) ? "طلب بطاقة هوية" : $"طلب بطاقة: {model.Notes}",
            FileId = model.AttachmentFileId
        });
        await db.SaveChangesAsync();

        TempData["Success"] = "تم تسجيل طلب البطاقة وسيتم معالجته من قسم الهويات";
        return RedirectToAction(nameof(IdCard));
    }
}

public class EssLeaveRequestModel
{
    public string LeaveType { get; set; } = "Annual";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Notes { get; set; }
}

public class EssAdvanceRequestModel
{
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
}

public class EssIdCardRequestModel
{
    public string? Notes { get; set; }
    public Guid? AttachmentFileId { get; set; }
}
