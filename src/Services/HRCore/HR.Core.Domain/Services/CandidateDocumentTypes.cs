using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-030 (E3/E4): candidate document types. A requisition stores its required types as comma-separated codes
/// (<c>RecruitmentRequisition.RequiredDocumentTypes</c>), like <c>RecruitmentApplication.EligibilityFlags</c>.
/// </summary>
public static class CandidateDocumentTypes
{
    public const string Cv = "Cv";
    public const string Certificate = "Certificate";
    public const string IdCopy = "IdCopy";
    public const string Other = "Other";

    /// <summary>Type codes in display order, with their Arabic labels.</summary>
    public static readonly IReadOnlyList<(string Code, string LabelAr)> All =
    [
        (Cv, "سيرة ذاتية"),
        (Certificate, "شهادة"),
        (IdCopy, "صورة الهوية"),
        (Other, "أخرى")
    ];

    public static bool IsValid(string? code) => code is not null && All.Any(t => t.Code == code);

    public static string LabelAr(string code) => All.FirstOrDefault(t => t.Code == code).LabelAr ?? code;

    /// <summary>Known codes (case-insensitive match), distinct, in display order; unknown codes are dropped.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string?>? codes)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(code))
                set.Add(code.Trim());
        }
        return All.Select(t => t.Code).Where(set.Contains).ToList();
    }

    public static IReadOnlyList<string> Parse(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : Normalize(stored.Split(',', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Required types that apply to an application of <paramref name="channel"/>: only public applications can attach
    /// documents (internal/ESS, HR-entered and legacy rows have no upload path), so the others require none. One rule
    /// for the <c>MissingRequiredDocuments</c> flag and the HR completeness badge/alert.
    /// </summary>
    public static IReadOnlyList<string> RequiredFor(RecruitmentIntakeChannel channel, string? stored) =>
        channel == RecruitmentIntakeChannel.Public ? Parse(stored) : [];

    /// <summary>Stored form: comma-separated codes in display order, null when none.</summary>
    public static string? Format(IEnumerable<string?>? codes)
    {
        var normalized = Normalize(codes);
        return normalized.Count == 0 ? null : string.Join(',', normalized);
    }

    /// <summary>Required types without at least one provided document of that type (display order).</summary>
    public static IReadOnlyList<string> Missing(IEnumerable<string> requiredTypes, IEnumerable<string> providedTypes)
    {
        var provided = new HashSet<string>(providedTypes, StringComparer.Ordinal);
        return Normalize(requiredTypes).Where(t => !provided.Contains(t)).ToList();
    }
}

/// <summary>
/// HCM-030 (E6): File.Storage scan states (mirrored from <c>File.Storage.Domain.Enums.FileScanStatus</c>, which HR.Core
/// does not reference) and how the HR card shows them. Only <see cref="Clean"/> files can be downloaded.
/// </summary>
public static class CandidateDocumentScanStatus
{
    public const string PendingScan = "PendingScan";
    public const string Clean = "Clean";
    public const string Infected = "Infected";
    public const string Quarantined = "Quarantined";
    public const string ScanError = "ScanError";

    public const string QuarantinedLabelAr = "محجور — غير متاح";
    public const string PendingLabelAr = "قيد الفحص";
    public const string UnknownLabelAr = "غير متاح حالياً";

    public static bool CanDownload(string? scanStatus) => scanStatus == Clean;

    /// <summary>Arabic label; null (File.Storage unreachable or the file missing from its answer) is fail-closed.</summary>
    public static string LabelAr(string? scanStatus) => scanStatus switch
    {
        Clean => "سليم",
        PendingScan => PendingLabelAr,
        Infected or Quarantined => QuarantinedLabelAr,
        ScanError => "تعذّر الفحص — غير متاح",
        _ => UnknownLabelAr
    };
}
