using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Consumers;

public enum AttendanceHandoffOutcome
{
    /// <summary>A new version with a difference: a row was written and the receipt updated.</summary>
    Recorded = 0,
    /// <summary>A new version whose absence equals what was recorded: the receipt alone was updated.</summary>
    RecordedNoDifference,
    /// <summary>The version was recorded before: nothing changed.</summary>
    Duplicate,
    /// <summary>A version older than the latest recorded: nothing changed (the latest is absolute, so nothing is lost).</summary>
    IgnoredOlderVersion
}

/// <summary>
/// PLAN-57 (§4, §5): how payroll takes one version of an attendance summary. In one transaction:
/// <list type="number">
/// <item>the receipt of the summary is created if absent and its row locked (<c>SELECT … FOR UPDATE</c> on PostgreSQL), so two
/// versions that arrive at once never compute their difference from the same base;</item>
/// <item>a version not newer than the latest recorded is ignored (a duplicate, or an older version arriving late);</item>
/// <item>the difference is the version's absence less what was recorded for the summary so far. A difference that is not zero
/// becomes one <see cref="AbsenceDeduction"/> keyed <c>ATT:{SummaryId}:v{Version}</c> (unique), dated in the original period, marked
/// <c>CarryForward</c> when a regular run of that period has been calculated (<see cref="PayrollAttendanceFreeze"/>); a zero
/// difference writes no row;</item>
/// <item>the receipt takes the version. No row a run applied is ever changed.</item>
/// </list>
/// The tenant on the receipt and on the row is the message's.
/// </summary>
public sealed class AttendanceSummaryHandoff(PayrollDbContext db)
{
    public const string CloseReason = "Close";

    /// <summary>The row's reason, which a run of its own period shows as the deduction's name.</summary>
    public static string RowReason(IAttendanceSummaryPublished message) =>
        string.Equals(message.Reason, CloseReason, StringComparison.Ordinal)
            ? $"خصم غياب — فترة الحضور {message.PeriodStart:yyyy-MM}"
            : $"فرق غياب بتصحيح الحضور — فترة {message.PeriodStart:yyyy-MM}";

    /// <param name="stageInboxReceipt">Stages the consumer's inbox receipt, so that it commits with the rows (called once per attempt).</param>
    public async Task<AttendanceHandoffOutcome> ApplyAsync(
        IAttendanceSummaryPublished message, Action? stageInboxReceipt = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.SummaryId == Guid.Empty || message.Version < 1 || message.PeriodEnd < message.PeriodStart)
            throw new InvalidOperationException("IAttendanceSummaryPublished carries no summary, no version or reversed bounds.");

        var tenantId = message.TenantId is { } carried && carried != Guid.Empty ? carried : db.EffectiveTenantId;
        var outcome = AttendanceHandoffOutcome.Duplicate;
        var staged = new List<object>();

        await OutboxUnitOfWork.ExecuteAsync(db, async token =>
        {
            // The execution strategy may run this body again after a transient failure: what the earlier attempt staged is dropped.
            foreach (var entity in staged)
                db.Entry(entity).State = EntityState.Detached;
            staged.Clear();

            var now = DateTime.UtcNow;
            var receipt = await LockReceiptAsync(message, tenantId, now, staged, token);
            if (message.Version <= receipt.LatestVersion)
            {
                outcome = message.Version == receipt.LatestVersion ? AttendanceHandoffOutcome.Duplicate : AttendanceHandoffOutcome.IgnoredOlderVersion;
                stageInboxReceipt?.Invoke();
                return;
            }

            var difference = message.AbsentDays - receipt.RecordedAbsentDays;
            if (difference != 0)
            {
                var row = new AbsenceDeduction
                {
                    EmployeeId = message.EmployeeId,
                    PeriodStart = message.PeriodStart,
                    PeriodEnd = message.PeriodEnd,
                    OriginalPeriodStart = message.PeriodStart,
                    OriginalPeriodEnd = message.PeriodEnd,
                    AbsentDays = difference,
                    Amount = 0,
                    Reason = RowReason(message),
                    SourceReference = AbsenceDeduction.AttendanceReference(message.SummaryId, message.Version),
                    SourceVersion = message.Version,
                    CarryForward = await PayrollAttendanceFreeze.HasCalculatedRunAsync(db.PayrollRuns, message.PeriodStart, message.PeriodEnd, token),
                    TenantId = tenantId
                };
                db.AbsenceDeductions.Add(row);
                staged.Add(row);
                receipt.RecordedAbsentDays += difference;
                outcome = AttendanceHandoffOutcome.Recorded;
            }
            else
            {
                outcome = AttendanceHandoffOutcome.RecordedNoDifference;
            }

            receipt.LatestVersion = message.Version;
            receipt.LatestAbsentDays = message.AbsentDays;
            receipt.UpdatedAt = now;
            stageInboxReceipt?.Invoke();
        }, ct);

        return outcome;
    }

    /// <summary>The summary's receipt, created at version 0 if absent, with its row locked for this transaction on PostgreSQL.</summary>
    private async Task<AttendanceSummaryReceipt> LockReceiptAsync(
        IAttendanceSummaryPublished message, Guid tenantId, DateTime now, List<object> staged, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            // The row must exist before it can be locked: without it two first versions would both find nothing and both insert.
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AttendanceSummaryReceipts"
                    ("Id", "TenantId", "CreatedAt", "UpdatedAt", "SummaryId", "AttendancePeriodId", "EmployeeId", "PeriodStart", "PeriodEnd",
                     "LatestVersion", "LatestAbsentDays", "RecordedAbsentDays", "ReceivedAt")
                VALUES ({id}, {tenantId}, {now}, {now}, {message.SummaryId}, {message.AttendancePeriodId}, {message.EmployeeId},
                        {message.PeriodStart}, {message.PeriodEnd}, 0, 0, 0, {now})
                ON CONFLICT ("SummaryId") DO NOTHING
                """, ct);
            await db.Database
                .SqlQuery<int>($"""SELECT 1 AS "Value" FROM "AttendanceSummaryReceipts" WHERE "SummaryId" = {message.SummaryId} FOR UPDATE""")
                .ToListAsync(ct);

            var tracked = db.AttendanceSummaryReceipts.Local.FirstOrDefault(r => r.SummaryId == message.SummaryId);
            if (tracked is not null)
            {
                await db.Entry(tracked).ReloadAsync(ct);
                return tracked;
            }

            return await db.AttendanceSummaryReceipts.SingleAsync(r => r.SummaryId == message.SummaryId, ct);
        }

        // Other providers (the tests' SQLite and in-memory databases) have no row lock: find, or add.
        var receipt = db.AttendanceSummaryReceipts.Local.FirstOrDefault(r => r.SummaryId == message.SummaryId)
                      ?? await db.AttendanceSummaryReceipts.FirstOrDefaultAsync(r => r.SummaryId == message.SummaryId, ct);
        if (receipt is not null)
            return receipt;

        receipt = new AttendanceSummaryReceipt
        {
            SummaryId = message.SummaryId,
            AttendancePeriodId = message.AttendancePeriodId,
            EmployeeId = message.EmployeeId,
            PeriodStart = message.PeriodStart,
            PeriodEnd = message.PeriodEnd,
            LatestVersion = 0,
            ReceivedAt = now,
            TenantId = tenantId
        };
        db.AttendanceSummaryReceipts.Add(receipt);
        staged.Add(receipt);
        return receipt;
    }
}
