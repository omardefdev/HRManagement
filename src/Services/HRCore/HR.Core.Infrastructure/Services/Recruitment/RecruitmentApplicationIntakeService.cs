using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed record PublishedRequisitionOption(
    Guid Id,
    string RequisitionNumber,
    string JobTitle,
    string? OrgUnitName);

/// <summary>HCM-003: validates and loads published requisitions for unified intake.</summary>
public class RecruitmentApplicationIntakeService(HRCoreDbContext db)
{
    public async Task<string?> ValidatePublishedRequisitionAsync(
        Guid requisitionId,
        DateOnly? postingClosingDate = null,
        bool forPublicPortal = false,
        CancellationToken cancellationToken = default)
    {
        var requisitions = forPublicPortal
            ? db.RecruitmentRequisitions.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            : db.RecruitmentRequisitions;
        var requisition = await requisitions
            .Include(r => r.FundedPosition)
            .FirstOrDefaultAsync(r => r.Id == requisitionId, cancellationToken);

        return RecruitmentApplicationIntakeRules.ValidatePublishedRequisition(
            requisition,
            requisition?.FundedPosition,
            DateOnly.FromDateTime(DateTime.UtcNow),
            postingClosingDate);
    }

    public async Task<IReadOnlyList<PublishedRequisitionOption>> GetOpenPublishedRequisitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.RecruitmentRequisitions
            .AsNoTracking()
            .Include(r => r.FundedPosition)
                .ThenInclude(p => p!.OrgUnit)
            .Include(r => r.PublicJobPosting)
            .Where(r => r.Status == RequisitionStatus.Published
                && r.ClosedDate == null
                && r.IntakeClosedAt == null
                && (r.ApplicationClosingDate == null || r.ApplicationClosingDate >= today)
                && r.FundedPosition!.Status == FundedPositionStatus.Vacant)
            .Where(r => r.PublicJobPosting == null
                || r.PublicJobPosting.ClosingDate == null
                || r.PublicJobPosting.ClosingDate >= today)
            .OrderByDescending(r => r.PublishedAt ?? r.RequestedDate)
            .ThenBy(r => r.RequisitionNumber)
            .Select(r => new PublishedRequisitionOption(
                r.Id,
                r.RequisitionNumber,
                r.JobTitle,
                r.FundedPosition!.OrgUnit!.NameAr))
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> ValidateInternalVacancyAsync(
        Guid internalVacancyId,
        CancellationToken cancellationToken = default)
    {
        var vacancy = await db.InternalVacancies
            .Include(v => v.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .FirstOrDefaultAsync(v => v.Id == internalVacancyId, cancellationToken);

        return RecruitmentApplicationIntakeRules.ValidateInternalApplication(
            vacancy?.Requisition,
            vacancy?.Requisition?.FundedPosition,
            vacancy,
            DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public async Task<bool> HasDuplicateInternalApplicationAsync(
        Guid employeeId,
        Guid requisitionId,
        CancellationToken cancellationToken = default) =>
        await db.InternalApplications.AnyAsync(
            a => a.EmployeeId == employeeId && a.RequisitionId == requisitionId,
            cancellationToken);

    public async Task<bool> HasDuplicateCitizenApplicationAsync(
        string nationalId,
        Guid requisitionId,
        string? nationalIdHash = null,
        bool forPublicPortal = false,
        CancellationToken cancellationToken = default)
    {
        var normalized = nationalId.Trim();
        var hash = nationalIdHash ?? PiiMask.Sha256Hex(normalized);
        var applications = forPublicPortal
            ? db.CitizenApplications.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            : db.CitizenApplications;
        return await applications.AnyAsync(
            a => a.RequisitionId == requisitionId
                 && (a.NationalId == normalized || a.NationalIdHash == hash),
            cancellationToken);
    }
}
