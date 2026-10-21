using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HRManagement.SharedKernel.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

public sealed class EstablishmentPayloadSummaryBuilder(HRCoreDbContext db)
{
    private const int BulkPreviewLimit = 10;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<EstablishmentPayloadSummaryViewModel> BuildAsync(
        EstablishmentChangeRequest request,
        CancellationToken ct = default)
    {
        return request.ChangeType switch
        {
            EstablishmentChangeType.CreateJobCatalog or EstablishmentChangeType.UpdateJobCatalog
                => new EstablishmentPayloadSummaryViewModel
                {
                    SummaryRows = await BuildJobCatalogAsync(request.PayloadJson, ct)
                },
            EstablishmentChangeType.BulkCreateJobCatalog
                => await BuildBulkJobCatalogAsync(request.PayloadJson, ct),
            EstablishmentChangeType.CreateFundedPosition
                => new EstablishmentPayloadSummaryViewModel
                {
                    SummaryRows = await BuildFundedPositionAsync(request.PayloadJson, ct)
                },
            EstablishmentChangeType.FreezeFundedPosition
                => new EstablishmentPayloadSummaryViewModel { SummaryRows = BuildFreeze(request.PayloadJson) },
            EstablishmentChangeType.UnfreezeFundedPosition
                => new EstablishmentPayloadSummaryViewModel { SummaryRows = BuildUnfreeze(request.PayloadJson) },
            EstablishmentChangeType.DeactivateJobCatalog
                => new EstablishmentPayloadSummaryViewModel
                {
                    SummaryRows = [new("الإجراء", "تعطيل دليل الوظيفة", null)]
                },
            _ => new EstablishmentPayloadSummaryViewModel()
        };
    }

    private async Task<EstablishmentPayloadSummaryViewModel> BuildBulkJobCatalogAsync(
        string payloadJson,
        CancellationToken ct)
    {
        var bulk = JsonSerializer.Deserialize<JobCatalogBulkPayload>(payloadJson, JsonOptions);
        if (bulk is null || bulk.Items.Count == 0)
            return new EstablishmentPayloadSummaryViewModel();

        var batch = await db.JobCatalogImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bulk.ImportBatchId, ct);

        var summaryRows = new List<EstablishmentPayloadSummaryRow>
        {
            new("نوع الاستيراد", "استيراد جماعي CSV", null),
            new("عدد الصفوف", bulk.Items.Count.ToString(), null),
            new("وضع الاستيراد", batch?.ImportMode.ToString() ?? "—", null),
            new("ملف المصدر", batch?.SourceFileName ?? "—", null)
        };

        var preview = new List<JobCatalogBulkPreviewRow>();
        foreach (var item in bulk.Items.Take(BulkPreviewLimit))
        {
            var gradeLabel = await ResolveReferenceLabelAsync(CatalogKind.Grade, item.GradeCode, ct);
            var classLabel = await ResolveReferenceLabelAsync(CatalogKind.JobClassification, item.OccupationalGroup, ct);
            preview.Add(new JobCatalogBulkPreviewRow(
                item.TitleAr,
                item.GradeCode,
                gradeLabel,
                item.OccupationalGroup ?? "—",
                classLabel,
                string.IsNullOrWhiteSpace(item.JobCode) ? "— (COD)" : item.JobCode));
        }

        var overflow = bulk.Items.Count > BulkPreviewLimit
            ? $"+{bulk.Items.Count - BulkPreviewLimit} صفوف أخرى"
            : null;

