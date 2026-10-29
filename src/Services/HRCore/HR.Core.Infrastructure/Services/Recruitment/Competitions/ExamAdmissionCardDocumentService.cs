using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031 (PLAN-31): fields printed on an exam admission card. No national id (the exam number identifies the paper).</summary>
public sealed record ExamAdmissionCardData(
    string CompetitionNumber,
    string CompetitionTitle,
    string CandidateName,
    string ExamNumber,
    string ExamTypeLabel,
    string SessionTitle,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    string Venue,
    int SeatNumber,
    string InstructionsAr)
{
    public static string FormatDateTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local)
            .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>HCM-031: the card as it renders now, with the hash stored at issue.</summary>
public sealed record ExamAdmissionCardView(
    Guid ExamCandidateId,
    ExamAdmissionCardData Data,
    string CurrentSha256,
    string? StoredSha256,
    DateTime? IssuedAt)
{
    public bool IsIssued => !string.IsNullOrWhiteSpace(StoredSha256);
    public bool IsIntact => IsIssued && string.Equals(StoredSha256!.Trim(), CurrentSha256, StringComparison.OrdinalIgnoreCase);
    public string? VerificationCode => IsIssued ? ExamAdmissionCardDocumentService.VerificationCodeOf(StoredSha256!) : null;
}

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31b): exam admission cards — data, canonical SHA-256 and the 12-hex verification code (the
/// PLAN-29 offer-letter pattern). The PDF is generated on demand (<c>HR.Core.Web.Services.ExamAdmissionCardDocument</c>);
/// the hash stored at issue proves later renders show the issued content.
/// </summary>
public sealed class ExamAdmissionCardDocumentService(HRCoreDbContext db, IOptions<RecruitmentOptions> options)
{
    public const int VerificationCodeLength = 12;
    public const string NotIssuedMessage = "لم تصدر بطاقة الامتحان بعد.";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { WriteIndented = false };

    public static string VerificationCodeOf(string sha256) =>
        sha256.Trim()[..Math.Min(VerificationCodeLength, sha256.Trim().Length)].ToUpperInvariant();

    public static string CanonicalJson(ExamAdmissionCardData data)
    {
        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["candidateName"] = data.CandidateName,
            ["competitionNumber"] = data.CompetitionNumber,
            ["competitionTitle"] = data.CompetitionTitle,
            ["durationMinutes"] = data.DurationMinutes.ToString(CultureInfo.InvariantCulture),
            ["examNumber"] = data.ExamNumber,
            ["examType"] = data.ExamTypeLabel,
            ["instructions"] = data.InstructionsAr,
            ["scheduledAt"] = DateTime.SpecifyKind(data.ScheduledAtUtc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["schema"] = "exam-admission-card/1",
            ["seatNumber"] = data.SeatNumber.ToString(CultureInfo.InvariantCulture),
            ["sessionTitle"] = data.SessionTitle,
            ["venue"] = data.Venue
        };
        return JsonSerializer.Serialize(fields, CanonicalJsonOptions);
    }

    public static string ComputeSha256(ExamAdmissionCardData data) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson(data)))).ToLowerInvariant();

    public static string ExamTypeLabel(ExamSessionType type) => type switch
    {
        ExamSessionType.Written => "كتابي",
        ExamSessionType.Practical => "عملي",
        _ => "شفهي"
    };

    /// <summary>The card of one seated candidate as it renders now (HR side, tenant filter applies).</summary>
    public Task<ExamAdmissionCardView?> GetAsync(Guid examCandidateId, CancellationToken ct = default) =>
        LoadAsync(db.ExamCandidates.Where(c => c.Id == examCandidateId), ct);

    /// <summary>
    /// The issued cards of one application (candidate portal: the reads bypass the tenant filter and are scoped to the
    /// application's own tenant). Unissued seats are not returned.
    /// </summary>
    public async Task<IReadOnlyList<ExamAdmissionCardView>> GetIssuedForApplicationAsync(
        Guid tenantId, Guid recruitmentApplicationId, CancellationToken ct = default)
    {
        var ids = await db.ExamCandidates
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .Where(c => c.TenantId == tenantId && c.RecruitmentApplicationId == recruitmentApplicationId && c.AdmissionCardIssuedAt != null)
            .OrderBy(c => c.ExamType)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var views = new List<ExamAdmissionCardView>();
        foreach (var id in ids)
            if (await LoadAsync(db.ExamCandidates.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                    .Where(c => c.TenantId == tenantId && c.Id == id), ct) is { } view)
                views.Add(view);
        return views;
    }

    /// <summary>The card data of a seated candidate (for stamping at issue).</summary>
    public static ExamAdmissionCardData BuildData(
        RecruitmentCompetition competition,
        ExamSession session,
        ExamCandidate candidate,
        string candidateName,
        string instructionsAr) =>
        new(
            competition.Number,
            competition.Title,
            candidateName,
            candidate.ExamNumber,
            ExamTypeLabel(session.Type),
            session.Title,
            session.ScheduledAt,
            session.DurationMinutes,
            session.Venue,
            candidate.SeatNumber,
            instructionsAr);

    public string Instructions => options.Value.Competition.AdmissionCardInstructionsAr;

    private async Task<ExamAdmissionCardView?> LoadAsync(IQueryable<ExamCandidate> query, CancellationToken ct)
    {
        var row = await query.AsNoTracking()
            .Select(c => new
            {
                Candidate = c,
                Session = c.ExamSession!,
                Competition = c.ExamSession!.Competition!,
                Name = c.RecruitmentApplication!.ApplicantFullName
            })
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return null;
        var data = BuildData(row.Competition, row.Session, row.Candidate, row.Name, Instructions);
        return new ExamAdmissionCardView(
            row.Candidate.Id,
            data,
            ComputeSha256(data),
            row.Candidate.AdmissionCardSha256?.Trim(),
            row.Candidate.AdmissionCardIssuedAt);
    }
}
