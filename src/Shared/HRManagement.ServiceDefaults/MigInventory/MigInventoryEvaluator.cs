namespace HRManagement.ServiceDefaults.MigInventory;

public static class MigInventoryEvaluator
{
    public static List<string> Evaluate(MigInventoryMatrixDocument matrix, bool liveAgencyExtractClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "MIG-001", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be MIG-001, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateCoverage(matrix));
        failures.AddRange(EvaluateSources(matrix));
        failures.AddRange(EvaluateFieldSot(matrix));

        if (liveAgencyExtractClaim)
        {
            var agency = matrix.Blockers.FirstOrDefault(b =>
                string.Equals(b.Id, "agency-extract-access", StringComparison.OrdinalIgnoreCase));
            if (agency is null || string.Equals(agency.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
                failures.Add("LiveAgencyExtractClaim forbidden while agency-extract-access is Blocked");
        }

        return failures;
    }

    public static List<string> EvaluateBlockers(MigInventoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("STR-001", "Satisfied"),
                     ("GOV-003", "Satisfied"),
                     ("agency-extract-access", "Blocked"),
                     ("SYR-002", "Blocked")
                 })
        {
            var b = matrix.Blockers.FirstOrDefault(x =>
                string.Equals(x.Id, need.Id, StringComparison.OrdinalIgnoreCase));
            if (b is null)
                failures.Add($"blocker {need.Id} missing");
            else if (!string.Equals(b.Status, need.Status, StringComparison.OrdinalIgnoreCase))
                failures.Add($"blocker {need.Id} must be {need.Status} (got {b.Status})");
        }

        return failures;
    }

    public static List<string> EvaluateCoverage(MigInventoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.Sources.Count < 6)
            failures.Add($"need >= 6 sources (got {matrix.Sources.Count})");

        var sourceIds = matrix.Sources
            .Select(s => s.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exceptionsBySource = matrix.Exceptions
            .GroupBy(e => e.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var uid in matrix.KnownSourceUniverse)
        {
            if (!sourceIds.Contains(uid))
            {
                failures.Add($"universe source {uid} missing from sources");
                continue;
            }

            var s = matrix.Sources.First(x =>
                string.Equals(x.SourceId, uid, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(s.Status, "Unsupported", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Status, "ExceptionSigned", StringComparison.OrdinalIgnoreCase))
            {
                if (!exceptionsBySource.TryGetValue(uid, out var ex))
                {
                    failures.Add($"universe source {uid} status {s.Status} without signed exception");
                }
                else
                {
                    var exErr = ValidateException(ex);
                    if (exErr is not null)
                        failures.Add(exErr);
                }
            }
        }

        return failures;
    }

    public static string? ValidateException(MigInventoryException ex)
    {
        if (string.IsNullOrWhiteSpace(ex.SignedByRole) || string.IsNullOrWhiteSpace(ex.ExpiresOn))
            return $"exception {ex.ExceptionId}: missing signedByRole/expiresOn";
        return null;
    }

    public static List<string> EvaluateSources(MigInventoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var s in matrix.Sources)
        {
            var err = ValidateInventoriedSource(s);
            if (err is not null)
                failures.Add(err);

            if ((string.Equals(s.Status, "Unsupported", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(s.Status, "ExceptionSigned", StringComparison.OrdinalIgnoreCase))
                && !matrix.Exceptions.Any(e =>
                    string.Equals(e.SourceId, s.SourceId, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"source {s.SourceId}: {s.Status} without signed exception");
            }
        }

        return failures;
    }

    public static string? ValidateInventoriedSource(MigSource source)
    {
        if (!string.Equals(source.Status, "Inventoried", StringComparison.OrdinalIgnoreCase))
            return null;

        if (source.AccessRisks.Count < 1)
            return $"source {source.SourceId}: Inventoried without accessRisks";
        if (source.Volume?.EstimateOnly == true)
            return $"source {source.SourceId}: Inventoried with estimateOnly";
        if (string.IsNullOrWhiteSpace(source.Volume?.MeasuredAt)
            || string.IsNullOrWhiteSpace(source.Volume?.Method))
        {
            return $"source {source.SourceId}: missing volume measuredAt/method";
        }

        return null;
    }

    public static List<string> EvaluateFieldSot(MigInventoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        var sourceIds = matrix.Sources
            .Select(s => s.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sotFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in matrix.TargetFieldSot)
        {
            var err = ValidateFieldSot(t, sourceIds);
            if (err is not null)
                failures.Add(err);
            if (!string.IsNullOrWhiteSpace(t.TargetField))
                sotFields.Add(t.TargetField);
        }

        foreach (var need in matrix.RequiredTargetFields)
        {
            if (!sotFields.Contains(need))
                failures.Add($"required target field {need} missing SoT");
        }

        return failures;
    }

    public static string? ValidateFieldSot(MigFieldSot row, IReadOnlySet<string> sourceIds)
    {
        if (string.IsNullOrWhiteSpace(row.TargetField))
            return "targetFieldSot missing targetField";
        if (string.IsNullOrWhiteSpace(row.SourceOfTruthSourceId))
            return $"targetFieldSot {row.TargetField}: missing sourceOfTruthSourceId";
        if (string.IsNullOrWhiteSpace(row.SourceField))
            return $"targetFieldSot {row.TargetField}: missing sourceField";
        if (string.IsNullOrWhiteSpace(row.Gov003Join))
            return $"targetFieldSot {row.TargetField}: missing gov003Join";
        if (!sourceIds.Contains(row.SourceOfTruthSourceId))
            return $"SoT source {row.SourceOfTruthSourceId} not in inventory";
        return null;
    }
}