        return new EstablishmentPayloadSummaryViewModel
        {
            SummaryRows = summaryRows,
            BulkPreviewRows = preview,
            BulkOverflowNote = overflow
        };
    }

    private async Task<IReadOnlyList<EstablishmentPayloadSummaryRow>> BuildJobCatalogAsync(
        string payloadJson,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JobCatalogPayload>(payloadJson, JsonOptions);
        if (payload is null) return [];

        var gradeLabel = await ResolveReferenceLabelAsync(CatalogKind.Grade, payload.GradeCode, ct);
        var classLabel = await ResolveReferenceLabelAsync(CatalogKind.JobClassification, payload.OccupationalGroup, ct);

        var rows = new List<EstablishmentPayloadSummaryRow>
        {
            new("المسمى بالعربي", payload.TitleAr, null),
            new("المسمى بالإنجليزي", payload.TitleEn ?? "—", null),
            new("رمز الدرجة", payload.GradeCode, gradeLabel),
            new("التصنيف الوظيفي", payload.OccupationalGroup ?? "—", classLabel),
            new("الوصف", payload.DescriptionAr ?? "—", null),
            new("نشط", payload.IsActive ? "نعم" : "لا", null)
        };

        if (!string.IsNullOrWhiteSpace(payload.JobCode))
            rows.Insert(0, new("رمز الوظيفة", payload.JobCode, "يُولَّد تلقائياً عند التطبيق إذا تُرك فارغاً"));

        return rows;
    }

    private async Task<IReadOnlyList<EstablishmentPayloadSummaryRow>> BuildFundedPositionAsync(
        string payloadJson,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<FundedPositionCreatePayload>(payloadJson, JsonOptions);
        if (payload is null) return [];

        var org = await db.OrgUnits.AsNoTracking()
            .Where(o => o.Id == payload.OrgUnitId)
            .Select(o => o.NameAr)
            .FirstOrDefaultAsync(ct);
        var catalog = await db.JobCatalogEntries.AsNoTracking()
            .Where(e => e.Id == payload.JobCatalogEntryId)
            .Select(e => new { e.TitleAr, e.JobCode })
            .FirstOrDefaultAsync(ct);
        var gradeLabel = await ResolveReferenceLabelAsync(CatalogKind.Grade, payload.GradeCode, ct);

        return
        [
            new("سنة الموازنة", payload.BudgetYear.ToString(), null),
            new("الوحدة التنظيمية", org ?? payload.OrgUnitId.ToString(), null),
            new("دليل الوظائف", catalog is null ? payload.JobCatalogEntryId.ToString() : $"{catalog.TitleAr} ({catalog.JobCode})", null),
            new("رمز الدرجة", payload.GradeCode, gradeLabel),
            new("مكافئ الدوام الكامل", payload.FTE.ToString("0.##"), null)
        ];
    }

    private static IReadOnlyList<EstablishmentPayloadSummaryRow> BuildFreeze(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<FreezeFundedPositionPayload>(payloadJson, JsonOptions);
        return payload is null
            ? []
            : [new("سبب التجميد", payload.FreezeReason, null)];
    }

    private static IReadOnlyList<EstablishmentPayloadSummaryRow> BuildUnfreeze(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<UnfreezeFundedPositionPayload>(payloadJson, JsonOptions);
        return payload is null
            ? []
            : [new("الإجراء", "إلغاء تجميد المنصب", null)];
    }

    private async Task<string?> ResolveReferenceLabelAsync(
        CatalogKind kind,
        string? code,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        return await db.ReferenceCatalogEntries
            .AsNoTracking()
            .Where(e => e.CatalogKind == kind && e.Code == code)
            .Select(e => e.NameAr)
            .FirstOrDefaultAsync(ct);
    }
}

public sealed class EstablishmentPayloadSummaryViewModel
{
    public IReadOnlyList<EstablishmentPayloadSummaryRow> SummaryRows { get; init; } = [];
    public IReadOnlyList<JobCatalogBulkPreviewRow> BulkPreviewRows { get; init; } = [];
    public string? BulkOverflowNote { get; init; }
}

public sealed record EstablishmentPayloadSummaryRow(string Label, string Value, string? ReferenceLabel);

public sealed record JobCatalogBulkPreviewRow(
    string TitleAr,
    string GradeCode,
    string? GradeLabel,
    string JobClassificationCode,
    string? ClassificationLabel,
    string JobCodeNote);
