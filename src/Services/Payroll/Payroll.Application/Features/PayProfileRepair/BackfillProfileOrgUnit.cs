using System.Text.Json;
using HRManagement.SharedKernel.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.InboundEvents;
using Payroll.Application.Services;

namespace Payroll.Application.Features.PayProfileRepair;

/// <summary>
/// PLAN-55 (§3, P-8): the repair of old pay profiles. Builds before PLAN-55 made a contract activation's version without the unit and
/// the regime. For each <b>active</b> profile of the caller's agency whose unit or regime is empty, the value is copied from the
/// employee's latest profile that carries it. A dry run (the default) lists and changes nothing.
/// </summary>
public sealed record BackfillProfileOrgUnitCommand(bool DryRun, Guid? ActorId, string? ActorName) : IRequest<ProfileOrgUnitBackfillReport>;

/// <summary>
/// One active profile the repair fills: the value it takes for each empty field (null when that field is not filled), and the profile
/// each value comes from.
/// </summary>
public sealed record ProfileOrgUnitBackfillItem(
    Guid ProfileId,
    Guid EmployeeId,
    Guid? OrgUnitId,
    Guid? OrgUnitFromProfileId,
    string? RegimeCode,
    Guid? RegimeFromProfileId);

/// <summary>
/// What a run found. <see cref="Items"/> is what a real run changes — exactly what the dry run before it listed.
/// <see cref="NotFillableProfileIds"/> are active profiles with an empty unit or regime that no profile of the same employee carries:
/// the repair copies from the employee's own history only, and never invents a value.
/// </summary>
public sealed record ProfileOrgUnitBackfillReport(
    bool DryRun,
    DateTime RanAt,
    int ActiveProfilesWithEmptyUnit,
    int ActiveProfilesWithEmptyRegime,
    IReadOnlyList<ProfileOrgUnitBackfillItem> Items,
    IReadOnlyList<Guid> NotFillableProfileIds)
{
    /// <summary>The profiles a real run fills (or filled).</summary>
    public int Found => Items.Count;

    /// <summary>The profiles this run changed: 0 for a dry run.</summary>
    public int Changed { get; init; }

    /// <summary>The audit entry of a real run that changed something.</summary>
    public Guid? AuditEntryId { get; init; }
}

/// <summary>
/// The repair. It reads; the write itself is <see cref="HandleEmployeeCreatedHandler"/>'s — the one rule that fills the unit and the
/// regime of an employee's current profile <b>where they are empty and never overwrites</b> — so the repair adds no writer of pay
/// profiles (<c>PayProfileWritersTests</c>) and a second run finds nothing. One agency per call: the caller's (the context's tenant
/// filter and row security). A real run writes one audit entry that names the operator and the profiles.
/// </summary>
public sealed class BackfillProfileOrgUnitHandler(IPayrollRepository repository, ISender sender, IPayrollCalendar calendar)
    : IRequestHandler<BackfillProfileOrgUnitCommand, ProfileOrgUnitBackfillReport>
{
    public const string AuditAction = "PayProfileOrgUnitBackfill";

    /// <summary>The most employee ids one history query names.</summary>
    private const int Chunk = 1000;

    public async Task<ProfileOrgUnitBackfillReport> Handle(BackfillProfileOrgUnitCommand request, CancellationToken cancellationToken)
    {
        var now = calendar.UtcNow;
        var listed = await ListAsync(now, cancellationToken);
        if (request.DryRun || listed.Items.Count == 0)
            return listed with { DryRun = request.DryRun };

        foreach (var item in listed.Items)
        {
            // The fill of an employee's current profile, where empty: the named writer's rule, in its own save.
            await sender.Send(new HandleEmployeeCreatedCommand(item.EmployeeId, item.OrgUnitId, item.RegimeCode), cancellationToken);
        }

        // What this run changed, read back: the listed profiles that now hold the listed values.
        repository.ClearChangeTracker();
        var ids = listed.Items.Select(i => i.ProfileId).ToList();
        var after = await repository.EmployeePayProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.OrgUnitId, p.RegimeCode })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var changed = listed.Items.Where(i => after.TryGetValue(i.ProfileId, out var p)
                                              && (i.OrgUnitId is null || p.OrgUnitId == i.OrgUnitId)
                                              && (i.RegimeCode is null || p.RegimeCode == i.RegimeCode)).ToList();

        var audit = new EntityAuditLog
        {
            EntityType = nameof(Domain.Entities.EmployeePayProfile),
            EntityId = Guid.NewGuid(),            // the run's own id: the entry is of the run, not of one profile
            Action = AuditAction,
            UserId = request.ActorId,
            ChangedFields = JsonSerializer.Serialize(new
            {
                operatorName = request.ActorName,
                listed = listed.Items.Count,
                changed = changed.Count,
                profiles = changed.Select(i => new { i.ProfileId, filledUnit = i.OrgUnitId is not null, filledRegime = i.RegimeCode is not null }),
            }),
        };
        await repository.AddAsync(audit, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return listed with { DryRun = false, Changed = changed.Count, AuditEntryId = audit.Id };
    }

    private async Task<ProfileOrgUnitBackfillReport> ListAsync(DateTime now, CancellationToken ct)
    {
        var incomplete = (await repository.EmployeePayProfiles.AsNoTracking()
                .Where(p => p.IsActive && (p.OrgUnitId == null || p.RegimeCode == null || p.RegimeCode.Trim() == ""))
                .Select(p => new { p.Id, p.EmployeeId, p.OrgUnitId, p.RegimeCode })
                .ToListAsync(ct))
            .OrderBy(p => p.EmployeeId).ThenBy(p => p.Id)
            .ToList();

        var items = new List<ProfileOrgUnitBackfillItem>();
        var notFillable = new List<Guid>();
        foreach (var chunk in incomplete.Chunk(Chunk))
        {
            var employeeIds = chunk.Select(p => p.EmployeeId).Distinct().ToList();
            var history = (await repository.EmployeePayProfiles.AsNoTracking()
                    .Where(p => employeeIds.Contains(p.EmployeeId) && !p.IsActive)
                    .Select(p => new { p.Id, p.EmployeeId, p.OrgUnitId, p.RegimeCode, p.EffectiveFrom, p.CreatedAt })
                    .ToListAsync(ct))
                .ToLookup(p => p.EmployeeId);

            foreach (var profile in chunk)
            {
                // The employee's latest profile that carries the value: by its start, then by when it was written.
                var latest = history[profile.EmployeeId].OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.CreatedAt).ThenBy(p => p.Id).ToList();
                var unitSource = profile.OrgUnitId is null ? latest.FirstOrDefault(p => p.OrgUnitId is not null) : null;
                var regimeSource = string.IsNullOrWhiteSpace(profile.RegimeCode)
                    ? latest.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.RegimeCode))
                    : null;
                if (unitSource is null && regimeSource is null)
                {
                    notFillable.Add(profile.Id);
                    continue;
                }

                items.Add(new ProfileOrgUnitBackfillItem(
                    profile.Id, profile.EmployeeId, unitSource?.OrgUnitId, unitSource?.Id, regimeSource?.RegimeCode!.Trim(), regimeSource?.Id));
            }
        }

        return new ProfileOrgUnitBackfillReport(
            true,
            now,
            incomplete.Count(p => p.OrgUnitId is null),
            incomplete.Count(p => string.IsNullOrWhiteSpace(p.RegimeCode)),
            items,
            notFillable);
    }
}
