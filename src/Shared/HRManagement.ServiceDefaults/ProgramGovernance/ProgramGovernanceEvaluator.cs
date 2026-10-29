namespace HRManagement.ServiceDefaults.ProgramGovernance;

public static class ProgramGovernanceEvaluator
{
    public static List<string> Evaluate(
        GovernanceMatrixDocument matrix,
        DecisionLogDocument decisionLog,
        bool goLiveDeclared,
        GoLiveEvidenceDocument? evidence,
        DateTime? utcNow = null)
    {
        var failures = new List<string>();
        var now = (utcNow ?? DateTime.UtcNow).Date;

        if (!string.Equals(matrix.Card, "STR-005", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be STR-005, got '{matrix.Card}'");

        var syd = matrix.Blockers.FirstOrDefault(b =>
            string.Equals(b.Id, "SYD-017", StringComparison.OrdinalIgnoreCase));
        if (syd is null)
            failures.Add("blocker SYD-017 missing");
        else if (!string.Equals(syd.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
            failures.Add($"blocker SYD-017 must remain Blocked (got {syd.Status})");

        var pgm = matrix.Blockers.FirstOrDefault(b =>
            string.Equals(b.Id, "PGM-001", StringComparison.OrdinalIgnoreCase));
        if (pgm is null)
            failures.Add("blocker PGM-001 missing");
        else if (!string.Equals(pgm.Status, "Satisfied", StringComparison.OrdinalIgnoreCase))
            failures.Add($"blocker PGM-001 must remain Satisfied (got {pgm.Status})");

        foreach (var ex in matrix.Exceptions)
        {
            if (!string.Equals(ex.Status, "Declared", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(ex.ExpiresOn))
            {
                failures.Add($"{ex.ExceptionId}: Declared exception missing expiresOn");
                continue;
            }

            if (!GovernanceDateParser.TryParseUtcDate(ex.ExpiresOn, out var expires))
            {
                failures.Add($"{ex.ExceptionId}: expiresOn unparseable '{ex.ExpiresOn}' (expected yyyy-MM-dd)");
                continue;
            }

            if (expires < now)
                failures.Add($"{ex.ExceptionId}: Declared exception expired on {ex.ExpiresOn}");
        }

        var critical = matrix.CriticalDecisionTypes.Count > 0
            ? matrix.CriticalDecisionTypes
            : [];

        foreach (var d in decisionLog.Decisions)
        {
            var isCritical = critical.Count == 0
                             || critical.Contains(d.Type, StringComparer.OrdinalIgnoreCase);
            if (!isCritical) continue;

            if (string.IsNullOrWhiteSpace(d.OwnerRole) || string.IsNullOrWhiteSpace(d.ReviewerRole))
            {
                failures.Add($"{d.DecisionId}: critical decision missing owner/reviewer");
                continue;
            }

            if (string.Equals(d.OwnerRole, d.ReviewerRole, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{d.DecisionId}: ownerRole must differ from reviewerRole");
        }

        if (goLiveDeclared)
        {
            var goLiveErr = ValidateGoLiveEvidence(matrix, evidence);
            if (goLiveErr is not null)
                failures.Add(goLiveErr);
        }

        return failures;
    }

    public static string? ValidateGoLiveEvidence(
        GovernanceMatrixDocument matrix,
        GoLiveEvidenceDocument? evidence)
    {
        if (evidence is null)
            return "GoLiveDeclared requires go-live evidence file";

        var rules = matrix.GoLiveMultiSeat;
        if (rules is null)
            return "matrix missing goLiveMultiSeat rules";

        var seats = evidence.Seats
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        if (seats.Count == 0)
            return "go-live evidence has no seats";

        var engAliases = rules.EngRoleAliases.Count > 0
            ? rules.EngRoleAliases
            : ["engineering", "eng", "developer", "dev"];

        var nonEng = seats
            .Where(s => !engAliases.Contains(s, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (nonEng.Count == 0)
            return "go-live eng-only seats forbidden";

        foreach (var required in rules.RequiredSeats)
        {
            if (!seats.Any(s => string.Equals(s, required, StringComparison.OrdinalIgnoreCase)))
                return $"go-live missing required seat '{required}'";
        }

        if (rules.Forbidden.Any(f =>
                string.Equals(f, "eng-only", StringComparison.OrdinalIgnoreCase))
            && nonEng.Count == 0)
        {
            return "go-live forbidden eng-only";
        }

        return null;
    }
}
