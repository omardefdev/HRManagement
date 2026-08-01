using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.HireCases;

public static class HireCaseSupport
{
    public static async Task<ServiceTransaction?> GetOpenAppointmentTransactionAsync(
        IContractsRepository repository,
        Guid employeeId,
        CancellationToken ct) =>
        await repository.ServiceTransactions
            .Where(x => x.EmployeeId == employeeId
                        && x.TransactionType == ServiceTransactionType.Appointment
                        && x.State != ServiceTransactionState.Archived
                        && x.State != ServiceTransactionState.Cancelled)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public static async Task SeedChecklistAsync(IContractsRepository repository, Guid serviceTransactionId, CancellationToken ct)
    {
        var exists = await repository.HireChecklists.AnyAsync(c => c.ServiceTransactionId == serviceTransactionId, ct);
        if (exists)
            return;

        foreach (var item in HireChecklistDefaults.CreateForTransaction(serviceTransactionId))
            await repository.AddAsync(item, ct);

        await repository.SaveChangesAsync(ct);
    }

    public static async Task MarkCandidateReceivedAsync(IContractsRepository repository, Guid serviceTransactionId, CancellationToken ct) =>
        await MarkStepAsync(repository, serviceTransactionId, HireStep.CandidateReceived, "استلام من حدث التوظيف", ct);

    public static async Task MarkContractClassifiedAsync(IContractsRepository repository, Guid employeeId, string? notes, CancellationToken ct)
    {
        var txn = await GetOpenAppointmentTransactionAsync(repository, employeeId, ct);
        if (txn is null)
            return;

        await SeedChecklistAsync(repository, txn.Id, ct);
        await MarkStepAsync(repository, txn.Id, HireStep.ContractClassified, notes ?? "عقد ومباشرة", ct);
    }

    public static async Task MarkPayrollLinkedAsync(IContractsRepository repository, Guid employeeId, CancellationToken ct)
    {
        var txn = await GetOpenAppointmentTransactionAsync(repository, employeeId, ct);
        if (txn is null)
            return;

        await MarkStepAsync(repository, txn.Id, HireStep.PayrollLinked, "تفعيل العقد وربط الراتب", ct);
    }

    public static async Task MarkPersonalFileOpenedAsync(IContractsRepository repository, Guid employeeId, CancellationToken ct)
    {
        var txn = await GetOpenAppointmentTransactionAsync(repository, employeeId, ct);
        if (txn is null)
            return;

        await MarkStepAsync(repository, txn.Id, HireStep.PersonalFileOpened, "ملف الموظف في النواة", ct);
    }

    public static async Task MarkIdentityIssuedAsync(IContractsRepository repository, Guid employeeId, CancellationToken ct)
    {
        var txn = await GetOpenAppointmentTransactionAsync(repository, employeeId, ct);
        if (txn is null)
            return;

        await MarkStepAsync(repository, txn.Id, HireStep.IdentityIssued, "بطاقة هوية", ct);
    }

    public static async Task MarkHireArchivedAsync(IContractsRepository repository, Guid serviceTransactionId, CancellationToken ct) =>
        await MarkStepAsync(repository, serviceTransactionId, HireStep.HireArchived, "أرشفة معاملة التعيين", ct);

    private static async Task MarkStepAsync(
        IContractsRepository repository,
        Guid serviceTransactionId,
        HireStep step,
        string? notes,
        CancellationToken ct)
    {
        var item = await repository.HireChecklists
            .FirstOrDefaultAsync(c => c.ServiceTransactionId == serviceTransactionId && c.Step == step, ct);
        if (item is null || item.IsCompleted)
            return;

        HireWorkflowRules.MarkStepCompleted(item, notes, evidenceRef: notes);
        await repository.SaveChangesAsync(ct);
    }
}
