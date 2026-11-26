using System.Security.Cryptography;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace HR.Core.Infrastructure.Services.Attendance;

/// <summary>A save met a day that a close locked after the writer had read it as free. Nothing was written.</summary>
public sealed class AttendanceDayLockedException() : InvalidOperationException(AttendanceMessages.LockedDay);

/// <summary>
/// PLAN-57 (review): what makes «a closed day is written by a correction only» hold between two requests. A writer reads the lock,
/// then saves; a close reads the month's days, then saves its summaries. Without a common point one could pass the other: a day
/// changed after the close counted it, with no correction and no new version.
/// <para>
/// The common point is a PostgreSQL advisory lock on (agency, month), held to the end of the transaction: a close takes it
/// exclusively before it reads the days; every save that writes an attendance day takes it shared, then reads the lock again
/// before it writes (<see cref="SaveAsync"/>, called by <see cref="HRCoreDbContext"/> for every save). So a save that began before
/// the close finishes first and the close counts it, and a save that began after waits for the close and is refused
/// (<see cref="AttendanceDayLockedException"/>). The days an approved correction writes are exempt: they are the one write of a
/// locked day, and they move the summary's version in the same save.
/// </para>
/// Other providers (the SQLite of the unit tests) have one writer at a time and take no lock.
/// </summary>
public static class AttendanceMonthLock
{
    /// <summary>The lock's key: the first eight bytes of SHA-256 over the agency and the month.</summary>
    public static long Key(Guid tenantId, int year, int month)
    {
        Span<byte> input = stackalloc byte[20];
        tenantId.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], (year * 100) + month);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return BitConverter.ToInt64(hash);
    }

    /// <summary>The months between two days, as their keys in ascending order (one order for everyone: no two holders wait on each other).</summary>
    public static List<long> Keys(Guid tenantId, DateOnly from, DateOnly to)
    {
        var keys = new SortedSet<long>();
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
            keys.Add(Key(tenantId, month.Year, month.Month));
        return [.. keys];
    }

    /// <summary>A transaction of the caller's own when the provider is PostgreSQL and none is open; otherwise null (the ambient one is joined).</summary>
    public static async Task<IDbContextTransaction?> BeginAsync(DbContext db, CancellationToken ct) =>
        db.Database.IsNpgsql() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

    /// <summary>Takes the months' locks in the open transaction; they are released when it ends.</summary>
    public static async Task TakeAsync(DbContext db, IEnumerable<long> keys, bool exclusive, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return;
        foreach (var key in keys)
        {
            if (exclusive)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
        }
    }

    /// <summary>The attendance days a save is about to write.</summary>
    internal static List<EntityEntry<DailyAttendanceRecord>> Pending(HRCoreDbContext db) =>
        db.ChangeTracker.Entries<DailyAttendanceRecord>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

    /// <summary>
    /// A save that writes attendance days: in one transaction, the shared lock of their months, the lock read again for every day
    /// that is not a correction's, then the save itself.
    /// </summary>
    internal static async Task<int> SaveAsync(
        HRCoreDbContext db, List<EntityEntry<DailyAttendanceRecord>> entries, Func<Task<int>> save, CancellationToken ct)
    {
        await using var own = await BeginAsync(db, ct);
        var days = entries
            .Select(e => (Entry: e, Employee: e.Property(r => r.EmployeeId).CurrentValue,
                Day: e.State == EntityState.Added ? e.Property(r => r.AttendanceDate).CurrentValue : e.Property(r => r.AttendanceDate).OriginalValue))
            .ToList();
        var tenant = db.EffectiveTenantId;
        var keys = new SortedSet<long>(days.Select(d => Key(tenant, d.Day.Year, d.Day.Month)));
        await TakeAsync(db, keys, exclusive: false, ct);

        var guarded = days.Where(d => !db.AttendanceCorrectionWrites.Contains(d.Entry.Entity)).ToList();
        if (guarded.Count > 0)
        {
            var employees = guarded.Select(d => d.Employee).Distinct().ToList();
            var first = guarded.Min(d => d.Day);
            var last = guarded.Max(d => d.Day);
            var closed = await db.AttendanceEmployeeSummaries.AsNoTracking()
                .Where(s => employees.Contains(s.EmployeeId) && s.Period!.PeriodStart <= last && s.Period.PeriodEnd >= first)
                .Select(s => new { s.EmployeeId, s.Period!.PeriodStart, s.Period.PeriodEnd })
                .ToListAsync(ct);
            if (closed.Count > 0)
            {
                var byEmployee = closed.ToLookup(c => c.EmployeeId);
                if (guarded.Any(d => byEmployee[d.Employee].Any(c => c.PeriodStart <= d.Day && d.Day <= c.PeriodEnd)))
                    throw new AttendanceDayLockedException();
            }
        }

        var written = await save();
        if (own is not null)
            await own.CommitAsync(ct);
        db.AttendanceCorrectionWrites.Clear();
        return written;
    }
}
