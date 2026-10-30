using System.Globalization;

namespace HR.Core.Infrastructure.Services.Establishment;

public sealed record FinanceBudgetImportRow(
    string CostCenter,
    string? GradeCode,
    int ApprovedCount,
    decimal ApprovedFTE,
    decimal? BudgetAmount,
    string? ExternalReference);

public interface IFinanceBudgetPort
{
    Task<IReadOnlyList<FinanceBudgetImportRow>> ParseCsvAsync(Stream stream, CancellationToken ct = default);
}

public sealed class FileFinanceBudgetPort : IFinanceBudgetPort
{
    public async Task<IReadOnlyList<FinanceBudgetImportRow>> ParseCsvAsync(Stream stream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(ct);
        return ParseCsvContent(content);
    }

    public static IReadOnlyList<FinanceBudgetImportRow> ParseCsvContent(string content)
    {
        var rows = new List<FinanceBudgetImportRow>();
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return rows;

        var start = 0;
        if (LooksLikeHeader(lines[0]))
            start = 1;

        for (var i = start; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 2) continue;

            var costCenter = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(costCenter)) continue;

            var gradeCode = parts.Length > 1 ? NullIfEmpty(parts[1].Trim()) : null;
            // QA-034 (owner decision Q-2): CSV is a machine format. With the request culture (ar-SY) "-1" did not parse
            // (minus sign U+061C + "-") and became 0, and "1000.50" did not parse (decimal separator "٫").
            var approvedCount = parts.Length > 2 && int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
            var approvedFte = parts.Length > 3 && decimal.TryParse(parts[3].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var f) ? f : approvedCount;
            decimal? budgetAmount = parts.Length > 4 && decimal.TryParse(parts[4].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var b) ? b : null;
            var externalRef = parts.Length > 5 ? NullIfEmpty(parts[5].Trim()) : null;

            rows.Add(new FinanceBudgetImportRow(costCenter, gradeCode, approvedCount, approvedFte, budgetAmount, externalRef));
        }

        return rows;
    }

    private static bool LooksLikeHeader(string line) =>
        line.Contains("CostCenter", StringComparison.OrdinalIgnoreCase)
        || line.Contains("مركز", StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
