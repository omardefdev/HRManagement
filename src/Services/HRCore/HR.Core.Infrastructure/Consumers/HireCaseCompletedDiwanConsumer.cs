using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Integration;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

public class HireCaseCompletedDiwanConsumer(
    HR.Core.Infrastructure.Data.HRCoreDbContext db,
    DiwanIntegrationService diwan,
    ILogger<HireCaseCompletedDiwanConsumer> logger) : IConsumer<IHireCaseCompleted>
{
    public async Task Consume(ConsumeContext<IHireCaseCompleted> context)
    {
        var m = context.Message;
        var reference = $"DIWAN-HIRE-{m.ServiceTransactionId:N}";

        var pendingEmployee = await db.Employees.FirstOrDefaultAsync(
            x => x.Id == m.EmployeeId && x.CurrentStatus == EmploymentStatus.PendingActivation,
            context.CancellationToken);
        var pendingRecord = pendingEmployee is null
            ? null
            : await db.EmploymentRecords.FirstOrDefaultAsync(
                x => x.EmployeeId == m.EmployeeId
                     && x.Status == EmploymentStatus.PendingActivation.ToString()
                     && x.Notes == "سجل وظيفي أولي — بانتظار استكمال معالج التعيين",
                context.CancellationToken);

        if (pendingEmployee is not null && pendingRecord is not null)
        {
            pendingEmployee.CurrentStatus = EmploymentStatus.Active;
            pendingRecord.Status = EmploymentStatus.Active.ToString();
            db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
            {
                EmployeeId = pendingEmployee.Id,
                EmploymentRecordId = pendingRecord.Id,
                OldStatus = EmploymentStatus.PendingActivation.ToString(),
                NewStatus = EmploymentStatus.Active.ToString(),
                ChangedAt = DateTime.UtcNow,
                Reason = "اكتمال قائمة تحقق التعيين وتسجيله في الديوان"
            });
            await db.SaveChangesAsync(context.CancellationToken);
        }

        if (await db.DiwanCorrespondences.AnyAsync(
                x => x.ServiceTransactionId == m.ServiceTransactionId,
                context.CancellationToken))
            return;

        await diwan.RegisterCorrespondenceAsync(new DiwanCorrespondence
        {
            ReferenceNumber = reference,
            Subject = $"تسجيل ديوان — تعيين موظف {m.EmployeeId}",
            Direction = CorrespondenceDirection.Outbound,
            RegisteredAt = DateTime.UtcNow,
            RelatedEmployeeId = m.EmployeeId,
            ServiceTransactionId = m.ServiceTransactionId,
            Body = "أُنجزت معاملة التعيين وتم تسجيلها في الديوان."
        }, context.CancellationToken);

        logger.LogInformation(
            "HR.Core: Diwan registered for hire ServiceTransactionId={TransactionId}",
            m.ServiceTransactionId);
    }
}
