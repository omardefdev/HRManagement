using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Integration;

public class RecruitmentIntegrationService(HRCoreDbContext db, IPublishEndpoint publish)
{
    [Obsolete("Use HireAndCreateEmployeeAsync or HireExistingEmployeeAsync via the Hire form.")]
    public Task HireAndPublishAsync(
        RecruitmentApplication app,
        Guid hiredEmployeeId,
        DateOnly expectedStartDate,
        string employmentCategory,
        CancellationToken cancellationToken = default) =>
        HireExistingEmployeeAsync(app, hiredEmployeeId, expectedStartDate, employmentCategory, cancellationToken);

    public async Task<Employee> HireAndCreateEmployeeAsync(
        RecruitmentApplication app,
        string nationalId,
        string firstName,
        string? middleName,
        string lastName,
        DateTime birthDate,
        Gender gender,
        Guid? primaryOrgUnitId,
        DateOnly expectedStartDate,
        string employmentCategory,
        CancellationToken cancellationToken = default)
    {
        var fullName = $"{firstName} {middleName} {lastName}".Replace("  ", " ").Trim();
        var employee = new Employee
        {
            NationalId = nationalId,
            FirstName = firstName,
            MiddleName = middleName,
            LastName = lastName,
            FullName = fullName,
            BirthDate = birthDate.AsUtc(),
            Gender = gender,
            PrimaryOrgUnitId = primaryOrgUnitId,
            HireDate = expectedStartDate.ToDateTime(TimeOnly.MinValue).AsUtc(),
            CurrentStatus = EmploymentStatus.Active
        };

        db.Employees.Add(employee);
        await db.SaveChangesAsync(cancellationToken);

        await SeedEmployeeSideEffectsAsync(employee, app.AppliedPosition, cancellationToken);

        app.Status = RecruitmentApplicationStatus.Hired;
        app.HiredEmployeeId = employee.Id;
        app.ReviewedAt = DateTime.UtcNow;

        await PublishHireEventsAsync(employee, app, expectedStartDate, employmentCategory, cancellationToken);
        return employee;
    }

    public async Task<Employee> HireExistingEmployeeAsync(
        RecruitmentApplication app,
        Guid hiredEmployeeId,
        DateOnly expectedStartDate,
        string employmentCategory,
        CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == hiredEmployeeId, cancellationToken)
            ?? throw new InvalidOperationException("الموظف المحدد غير موجود في النواة.");

        app.Status = RecruitmentApplicationStatus.Hired;
        app.HiredEmployeeId = hiredEmployeeId;
        app.ReviewedAt = DateTime.UtcNow;

        await PublishHireEventsAsync(employee, app, expectedStartDate, employmentCategory, cancellationToken);
        return employee;
    }

    private async Task SeedEmployeeSideEffectsAsync(
        Employee employee,
        string positionTitle,
        CancellationToken cancellationToken)
    {
        db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = employee.Id,
            NewStatus = employee.CurrentStatus.ToString(),
            ChangedAt = DateTime.UtcNow,
            Reason = "تعيين من طلب توظيف"
        });

        if (!await db.EmploymentRecords.AnyAsync(e => e.EmployeeId == employee.Id, cancellationToken))
        {
            db.EmploymentRecords.Add(new EmploymentRecord
            {
                EmployeeId = employee.Id,
                PositionTitle = string.IsNullOrWhiteSpace(positionTitle) ? "تعيين من التوظيف" : positionTitle,
                StartDate = (employee.HireDate ?? DateTime.UtcNow.Date).AsUtc(),
                EmploymentType = EmploymentType.Permanent,
                Status = EmploymentStatus.Active.ToString(),
                Notes = "سجل وظيفي أولي — بانتظار اعتماد السيرة"
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishHireEventsAsync(
        Employee employee,
        RecruitmentApplication app,
        DateOnly expectedStartDate,
        string employmentCategory,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationIdFlow.Get() ?? Guid.NewGuid().ToString();

        await publish.Publish<ICandidateAccepted>(new
        {
            EmployeeId = employee.Id,
            EmploymentCategory = employmentCategory,
            ExpectedStartDate = expectedStartDate.ToDateTime(TimeOnly.MinValue),
            FullName = app.ApplicantFullName,
            SourceSystem = "HR.Core",
            CorrelationId = correlationId
        }, cancellationToken);

        await publish.Publish<IEmployeeCreated>(new
        {
            EmployeeId = employee.Id,
            employee.FullName,
            NationalIdHash = HRManagement.SharedKernel.PiiMask.Sha256Hex(employee.NationalId),
            EmploymentType = EmploymentType.Permanent.ToString(),
            TenantId = employee.TenantId
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }
}
