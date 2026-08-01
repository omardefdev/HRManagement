using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dev.Analytics.Infrastructure.ExportJobs;

public interface IAnalyticsExportJobService
{
    Task<(bool Ok, Guid? JobId, string? Error)> EnqueueAsync(
        string exportType, object filter, ClaimsPrincipal user, CancellationToken ct = default);
    Task<ExportJob?> GetAsync(Guid jobId, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> CancelAsync(Guid jobId, Guid userId, CancellationToken ct = default);
    Task ProcessAsync(Guid jobId, CancellationToken ct = default);
    Task<(bool Ok, Stream? Stream, string? FileName, string? ContentType, string? Error)> OpenDownloadAsync(
        Guid jobId, ClaimsPrincipal user, CancellationToken ct = default);
}

public sealed class AnalyticsExportJobService(
    AnalyticsDbContext db,
    IPublishEndpoint publish,
    IExportArtifactStore artifacts,
    IOptions<ExportJobOptions> options,
    ITenantContext? tenantContext = null) : IAnalyticsExportJobService
{
    public const string ServiceKey = "dev-analytics";

    public async Task<(bool Ok, Guid? JobId, string? Error)> EnqueueAsync(
        string exportType, object filter, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var snapshot = ExportJobAuth.Capture(user, tenantContext);
        if (snapshot.UserId == Guid.Empty)
            return (false, null, "تعذّر تحديد هوية المستخدم.");

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
            TemplateCode = ExportJobTemplates.AnalyticsWorkforceCsv,
            TemplateVersion = ExportJobTemplates.Version1,
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

        job.Status = ExportJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            await using var temp = new FileStream(
                Path.Combine(Path.GetTempPath(), $"export-{jobId:N}.tmp"),
                FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);

            Guid? orgFilter = null;
            if (!string.IsNullOrWhiteSpace(job.FilterJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(job.FilterJson);
                    if (doc.RootElement.TryGetProperty("orgUnitId", out var o) && o.ValueKind == JsonValueKind.String
                        && Guid.TryParse(o.GetString(), out var g))
                        orgFilter = g;
                }
                catch { /* ignore */ }
            }

            var events = await db.AnalyticsEvents.AsNoTracking()
                .Where(e => e.EventName == "EmployeeCreated" || e.EventName == "SeparationCompleted")
                .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
                .ToListAsync(ct);

            var projection = AnalyticsProjectionRules.BuildHeadcountProjection(events, orgFilter);
            await using (var writer = new StreamWriter(temp, new UTF8Encoding(true), 64 * 1024, leaveOpen: true))
            {
                await writer.WriteLineAsync("OrgUnitId,Created,Separated,Active,Fingerprint");
                using var proj = JsonDocument.Parse(projection);
                var root = proj.RootElement;
                await writer.WriteLineAsync(
                    $"{root.GetProperty("orgUnitId")},{root.GetProperty("created")},{root.GetProperty("separated")},{root.GetProperty("active")},{root.GetProperty("fingerprint")}");
                await writer.FlushAsync(ct);
            }

            temp.Position = 0;
            var artifact = await artifacts.SaveAsync(
                job.TenantId, job.Id, "workforce.csv", "text/csv; charset=utf-8", temp, temp.Length, ct);

            job = await db.ExportJobs.FirstAsync(j => j.Id == jobId, ct);
            job.Status = ExportJobStatus.Completed;
            job.ProcessedCount = 1;
            job.TotalCount = 1;
            job.ResultFileId = artifact.FileId;
            job.ResultBucket = "local";
            job.ResultObjectKey = artifact.RelativePath;
            job.ResultContentType = artifact.ContentType;
            job.ResultFileName = artifact.FileName;
            job.CompletedAt = DateTime.UtcNow;
            job.ExpiresAt = DateTime.UtcNow.AddHours(options.Value.ResultRetentionHours);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            job = await db.ExportJobs.FirstAsync(j => j.Id == jobId, ct);
            job.Status = ExportJobStatus.Failed;
            job.Error = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<(bool Ok, Stream? Stream, string? FileName, string? ContentType, string? Error)> OpenDownloadAsync(
        Guid jobId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var job = await db.ExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return (false, null, null, null, "المهمة غير موجودة.");
        if (job.Status != ExportJobStatus.Completed || string.IsNullOrEmpty(job.ResultObjectKey) || job.ResultFileId is null)
            return (false, null, null, null, "النتيجة غير جاهزة.");
        var snapshot = ExportJobAuth.Deserialize(job.AuthSnapshotJson);
        if (snapshot is null || !ExportJobAuth.MatchesCurrentUser(snapshot, user, tenantContext))
            return (false, null, null, null, "غير مصرح بتحميل هذا التصدير.");
        var stream = await artifacts.OpenReadAsync(
            new ExportArtifactRef(job.ResultFileId.Value, job.ResultObjectKey!, job.ResultContentType ?? "application/octet-stream", job.ResultFileName ?? "export.bin"),
            ct);
        return (true, stream, job.ResultFileName, job.ResultContentType, null);
    }
}

public sealed class ExportJobRequestedConsumer(
    IAnalyticsExportJobService exportJobs,
    ILogger<ExportJobRequestedConsumer> logger) : IConsumer<IExportJobRequested>
{
    public async Task Consume(ConsumeContext<IExportJobRequested> context)
    {
        var msg = context.Message;
        if (!string.Equals(msg.ServiceKey, AnalyticsExportJobService.ServiceKey, StringComparison.OrdinalIgnoreCase))
            return;
        logger.LogInformation("FUN-006/PERF-004 analytics export {JobId}", msg.JobId);
        await exportJobs.ProcessAsync(msg.JobId, context.CancellationToken);
    }
}
