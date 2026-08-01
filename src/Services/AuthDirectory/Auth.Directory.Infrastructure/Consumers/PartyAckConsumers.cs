using Auth.Directory.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Auth.Directory.Infrastructure.Consumers;

/// <summary>FUN-002: disable on execute; re-enable on compensate if disabled during saga.</summary>
public class SeparationPartyAckConsumer(AuthDirectoryDbContext db) : SeparationPartyAckConsumerBase(ProcessParties.AuthDirectory)
{
    protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context)
    {
        var users = await db.AuthUsers
            .Where(u => u.EmployeeId == context.Message.EmployeeId && u.IsActive)
            .ToListAsync(context.CancellationToken);
        foreach (var u in users)
            u.IsActive = false;
        if (users.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);
        return users.Count > 0 ? "auth:disabled" : "auth:ack-noop";
    }

    protected override async Task<string?> CompensateAsync(ConsumeContext<IExecuteSeparationParty> context)
    {
        var users = await db.AuthUsers
            .Where(u => u.EmployeeId == context.Message.EmployeeId && !u.IsActive)
            .ToListAsync(context.CancellationToken);
        foreach (var u in users)
            u.IsActive = true;
        if (users.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);
        return users.Count > 0 ? "auth:reenabled" : "auth:compensate-noop";
    }
}

public class TransferPartyAckConsumer() : TransferPartyAckConsumerBase(ProcessParties.AuthDirectory);

/// <summary>FUN-001-B: hire saga party — require AuthUser linked to EmployeeId.</summary>
public class HirePartyAckConsumer(AuthDirectoryDbContext db) : HirePartyAckConsumerBase(ProcessParties.AuthDirectory)
{
    protected override async Task ExecuteAsync(ConsumeContext<IExecuteHireParty> context)
    {
        var employeeId = context.Message.EmployeeId;
        var exists = await db.AuthUsers.AsNoTracking()
            .AnyAsync(u => u.EmployeeId == employeeId && u.IsActive, context.CancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException(
                $"لا يوجد حساب دليل نشط مرتبط بالموظف {employeeId} — انتظر IEmployeeCreated أو أنشئ الحساب.");
        }
    }

    protected override async Task CompensateAsync(ConsumeContext<IExecuteHireParty> context)
    {
        var users = await db.AuthUsers
            .Where(u => u.EmployeeId == context.Message.EmployeeId && u.IsActive)
            .ToListAsync(context.CancellationToken);
        foreach (var u in users)
            u.IsActive = false;
        if (users.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);
    }
}

public class SecondmentPartyAckConsumer() : SecondmentPartyAckConsumerBase(ProcessParties.AuthDirectory);

