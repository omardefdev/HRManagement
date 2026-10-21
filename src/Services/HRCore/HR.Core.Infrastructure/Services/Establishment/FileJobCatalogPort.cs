namespace HR.Core.Infrastructure.Services.Establishment;

public sealed record JobCatalogImportRow(
    int LineNumber,
    string TitleAr,
    string? TitleEn,
    string? DescriptionAr,
    string GradeCode,
    string JobClassificationCode,
    bool IsActive,
    string? LegacyJobCode);

public interface IJobCatalogImportPort
{
    Task<IReadOnlyList<JobCatalogImportRow>> ParseCsvAsync(Stream stream, CancellationToken ct = default);
}

public sealed class FileJobCatalogPort : IJobCatalogImportPort
{
    public async Task<IReadOnlyList<JobCatalogImportRow>> ParseCsvAsync(Stream stream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(ct);
        return ParseCsvContent(content);
    }

    public static IReadOnlyList<JobCatalogImportRow> ParseCsvContent(string content)
    {
        var rows = new List<JobCatalogImportRow>();
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return rows;

        var start = LooksLikeHeader(lines[0]) ? 1 : 0;

        for (var i = start; i < lines.Length; i++)
        {
            var parts = SplitCsvLine(lines[i]);
            if (parts.Count < 3) continue;

            var titleAr = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(titleAr)) continue;

            var titleEn = parts.Count > 1 ? NullIfEmpty(parts[1].Trim()) : null;
            var descriptionAr = parts.Count > 2 ? NullIfEmpty(parts[2].Trim()) : null;
            var gradeCode = parts.Count > 3 ? parts[3].Trim() : string.Empty;
            var jobClassification = parts.Count > 4 ? parts[4].Trim() : string.Empty;
            var isActive = parts.Count <= 5 || !bool.TryParse(parts[5].Trim(), out var active) || active;
            var legacyJobCode = parts.Count > 6 ? NullIfEmpty(parts[6].Trim()) : null;

            rows.Add(new JobCatalogImportRow(
                i + 1,
                titleAr,
                titleEn,
                descriptionAr,
                gradeCode,
                jobClassification,
                isActive,
                legacyJobCode));
        }

        return rows;
    }

    private static bool LooksLikeHeader(string line) =>
        line.Contains("TitleAr", StringComparison.OrdinalIgnoreCase)
        || line.Contains("GradeCode", StringComparison.OrdinalIgnoreCase)
        || line.Contains("المسمى", StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitCsvLine(string line)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
