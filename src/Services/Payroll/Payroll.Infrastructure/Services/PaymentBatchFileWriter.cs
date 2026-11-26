using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Integration;

namespace Payroll.Infrastructure.Services;

/// <summary>
/// PLAN-58 (§2, P-6): stores the bank file of a sending, in the sending's transaction (the caller saves). The sequence is the batch's next
/// one; the unique index on (batch, sequence) lets one of two concurrent sendings in. The orders it carries keep its id. A reissue
/// (D-PAY-19, approved by four eyes) builds the same orders again into a new sequence and marks the file before it superseded.
/// </summary>
public sealed class PaymentBatchFileWriter(PayrollDbContext db, IEmployeeLookup? employeeLookup = null) : IPaymentBatchFileWriter
{
    public const string AlreadySuperseded = "هذا الملف أُبطل بإعادة إصدار سابقة؛ يُعاد إصدار الملف الساري فقط.";
    public const string FileNotFound = "ملف الصرف غير موجود.";

    public async Task<Guid> WriteAsync(
        PaymentBatch batch,
        IReadOnlyList<PaymentOrder> sent,
        PaymentBatchFileKind kind,
        Guid generatedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var file = await BuildAsync(batch, sent, kind, generatedBy, cancellationToken);
        foreach (var order in sent)
            order.PaymentBatchFileId = file.Id;
        return file.Id;
    }

    /// <summary>
    /// The reissue the four-eyes decision executes: the orders of <paramref name="fileId"/> in a new file of the next sequence; the old
    /// file is superseded. Returns the new file's id, or the refusal.
    /// </summary>
    public async Task<(Guid? FileId, string? Error)> ReissueAsync(
        Guid fileId, string reason, Guid requestedBy, Guid approvedBy, CancellationToken cancellationToken)
    {
        var old = await db.PaymentBatchFiles.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (old is null)
            return (null, FileNotFound);
        if (old.SupersededById is not null)
            return (null, AlreadySuperseded);
        var batch = await db.PaymentBatches.FirstAsync(b => b.Id == old.PaymentBatchId, cancellationToken);
        var orders = await db.PaymentOrders.Where(o => o.PaymentBatchFileId == old.Id).ToListAsync(cancellationToken);

        var file = await BuildAsync(batch, orders, PaymentBatchFileKind.Reissue, requestedBy, cancellationToken);
        file.ReissueReason = reason.Trim().Length <= PaymentBatchFile.ReasonMaxLength ? reason.Trim() : reason.Trim()[..PaymentBatchFile.ReasonMaxLength];
        file.ReissueApprovedBy = approvedBy;
        old.SupersededById = file.Id;
        old.UpdatedAt = DateTime.UtcNow;
        foreach (var order in orders)
            order.PaymentBatchFileId = file.Id;
        return (file.Id, null);
    }

    private async Task<PaymentBatchFile> BuildAsync(
        PaymentBatch batch, IReadOnlyList<PaymentOrder> orders, PaymentBatchFileKind kind, Guid generatedBy, CancellationToken cancellationToken)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == batch.PayrollRunId, cancellationToken);
        var lineIds = orders.Select(o => o.SalaryLineId).ToList();
        var lines = await db.SalaryLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        var names = employeeLookup is null || orders.Count == 0
            ? new Dictionary<Guid, string>()
            : await employeeLookup.GetEmployeeDisplayNamesAsync(orders.Select(o => o.EmployeeId).Distinct(), cancellationToken);
        var rows = orders.Select(o => new PaymentBatchFileRow(o, lines.GetValueOrDefault(o.SalaryLineId), names.GetValueOrDefault(o.EmployeeId))).ToList();

        // The next sequence: the stored ones and any added in this unit of work (a sending and its reissue never share one).
        var stored = await db.PaymentBatchFiles.Where(f => f.PaymentBatchId == batch.Id).MaxAsync(f => (int?)f.Sequence, cancellationToken) ?? 0;
        var pending = db.ChangeTracker.Entries<PaymentBatchFile>()
            .Where(e => e.State == EntityState.Added && e.Entity.PaymentBatchId == batch.Id)
            .Select(e => e.Entity.Sequence).DefaultIfEmpty(0).Max();
        var sequence = Math.Max(stored, pending) + 1;

        var built = PaymentBatchFileBuilder.Build(batch, run, rows, sequence, kind);
        var file = new PaymentBatchFile
        {
            TenantId = batch.TenantId,
            PaymentBatchId = batch.Id,
            Sequence = sequence,
            Kind = kind,
            ControlCount = built.ControlCount,
            ControlTotal = built.ControlTotal,
            Sha256 = built.Sha256,
            Content = built.Content,
            GeneratedBy = generatedBy,
            GeneratedAt = DateTime.UtcNow,
        };
        db.PaymentBatchFiles.Add(file);
        return file;
    }
}
