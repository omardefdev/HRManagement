using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.ExportJobs;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Localization;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Web.Controllers;

public class ExportController : Controller
{
    private readonly ContractsDbContext _db;
    private readonly IContractsExportJobService _exportJobs;

    public ExportController(ContractsDbContext db, IContractsExportJobService exportJobs)
    {
        _db = db;
        _exportJobs = exportJobs;
    }

    public async Task<IActionResult> LeaveRequestsCsv(bool async = false, CancellationToken ct = default)
    {
        var query = _db.LeaveRequests.AsNoTracking();
        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.ContractsLeaveCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var items = await query.OrderByDescending(l => l.CreatedAt).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("EmployeeId,LeaveType,StartDate,EndDate,Status,CreatedAt");
        foreach (var l in items)
            csv.AppendLine($"{l.EmployeeId},{EnumDisplayExtensions.GetDisplayName(l.LeaveType)},{l.StartDate:yyyy-MM-dd},{l.EndDate:yyyy-MM-dd},{EnumDisplayExtensions.GetDisplayName(l.Status)},{l.CreatedAt:yyyy-MM-dd}");
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "leave-requests.csv");
    }

    public async Task<IActionResult> LeaveRequestsExcel(bool async = false, CancellationToken ct = default)
    {
        var query = _db.LeaveRequests.AsNoTracking();
        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.ContractsLeaveCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var items = await query.OrderByDescending(l => l.CreatedAt).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("طلبات الإجازة");
        ws.RightToLeft = true;
        ws.Cell(1, 1).Value = "الموظف"; ws.Cell(1, 2).Value = "النوع"; ws.Cell(1, 3).Value = "من"; ws.Cell(1, 4).Value = "إلى"; ws.Cell(1, 5).Value = "الحالة";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var l in items)
        {
            ws.Cell(row, 1).Value = l.EmployeeId.ToString();
            ws.Cell(row, 2).Value = EnumDisplayExtensions.GetDisplayName(l.LeaveType);
            ws.Cell(row, 3).Value = l.StartDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 4).Value = l.EndDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 5).Value = EnumDisplayExtensions.GetDisplayName(l.Status);
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "leave-requests.xlsx");
    }

    public async Task<IActionResult> AdvanceRequestsCsv(bool async = false, CancellationToken ct = default)
    {
        var query = _db.AdvanceRequests.AsNoTracking();
        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.ContractsAdvanceCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var items = await query.OrderByDescending(a => a.CreatedAt).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("EmployeeId,Amount,Status,Reason,CreatedAt");
        foreach (var a in items)
            csv.AppendLine($"{a.EmployeeId},{a.Amount},{EnumDisplayExtensions.GetDisplayName(a.Status)},\"{a.Reason}\",{a.CreatedAt:yyyy-MM-dd}");
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "advance-requests.csv");
    }

    public async Task<IActionResult> AdvanceRequestsExcel(bool async = false, CancellationToken ct = default)
    {
        var query = _db.AdvanceRequests.AsNoTracking();
        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.ContractsAdvanceCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var items = await query.OrderByDescending(a => a.CreatedAt).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("طلبات السلف");
        ws.RightToLeft = true;
        ws.Cell(1, 1).Value = "الموظف"; ws.Cell(1, 2).Value = "المبلغ"; ws.Cell(1, 3).Value = "الحالة"; ws.Cell(1, 4).Value = "السبب";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var a in items)
        {
            ws.Cell(row, 1).Value = a.EmployeeId.ToString();
            ws.Cell(row, 2).Value = (double)a.Amount;
            ws.Cell(row, 3).Value = EnumDisplayExtensions.GetDisplayName(a.Status);
            ws.Cell(row, 4).Value = a.Reason;
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "advance-requests.xlsx");
    }
}
