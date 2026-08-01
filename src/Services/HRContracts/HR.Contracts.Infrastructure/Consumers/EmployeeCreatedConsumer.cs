using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Infrastructure.Consumers;

public class EmployeeCreatedConsumer : IConsumer<IEmployeeCreated>
{
    private readonly ContractsDbContext _db;
    private readonly ILogger<EmployeeCreatedConsumer> _logger;

    public EmployeeCreatedConsumer(ContractsDbContext db, ILogger<EmployeeCreatedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IEmployeeCreated> context)
    {
        var m = context.Message;

        await UpsertEmployeeOrgSnapshotAsync(m.EmployeeId, m.TenantId, m.PrimaryOrgUnitId, context.CancellationToken);

        // العقود عند التعيين تُنشأ عبر ICandidateAccepted فقط لتجنب الازدواجية.
        var hasAppointmentFlow = await _db.ServiceTransactions.AnyAsync(t =>
            t.EmployeeId == m.EmployeeId && t.TransactionType == HR.Contracts.Domain.Enums.ServiceTransactionType.Appointment);

        if (hasAppointmentFlow)
        {
            _logger.LogInformation(
                "EmployeeCreated: skipping contract provisioning for {EmployeeId} (hire transaction exists)",
                m.EmployeeId);
            await TryMarkPersonalFileStepAsync(m.EmployeeId);
            return;
        }

        var hasContract = await _db.Contracts.AnyAsync(c => c.EmployeeId == m.EmployeeId);
        if (hasContract)
            return;

        _logger.LogInformation(
            "EmployeeCreated: no ICandidateAccepted hire flow for {EmployeeId}; contract not auto-created (use recruitment hire)",
            m.EmployeeId);

        await TryMarkPersonalFileStepAsync(m.EmployeeId);
    }

    private async Task UpsertEmployeeOrgSnapshotAsync(
        Guid employeeId,
        Guid? tenantId,
        Guid? primaryOrgUnitId,
        CancellationToken ct)
    {
        var snap = await _db.EmployeeOrgUnitSnapshots.FindAsync([employeeId], ct);
        if (snap is null)
        {
            _db.EmployeeOrgUnitSnapshots.Add(new EmployeeOrgUnitSnapshot
            {
                EmployeeId = employeeId,
                TenantId = tenantId ?? Guid.Empty,
                PrimaryOrgUnitId = primaryOrgUnitId
            });
        }
        else
        {
            snap.TenantId = tenantId ?? Guid.Empty;
            snap.PrimaryOrgUnitId = primaryOrgUnitId;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task TryMarkPersonalFileStepAsync(Guid employeeId)
    {
        var txn = await _db.ServiceTransactions
            .Where(t => t.EmployeeId == employeeId
                        && t.TransactionType == ServiceTransactionType.Appointment
                        && t.State != ServiceTransactionState.Archived
                        && t.State != ServiceTransactionState.Cancelled)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (txn is null)
            return;

        var item = await _db.HireChecklists
            .FirstOrDefaultAsync(c => c.ServiceTransactionId == txn.Id && c.Step == HireStep.PersonalFileOpened);
        if (item is null || item.IsCompleted)
            return;

        HireWorkflowRules.MarkStepCompleted(item, "ملف الموظف في النواة");
        await _db.SaveChangesAsync();
    }
}
