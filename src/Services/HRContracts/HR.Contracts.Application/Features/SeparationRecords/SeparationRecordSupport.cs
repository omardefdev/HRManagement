using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.SeparationRecords;

internal static class SeparationRecordSupport
{
    public static async Task SeedChecklistAsync(IContractsRepository repository, Guid separationRecordId, CancellationToken ct)
    {
        var exists = await repository.SeparationChecklists.AnyAsync(c => c.SeparationRecordId == separationRecordId, ct);
        if (exists)
            return;

        foreach (var item in SeparationChecklistDefaults.CreateForRecord(separationRecordId))
            await repository.AddAsync(item, ct);
    }

    /// <summary>
    /// PLAN-60 (§5): the execution ends the linked contract at the separation's effective date. Returns an error when that date is before
    /// the contract's start; nothing to do when there is no contract, or it already ended.
    /// </summary>
    public static async Task<(Contract? Contract, string? Error)> TerminateLinkedContractAsync(
        IContractsRepository repository, SeparationRecord record, CancellationToken ct)
    {
        if (!record.ContractId.HasValue)
            return (null, null);

        var contract = await repository.GetContractAsync(record.ContractId.Value, ct);
        if (contract is null || !ContractWorkflowRules.CanTerminate(contract))
            return (contract, null);
        if (record.EffectiveDate < contract.StartDate)
            return (contract, $"تاريخ النفاذ ({record.EffectiveDate:yyyy-MM-dd}) يسبق بداية العقد ({contract.StartDate:yyyy-MM-dd}).");

        ContractWorkflowRules.Terminate(contract, record.EffectiveDate);
        return (contract, null);
    }

    public static async Task<int> ComputeAnnualLeaveRemainingAsync(
        IContractsRepository repository,
        Guid employeeId,
        DateOnly effectiveDate,
        CancellationToken ct)
    {
        var year = effectiveDate.Year;
        var balances = await repository.LeaveBalances
            .AsNoTracking()
            .Where(b => b.EmployeeId == employeeId
                        && b.LeaveType == LeaveType.Annual
                        && b.Year == year)
            .ToListAsync(ct);
        return balances.Sum(b => b.Remaining);
    }
}
