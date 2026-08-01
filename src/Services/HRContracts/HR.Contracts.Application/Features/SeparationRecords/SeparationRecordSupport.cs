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

    public static async Task TerminateLinkedContractAsync(IContractsRepository repository, SeparationRecord record, CancellationToken ct)
    {
        if (!record.ContractId.HasValue)
            return;

        var contract = await repository.GetContractAsync(record.ContractId.Value, ct);
        if (contract is not null && ContractWorkflowRules.CanTerminate(contract))
            ContractWorkflowRules.Terminate(contract);
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
