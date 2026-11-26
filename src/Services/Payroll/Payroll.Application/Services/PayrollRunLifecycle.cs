using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;

namespace Payroll.Application.Services;

/// <summary>PLAN-56: what the acts of a run's life share: the fingerprint of a version's lines, and the checks before a run goes.</summary>
public static class PayrollRunLifecycle
{
    /// <summary>The action name of the audit row written when a run's lines are deleted for a recalculation.</summary>
    public const string LinesDeletedAuditAction = "LinesDeleted";

    /// <summary>What a version of a run's results was: how many lines, and a fingerprint of them.</summary>
    public sealed record LinesSnapshot(int LineCount, string Fingerprint);

    /// <summary>
    /// A fingerprint of a run's saved lines and their components, in a fixed order (by employee, then by component): two versions
    /// with the same lines have the same fingerprint. Read in pages, so a run of any size is fingerprinted in bounded memory.
    /// </summary>
    public static async Task<LinesSnapshot> SnapshotLinesAsync(
        IPayrollRepository repository,
        Guid payrollRunId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        Guid? after = null;
        while (true)
        {
            var cursor = after;
            var page = await repository.SalaryLines
                .AsNoTracking()
                .Where(l => l.PayrollRunId == payrollRunId && (cursor == null || l.EmployeeId > cursor))
                .OrderBy(l => l.EmployeeId)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            if (page.Count == 0)
                break;

            var ids = page.Select(l => l.Id).ToList();
            var components = (await repository.SalaryLineComponents
                    .AsNoTracking()
                    .Where(c => ids.Contains(c.SalaryLineId))
                    .ToListAsync(cancellationToken))
                .ToLookup(c => c.SalaryLineId);

            foreach (var line in page)
            {
                var text = new StringBuilder()
                    .Append(line.EmployeeId.ToString("N")).Append('|')
                    .Append(Money(line.GrossPay)).Append('|')
                    .Append(Money(line.TotalDeductions)).Append('|')
                    .Append(Money(line.TotalAdjustments)).Append('|')
                    .Append(Money(line.NetPay));
                foreach (var component in components[line.Id]
                             .OrderBy(c => (int)c.ComponentType)
                             .ThenBy(c => c.ComponentName, StringComparer.Ordinal)
                             .ThenBy(c => c.Amount))
                {
                    text.Append(';').Append((int)component.ComponentType).Append(':')
                        .Append(component.ComponentName).Append(':').Append(Money(component.Amount));
                }

                text.Append('\n');
                hash.AppendData(Encoding.UTF8.GetBytes(text.ToString()));
            }

            count += page.Count;
            after = page[^1].EmployeeId;
        }

        return new LinesSnapshot(count, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// PLAN-56 (P-7), narrowed by PLAN-58 (P-5): true when the run has a funding decision or a partial batch that this plan's path did not
    /// make — a decision with no batch of the issuer's, or a batch older than PLAN-58 that is partial or funded (the removed partial
    /// disbursement). Such a run is neither cancelled nor reversed. A funding decision on the posting's own batch (source
    /// <c>PayrollRunPosted</c>) no longer refuses: while that batch has not left, a reversal voids it, cancels its orders and closes the
    /// run's open arrears rows (nothing of them was paid); a batch that left refuses the reversal as before (D-PAY-16).
    /// </summary>
    public static async Task<bool> HasFundingOrPartialDisbursementAsync(
        IPayrollRepository repository,
        PayrollRun run,
        CancellationToken cancellationToken)
    {
        if (await repository.PaymentBatches.AnyAsync(
                b => b.PayrollRunId == run.Id && b.Source != Domain.Enums.PaymentBatchSource.PayrollRunPosted
                     && (b.FundingDecisionId != null || b.IsPartial), cancellationToken))
            return true;

        var issuerFunded = await repository.PaymentBatches.AsNoTracking()
            .Where(b => b.PayrollRunId == run.Id && b.Source == Domain.Enums.PaymentBatchSource.PayrollRunPosted && b.FundingDecisionId != null)
            .Select(b => b.FundingDecisionId!.Value)
            .ToListAsync(cancellationToken);
        return await repository.FundingDecisions.AnyAsync(f => f.PayrollRunId == run.Id && !issuerFunded.Contains(f.Id), cancellationToken);
    }

    /// <summary>
    /// PLAN-58 (P-5): the reason an arrears row of a reversed run carries when it is closed (its batch never left; nothing was paid).
    /// </summary>
    public static string ArrearsVoidedByReversal(string? reversalReason) =>
        "أُلغي التشغيل بالعكس قبل خروج دفعته؛ لم يُصرف من هذه المتأخرات شيء" + (string.IsNullOrWhiteSpace(reversalReason) ? "." : $": {reversalReason.Trim()}");

    /// <summary>
    /// Deletes a run's lines and components for a recalculation, in the caller's transaction, and writes one audit row on the run
    /// saying what was deleted and why (the lines themselves are kept as a fingerprint where a revision is recorded).
    /// </summary>
    public static async Task<(int Lines, int Components)> DeleteLinesAsync(
        IPayrollRepository repository,
        PayrollRun run,
        string reason,
        Guid? actorId,
        LinesSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        var (lines, components) = await repository.DeleteRunLinesAsync(run.Id, cancellationToken);
        if (lines > 0 || components > 0)
        {
            await repository.AddAsync(
                new EntityAuditLog
                {
                    TenantId = run.TenantId,
                    EntityType = nameof(SalaryLine),
                    EntityId = run.Id,
                    Action = LinesDeletedAuditAction,
                    UserId = actorId,
                    ChangedFields = JsonSerializer.Serialize(new
                    {
                        reason,
                        payrollRunId = run.Id,
                        lines,
                        components,
                        linesFingerprint = snapshot?.Fingerprint,
                    }),
                },
                cancellationToken);
        }

        return (lines, components);
    }
}
