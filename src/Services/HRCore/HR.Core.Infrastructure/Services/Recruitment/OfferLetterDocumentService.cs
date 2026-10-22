using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-029 (D1): the rendered fields of an official offer letter. No national id. Dates are calendar dates (UTC date
/// of the stored value) so rendering and hashing never depend on the server time zone. The organization name in the
/// page header is a layout constant (<c>HrOfficialDocumentDefaults</c>) and is not part of the hash.
/// </summary>
public sealed record OfferLetterDocumentData(
    string? OrgUnitName,
    string OfferNumber,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    string CandidateName,
    string PositionTitle,
    string? PositionCode,
    string? GradeCode,
    decimal OfferedSalary,
    DateOnly ProposedStartDate,
    string EmploymentTypeLabel,
    string LegalReferenceLabel,
    string ConditionsAr)
{
    public const string DateFormat = "yyyy/MM/dd";

    public static string FormatDate(DateOnly? date) =>
        date?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "—";

    public static string FormatSalary(decimal amount) => OfferTermsRules.FormatAmount(amount);
}

/// <summary>HCM-029 (D2): the document as rendered now, with the hash stored at issue.</summary>
public sealed record OfferLetterDocumentView(
    Guid OfferId,
    OfferStatus Status,
    OfferLetterDocumentData Data,
    string CurrentSha256,
    string? StoredSha256,
    DateTime? GeneratedAt)
{
    public bool HasStoredHash => !string.IsNullOrWhiteSpace(StoredSha256);

    /// <summary>The fields rendered now reproduce the hash stored at issue.</summary>
    public bool IsIntact => HasStoredHash && string.Equals(StoredSha256!.Trim(), CurrentSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>First 12 hex characters of the stored hash (never of a recomputed one).</summary>
    public string? VerificationCode => HasStoredHash ? OfferLetterDocumentService.VerificationCodeOf(StoredSha256!) : null;

    public bool CanDownload => OfferLetterDocumentService.CanDownload(Status);
}

/// <summary>
/// HCM-029 (D): official offer letter — data, canonical hash and the issue-time stamp. The PDF is generated on demand
/// (like the PLAN-28 committee minutes); the hash stored at issue proves later renders show the issued content.
/// PLAN-29 follow-up: archive in File.Storage
/// </summary>
public sealed class OfferLetterDocumentService(
    HRCoreDbContext db,
    ILegislationRegisterProvider legislation,
    IOptions<RecruitmentOptions> options)
{
    public const int VerificationCodeLength = 12;

    public const string NotDownloadableMessage = "خطاب العرض الرسمي متاح فقط للعروض الصادرة أو المقبولة.";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { WriteIndented = false };

    public static bool CanDownload(OfferStatus status) => status is OfferStatus.Issued or OfferStatus.Accepted;

    public static string VerificationCodeOf(string sha256) =>
        sha256.Trim()[..Math.Min(VerificationCodeLength, sha256.Trim().Length)].ToUpperInvariant();

    /// <summary>
    /// Canonical JSON: fixed property order (ordinal), invariant culture, ISO dates, the salary with two decimals, so
    /// the same fields always give the same bytes regardless of culture or the database's decimal scale.
    /// </summary>
    public static string CanonicalJson(OfferLetterDocumentData data)
    {
        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["candidateName"] = data.CandidateName,
            ["conditions"] = data.ConditionsAr,
            ["employmentType"] = data.EmploymentTypeLabel,
            ["expiresOn"] = data.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["gradeCode"] = data.GradeCode,
            ["issuedOn"] = data.IssuedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["legalReference"] = data.LegalReferenceLabel,
            ["offerNumber"] = data.OfferNumber,
            ["offeredSalary"] = data.OfferedSalary.ToString("0.00", CultureInfo.InvariantCulture),
            ["orgUnit"] = data.OrgUnitName,
            ["positionCode"] = data.PositionCode,
            ["positionTitle"] = data.PositionTitle,
            ["proposedStart"] = data.ProposedStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["schema"] = "offer-letter/1"
        };
        return JsonSerializer.Serialize(fields, CanonicalJsonOptions);
    }

    public static string ComputeSha256(OfferLetterDocumentData data) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson(data)))).ToLowerInvariant();

    /// <summary>
    /// D2: at issue, stores the hash of the fields as they will be rendered. Call after the issue fields are set
    /// (<see cref="OfferWorkflowRules.MarkIssued"/>) and before <c>SaveChanges</c>.
    /// </summary>
    public async Task StampAsync(OfferLetter offer, DateTime nowUtc, CancellationToken ct = default)
    {
        var data = await BuildDataAsync(offer, ct);
        offer.DocumentSha256 = ComputeSha256(data);
        offer.DocumentGeneratedAt = nowUtc;
    }

    /// <summary>The document as it renders now, or null when the offer does not exist.</summary>
    public async Task<OfferLetterDocumentView?> GetAsync(Guid offerId, CancellationToken ct = default)
    {
        var offer = await db.OfferLetters.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null)
            return null;

        var data = await BuildDataAsync(offer, ct);
        return new OfferLetterDocumentView(
            offer.Id,
            offer.Status,
            data,
            ComputeSha256(data),
            offer.DocumentSha256?.Trim(),
            offer.DocumentGeneratedAt);
    }

    private async Task<OfferLetterDocumentData> BuildDataAsync(OfferLetter offer, CancellationToken ct)
    {
        var app = await db.RecruitmentApplications.AsNoTracking()
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == offer.RecruitmentApplicationId, ct);
        var position = await db.FundedPositions.AsNoTracking()
            .Include(p => p.OrgUnit)
            .Include(p => p.JobCatalogEntry)
            .FirstOrDefaultAsync(p => p.Id == offer.FundedPositionId, ct);

        var legalLabel = "—";
        if (!string.IsNullOrWhiteSpace(offer.LegalSourceId))
            legalLabel = (await legislation.GetByIdAsync(offer.LegalSourceId, ct))?.DisplayLabel ?? offer.LegalSourceId;

        var settings = options.Value;
        return new OfferLetterDocumentData(
            OrgUnitName: position?.OrgUnit?.NameAr,
            OfferNumber: offer.OfferNumber,
            IssuedOn: offer.IssuedDate is DateTime issued ? DateOnly.FromDateTime(issued) : null,
            ExpiresOn: offer.ExpiryDate is DateTime expiry ? DateOnly.FromDateTime(expiry) : null,
            CandidateName: app?.ApplicantFullName ?? "—",
            PositionTitle: ResolvePositionTitle(app, position),
            PositionCode: position?.PositionCode,
            GradeCode: offer.GradeCode ?? position?.GradeCode,
            OfferedSalary: offer.OfferedSalary,
            ProposedStartDate: DateOnly.FromDateTime(offer.ProposedStartDate),
            EmploymentTypeLabel: HireEmploymentTypeRules.LabelOf(settings.DefaultEmploymentType),
            LegalReferenceLabel: legalLabel,
            ConditionsAr: string.IsNullOrWhiteSpace(settings.OfferLetterConditionsAr)
                ? RecruitmentOptions.DefaultOfferLetterConditionsAr
                : settings.OfferLetterConditionsAr.Trim());
    }

    private static string ResolvePositionTitle(RecruitmentApplication? app, FundedPosition? position)
    {
        if (!string.IsNullOrWhiteSpace(app?.Requisition?.JobTitle))
            return app.Requisition.JobTitle;
        if (!string.IsNullOrWhiteSpace(position?.JobCatalogEntry?.TitleAr))
            return position.JobCatalogEntry.TitleAr;
        return string.IsNullOrWhiteSpace(app?.AppliedPosition) ? "—" : app.AppliedPosition;
    }
}
