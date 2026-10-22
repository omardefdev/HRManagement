using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>HCM-005: atomic hire closure — employee, position occupancy, requisition close, offer conversion.</summary>
public sealed class RecruitmentHireClosureService(
    HRCoreDbContext db,
    EstablishmentService establishmentService,
    IFundedPositionVersionService fundedPositionVersions,
    RequisitionCloseService requisitionCloseService,
    RecruitmentLegalSourceValidator legalSourceValidator,
    Legal.ServiceRegimeValidator serviceRegimeValidator,
    Legal.ServiceRegimeExceptionService serviceRegimeExceptionService,
    ReserveListService reserveList,
    IPublishEndpoint publish,
    IConfiguration configuration,
    ILogger<RecruitmentHireClosureService> logger)
{
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
            CurrentStatus = EmploymentStatus.PendingActivation
        };

        return ExecuteHireClosureAsync(
            app.Id,
            employee,
            linkExistingEmployeeId: null,
            expectedStartDate,
            employmentCategory,
            cancellationToken);
    }

    public Task<Employee> HireExistingEmployeeAsync(
        RecruitmentApplication app,
        Guid hiredEmployeeId,
        DateOnly expectedStartDate,
        string employmentCategory,
        CancellationToken cancellationToken = default) =>
        ExecuteHireClosureAsync(
            app.Id,
            employeePayload: null,
            linkExistingEmployeeId: hiredEmployeeId,
            expectedStartDate,
            employmentCategory,
            cancellationToken);

    private async Task<Employee> ExecuteHireClosureAsync(
        Guid applicationId,
        Employee? employeePayload,
        Guid? linkExistingEmployeeId,
        DateOnly expectedStartDate,
        string employmentCategory,
        CancellationToken cancellationToken)
    {
        var legacyWithoutOffer = configuration.GetValue("Recruitment:LegacyHireWithoutOffer", false);
        var rejectSiblings = configuration.GetValue("Recruitment:RejectSiblingApplicationsOnHire", true);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var app = await db.RecruitmentApplications
            .Include(a => a.Requisition)
                .ThenInclude(r => r!.FundedPosition)
                    .ThenInclude(p => p!.JobCatalogEntry)
            .Include(a => a.Requisition)
                .ThenInclude(r => r!.PublicJobPosting)
            .Include(a => a.ActiveOfferLetter)
            .Include(a => a.SourceInternalApplication)
                .ThenInclude(i => i!.Employee)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new InvalidOperationException("طلب التوظيف غير موجود.");

        if (app.Status == RecruitmentApplicationStatus.Hired && app.HiredEmployeeId is Guid hiredId)
        {
            if (linkExistingEmployeeId is Guid linkId && linkId == hiredId)
            {
                return await db.Employees.FirstAsync(e => e.Id == hiredId, cancellationToken);
            }

            throw new InvalidOperationException("طلب معيّن مسبقاً.");
        }

        var appealError = await RecruitmentHireAppealGuard.ValidateAsync(
            db,
            app.RequisitionId,
            configuration.GetValue("Recruitment:BlockHireOnPendingAppeal", true),
            cancellationToken);
        if (appealError is not null)
            throw new InvalidOperationException(appealError);

        var offer = app.ActiveOfferLetter;
        var requiresOffer = !legacyWithoutOffer || app.RequisitionId is not null;

        if (requiresOffer)
        {
            if (app.Status != RecruitmentApplicationStatus.OfferAccepted)
                throw new InvalidOperationException("يمكن التعيين فقط بعد قبول خطاب العرض.");

            if (offer is null || offer.Status != OfferStatus.Accepted)
                throw new InvalidOperationException("يجب قبول خطاب العرض قبل التعيين.");

            if (offer.IsConvertedToEmployee)
                throw new InvalidOperationException("تم تحويل هذا العرض سابقاً.");

            var legalError = await legalSourceValidator.ValidateRequiredAsync(
                offer.LegalSourceId,
                RecruitmentLegalSourceRules.RequiredOnHireMessage,
                cancellationToken);
            if (legalError is not null)
                throw new InvalidOperationException(legalError);

            if (!RecruitmentLegalSourceRules.LineageMatches(app.Requisition?.LegalSourceId, offer.LegalSourceId))
                throw new InvalidOperationException(RecruitmentLegalSourceRules.LineageMismatchMessage);
        }
        else if (app.Status != RecruitmentApplicationStatus.Shortlisted)
        {
            throw new InvalidOperationException("لا يمكن التعيين في الحالة الحالية.");
        }

        Employee employee;
        Guid? previousOrgUnitId = null;
        var isInternalMobility = app.SourceInternalApplicationId.HasValue;

        if (linkExistingEmployeeId is Guid existingId)
        {
            employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == existingId, cancellationToken)
                ?? throw new InvalidOperationException("الموظف المحدد غير موجود في النواة.");
        }
        else if (isInternalMobility && app.SourceInternalApplication?.EmployeeId is Guid internalEmployeeId)
        {
            employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == internalEmployeeId, cancellationToken)
                ?? throw new InvalidOperationException("الموظف المتقدّم داخلياً غير موجود.");
        }
        else
        {
            employee = employeePayload
                ?? throw new InvalidOperationException("بيانات الموظف الجديد مطلوبة.");
            db.Employees.Add(employee);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (isInternalMobility)
        {
            previousOrgUnitId = employee.PrimaryOrgUnitId;
            var previousPosition = await db.FundedPositions
                .FirstOrDefaultAsync(p => p.OccupantEmployeeId == employee.Id, cancellationToken);
            if (previousPosition is not null)
            {
                var vacate = establishmentService.VacatePosition(previousPosition);
                if (!vacate.Success)
                    throw new InvalidOperationException(vacate.Error ?? "تعذر إخلاء المنصب السابق.");
                await fundedPositionVersions.AppendFromPositionAsync(
                    previousPosition,
                    EstablishmentOperationType.Vacated,
                    DateTime.UtcNow,
                    ct: cancellationToken);
            }
        }

        var requisition = app.Requisition;
        var position = requisition?.FundedPosition;
        var orgUnitId = employee.PrimaryOrgUnitId ?? position?.OrgUnitId;

        if (position is not null)
        {
            if (position.Status != FundedPositionStatus.Vacant)
                throw new InvalidOperationException("المنصب لم يعد شاغراً.");

            var assignResult = establishmentService.AssignEmployee(position, employee.Id);
            if (!assignResult.Success)
                throw new InvalidOperationException(assignResult.Error ?? "تعذر شغل المنصب الممول.");

            await fundedPositionVersions.AppendFromPositionAsync(
                position,
                EstablishmentOperationType.Occupied,
                DateTime.UtcNow,
                ct: cancellationToken);
        }

        if (!employee.PrimaryOrgUnitId.HasValue && orgUnitId is Guid resolvedOrg)
            employee.PrimaryOrgUnitId = resolvedOrg;

        var positionTitle = ResolvePositionTitle(app, position);
        await SeedEmployeeSideEffectsAsync(employee, positionTitle, orgUnitId, offer, cancellationToken);

        app.Status = RecruitmentApplicationStatus.Hired;
        app.HiredEmployeeId = employee.Id;
        app.ReviewedAt = DateTime.UtcNow;

        if (offer is not null)
            offer.IsConvertedToEmployee = true;

        if (requisition is not null)
        {
            requisitionCloseService.DecrementVacancy(requisition);
            requisitionCloseService.TryClose(requisition);
            requisitionCloseService.UnpublishPosting(requisition.PublicJobPosting);
            requisitionCloseService.CloseInternalVacancy(requisition.InternalVacancy);
            if (requisition.InternalVacancyId is null)
            {
                var linkedVacancy = await db.InternalVacancies
                    .FirstOrDefaultAsync(v => v.RequisitionId == requisition.Id, cancellationToken);
                requisitionCloseService.CloseInternalVacancy(linkedVacancy);
            }
        }

        if (rejectSiblings && app.RequisitionId is Guid requisitionId)
            await RejectSiblingApplicationsAsync(requisitionId, app.Id, cancellationToken);

        await PublishHireEventsAsync(
            employee,
            app,
            requisition,
            position,
            offer,
            expectedStartDate,
            employmentCategory,
            isInternalMobility,
            previousOrgUnitId,
            orgUnitId,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return employee;
    }

    private static string ResolvePositionTitle(RecruitmentApplication app, FundedPosition? position)
    {
        if (!string.IsNullOrWhiteSpace(app.Requisition?.JobTitle))
            return app.Requisition.JobTitle;

        if (!string.IsNullOrWhiteSpace(position?.JobCatalogEntry?.TitleAr))
            return position.JobCatalogEntry.TitleAr;

        return string.IsNullOrWhiteSpace(app.AppliedPosition) ? "تعيين من التوظيف" : app.AppliedPosition;
    }

    private async Task SeedEmployeeSideEffectsAsync(
        Employee employee,
        string positionTitle,
        Guid? defaultOrgUnitId,
        OfferLetter? offer,
        CancellationToken cancellationToken)
    {
        if (!employee.PrimaryOrgUnitId.HasValue && defaultOrgUnitId.HasValue)
            employee.PrimaryOrgUnitId = defaultOrgUnitId;

        db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = employee.Id,
            NewStatus = employee.CurrentStatus.ToString(),
            ChangedAt = DateTime.UtcNow,
            Reason = "تعيين من طلب توظيف"
        });

        if (!await db.EmploymentRecords.AnyAsync(e => e.EmployeeId == employee.Id, cancellationToken))
        {
            string? legalRegimeId = null;
            if (!string.IsNullOrWhiteSpace(offer?.LegalSourceId))
            {
                legalRegimeId = LegalSourceToRegimeMapper.MapOfferToRegime(offer.LegalSourceId);
                if (legalRegimeId is not null)
                {
                    var regimeError = await serviceRegimeValidator.ValidateAssignableAsync(legalRegimeId, cancellationToken);
                    if (regimeError is not null)
                        throw new InvalidOperationException(regimeError);
                }
                else
                {
                    logger.LogInformation(
                        "Hire for employee {EmployeeId}: no SYR-003 regime mapping for legal source {LegalSourceId}",
                        employee.Id,
                        offer.LegalSourceId);
                }
            }

            db.EmploymentRecords.Add(new EmploymentRecord
            {
                EmployeeId = employee.Id,
                PositionTitle = string.IsNullOrWhiteSpace(positionTitle) ? "تعيين من التوظيف" : positionTitle,
                StartDate = (employee.HireDate ?? DateTime.UtcNow.Date).AsUtc(),
                EmploymentType = HRManagement.SharedKernel.Enums.EmploymentType.Permanent,
                Status = EmploymentStatus.PendingActivation.ToString(),
                Notes = "سجل وظيفي أولي — بانتظار استكمال معالج التعيين",
                LegalRegimeId = legalRegimeId
            });

            var employmentRecord = db.EmploymentRecords.Local
                .LastOrDefault(r => r.EmployeeId == employee.Id);

            if (ServiceRegimeExceptionRules.ShouldEnqueueOnHire(legalRegimeId))
            {
                await serviceRegimeExceptionService.EnqueueIfUnclassifiedAsync(
                    employee,
                    employmentRecord,
                    ServiceRegimeExceptionSource.Hire,
                    offer?.LegalSourceId,
                    legalRegimeId,
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// HCM-027 (F2): Ranked siblings move to the reserve list (Recruitment:ReserveValidityMonths); other
    /// non-terminal siblings are Rejected; ReserveListed siblings keep their reserve date.
    /// </summary>
    private Task RejectSiblingApplicationsAsync(
        Guid requisitionId,
        Guid winnerApplicationId,
        CancellationToken cancellationToken) =>
        reserveList.MoveSiblingsToReserveAsync(requisitionId, winnerApplicationId, DateTime.UtcNow, cancellationToken);

    private async Task PublishHireEventsAsync(
        Employee employee,
        RecruitmentApplication app,
        RecruitmentRequisition? requisition,
        FundedPosition? position,
        OfferLetter? offer,
        DateOnly expectedStartDate,
        string employmentCategory,
        bool isInternalMobility,
        Guid? fromOrgUnitId,
        Guid? toOrgUnitId,
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
            CorrelationId = correlationId,
            TenantId = employee.TenantId,
            RecruitmentApplicationId = app.Id,
            RequisitionId = app.RequisitionId ?? requisition?.Id,
            FundedPositionId = position?.Id ?? offer?.FundedPositionId,
            OfferLetterId = offer?.Id
        }, cancellationToken);

        if (!isInternalMobility)
        {
            var legalRegimeId = await db.EmploymentRecords.AsNoTracking()
                .Where(r => r.EmployeeId == employee.Id)
                .OrderByDescending(r => r.StartDate)
                .Select(r => r.LegalRegimeId)
                .FirstOrDefaultAsync(cancellationToken);

            await publish.Publish<IEmployeeCreated>(new
            {
                EmployeeId = employee.Id,
                employee.FullName,
                NationalIdHash = PiiMask.Sha256Hex(employee.NationalId),
                EmploymentType = HRManagement.SharedKernel.Enums.EmploymentType.Permanent.ToString(),
                TenantId = employee.TenantId,
                PrimaryOrgUnitId = employee.PrimaryOrgUnitId,
                CorrelationId = correlationId,
                FundedPositionId = position?.Id ?? offer?.FundedPositionId,
                PositionCode = position?.PositionCode,
                LegalRegimeId = legalRegimeId,
                PayrollRegimeCode = ServiceRegimeToPayrollMapper.MapToPayrollCode(legalRegimeId)
            }, cancellationToken);
        }
        else
        {
            await publish.Publish<IInternalPlacementCompleted>(new
            {
                EmployeeId = employee.Id,
                RecruitmentApplicationId = app.Id,
                RequisitionId = app.RequisitionId ?? requisition?.Id,
                FundedPositionId = position?.Id ?? offer?.FundedPositionId,
                FromOrgUnitId = fromOrgUnitId,
                ToOrgUnitId = toOrgUnitId ?? employee.PrimaryOrgUnitId ?? position?.OrgUnitId,
                TenantId = employee.TenantId,
                CorrelationId = correlationId
            }, cancellationToken);
        }
    }
}
