using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.ExportJobs;

public interface IPayrollExportJobService
{
    Task<(bool Ok, Guid? JobId, string? Error)> EnqueueAsync(
        string exportType,
        object filter,
        ClaimsPrincipal user,
        CancellationToken ct = default);

    Task<ExportJob?> GetAsync(Guid jobId, CancellationToken ct = default);

    Task<(bool Ok, string? Error)> CancelAsync(Guid jobId, Guid userId, CancellationToken ct = default);

    Task ProcessAsync(Guid jobId, CancellationToken ct = default);

    Task<(bool Ok, Stream? Stream, string? FileName, string? ContentType, string? Error)> OpenDownloadAsync(
        Guid jobId,
        ClaimsPrincipal user,
        CancellationToken ct = default);
}

public sealed class PayrollExportJobService(
    PayrollDbContext db,
    IPublishEndpoint publish,
    IExportArtifactStore artifacts,
    IOptions<ExportJobOptions> options,
    ITenantContext? tenantContext = null) : IPayrollExportJobService
{
    public const string ServiceKey = "payroll";

    public async Task<(bool Ok, Guid? JobId, string? Error)> EnqueueAsync(
        string exportType,
        object filter,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var snapshot = ExportJobAuth.Capture(user, tenantContext);
        if (snapshot.UserId == Guid.Empty)
            return (false, null, "تعذّر تحديد هوية المستخدم.");

        var (templateCode, templateVersion) = ResolveTemplate(exportType);
        var job = new ExportJob
        {
            TenantId = snapshot.TenantId == Guid.Empty
                ? (tenantContext?.TenantId ?? Guid.Empty)
                : snapshot.TenantId,
            ServiceKey = ServiceKey,
            ExportType = exportType,
            Status = ExportJobStatus.Pending,
            RequestedByUserId = snapshot.UserId,
            FilterJson = JsonSerializer.Serialize(filter),
            AuthSnapshotJson = ExportJobAuth.Serialize(snapshot),
            TemplateCode = templateCode,
            TemplateVersion = templateVersion,
            ExpiresAt = DateTime.UtcNow.AddHours(options.Value.ResultRetentionHours)
        };

        db.ExportJobs.Add(job);
        await db.SaveChangesAsync(ct);

        await publish.Publish<IExportJobRequested>(new
        {
            JobId = job.Id,
            TenantId = job.TenantId,
            ServiceKey,
            ExportType = exportType
        }, ct);

        await db.SaveChangesAsync(ct);
        return (true, job.Id, null);
    }

    public Task<ExportJob?> GetAsync(Guid jobId, CancellationToken ct = default) =>
        db.ExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);

    public async Task<(bool Ok, string? Error)> CancelAsync(Guid jobId, Guid userId, CancellationToken ct = default)
    {
        var job = await db.ExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return (false, "المهمة غير موجودة.");
        if (job.RequestedByUserId != userId) return (false, "غير مصرح.");
        if (job.Status is ExportJobStatus.Completed or ExportJobStatus.Failed or ExportJobStatus.Cancelled)
            return (false, "لا يمكن إلغاء مهمة منتهية.");

        job.Status = ExportJobStatus.Cancelled;
        job.CompletedAt = DateTime.UtcNow;
        job.Error = "أُلغيت بواسطة المستخدم.";
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task ProcessAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.ExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return;
        if (job.Status is ExportJobStatus.Completed or ExportJobStatus.Cancelled) return;
        if (job.Status == ExportJobStatus.Failed && job.ResultFileId.HasValue) return;

        var owner = Environment.MachineName + ":" + Guid.NewGuid().ToString("N")[..8];
        job.Status = ExportJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        job.LeaseOwner = owner;
        job.LeaseUntil = DateTime.UtcNow.AddSeconds(options.Value.LeaseSeconds);
        await db.SaveChangesAsync(ct);

        try
        {
            await using var temp = new FileStream(
                Path.Combine(Path.GetTempPath(), $"export-{jobId:N}.tmp"),
                FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);

            var (fileName, contentType, total) = await WritePayloadAsync(job, temp, ct);
            if (await IsCancelledAsync(jobId, ct))
            {
                job = await db.ExportJobs.FirstAsync(j => j.Id == jobId, ct);
                job.Status = ExportJobStatus.Cancelled;
                job.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }

            temp.Position = 0;
            var artifact = await artifacts.SaveAsync(
                job.TenantId, job.Id, fileName, contentType, temp, temp.Length, ct);

            job = await db.ExportJobs.FirstAsync(j => j.Id == jobId, ct);
            job.Status = ExportJobStatus.Completed;
            job.ProcessedCount = total;
            job.TotalCount = total;
            job.ResultFileId = artifact.FileId;
            job.ResultBucket = "local";
            job.ResultObjectKey = artifact.RelativePath;
            job.ResultContentType = artifact.ContentType;
            job.ResultFileName = artifact.FileName;
            job.CompletedAt = DateTime.UtcNow;
            job.ExpiresAt = DateTime.UtcNow.AddHours(options.Value.ResultRetentionHours);
            job.LeaseUntil = null;
            await db.SaveChangesAsync(ct);
            ExportJobMetrics.RecordCompleted(ServiceKey, job.ExportType);
        }
        catch (Exception ex)
        {
            job = await db.ExportJobs.FirstAsync(j => j.Id == jobId, ct);
            job.Status = ExportJobStatus.Failed;
            job.Error = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            job.LeaseUntil = null;
            await db.SaveChangesAsync(ct);
            ExportJobMetrics.RecordFailed(ServiceKey, job.ExportType);
        }
    }

    public async Task<(bool Ok, Stream? Stream, string? FileName, string? ContentType, string? Error)> OpenDownloadAsync(
        Guid jobId,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var job = await db.ExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return (false, null, null, null, "المهمة غير موجودة.");
        if (job.Status != ExportJobStatus.Completed || string.IsNullOrEmpty(job.ResultObjectKey) || job.ResultFileId is null)
            return (false, null, null, null, "النتيجة غير جاهزة.");
        if (job.ExpiresAt is { } exp && exp < DateTime.UtcNow)
            return (false, null, null, null, "انتهت صلاحية ملف التصدير.");

        var snapshot = ExportJobAuth.Deserialize(job.AuthSnapshotJson);
        if (snapshot is null || !ExportJobAuth.MatchesCurrentUser(snapshot, user, tenantContext))
            return (false, null, null, null, "غير مصرح بتحميل هذا التصدير.");

        var stream = await artifacts.OpenReadAsync(
            new ExportArtifactRef(job.ResultFileId.Value, job.ResultObjectKey!, job.ResultContentType ?? "application/octet-stream", job.ResultFileName ?? "export.bin"),
            ct);
        return (true, stream, job.ResultFileName, job.ResultContentType, null);
    }

    private async Task<(string FileName, string ContentType, int Total)> WritePayloadAsync(
        ExportJob job, Stream output, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);
        var chunk = Math.Max(50, options.Value.ChunkSize);

        if (job.ExportType is ExportJobTemplates.PayrollRunCsv or "ExportRunCsv")
        {
            var filter = JsonSerializer.Deserialize<RunFilter>(job.FilterJson) ?? new RunFilter(Guid.Empty);
            await writer.WriteLineAsync("EmployeeId,GrossPay,TotalDeductions,NetPay");
            var total = 0;
            var skip = 0;
            while (true)
            {
                if (await IsCancelledAsync(job.Id, ct)) break;
                var batch = await db.SalaryLines.AsNoTracking()
                    .Where(l => l.PayrollRunId == filter.RunId)
                    .OrderBy(l => l.EmployeeId)
                    .Skip(skip)
                    .Take(chunk)
                    .Select(l => new { l.EmployeeId, l.GrossPay, l.TotalDeductions, l.NetPay })
                    .ToListAsync(ct);
                if (batch.Count == 0) break;
                foreach (var row in batch)
                    await writer.WriteLineAsync($"{row.EmployeeId},{row.GrossPay},{row.TotalDeductions},{row.NetPay}");
                total += batch.Count;
                skip += batch.Count;
                job.ProcessedCount = total;
                job.LeaseUntil = DateTime.UtcNow.AddSeconds(options.Value.LeaseSeconds);
                await db.SaveChangesAsync(ct);
                if (batch.Count < chunk) break;
            }
            await writer.FlushAsync(ct);
            return ($"payroll-run-{filter.RunId:N}.csv", "text/csv; charset=utf-8", total);
        }

        if (job.ExportType is ExportJobTemplates.PayrollEosCsv or "ExportEosCsv")
        {
            await writer.WriteLineAsync("EmployeeId,BaseSalary,NetSettlement,Status,CalculatedAt,CreatedAt");
            var total = 0;
            var skip = 0;
            while (true)
            {
                if (await IsCancelledAsync(job.Id, ct)) break;
                var batch = await db.EndOfServiceSettlements.AsNoTracking()
                    .OrderByDescending(e => e.CreatedAt)
                    .Skip(skip)
                    .Take(chunk)
                    .ToListAsync(ct);
                if (batch.Count == 0) break;
                foreach (var e in batch)
                    await writer.WriteLineAsync($"{e.EmployeeId},{e.BaseSalary},{e.NetSettlement},{e.Status},{e.CalculatedAt:yyyy-MM-dd},{e.CreatedAt:yyyy-MM-dd}");
                total += batch.Count;
                skip += batch.Count;
                job.ProcessedCount = total;
                await db.SaveChangesAsync(ct);
                if (batch.Count < chunk) break;
            }
            await writer.FlushAsync(ct);
            return ("eos-settlements.csv", "text/csv; charset=utf-8", total);
        }

        if (job.ExportType is ExportJobTemplates.PayrollRegister or "PayrollRegister")
        {
            var filter = JsonSerializer.Deserialize<RegisterFilter>(job.FilterJson) ?? new RegisterFilter(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
            await writer.WriteLineAsync("RunId,PeriodStart,PeriodEnd,EmployeeId,GrossPay,TotalDeductions,NetPay");
            var total = 0;
            var lines = db.SalaryLines.AsNoTracking()
                .Include(l => l.PayrollRun)
                .Where(l => l.PayrollRun.PeriodStart.Year == filter.Year && l.PayrollRun.PeriodStart.Month == filter.Month)
                .OrderBy(l => l.PayrollRunId).ThenBy(l => l.EmployeeId);
            var skip = 0;
            while (true)
            {
                if (await IsCancelledAsync(job.Id, ct)) break;
                var batch = await lines.Skip(skip).Take(chunk).Select(l => new
                {
                    l.PayrollRunId,
                    l.PayrollRun.PeriodStart,
                    l.PayrollRun.PeriodEnd,
                    l.EmployeeId,
                    l.GrossPay,
                    l.TotalDeductions,
                    l.NetPay
                }).ToListAsync(ct);
                if (batch.Count == 0) break;
                foreach (var r in batch)
                    await writer.WriteLineAsync($"{r.PayrollRunId},{r.PeriodStart:yyyy-MM-dd},{r.PeriodEnd:yyyy-MM-dd},{r.EmployeeId},{r.GrossPay},{r.TotalDeductions},{r.NetPay}");
                total += batch.Count;
                skip += batch.Count;
                job.ProcessedCount = total;
                await db.SaveChangesAsync(ct);
                if (batch.Count < chunk) break;
            }
            await writer.FlushAsync(ct);
            return ($"payroll-register-{filter.Year}{filter.Month:00}.csv", "text/csv; charset=utf-8", total);
        }

        // Bulk payslips: CSV index (full ZIP of PDFs remains sync for small runs; async produces line index)
        if (job.ExportType is ExportJobTemplates.PayrollBulkPayslips or "BulkPayslips")
        {
            var filter = JsonSerializer.Deserialize<RunFilter>(job.FilterJson) ?? new RunFilter(Guid.Empty);
            await writer.WriteLineAsync("SalaryLineId,EmployeeId,GrossPay,NetPay");
            var total = 0;
            var skip = 0;
            while (true)
            {
                if (await IsCancelledAsync(job.Id, ct)) break;
                var batch = await db.SalaryLines.AsNoTracking()
                    .Where(l => l.PayrollRunId == filter.RunId)
                    .OrderBy(l => l.Id)
                    .Skip(skip).Take(chunk)
                    .Select(l => new { l.Id, l.EmployeeId, l.GrossPay, l.NetPay })
                    .ToListAsync(ct);
                if (batch.Count == 0) break;
                foreach (var r in batch)
                    await writer.WriteLineAsync($"{r.Id},{r.EmployeeId},{r.GrossPay},{r.NetPay}");
                total += batch.Count;
                skip += batch.Count;
                job.ProcessedCount = total;
                await db.SaveChangesAsync(ct);
                if (batch.Count < chunk) break;
            }
            await writer.FlushAsync(ct);
            return ($"payslips-index-{filter.RunId:N}.csv", "text/csv; charset=utf-8", total);
        }

        throw new InvalidOperationException($"Unsupported export type: {job.ExportType}");
    }

    private async Task<bool> IsCancelledAsync(Guid jobId, CancellationToken ct)
    {
        var status = await db.ExportJobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.Status)
            .FirstOrDefaultAsync(ct);
        return status == ExportJobStatus.Cancelled;
    }

    private static (string Code, string Version) ResolveTemplate(string exportType) => exportType switch
    {
        ExportJobTemplates.PayrollRunCsv or "ExportRunCsv" => (ExportJobTemplates.PayrollRunCsv, ExportJobTemplates.Version1),
        ExportJobTemplates.PayrollEosCsv or "ExportEosCsv" => (ExportJobTemplates.PayrollEosCsv, ExportJobTemplates.Version1),
        ExportJobTemplates.PayrollRegister or "PayrollRegister" => (ExportJobTemplates.PayrollRegister, ExportJobTemplates.Version1),
        ExportJobTemplates.PayrollBulkPayslips or "BulkPayslips" => (ExportJobTemplates.PayrollBulkPayslips, ExportJobTemplates.Version1),
        _ => (exportType, ExportJobTemplates.Version1)
    };

    private sealed record RunFilter(Guid RunId);
    private sealed record RegisterFilter(int Year, int Month);
}
