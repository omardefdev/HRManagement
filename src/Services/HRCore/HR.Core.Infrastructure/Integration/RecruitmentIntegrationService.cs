using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Infrastructure.Integration;

/// <summary>Thin wrapper delegating hire closure to <see cref="RecruitmentHireClosureService"/>.</summary>
public class RecruitmentIntegrationService(RecruitmentHireClosureService closure)
{
    [Obsolete("Use HireAndCreateEmployeeAsync or HireExistingEmployeeAsync via the Hire form.")]
    public Task HireAndPublishAsync(
        RecruitmentApplication app,
        Guid hiredEmployeeId,
        DateOnly expectedStartDate,
        string employmentCategory,
        CancellationToken cancellationToken = default) =>
        HireExistingEmployeeAsync(app, hiredEmployeeId, expectedStartDate, employmentCategory, cancellationToken: cancellationToken);

    public Task<Employee> HireAndCreateEmployeeAsync(
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
        HireClosureJustification? justification = null,
        CancellationToken cancellationToken = default) =>
        closure.HireAndCreateEmployeeAsync(
            app,
            nationalId,
            firstName,
            middleName,
            lastName,
            birthDate,
            gender,
            primaryOrgUnitId,
            expectedStartDate,
            employmentCategory,
            justification,
            cancellationToken);

    public Task<Employee> HireExistingEmployeeAsync(
        RecruitmentApplication app,
        Guid hiredEmployeeId,
        DateOnly expectedStartDate,
        string employmentCategory,
        HireClosureJustification? justification = null,
        CancellationToken cancellationToken = default) =>
        closure.HireExistingEmployeeAsync(app, hiredEmployeeId, expectedStartDate, employmentCategory, justification, cancellationToken);
}
