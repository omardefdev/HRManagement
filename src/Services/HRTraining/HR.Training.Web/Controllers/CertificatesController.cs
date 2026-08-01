using HR.Training.Domain.Services;
using HR.Training.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Training.Web.Controllers;

/// <summary>FUN-004: public-authenticated certificate verification (no medical data).</summary>
[Authorize]
[ApiController]
[Route("api/certificates")]
public class CertificatesController(TrainingDbContext db) : ControllerBase
{
    public sealed record VerifyRequest(string CertificateNo);

    public sealed record VerifyResponse(
        bool Found,
        bool Valid,
        string Status,
        string? CertificateNo,
        DateTime? IssuedAt,
        Guid? EmployeeId,
        string? Source);

    [HttpPost("verify")]
    public async Task<ActionResult<VerifyResponse>> Verify([FromBody] VerifyRequest request, CancellationToken ct)
    {
        var no = request.CertificateNo?.Trim() ?? "";
        if (!CertificateVerificationRules.IsValidFormat(no))
            return Ok(new VerifyResponse(false, false, "InvalidFormat", no, null, null, null));

        var learning = await db.LearningCertificates.AsNoTracking()
            .Include(c => c.LearningEnrollment)
            .FirstOrDefaultAsync(c => c.CertificateNo == no, ct);
        if (learning is not null)
        {
            if (learning.IsRevoked)
                return Ok(new VerifyResponse(true, false, "Revoked", learning.CertificateNo, learning.IssuedAt,
                    learning.LearningEnrollment?.EmployeeId, "Learning"));
            return Ok(new VerifyResponse(true, true, "Valid", learning.CertificateNo, learning.IssuedAt,
                learning.LearningEnrollment?.EmployeeId, "Learning"));
        }

        var classroom = await db.TrainingEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.CertificateNo == no, ct);
        if (classroom is not null)
        {
            var valid = classroom.Status == Domain.Enums.EnrollmentStatus.Completed
                        && classroom.CancelledAt is null;
            return Ok(new VerifyResponse(true, valid, valid ? "Valid" : "Invalid",
                classroom.CertificateNo, classroom.UpdatedAt, classroom.EmployeeId, "Classroom"));
        }

        return Ok(new VerifyResponse(false, false, "NotFound", no, null, null, null));
    }

    [HttpGet("verify")]
    public Task<ActionResult<VerifyResponse>> VerifyGet([FromQuery] string certificateNo, CancellationToken ct) =>
        Verify(new VerifyRequest(certificateNo), ct);
}
