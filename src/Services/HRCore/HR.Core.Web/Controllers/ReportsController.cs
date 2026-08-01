using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using HR.Core.Infrastructure.ExportJobs;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class ReportsController : Controller
{
    private readonly HRCoreDbContext _context;
    private readonly HR.Core.Infrastructure.OrgScopeService _orgScope;
    private readonly IHRCoreExportJobService _exportJobs;

    public ReportsController(
        HRCoreDbContext context,
        HR.Core.Infrastructure.OrgScopeService orgScope,
        IHRCoreExportJobService exportJobs)
    {
        _context = context;
        _orgScope = orgScope;
        _exportJobs = exportJobs;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (orgScope.IsDenied) return Forbid();
        var employees = _context.Employees.AsQueryable();
        if (orgScope.MustFilter)
            employees = employees.Where(e => e.PrimaryOrgUnitId.HasValue && orgScope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        var vm = new ReportsSummaryViewModel
        {
            TotalEmployees = await employees.CountAsync(ct),
            ByStatus = await employees
                .GroupBy(e => e.CurrentStatus)
                .Select(g => new ReportGroupItem { Label = g.Key.ToString(), Count = g.Count() })
                .Take(SearchFilterBase.MaxPageSize)
                .ToListAsync(ct),
            ByOrgUnit = await employees
                .Where(e => e.PrimaryOrgUnit != null)
                .GroupBy(e => e.PrimaryOrgUnit!.NameAr)
                .Select(g => new ReportGroupItem { Label = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(SearchFilterBase.MaxPageSize)
                .ToListAsync(ct),
            ByCadre = await employees
                .GroupBy(e => e.Cadre)
                .Select(g => new ReportGroupItem { Label = g.Key.ToString(), Count = g.Count() })
                .Take(SearchFilterBase.MaxPageSize)
                .ToListAsync(ct),
            ByGender = await employees
                .GroupBy(e => e.Gender)
                .Select(g => new ReportGroupItem { Label = g.Key.ToString(), Count = g.Count() })
                .Take(SearchFilterBase.MaxPageSize)
                .ToListAsync(ct)
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportEmployeesCsv(bool async = false, CancellationToken ct = default)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "Reports/ExportEmployeesCsv");
        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (orgScope.IsDenied) return Forbid();
        var query = _context.Employees.AsNoTracking();
        if (orgScope.MustFilter)
            query = query.Where(e => e.PrimaryOrgUnitId.HasValue && orgScope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.HrCoreEmployeesCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var rows = await query
            .OrderBy(e => e.FullName)
            .Take(SearchFilterBase.MaxExportRows)
            .Select(e => new { e.Id, e.NationalId, e.FullName, e.CurrentStatus, e.HireDate })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Id,NationalIdMasked,FullName,CurrentStatus,HireDate");
        foreach (var r in rows)
            sb.AppendLine($"{r.Id},{Csv(PiiMask.NationalId(r.NationalId))},{Csv(r.FullName)},{r.CurrentStatus},{r.HireDate?.ToString("yyyy-MM-dd")}");

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"employees-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportEmployeesExcel(string? status, Guid? orgUnitId, string? cadre, CancellationToken ct)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "Reports/ExportEmployeesExcel");
        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (orgScope.IsDenied) return Forbid();
        if (orgUnitId.HasValue && !orgScope.CanAccess(orgUnitId))
            return Forbid();

        var query = _context.Employees.Include(e => e.PrimaryOrgUnit).AsNoTracking().AsQueryable();
        if (orgScope.MustFilter)
            query = query.Where(e => e.PrimaryOrgUnitId.HasValue && orgScope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<HRManagement.SharedKernel.Enums.EmploymentStatus>(status, out var st))
            query = query.Where(e => e.CurrentStatus == st);
        if (orgUnitId.HasValue)
            query = query.Where(e => e.PrimaryOrgUnitId == orgUnitId.Value);
        if (!string.IsNullOrWhiteSpace(cadre) && Enum.TryParse<HRManagement.SharedKernel.Enums.EmployeeCadre>(cadre, out var c))
            query = query.Where(e => e.Cadre == c);

        var (allowedXlsx, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (!allowedXlsx)
            return BadRequest(ExportQueryLimits.OverLimitArabicMessage);

        var employees = await query.OrderBy(e => e.FullName).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("الموظفون");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = "الرقم الوطني (مقنّع)";
        ws.Cell(1, 2).Value = "الاسم الكامل";
        ws.Cell(1, 3).Value = "الحالة";
        ws.Cell(1, 4).Value = "تاريخ التعيين";
        ws.Cell(1, 5).Value = "الوحدة التنظيمية";
        ws.Cell(1, 6).Value = "الشريحة";
        ws.Range(1, 1, 1, 6).Style.Font.Bold = true;
        ws.Range(1, 1, 1, 6).Style.Fill.BackgroundColor = XLColor.LightGray;

        for (int i = 0; i < employees.Count; i++)
        {
            var e = employees[i];
            ws.Cell(i + 2, 1).Value = PiiMask.NationalId(e.NationalId);
            ws.Cell(i + 2, 2).Value = e.FullName;
            ws.Cell(i + 2, 3).Value = e.CurrentStatus.ToString();
            ws.Cell(i + 2, 4).Value = e.HireDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(i + 2, 5).Value = e.PrimaryOrgUnit?.NameAr ?? "—";
            ws.Cell(i + 2, 6).Value = e.Cadre.ToString();
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"employees-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportEmployeesPdf(string? status, Guid? orgUnitId, CancellationToken ct)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "Reports/ExportEmployeesPdf");
        QuestPDF.Settings.License = LicenseType.Community;

        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (orgScope.IsDenied) return Forbid();
        if (orgUnitId.HasValue && !orgScope.CanAccess(orgUnitId))
            return Forbid();

        var query = _context.Employees.Include(e => e.PrimaryOrgUnit).AsNoTracking().AsQueryable();
        if (orgScope.MustFilter)
            query = query.Where(e => e.PrimaryOrgUnitId.HasValue && orgScope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<HRManagement.SharedKernel.Enums.EmploymentStatus>(status, out var st2))
            query = query.Where(e => e.CurrentStatus == st2);
        if (orgUnitId.HasValue)
            query = query.Where(e => e.PrimaryOrgUnitId == orgUnitId.Value);

        var (allowedPdf, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (!allowedPdf)
            return BadRequest(ExportQueryLimits.OverLimitArabicMessage);

        var employees = await query.OrderBy(e => e.FullName).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.ContentFromRightToLeft();

                page.Header().Text("تقرير الموظفين").FontSize(18).Bold().AlignCenter();

                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(3);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("الاسم الكامل").Bold();
                        header.Cell().Text("الحالة").Bold();
                        header.Cell().Text("تاريخ التعيين").Bold();
                        header.Cell().Text("الوحدة التنظيمية").Bold();
                    });

                    foreach (var e in employees)
                    {
                        table.Cell().Text(e.FullName);
                        table.Cell().Text(e.CurrentStatus.ToString());
                        table.Cell().Text(e.HireDate?.ToString("yyyy-MM-dd") ?? "—");
                        table.Cell().Text(e.PrimaryOrgUnit?.NameAr ?? "—");
                    }
                });

                page.Footer().AlignCenter().Text(txt =>
                {
                    txt.Span("صفحة ");
                    txt.CurrentPageNumber();
                    txt.Span(" من ");
                    txt.TotalPages();
                });
            });
        });

        var pdfBytes = document.GeneratePdf();
        return File(pdfBytes, "application/pdf", $"employees-{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportStatusHistoryCsv(CancellationToken ct)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "Reports/ExportStatusHistoryCsv");
        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (orgScope.IsDenied) return Forbid();

        var history = _context.EmploymentStatusHistories.AsNoTracking().AsQueryable();
        if (orgScope.MustFilter)
        {
            var empIds = _context.Employees
                .Where(e => e.PrimaryOrgUnitId.HasValue && orgScope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value))
                .Select(e => e.Id);
            history = history.Where(h => empIds.Contains(h.EmployeeId));
        }

        var (allowedHist, _) = await ExportQueryLimits.EnsureWithinLimitAsync(history, cancellationToken: ct);
        if (!allowedHist)
            return BadRequest(ExportQueryLimits.OverLimitArabicMessage);

        var rows = await history
            .OrderByDescending(h => h.ChangedAt)
            .Take(SearchFilterBase.MaxExportRows)
            .Select(h => new { h.Id, h.EmployeeId, h.OldStatus, h.NewStatus, h.ChangedAt, h.Reason, h.EmploymentRecordId })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Id,EmployeeId,OldStatus,NewStatus,ChangedAtUtc,Reason,EmploymentRecordId");
        foreach (var h in rows)
            sb.AppendLine($"{h.Id},{h.EmployeeId},{Csv(h.OldStatus)},{Csv(h.NewStatus)},{h.ChangedAt:O},{Csv(h.Reason)},{h.EmploymentRecordId}");

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"employment-status-history-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportEvaluationsCsv(CancellationToken ct)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "Reports/ExportEvaluationsCsv");
        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (orgScope.IsDenied) return Forbid();

        var evals = _context.PerformanceEvaluations.Include(e => e.Employee).AsNoTracking().AsQueryable();
        if (orgScope.MustFilter)
            evals = evals.Where(e => e.Employee != null && e.Employee.PrimaryOrgUnitId.HasValue
                && orgScope.AllowedOrgUnitIds.Contains(e.Employee.PrimaryOrgUnitId.Value));

        var (allowedEval, _) = await ExportQueryLimits.EnsureWithinLimitAsync(evals, cancellationToken: ct);
        if (!allowedEval)
            return BadRequest(ExportQueryLimits.OverLimitArabicMessage);

        var rows = await evals
            .OrderByDescending(e => e.Year)
            .Take(SearchFilterBase.MaxExportRows)
            .Select(e => new { e.Id, e.EmployeeId, EmployeeName = e.Employee!.FullName, e.Year, e.Score, e.Status })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Id,EmployeeId,EmployeeName,Year,Score,Status");
        foreach (var r in rows)
            sb.AppendLine($"{r.Id},{r.EmployeeId},{Csv(r.EmployeeName)},{r.Year},{r.Score},{Csv(r.Status)}");

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"evaluations-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var escaped = s.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
