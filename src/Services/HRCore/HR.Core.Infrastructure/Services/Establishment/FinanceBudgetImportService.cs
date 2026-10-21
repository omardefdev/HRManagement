using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Establishment;

public interface IFinanceBudgetImportService
{
    Task<(FinanceBudgetImportBatch? Batch, string? Error)> ImportCsvAsync(
        Stream stream,
        string fileName,
        int fiscalYear,
        Guid? importedBy,
        CancellationToken ct = default);

    Task<IReadOnlyList<FinanceBudgetImportBatch>> ListBatchesAsync(int? fiscalYear, CancellationToken ct = default);
}

public sealed class FinanceBudgetImportService(
    HRCoreDbContext db,
    IFinanceBudgetPort financeBudgetPort) : IFinanceBudgetImportService
{
    public async Task<(FinanceBudgetImportBatch? Batch, string? Error)> ImportCsvAsync(
        Stream stream,
        string fileName,
        int fiscalYear,
        Guid? importedBy,
        CancellationToken ct = default)
    {
        IReadOnlyList<FinanceBudgetImportRow> parsed;
        try
        {
            parsed = await financeBudgetPort.ParseCsvAsync(stream, ct);
        }
        catch (Exception ex)
        {
            return (null, $"فشل قراءة الملف: {ex.Message}");
        }

        if (parsed.Count == 0)
            return (null, "الملف لا يحتوي على صفوف صالحة.");

        var invalid = parsed.Where(r => string.IsNullOrWhiteSpace(r.CostCenter) || r.ApprovedCount < 0).ToList();
        if (invalid.Count > 0)
            return (null, $"صفوف غير صالحة: {invalid.Count}");

        var batch = new FinanceBudgetImportBatch
        {
            SourceFileName = fileName,
            FiscalYear = fiscalYear,
            ImportedAt = DateTime.UtcNow,
            Status = FinanceImportStatus.Applied,
            RowCount = parsed.Count,
            ImportedBy = importedBy
        };

        foreach (var row in parsed)
        {
            batch.Lines.Add(new FinanceBudgetLine
            {
                CostCenter = row.CostCenter,
                GradeCode = row.GradeCode,
                ApprovedCount = row.ApprovedCount,
                ApprovedFTE = row.ApprovedFTE,
                BudgetAmount = row.BudgetAmount,
                ExternalReference = row.ExternalReference
            });
        }

        db.FinanceBudgetImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return (batch, null);
    }

    public async Task<IReadOnlyList<FinanceBudgetImportBatch>> ListBatchesAsync(int? fiscalYear, CancellationToken ct = default)
    {
        var query = db.FinanceBudgetImportBatches.AsNoTracking().AsQueryable();
        if (fiscalYear is int fy)
            query = query.Where(b => b.FiscalYear == fy);
        return await query.OrderByDescending(b => b.ImportedAt).Take(100).ToListAsync(ct);
    }
}
