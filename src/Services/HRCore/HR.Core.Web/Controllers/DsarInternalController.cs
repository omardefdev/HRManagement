using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// AC-27 / HCM-006: masked DSAR subject slices for Compliance S2S collection. PLAN-30 K2: the candidate slices also carry
/// the profile, the intake channel, the eligibility flags, document metadata, the message log, the offers' acceptance
/// channel and the /careers/my code challenges as counts (see <see cref="CandidateRecordsAsync"/>).
/// </summary>
[ApiController]
[Route("api/internal/dsar-subjects")]
[Authorize(Policy = ServicePolicies.DsarSubjectRead)]
public sealed partial class DsarInternalController(HRCoreDbContext db) : ControllerBase
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
            // HCM-030 (B3): once linked, the citizen Status column is no longer written — the recruitment status is.
            var linked = citizen.PromotedRecruitmentApplicationId is Guid linkedId
                ? await db.RecruitmentApplications.AsNoTracking()
                    .Where(a => a.Id == linkedId)
                    .Select(a => new { a.Status, a.IntakeChannel, a.EligibilityFlags, a.EligibilityEvaluatedAt })
                    .FirstOrDefaultAsync(ct)
                : null;
            var citizenRecords = await CandidateRecordsAsync(citizen.PromotedRecruitmentApplicationId, citizen.Id, ct);
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
                promotedRecruitmentApplicationId = citizen.PromotedRecruitmentApplicationId,
                recruitmentStatus = linked?.Status,
                birthDate = citizen.BirthDate,
                educationLevelCode = citizen.EducationLevelCode,
                specialization = citizen.Specialization,
                graduationYear = citizen.GraduationYear,
                yearsOfExperience = citizen.YearsOfExperience,
                notificationChannel = citizen.NotificationChannel?.ToString(),
                notificationConsentAt = citizen.NotificationConsentAt,
                intakeChannel = linked?.IntakeChannel.ToString(),
                eligibilityFlags = linked?.EligibilityFlags,
                eligibilityEvaluatedAt = linked?.EligibilityEvaluatedAt,
                citizenRecords.Documents,
                citizenRecords.NotificationLog,
                citizenRecords.Offers,
                citizenRecords.OtpChallengeCount,
                citizenRecords.OtpFailedAttemptCount
            });
        }

        var recruitment = await db.RecruitmentApplications.AsNoTracking()
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);

        if (recruitment is null)
            return NotFound();

        var records = await CandidateRecordsAsync(recruitment.Id, null, ct);

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
            hiredEmployeeId = recruitment.HiredEmployeeId,
            birthDate = recruitment.BirthDate,
            educationLevelCode = recruitment.EducationLevelCode,
            specialization = recruitment.Specialization,
            graduationYear = recruitment.GraduationYear,
            yearsOfExperience = recruitment.YearsOfExperience,
            intakeChannel = recruitment.IntakeChannel.ToString(),
            eligibilityFlags = recruitment.EligibilityFlags,
            eligibilityEvaluatedAt = recruitment.EligibilityEvaluatedAt,
            records.Documents,
            records.NotificationLog,
            records.Offers,
            records.OtpChallengeCount,
            records.OtpFailedAttemptCount
        });
    }

    [HttpGet("candidates/by-hash/{hash}")]
    public async Task<ActionResult<object>> GetCandidatesByHash(string hash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
            return BadRequest(new { error = "Invalid hash." });

        var citizenRows = await db.CitizenApplications.AsNoTracking()
            .Where(a => a.NationalIdHash == hash)
            .ToListAsync(ct);
        var citizens = new List<object>(citizenRows.Count);
        foreach (var a in citizenRows)
        {
            // PLAN-30 K2: the profile, the row's own documents and its /careers/my code counts; a linked row's messages and
            // offers are listed under its recruitment application below.
            var records = await CandidateRecordsAsync(null, a.Id, ct);
            citizens.Add(new
            {
                a.Id,
                type = "CitizenApplication",
                a.ReferenceNumber,
                a.Status,
                a.SubmittedAt,
                promotedRecruitmentApplicationId = a.PromotedRecruitmentApplicationId,
                birthDate = a.BirthDate,
                educationLevelCode = a.EducationLevelCode,
                specialization = a.Specialization,
                graduationYear = a.GraduationYear,
                yearsOfExperience = a.YearsOfExperience,
                notificationChannel = a.NotificationChannel?.ToString(),
                notificationConsentAt = a.NotificationConsentAt,
                records.Documents,
                records.OtpChallengeCount,
                records.OtpFailedAttemptCount
            });
        }

        var recruitmentRows = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.NationalIdHash == hash)
            .ToListAsync(ct);
        var recruitments = new List<object>(recruitmentRows.Count);
        foreach (var a in recruitmentRows)
        {
            var records = await CandidateRecordsAsync(a.Id, null, ct);
            recruitments.Add(new
            {
                a.Id,
                type = "RecruitmentApplication",
                a.Status,
                appliedAt = a.AppliedAt,
                a.HiredEmployeeId,
                birthDate = a.BirthDate,
                educationLevelCode = a.EducationLevelCode,
                specialization = a.Specialization,
                graduationYear = a.GraduationYear,
                yearsOfExperience = a.YearsOfExperience,
                intakeChannel = a.IntakeChannel.ToString(),
                eligibilityFlags = a.EligibilityFlags,
                eligibilityEvaluatedAt = a.EligibilityEvaluatedAt,
                records.Documents,
                records.NotificationLog,
                records.Offers,
                records.OtpChallengeCount,
                records.OtpFailedAttemptCount
            });
        }

        return Ok(new { hash, citizens, recruitments });
    }

    /// <summary>PLAN-30 K2: the candidate's records besides the application rows (metadata and counts only).</summary>
    public sealed record CandidateRecords(
        IReadOnlyList<object> Documents,
        IReadOnlyList<object> NotificationLog,
        IReadOnlyList<object> Offers,
        int OtpChallengeCount,
        int OtpFailedAttemptCount);

    /// <summary>
    /// PLAN-30 K2: document metadata (no content, no file id), the message log (masked recipient, template, status, times —
    /// never the subject or body), the offers' status and acceptance/decline channel (no salary), and the /careers/my code
    /// challenges as counts only (never the code hash or the request address). Documents and codes are those of the
    /// recruitment application and of its linked citizen rows, or of the given citizen row. Digit runs of national-ID length
    /// in file names are masked: the Compliance collector drops a whole array that contains one.
    /// </summary>
    private async Task<CandidateRecords> CandidateRecordsAsync(Guid? recruitmentId, Guid? citizenId, CancellationToken ct)
    {
        var citizenIds = recruitmentId is Guid linkedId
            ? await db.CitizenApplications.AsNoTracking()
                .Where(c => c.PromotedRecruitmentApplicationId == linkedId)
                .Select(c => c.Id)
                .ToListAsync(ct)
            : [];
        if (citizenId is Guid ownId && !citizenIds.Contains(ownId))
            citizenIds.Add(ownId);
        var nullableCitizenIds = citizenIds.Select(id => (Guid?)id).ToList();

        var documents = await db.CandidateDocuments.AsNoTracking()
            .Where(d => (recruitmentId != null && d.RecruitmentApplicationId == recruitmentId)
                || nullableCitizenIds.Contains(d.CitizenApplicationId))
            .OrderBy(d => d.UploadedAt)
            .Select(d => new { d.DocumentType, d.OriginalFileName, d.ContentType, d.SizeBytes, d.UploadedAt, d.UploadedBy })
            .ToListAsync(ct);

        var notifications = await db.CandidateNotifications.AsNoTracking()
            .Where(n => recruitmentId != null && n.RecruitmentApplicationId == recruitmentId)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new { n.Channel, n.TemplateCode, n.EventType, n.RecipientMasked, n.Status, n.CreatedAt, n.SentAt })
            .ToListAsync(ct);

        var offers = await db.OfferLetters.AsNoTracking()
            .Where(o => recruitmentId != null && o.RecruitmentApplicationId == recruitmentId)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                o.OfferNumber, o.Status, o.IssuedDate, o.ExpiryDate, o.AcceptedBy, o.AcceptedDate, o.DeclinedBy, o.DeclinedAt
            })
            .ToListAsync(ct);

        var otp = db.CandidateOtpChallenges.AsNoTracking().Where(o => citizenIds.Contains(o.CitizenApplicationId));
        var otpCount = await otp.CountAsync(ct);
        var otpAttempts = otpCount == 0 ? 0 : await otp.SumAsync(o => o.Attempts, ct);

        return new CandidateRecords(
            documents.Select(d => (object)new
            {
                documentType = d.DocumentType,
                originalFileName = MaskDigitRuns(d.OriginalFileName),
                contentType = d.ContentType,
                sizeBytes = d.SizeBytes,
                uploadedAt = d.UploadedAt,
                uploadedBy = d.UploadedBy
            }).ToList(),
            notifications.Select(n => (object)new
            {
                channel = n.Channel.ToString(),
                templateCode = n.TemplateCode,
                eventType = n.EventType,
                recipientMasked = n.RecipientMasked,
                status = n.Status.ToString(),
                createdAt = n.CreatedAt,
                sentAt = n.SentAt
            }).ToList(),
            offers.Select(o => (object)new
            {
                offerNumber = o.OfferNumber,
                status = o.Status.ToString(),
                issuedDate = o.IssuedDate,
                expiryDate = o.ExpiryDate,
                acceptedBy = o.AcceptedBy,
                acceptedDate = o.AcceptedDate,
                declinedBy = o.DeclinedBy,
                declinedAt = o.DeclinedAt
            }).ToList(),
            otpCount,
            otpAttempts);
    }

    /// <summary>Masks runs of 9 or more digits (a national ID or phone number typed into a file name).</summary>
    public static string MaskDigitRuns(string value) =>
        DigitRun().Replace(value, m => PiiMask.NationalId(m.Value));

    [GeneratedRegex(@"\d{9,}")]
    private static partial Regex DigitRun();

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
