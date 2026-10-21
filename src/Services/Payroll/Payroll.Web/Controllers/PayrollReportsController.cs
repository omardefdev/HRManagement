using ClosedXML.Excel;
using HRManagement.Documents.QuestPdf;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Localization;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.InboundEvents;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.ExportJobs;
using Payroll.Infrastructure.Integration;
using System.IO.Compression;

namespace Payroll.Web.Controllers;

public class PayrollReportsController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IEmployeeLookup _employeeLookup;
    private readonly IMediator _mediator;
    private readonly PayrollExternalExportService _externalExport;
    private readonly IPayrollExportJobService _exportJobs;

    public PayrollReportsController(
        PayrollDbContext db,
        IEmployeeLookup employeeLookup,
        IMediator mediator,
        PayrollExternalExportService externalExport,
        IPayrollExportJobService exportJobs)
    {
        _db = db;
        _employeeLookup = employeeLookup;
        _mediator = mediator;
        _externalExport = externalExport;
        _exportJobs = exportJobs;
    }

    public async Task<IActionResult> Index()
    {
        var totalRuns = await _db.PayrollRuns.CountAsync();
        var totalPosted = await _db.PayrollRuns.CountAsync(r => r.Status == Domain.Enums.PayrollRunStatus.Posted);
        var totalEmployees = await _db.EmployeePayProfiles.Select(p => p.EmployeeId).Distinct().CountAsync();
        var totalEos = await _db.EndOfServiceSettlements.CountAsync();

        ViewBag.TotalRuns = totalRuns;
        ViewBag.TotalPosted = totalPosted;
        ViewBag.TotalEmployees = totalEmployees;
        ViewBag.TotalEos = totalEos;
        ViewBag.LatestRunId = await _db.PayrollRuns
            .OrderByDescending(r => r.PeriodStart)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync();
        return View();
    }

    public async Task<IActionResult> Payslip(Guid salaryLineId)
    {
        var line = await _db.SalaryLines
            .Include(l => l.PayrollRun)
            .FirstOrDefaultAsync(l => l.Id == salaryLineId);
        if (line is null) return NotFound();

        var ownEmployeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (ownEmployeeId.HasValue && line.EmployeeId != ownEmployeeId.Value)
            return Forbid();

        var components = await _db.SalaryLineComponents
            .Where(c => c.SalaryLineId == salaryLineId)
            .OrderBy(c => c.ComponentType)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        var employeeName = await _employeeLookup.GetEmployeeDisplayNameAsync(line.EmployeeId, CancellationToken.None)
                           ?? "غير متاح";

        var bytes = HrPayslipPdfGenerator.Generate(ToPayslipModel(line, line.PayrollRun, employeeName, components));
        await _mediator.Send(new MarkPayslipSentCommand(salaryLineId));
        return File(bytes, "application/pdf", $"payslip-{line.EmployeeId}-{line.PayrollRun.PeriodStart:yyyyMM}.pdf");
    }

    private static PayslipPdfModel ToPayslipModel(
        Payroll.Domain.Entities.SalaryLine line,
        Payroll.Domain.Entities.PayrollRun run,
        string employeeName,
        IReadOnlyList<Payroll.Domain.Entities.SalaryLineComponent> components) =>
        new(
            employeeName,
            line.EmployeeId,
            run.PeriodStart,
            run.PeriodEnd,
            run.ConfigContentHash,
            components.Select(c => new PayslipComponentLine(
                c.ComponentName,
                EnumDisplayExtensions.GetDisplayName(c.ComponentType),
                c.Amount)).ToList(),
            line.GrossPay,
            line.TotalDeductions,
            line.NetPay);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkPayslips(Guid payrollRunId, bool async = false)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, $"PayrollReports/BulkPayslips/{payrollRunId}");
        var run = await _db.PayrollRuns.Include(r => r.SalaryLines).FirstOrDefaultAsync(r => r.Id == payrollRunId);
        if (run is null) return NotFound();

        if (async || run.SalaryLines.Count > SearchFilterBase.MaxExportRows)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(
                ExportJobTemplates.PayrollBulkPayslips, new { RunId = payrollRunId }, User);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var names = await _employeeLookup.GetEmployeeDisplayNamesAsync(run.SalaryLines.Select(l => l.EmployeeId), CancellationToken.None);

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var line in run.SalaryLines)
            {
                var components = await _db.SalaryLineComponents
                    .Where(c => c.SalaryLineId == line.Id)
                    .OrderBy(c => c.ComponentType)
                    .Take(SearchFilterBase.MaxPageSize)
                    .ToListAsync();

                var employeeName = names.GetValueOrDefault(line.EmployeeId) ?? "غير متاح";
                var pdf = HrPayslipPdfGenerator.Generate(ToPayslipModel(line, run, employeeName, components));

                var entry = archive.CreateEntry($"payslip-{employeeName}-{run.PeriodStart:yyyyMM}.pdf");
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(pdf);
                await _mediator.Send(new MarkPayslipSentCommand(line.Id));
            }
        }

        ms.Position = 0;
        return File(ms.ToArray(), "application/zip", $"payslips-{run.PeriodStart:yyyyMM}.zip");
    }

    public async Task<IActionResult> PayrollRegister(Guid payrollRunId, bool async = false)
    {
        var run = await _db.PayrollRuns
            .Include(r => r.SalaryLines)
            .FirstOrDefaultAsync(r => r.Id == payrollRunId);
        if (run is null) return NotFound();

        var employeeNames = await _employeeLookup.GetEmployeeDisplayNamesAsync(
            run.SalaryLines.Select(line => line.EmployeeId), HttpContext.RequestAborted);

        if (async || run.SalaryLines.Count > SearchFilterBase.MaxExportRows)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(
                ExportJobTemplates.PayrollRegister,
                new { Year = run.PeriodStart.Year, Month = run.PeriodStart.Month },
                User);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("سجل الرواتب");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = "الموظف"; ws.Cell(1, 2).Value = "الإجمالي";
        ws.Cell(1, 3).Value = "الخصومات"; ws.Cell(1, 4).Value = "الصافي";
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        foreach (var line in run.SalaryLines.OrderBy(l => l.EmployeeId))
        {
            ws.Cell(row, 1).Value = employeeNames.GetValueOrDefault(line.EmployeeId, "غير متاح");
            ws.Cell(row, 2).Value = line.GrossPay;
            ws.Cell(row, 3).Value = line.TotalDeductions;
            ws.Cell(row, 4).Value = line.NetPay;
            row++;
        }

        ws.Cell(row, 1).Value = "المجموع";
        ws.Cell(row, 2).FormulaA1 = $"SUM(B2:B{row - 1})";
        ws.Cell(row, 3).FormulaA1 = $"SUM(C2:C{row - 1})";
        ws.Cell(row, 4).FormulaA1 = $"SUM(D2:D{row - 1})";
        ws.Row(row).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"payroll-register-{run.PeriodStart:yyyyMM}.xlsx");
    }

    public async Task<IActionResult> MonthlySummary(int? year, int? month)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var m = month ?? DateTime.UtcNow.Month;

        var runs = await _db.PayrollRuns
            .Include(r => r.SalaryLines)
            .Where(r => r.PeriodStart.Year == y && r.PeriodStart.Month == m)
            .AsNoTracking()
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        ViewBag.Year = y;
        ViewBag.Month = m;
        ViewBag.Runs = runs;
        ViewBag.TotalGross = runs.SelectMany(r => r.SalaryLines).Sum(l => l.GrossPay);
        ViewBag.TotalDeductions = runs.SelectMany(r => r.SalaryLines).Sum(l => l.TotalDeductions);
        ViewBag.TotalNet = runs.SelectMany(r => r.SalaryLines).Sum(l => l.NetPay);
        ViewBag.EmployeeCount = runs.SelectMany(r => r.SalaryLines).Select(l => l.EmployeeId).Distinct().Count();

        return View();
    }

    public async Task<IActionResult> ExportRunCsv(Guid runId, bool async = false)
    {
        var run = await _db.PayrollRuns
            .Include(r => r.SalaryLines)
            .FirstOrDefaultAsync(r => r.Id == runId);
        if (run is null) return NotFound();

        if (async || run.SalaryLines.Count > SearchFilterBase.MaxExportRows)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(
                ExportJobTemplates.PayrollRunCsv, new { RunId = runId }, User);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("EmployeeId,GrossPay,TotalDeductions,NetPay");
        foreach (var line in run.SalaryLines.OrderBy(l => l.EmployeeId).Take(SearchFilterBase.MaxExportRows))
            csv.AppendLine($"{line.EmployeeId},{line.GrossPay},{line.TotalDeductions},{line.NetPay}");

        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"payroll-{run.PeriodStart:yyyyMM}.csv");
    }

    /// <summary>تصدير ملف الرواتب للمالية/الخزانة عبر موصّل التكامل (CSV في مجلد integration-outbox/treasury).</summary>
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> ExportTreasuryFile(Guid runId)
    {
        try
        {
            var (disbursement, _) = await _externalExport.ExportRunAsync(runId);
            if (!disbursement.Success)
            {
                TempData["Error"] = disbursement.Message ?? "فشل تصدير ملف المالية";
                return RedirectToAction(nameof(MonthlySummary));
            }

            if (!string.IsNullOrEmpty(disbursement.OutputFilePath) && System.IO.File.Exists(disbursement.OutputFilePath))
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(disbursement.OutputFilePath);
                return File(bytes, "text/csv", Path.GetFileName(disbursement.OutputFilePath));
            }

            TempData["Success"] = disbursement.Message;
            return RedirectToAction(nameof(MonthlySummary));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(MonthlySummary));
        }
    }

    /// <summary>تصدير ملف اشتراكات التأمينات الاجتماعية عبر موصّل التكامل.</summary>
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> ExportSocialInsuranceFile(Guid runId)
    {
        try
        {
            var (_, insurance) = await _externalExport.ExportRunAsync(runId);
            if (!insurance.Success)
            {
                TempData["Error"] = insurance.Message ?? "فشل تصدير ملف التأمينات";
                return RedirectToAction(nameof(MonthlySummary));
            }

            if (!string.IsNullOrEmpty(insurance.OutputFilePath) && System.IO.File.Exists(insurance.OutputFilePath))
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(insurance.OutputFilePath);
                return File(bytes, "text/csv", Path.GetFileName(insurance.OutputFilePath));
            }

            TempData["Success"] = insurance.Message;
            return RedirectToAction(nameof(MonthlySummary));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(MonthlySummary));
        }
    }

    /// <summary>تصدير ملفي المالية والتأمينات معاً (JSON ملخص + تحميل ZIP اختياري لاحقاً — حالياً رسالة نجاح).</summary>
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> ExportExternalIntegrationBundle(Guid runId)
    {
        try
        {
            var (disbursement, insurance) = await _externalExport.ExportRunAsync(runId);
            if (!disbursement.Success || !insurance.Success)
            {
                TempData["Error"] = $"{disbursement.Message}; {insurance.Message}";
                return RedirectToAction(nameof(MonthlySummary));
            }

            TempData["Success"] = $"تم التصدير: المالية → {disbursement.OutputFilePath}; التأمينات → {insurance.OutputFilePath}";
            return RedirectToAction(nameof(MonthlySummary));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(MonthlySummary));
        }
    }

    public async Task<IActionResult> ExportEosCsv(bool async = false, CancellationToken ct = default)
    {
        var query = _db.EndOfServiceSettlements.AsNoTracking();
        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.PayrollEosCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var items = await query.OrderByDescending(e => e.CreatedAt).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("EmployeeId,BaseSalary,NetSettlement,Status,CalculatedAt,CreatedAt");
        foreach (var e in items)
            csv.AppendLine($"{e.EmployeeId},{e.BaseSalary},{e.NetSettlement},{EnumDisplayExtensions.GetDisplayName(e.Status)},{e.CalculatedAt:yyyy-MM-dd},{e.CreatedAt:yyyy-MM-dd}");
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "eos-settlements.csv");
    }

    public async Task<IActionResult> ExportEosExcel(bool async = false, CancellationToken ct = default)
    {
        var query = _db.EndOfServiceSettlements.AsNoTracking();
        var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(query, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await _exportJobs.EnqueueAsync(ExportJobTemplates.PayrollEosCsv, new { }, User, ct);
            if (!ok) return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var items = await query.OrderByDescending(e => e.CreatedAt).Take(SearchFilterBase.MaxExportRows).ToListAsync(ct);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("مستحقات نهاية الخدمة");
        ws.RightToLeft = true;
        ws.Cell(1, 1).Value = "الموظف"; ws.Cell(1, 2).Value = "الراتب الأساسي"; ws.Cell(1, 3).Value = "صافي التسوية"; ws.Cell(1, 4).Value = "الحالة"; ws.Cell(1, 5).Value = "تاريخ الحساب";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var e in items)
        {
            ws.Cell(row, 1).Value = e.EmployeeId.ToString(); ws.Cell(row, 2).Value = (double)e.BaseSalary;
            ws.Cell(row, 3).Value = (double)e.NetSettlement; ws.Cell(row, 4).Value = EnumDisplayExtensions.GetDisplayName(e.Status);
            ws.Cell(row, 5).Value = e.CalculatedAt.ToString("yyyy-MM-dd"); row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "eos-settlements.xlsx");
    }

    public record PayrollVarianceRow(Guid EmployeeId, decimal BaselineNet, decimal ComparisonNet, decimal Variance);

    public async Task<IActionResult> VarianceReport(Guid baselineRunId, Guid comparisonRunId)
    {
        var baseline = await _db.PayrollRuns
            .Include(r => r.SalaryLines)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == baselineRunId);
        var comparison = await _db.PayrollRuns
            .Include(r => r.SalaryLines)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == comparisonRunId);
        if (baseline is null || comparison is null) return NotFound();

        var baselineByEmp = baseline.SalaryLines.ToDictionary(l => l.EmployeeId);
        var comparisonByEmp = comparison.SalaryLines.ToDictionary(l => l.EmployeeId);
        var allEmployees = baselineByEmp.Keys.Union(comparisonByEmp.Keys).OrderBy(x => x).ToList();

        var rows = allEmployees.Select(empId =>
        {
            baselineByEmp.TryGetValue(empId, out var b);
            comparisonByEmp.TryGetValue(empId, out var c);
            var bNet = b?.NetPay ?? 0;
            var cNet = c?.NetPay ?? 0;
            return new PayrollVarianceRow(empId, bNet, cNet, cNet - bNet);
        }).ToList();

        ViewBag.BaselineRun = baseline;
        ViewBag.ComparisonRun = comparison;
        ViewBag.TotalBaseline = rows.Sum(r => r.BaselineNet);
        ViewBag.TotalComparison = rows.Sum(r => r.ComparisonNet);
        ViewBag.TotalVariance = rows.Sum(r => r.Variance);
        ViewBag.EmployeeNames = await _employeeLookup.GetEmployeeDisplayNamesAsync(
            rows.Select(r => r.EmployeeId), HttpContext.RequestAborted);
        return View(rows);
    }
}
