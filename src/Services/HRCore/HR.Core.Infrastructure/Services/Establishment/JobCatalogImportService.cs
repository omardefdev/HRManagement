using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Infrastructure.Services.Establishment;

public interface IJobCatalogImportService
{
    Task<(JobCatalogImportBatch? Batch, EstablishmentChangeRequest? ChangeRequest, string? Error)> ImportCsvAsync(
        Stream stream,
        string fileName,
        JobCatalogImportMode mode,
        Guid? importedBy,
        CancellationToken ct = default);
}

public sealed class JobCatalogImportService(
    HRCoreDbContext db,
    IJobCatalogImportPort importPort,
    EstablishmentReferenceValidator referenceValidator,
    EstablishmentChangeLifecycleService changeLifecycle,
    IConfiguration configuration) : IJobCatalogImportService
{
    private const int DefaultMaxRows = 500;

    public async Task<(JobCatalogImportBatch? Batch, EstablishmentChangeRequest? ChangeRequest, string? Error)> ImportCsvAsync(
        Stream stream,
        string fileName,
        JobCatalogImportMode mode,
        Guid? importedBy,
        CancellationToken ct = default)
    {
        IReadOnlyList<JobCatalogImportRow> parsed;
        try
        {
            parsed = await importPort.ParseCsvAsync(stream, ct);
        }
        catch (Exception ex)
        {
            return (null, null, $"فشل قراءة الملف: {ex.Message}");
        }

        if (parsed.Count == 0)
            return (null, null, "الملف لا يحتوي على صفوف صالحة.");

        var maxRows = configuration.GetValue("Establishment:JobCatalogImport:MaxRows", DefaultMaxRows);
        if (parsed.Count > maxRows)
            return (null, null, $"عدد الصفوف ({parsed.Count}) يتجاوز الحد الأقصى ({maxRows}).");

        var validationErrors = new List<string>();
        var titleGradeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var legacyCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in parsed)
        {
            if (string.IsNullOrWhiteSpace(row.TitleAr))
                validationErrors.Add($"السطر {row.LineNumber}: المسمى بالعربي مطلوب.");
            if (string.IsNullOrWhiteSpace(row.GradeCode))
                validationErrors.Add($"السطر {row.LineNumber}: رمز الدرجة مطلوب.");
            if (string.IsNullOrWhiteSpace(row.JobClassificationCode))
                validationErrors.Add($"السطر {row.LineNumber}: رمز التصنيف الوظيفي مطلوب.");

            if (!string.IsNullOrWhiteSpace(row.GradeCode)
                && await referenceValidator.ValidateGradeCodeAsync(row.GradeCode, null, ct) is { } gradeError)
                validationErrors.Add($"السطر {row.LineNumber}: {gradeError}");

            if (!string.IsNullOrWhiteSpace(row.JobClassificationCode)
                && await referenceValidator.ValidateJobClassificationAsync(row.JobClassificationCode, null, ct) is { } classError)
                validationErrors.Add($"السطر {row.LineNumber}: {classError}");

            if (!string.IsNullOrWhiteSpace(row.LegacyJobCode))
            {
                if (mode != JobCatalogImportMode.Migration)
                {
                    validationErrors.Add($"السطر {row.LineNumber}: LegacyJobCode مسموح في وضع الترحيل فقط.");
                }
                else if (!legacyCodes.Add(row.LegacyJobCode.Trim()))
                {
                    validationErrors.Add($"السطر {row.LineNumber}: LegacyJobCode مكرر في الملف.");
                }
                else if (await db.JobCatalogEntries.AnyAsync(e => e.JobCode == row.LegacyJobCode.Trim(), ct))
                {
                    validationErrors.Add($"السطر {row.LineNumber}: LegacyJobCode مستخدم مسبقاً.");
                }
            }

            var titleKey = $"{row.TitleAr.Trim()}|{row.GradeCode.Trim()}";
            if (!titleGradeKeys.Add(titleKey))
                validationErrors.Add($"السطر {row.LineNumber}: تكرار (المسمى + الدرجة) في الملف.");
        }

        if (validationErrors.Count > 0)
            return (null, null, string.Join("\n", validationErrors.Take(20)));

        var batch = new JobCatalogImportBatch
        {
            SourceFileName = fileName,
            ImportedAt = DateTime.UtcNow,
            Status = JobCatalogImportStatus.Validated,
            ImportMode = mode,
            RowCount = parsed.Count,
            ImportedBy = importedBy
        };

        foreach (var row in parsed)
        {
            batch.Lines.Add(new JobCatalogImportLine
            {
                LineNumber = row.LineNumber,
                TitleAr = row.TitleAr.Trim(),
                TitleEn = row.TitleEn,
                DescriptionAr = row.DescriptionAr,
                GradeCode = row.GradeCode.Trim(),
                JobClassificationCode = row.JobClassificationCode.Trim(),
                IsActive = row.IsActive,
                LegacyJobCode = row.LegacyJobCode?.Trim()
            });
        }

        var items = parsed.Select(row => new JobCatalogPayload
        {
            TitleAr = row.TitleAr.Trim(),
            TitleEn = row.TitleEn,
            DescriptionAr = row.DescriptionAr,
            GradeCode = row.GradeCode.Trim(),
            OccupationalGroup = row.JobClassificationCode.Trim(),
            IsActive = row.IsActive,
            JobCode = mode == JobCatalogImportMode.Migration && !string.IsNullOrWhiteSpace(row.LegacyJobCode)
                ? row.LegacyJobCode.Trim()
                : string.Empty
        }).ToList();

        db.JobCatalogImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        var bulkPayload = new JobCatalogBulkPayload
        {
            ImportBatchId = batch.Id,
            Items = items
        };

        var changeRequest = await changeLifecycle.CreateDraftAsync(
            EstablishmentChangeType.BulkCreateJobCatalog,
            bulkPayload,
            createdBy: importedBy,
            ct: ct);

        batch.EstablishmentChangeRequestId = changeRequest.Id;
        await db.SaveChangesAsync(ct);

        return (batch, changeRequest, null);
    }
}
