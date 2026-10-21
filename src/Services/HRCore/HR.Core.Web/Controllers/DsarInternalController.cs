using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>AC-27 / HCM-006: masked DSAR subject slices for Compliance S2S collection.</summary>
[ApiController]
[Route("api/internal/dsar-subjects")]
[Authorize(Policy = ServicePolicies.DsarSubjectRead)]
public sealed class DsarInternalController(HRCoreDbContext db) : ControllerBase
{    [HttpGet("{employeeId:guid}")]
    public async Task<ActionResult<object>> GetSubject(Guid employeeId, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.PrimaryOrgUnit)
            .Where(e => e.Id == employeeId)
            .Select(e => new
            {
                e.Id,
                e.FullName,
                displayNationalId = PiiMask.NationalId(e.NationalId),
                nationalIdHash = e.NationalIdHash,
                e.PrimaryOrgUnitId,
                orgUnitName = e.PrimaryOrgUnit != null ? e.PrimaryOrgUnit.NameAr : null,
                status = e.CurrentStatus.ToString(),
                e.HireDate
            })
            .FirstOrDefaultAsync(ct);

        if (employee is null)
            return NotFound();

        return Ok(employee);
    }

    [HttpGet("candidates/{applicationId:guid}")]
    public async Task<ActionResult<object>> GetCandidateByApplication(Guid applicationId, CancellationToken ct)
    {
        var citizen = await db.CitizenApplications.AsNoTracking()
            .Include(a => a.PublicJobPosting)
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);

        if (citizen is not null)
        {
            return Ok(new
            {
                applicationId = citizen.Id,
                type = "CitizenApplication",
                citizen.ReferenceNumber,
                displayNationalId = PiiMask.NationalId(citizen.NationalId),
                nationalIdHash = citizen.NationalIdHash,
                citizen.FullName,
                citizen.Status,
                citizen.SubmittedAt,
                jobTitle = citizen.PublicJobPosting?.Title ?? citizen.Requisition?.JobTitle,
                requisitionNumber = citizen.Requisition?.RequisitionNumber,
                promotedRecruitmentApplicationId = citizen.PromotedRecruitmentApplicationId
            });
        }

        var recruitment = await db.RecruitmentApplications.AsNoTracking()
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);

        if (recruitment is null)
            return NotFound();

        return Ok(new
        {
            applicationId = recruitment.Id,
            type = "RecruitmentApplication",
            displayNationalId = recruitment.NationalId is null ? null : PiiMask.NationalId(recruitment.NationalId),
            nationalIdHash = recruitment.NationalIdHash,
            recruitment.ApplicantFullName,
            recruitment.Status,
            recruitment.AppliedAt,
            jobTitle = recruitment.Requisition?.JobTitle ?? recruitment.AppliedPosition,
            requisitionNumber = recruitment.Requisition?.RequisitionNumber,
            hiredEmployeeId = recruitment.HiredEmployeeId
        });
    }

    [HttpGet("candidates/by-hash/{hash}")]
    public async Task<ActionResult<object>> GetCandidatesByHash(string hash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
            return BadRequest(new { error = "Invalid hash." });

        var citizens = await db.CitizenApplications.AsNoTracking()
            .Where(a => a.NationalIdHash == hash)
            .Select(a => new
            {
                a.Id,
                type = "CitizenApplication",
                a.ReferenceNumber,
                a.Status,
                a.SubmittedAt
            })
            .ToListAsync(ct);

        var recruitments = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.NationalIdHash == hash)
            .Select(a => new
            {
                a.Id,
                type = "RecruitmentApplication",
                a.Status,
                appliedAt = a.AppliedAt,
                a.HiredEmployeeId
            })
            .ToListAsync(ct);

        return Ok(new { hash, citizens, recruitments });
    }

    [HttpPost("candidates/{applicationId:guid}/anonymize")]
    public async Task<IActionResult> AnonymizeCandidate(Guid applicationId, CancellationToken ct)
    {
        var citizen = await db.CitizenApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (citizen is not null)
        {
            var (ok, error) = CandidateApplicationLegalLifecycleService.TryAnonymize(citizen);
            if (!ok) return Conflict(new { error });
            await db.SaveChangesAsync(ct);
            return Ok(new { citizen.Id, citizen.AnonymizedAt });
        }

        var recruitment = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (recruitment is null) return NotFound();

        var (recOk, recError) = CandidateApplicationLegalLifecycleService.TryAnonymize(recruitment);
        if (!recOk) return Conflict(new { error = recError });
        await db.SaveChangesAsync(ct);
        return Ok(new { recruitment.Id, recruitment.AnonymizedAt });
    }
}
