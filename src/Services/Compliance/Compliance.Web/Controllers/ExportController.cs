using ClosedXML.Excel;
using Compliance.Infrastructure.Data;
using Compliance.Infrastructure.ExportJobs;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Compliance.Web.Controllers;

public class ExportController : Controller
{
    private readonly ComplianceDbContext _db;
    private readonly IComplianceExportJobService _exportJobs;

    public ExportController(ComplianceDbContext db, IComplianceExportJobService exportJobs)
    {
        _db = db;
        _exportJobs = exportJobs;
    }

    public async Task<IActionResult> ComplaintsCsv(bool async = false, CancellationToken ct = default)
    {
        var query = _db.Complaints.AsNoTracking();
        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.ComplianceComplaintsCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var items = await query.OrderByDescending(c => c.CreatedAt).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Id,FiledBy,Subject,Status,FiledAt,CreatedAt");
        foreach (var c in items)
            csv.AppendLine($"{c.Id},\"{c.FiledBy}\",\"{c.Subject}\",{c.Status},{c.FiledAt:yyyy-MM-dd},{c.CreatedAt:yyyy-MM-dd}");
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "complaints.csv");
    }

    public async Task<IActionResult> ComplaintsExcel(bool async = false, CancellationToken ct = default)
    {
        var query = _db.Complaints.AsNoTracking();
        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.ComplianceComplaintsCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var items = await query.OrderByDescending(c => c.CreatedAt).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("سجل الشكاوى");
        ws.RightToLeft = true;
        ws.Cell(1, 1).Value = "المعرف"; ws.Cell(1, 2).Value = "مقدم الشكوى"; ws.Cell(1, 3).Value = "الموضوع"; ws.Cell(1, 4).Value = "الحالة"; ws.Cell(1, 5).Value = "تاريخ التقديم";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var c in items)
        {
            ws.Cell(row, 1).Value = c.Id.ToString(); ws.Cell(row, 2).Value = c.FiledBy;
            ws.Cell(row, 3).Value = c.Subject; ws.Cell(row, 4).Value = c.Status.ToString();
            ws.Cell(row, 5).Value = c.FiledAt.ToString("yyyy-MM-dd"); row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "complaints.xlsx");
    }
}
