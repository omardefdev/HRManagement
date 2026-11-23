using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// PLAN-53 (P-10; PLAN-54's step 0.7, moved here): the acceptance seed's current employees get a service regime, so a payroll run
/// passes the service-regime gate. <see cref="AcceptanceDataSeeder"/> creates the employment records with no <c>LegalRegimeId</c>, and
/// HR.Core's <c>ServiceRegimePayrollBlockService</c> blocks an employee whose current record has none, or who has an open regime
/// exception; Payroll then refuses every Regular and FinalSettlement run of the agency.
/// <para>
/// For each acceptance agency, under its own tenant: every current employment record (no end date) without a regime is classified
/// under Law 50/2004 (<c>law50</c>), and the open <c>UnclassifiedRegime</c> exception of such a record is resolved. It writes exactly
/// what <see cref="ServiceRegimeExceptionService.ResolveAsync"/> and <see cref="ServiceRegimeTransitionService.ApplyTransitionAsync"/>
/// write (the append-only transition row, the record's regime, the exception's resolution), once per record and with one save per
/// agency instead of two saves per employee; that path publishes no integration event, and neither does this. It deletes nothing,
/// touches no other exception type and no record that already has a regime, and is idempotent: a second run finds nothing to do.
/// </para>
/// Acceptance profile only (<see cref="AcceptanceSeedOrchestrator"/>): never the demo.
/// </summary>
public static class AcceptancePayrollRegimePatch
{
    public const string RegimeId = "law50";

    /// <summary>The decision the transition row names: the seed itself.</summary>
    public const string DecisionRef = "ACCEPTANCE-SEED-LAW50";

    public const string ResolutionAr = "تصنيف بذرة القبول: القانون 50/2004";

    /// <summary>What one application did, over every agency.</summary>
    public sealed record Result(int RecordsClassified, int ExceptionsResolved);

    public static async Task<Result> ApplyAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        // The transition service's own check, once: the regime exists, is in force and carries its legislation link.
        var legislationProvider = new LegislationRegisterProvider(db);
        var regimeValidator = new ServiceRegimeValidator(db, new RecruitmentLegalSourceValidator(legislationProvider));
        if (await regimeValidator.ValidateAssignableAsync(RegimeId, ct) is { } regimeError)
            throw new InvalidOperationException($"The acceptance seed cannot classify its employees under '{RegimeId}': {regimeError}");

        var (classified, resolved) = (0, 0);
        foreach (var agency in AcceptanceSeedCatalog.Agencies)
        {
            var previousTenantId = TenantIdFlow.Get();
            TenantIdFlow.Set(agency.Id);
            try
            {
                var records = await db.EmploymentRecords
                    .Where(r => r.TenantId == agency.Id && r.EndDate == null && r.LegalRegimeId == null)
                    .ToListAsync(ct);

                // What this patch leaves alone and what would still block a run of the agency: open exceptions of another type.
                var otherOpen = await db.ServiceRegimeExceptions.AsNoTracking()
                    .Where(e => e.TenantId == agency.Id
                                && e.Status == ServiceRegimeExceptionStatus.Open
                                && e.ExceptionType != ServiceRegimeExceptionType.UnclassifiedRegime)
                    .GroupBy(e => e.ExceptionType)
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .ToListAsync(ct);
                if (otherOpen.Count > 0)
                    logger?.LogWarning(
                        "Acceptance agency {AgencyCode}: open service-regime exceptions of other types are left as they are and block payroll for their employees: {Types}",
                        agency.Code, string.Join(", ", otherOpen.Select(o => $"{o.Type}={o.Count}")));

                if (records.Count == 0)
                    continue;

                var recordIds = records.Select(r => r.Id).ToHashSet();
                var employeeIds = records.Select(r => r.EmployeeId).ToHashSet();
                var open = (await db.ServiceRegimeExceptions
                        .Where(e => e.TenantId == agency.Id
                                    && e.Status == ServiceRegimeExceptionStatus.Open
                                    && e.ExceptionType == ServiceRegimeExceptionType.UnclassifiedRegime)
                        .ToListAsync(ct))
                    .Where(e => e.EmploymentRecordId is Guid recordId ? recordIds.Contains(recordId) : employeeIds.Contains(e.EmployeeId))
                    .ToList();

                var now = DateTime.UtcNow;
                foreach (var record in records)
                {
                    // As ServiceRegimeTransitionService.ApplyTransitionAsync: the append-only history row, then the record's regime.
                    db.EmploymentRegimeTransitions.Add(new EmploymentRegimeTransition
                    {
                        TenantId = agency.Id,
                        EmployeeId = record.EmployeeId,
                        EmploymentRecordId = record.Id,
                        FromRegimeId = null,
                        ToRegimeId = RegimeId,
                        EffectiveFrom = record.StartDate,
                        TransitionDecisionRef = DecisionRef,
                        ConditionCode = null,
                        ConflictNotes = null,
                        AppliedBy = null,
                        AppliedAt = now,
                    });
                    record.LegalRegimeId = RegimeId;
                }

                foreach (var entry in open)
                {
                    // As ServiceRegimeExceptionService.ResolveAsync, after its one transition (applied above, never twice).
                    entry.Status = ServiceRegimeExceptionStatus.Resolved;
                    entry.ResolvedRegimeId = RegimeId;
                    entry.Resolution = ResolutionAr;
                    entry.ResolvedBy = null;
                    entry.ResolvedAt = now;
                }

                await db.SaveChangesAsync(ct);
                classified += records.Count;
                resolved += open.Count;
                logger?.LogInformation(
                    "Acceptance agency {AgencyCode}: {Records} current employment record(s) classified under {Regime}, {Exceptions} open regime exception(s) resolved",
                    agency.Code, records.Count, RegimeId, open.Count);
            }
            finally
            {
                TenantIdFlow.Set(previousTenantId);
            }
        }

        return new Result(classified, resolved);
    }
}
