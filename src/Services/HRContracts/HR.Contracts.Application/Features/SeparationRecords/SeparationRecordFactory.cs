using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Services;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Contracts.Application.Features.SeparationRecords;

/// <summary>What a creation asks for, whichever entry it comes from.</summary>
public sealed record SeparationRecordDraft(
    Guid EmployeeId,
    Guid? ContractId,
    SeparationType SeparationType,
    DateOnly EffectiveDate,
    string? Reason,
    string? Documents,
    string TransactionNote);

public sealed record SeparationRecordCreateResult(bool Success, string? Error, SeparationRecord? Record);

/// <summary>What the employee and contract checks found: the contract the record takes (chosen for the employee when he has one).</summary>
public sealed record SeparationSubjectCheck(bool Success, string? Error, Guid? ContractId);

/// <summary>
/// PLAN-60 (§6, G-SEP-11): the one creation path of a separation record — the wizard's <c>Step1</c>, the form's <c>Create</c> and the
/// inbound requests (<c>ProcessSeparationRequestHandler</c>). It checks the employee (he exists and his service has not ended, read with
/// the legal hold in one call to HR.Core, fail-closed), that he has no other open record, and that the contract is his; then it saves
/// the record, its clearance items and its transaction, already linked, in one save.
/// </summary>
public sealed class SeparationRecordFactory(
    IContractsRepository repository,
    ICodeGenerationClient codeClient,
    IEmployeeLifecycleGate lifecycleGate,
    IOptions<SeparationOptions> options)
{
    public const string OpenRecordMessage = "يوجد سجل فصل مفتوح لهذا الموظف";
    public const string EmployeeRequiredMessage = "الموظف مطلوب.";
    public const string ContractNotEmployeesMessage = "العقد المختار ليس عقداً نشطاً أو معلّقاً لهذا الموظف.";
    public const string ContractChoiceRequiredMessage = "للموظف أكثر من عقد نشط أو معلّق — اختر العقد الذي ينتهي بالفصل.";

    /// <summary>
    /// The employee and contract checks of a creation and of a submission (P-7 checks them again). <paramref name="exceptRecordId"/> is the
    /// record being submitted: it does not count as «another» open record.
    /// </summary>
    public async Task<SeparationSubjectCheck> CheckSubjectAsync(
        Guid employeeId, Guid? contractId, Guid? exceptRecordId, CancellationToken ct)
    {
        if (employeeId == Guid.Empty)
            return new(false, EmployeeRequiredMessage, null);

        // PLAN-42's gate, one call: the employee exists, is not under a legal hold, and his service has not ended (D-S2S-4: no answer refuses).
        var (allowed, error) = await lifecycleGate.AssertCanStartSeparationAsync(employeeId, ct);
        if (!allowed)
            return new(false, error, null);

        var open = SeparationWorkflowRules.OpenStatuses.ToList();
        if (await repository.SeparationRecords.AnyAsync(
                r => r.EmployeeId == employeeId && open.Contains(r.Status) && (exceptRecordId == null || r.Id != exceptRecordId), ct))
            return new(false, OpenRecordMessage, null);

        var (contractOk, contractError, chosen) = await ResolveContractAsync(employeeId, contractId, ct);
        return new(contractOk, contractError, chosen);
    }

    /// <summary>
    /// The contract must be the same employee's, <c>Active</c> or <c>Pending</c>. With one such contract and none chosen, it is chosen for
    /// him; with several, the choice is required; with none, the record has no contract.
    /// </summary>
    public async Task<(bool Success, string? Error, Guid? ContractId)> ResolveContractAsync(
        Guid employeeId, Guid? contractId, CancellationToken ct)
    {
        var candidates = await repository.Contracts.AsNoTracking()
            .Where(c => c.EmployeeId == employeeId && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Pending))
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (contractId is Guid chosen)
            return candidates.Contains(chosen) ? (true, null, chosen) : (false, ContractNotEmployeesMessage, null);

        return candidates.Count switch
        {
            0 => (true, null, null),
            1 => (true, null, candidates[0]),
            _ => (false, ContractChoiceRequiredMessage, null)
        };
    }

    public async Task<SeparationRecordCreateResult> CreateAsync(SeparationRecordDraft draft, CancellationToken ct)
    {
        var check = await CheckSubjectAsync(draft.EmployeeId, draft.ContractId, exceptRecordId: null, ct);
        if (!check.Success)
            return new(false, check.Error, null);

        var record = new SeparationRecord
        {
            EmployeeId = draft.EmployeeId,
            ContractId = check.ContractId,
            SeparationType = draft.SeparationType,
            EffectiveDate = draft.EffectiveDate,
            Reason = string.IsNullOrWhiteSpace(draft.Reason) ? null : draft.Reason.Trim(),
            Documents = string.IsNullOrWhiteSpace(draft.Documents) ? null : draft.Documents.Trim()
        };
        var items = SeparationChecklistDefaults
            .CreateForRecord(record.Id, options.Value.Checklist.RequireEmployeeNotification)
            .ToList();
        var transaction = new ServiceTransaction
        {
            EmployeeId = draft.EmployeeId,
            TransactionType = ServiceTransactionType.Separation,
            SeparationRecordId = record.Id,
            Notes = draft.TransactionNote
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, ct);

        await repository.AddAsync(record, ct);
        foreach (var item in items)
            await repository.AddAsync(item, ct);
        await repository.AddAsync(transaction, ct);

        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsOpenRecordViolation(ex))
        {
            // Another creation for the same employee won the race: the index is the last word.
            repository.Remove(transaction);
            foreach (var item in items)
                repository.Remove(item);
            repository.Remove(record);
            return new(false, OpenRecordMessage, null);
        }

        return new(true, null, record);
    }

    /// <summary>The partial unique index refused a second open record (PostgreSQL names the index; SQLite names its columns).</summary>
    public static bool IsOpenRecordViolation(Exception ex)
    {
        for (var e = (Exception?)ex; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains("IX_SeparationRecords_Tenant_Employee_Open", StringComparison.Ordinal)
                || e.Message.Contains("SeparationRecords.TenantId, SeparationRecords.EmployeeId", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
