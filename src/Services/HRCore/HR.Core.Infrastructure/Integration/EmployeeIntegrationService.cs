using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;
using MassTransit;

namespace HR.Core.Infrastructure.Integration;

public class EmployeeIntegrationService(HRCoreDbContext db, IPublishEndpoint publish)
{
    public async Task<Employee> CreateEmployeeWithEventAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await db.SaveChangesAsync(cancellationToken);

        db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = employee.Id,
            NewStatus = employee.CurrentStatus.ToString(),
            ChangedAt = DateTime.UtcNow,
            Reason = "تعيين أولي للموظف"
        });

        if (!await db.EmploymentRecords.AnyAsync(e => e.EmployeeId == employee.Id, cancellationToken))
        {
            db.EmploymentRecords.Add(new EmploymentRecord
            {
                EmployeeId = employee.Id,
                PositionTitle = "تعيين أولي",
                StartDate = employee.HireDate?.AsUtc()
                    ?? DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc),
                EmploymentType = EmploymentType.Permanent,
                Status = EmploymentStatus.Active.ToString(),
                Notes = "سجل وظيفي أولي — بانتظار اعتماد السيرة"
            });
        }

        await publish.Publish<IEmployeeCreated>(new
        {
            EmployeeId = employee.Id,
            employee.FullName,
            // Hash, never the raw number: HR Core is the only service entitled to hold it.
            NationalIdHash = PiiMask.Sha256Hex(employee.NationalId),
            EmploymentType = EmploymentType.Permanent.ToString(),
            TenantId = employee.TenantId,
            PrimaryOrgUnitId = employee.PrimaryOrgUnitId
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return employee;
    }

    public async Task PublishStatusChangedIfNeededAsync(
        Employee employee,
        EmploymentStatus oldStatus,
        EmploymentStatus newStatus,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (oldStatus == newStatus)
            return;

        await publish.Publish<IEmployeeStatusChanged>(new
        {
            EmployeeId = employee.Id,
            OldStatus = oldStatus.ToString(),
            NewStatus = newStatus.ToString(),
            Reason = reason,
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }
}
