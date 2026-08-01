using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using HR.Core.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.ExportJobs;

public interface IHRCoreExportJobService
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

public sealed class HRCoreExportJobService(
    HRCoreDbContext db,
    IPublishEndpoint publish,
    IExportArtifactStore artifacts,
    IOptions<ExportJobOptions> options,
    OrgScopeService orgScope,
    ITenantContext? tenantContext = null) : IHRCoreExportJobService
{
    public const string ServiceKey = "hr-core";

    public async Task<(bool Ok, Guid? JobId, string? Error)> EnqueueAsync(
        string exportType,
        object filter,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(user, ct);
        if (scope.IsDenied)
            return (false, null, "غير مصرح.");

        var snapshot = ExportJobAuth.Capture(
            user,
            tenantContext,
            allowedOrgUnitIds: scope.MustFilter ? scope.AllowedOrgUnitIds : null);
        if (snapshot.UserId == Guid.Empty)
            return (false, null, "تعذّر تحديد هوية المستخدم.");

        var (templateCode, templateVersion) = ResolveTemplate(exportType);
        var filterJson = scope.MustFilter
            ? JsonSerializer.Serialize(new EmployeesFilter(scope.AllowedOrgUnitIds.ToList()))
            : JsonSerializer.Serialize(filter);
        var job = new ExportJob
        {
            TenantId = snapshot.TenantId == Guid.Empty
                ? (tenantContext?.TenantId ?? Guid.Empty)
                : snapshot.TenantId,
            ServiceKey = ServiceKey,
            ExportType = exportType,
            Status = ExportJobStatus.Pending,
            RequestedByUserId = snapshot.UserId,
            FilterJson = filterJson,
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

        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(user, ct);
        var snapshot = ExportJobAuth.Deserialize(job.AuthSnapshotJson);
        if (snapshot is null || !ExportJobAuth.MatchesCurrentUser(
                snapshot, user, tenantContext,
                scope.MustFilter ? scope.AllowedOrgUnitIds : null))
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

        if (job.ExportType is ExportJobTemplates.HrCoreEmployeesCsv or "ExportEmployeesCsv")
        {
            var filter = JsonSerializer.Deserialize<EmployeesFilter>(job.FilterJson) ?? new EmployeesFilter(null);
            var query = db.Employees.AsNoTracking();
            if (filter.AllowedOrgUnitIds is { Count: > 0 })
                query = query.Where(e => e.PrimaryOrgUnitId.HasValue && filter.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

            await writer.WriteLineAsync("Id,FullName,NationalIdMasked,CurrentStatus");
            var total = 0;
            var skip = 0;
            while (true)
            {
                if (await IsCancelledAsync(job.Id, ct)) break;
                var batch = await query
                    .OrderBy(e => e.FullName)
                    .Skip(skip)
                    .Take(chunk)
                    .Select(e => new { e.Id, e.FullName, e.NationalId, e.CurrentStatus })
                    .ToListAsync(ct);
                if (batch.Count == 0) break;
                foreach (var row in batch)
                    await writer.WriteLineAsync($"{row.Id},{Csv(row.FullName)},{Csv(PiiMask.NationalId(row.NationalId))},{row.CurrentStatus}");
                total += batch.Count;
                skip += batch.Count;
                job.ProcessedCount = total;
                job.LeaseUntil = DateTime.UtcNow.AddSeconds(options.Value.LeaseSeconds);
                await db.SaveChangesAsync(ct);
                if (batch.Count < chunk) break;
            }
            await writer.FlushAsync(ct);
            return ($"employees-{DateTime.UtcNow:yyyyMMdd}.csv", "text/csv; charset=utf-8", total);
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

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static (string Code, string Version) ResolveTemplate(string exportType) => exportType switch
    {
        ExportJobTemplates.HrCoreEmployeesCsv or "ExportEmployeesCsv" => (ExportJobTemplates.HrCoreEmployeesCsv, ExportJobTemplates.Version1),
        _ => (exportType, ExportJobTemplates.Version1)
    };

    private sealed record EmployeesFilter(List<Guid>? AllowedOrgUnitIds);
}
