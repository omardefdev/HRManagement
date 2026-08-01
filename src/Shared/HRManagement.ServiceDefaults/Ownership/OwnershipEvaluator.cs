namespace HRManagement.ServiceDefaults.Ownership;

public static class OwnershipEvaluator
{
    public static List<string> Evaluate(
        OwnershipMatrixDocument matrix,
        bool national24x7ClaimFromOptions)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "STR-006", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be STR-006, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateBusFactor(matrix));
        failures.AddRange(EvaluateBudget(matrix.BudgetLines, matrix.BudgetRule));
        failures.AddRange(EvaluateVendors(matrix));

        var claim = national24x7ClaimFromOptions || (matrix.Staffing?.National24x7Claim ?? false);
        var sre = matrix.Blockers.FirstOrDefault(b =>
            string.Equals(b.Id, "SRE-001", StringComparison.OrdinalIgnoreCase));
        if (claim && (sre is null || string.Equals(sre.Status, "Blocked", StringComparison.OrdinalIgnoreCase)))
            failures.Add("National24x7Claim forbidden while SRE-001 is Blocked");

        return failures;
    }

    public static List<string> EvaluateBlockers(OwnershipMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("STR-005", "Satisfied"),
                     ("SOV-007", "Satisfied"),
                     ("FIN-001", "NotStarted"),
                     ("SRE-001", "Blocked")
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

    public static List<string> EvaluateBusFactor(OwnershipMatrixDocument matrix)
    {
        var failures = new List<string>();
        var rule = matrix.BusFactorRule ?? new BusFactorRule();
        var minBf = rule.CriticalMinBusFactor > 0 ? rule.CriticalMinBusFactor : 2;
        var minBackups = rule.Tier0MinBackups > 0 ? rule.Tier0MinBackups : 1;

        foreach (var c in matrix.Components.Where(x => x.Critical))
        {
            if (c.BusFactor < minBf)
                failures.Add($"component {c.Id}: busFactor {c.BusFactor} < {minBf}");
            if (c.BackupRoles.Count < minBackups
                && (string.Equals(c.Tier, "0", StringComparison.Ordinal) || c.Tier == "0"))
            {
                failures.Add($"component {c.Id}: tier0 critical needs >= {minBackups} backupRoles");
            }
            else if (c.BackupRoles.Count < 1)
            {
                failures.Add($"component {c.Id}: critical without backupRoles");
            }
        }

        return failures;
    }

    public static List<string> EvaluateBudget(
        IEnumerable<OwnershipBudgetLine> budgetLines,
        BudgetRule? rule)
    {
        var failures = new List<string>();
        var lines = budgetLines.ToList();
        var budgetRule = rule ?? new BudgetRule
        {
            MustFundCategories = ["ops", "security", "backup"],
            ForbidDevOnly = true
        };
        var mustFund = budgetRule.MustFundCategories.Count > 0
            ? budgetRule.MustFundCategories
            : ["ops", "security", "backup"];

        var funded = lines
            .Where(l => l.Funded)
            .Select(l => l.Category)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (budgetRule.ForbidDevOnly
            && funded.Count > 0
            && funded.Count == 1
            && funded.Contains("dev"))
        {
            failures.Add("budget forbidDevOnly: only 'dev' is funded");
        }

        foreach (var cat in mustFund)
        {
            if (!funded.Contains(cat))
                failures.Add($"budget missing funded category '{cat}'");
        }

        return failures;
    }

    public static string? ValidateVendor(
        OwnershipVendor vendor,
        IReadOnlyList<string>? requiredClauses = null)
    {
        var required = requiredClauses is { Count: > 0 }
            ? requiredClauses
            : (IReadOnlyList<string>)["RTO", "IncidentDisclosure", "Exit"];

        foreach (var clause in required)
        {
            if (!vendor.Clauses.Any(c => string.Equals(c, clause, StringComparison.OrdinalIgnoreCase)))
                return $"vendor {vendor.VendorId}: missing clause '{clause}'";
        }

        if (required.Any(c => string.Equals(c, "Exit", StringComparison.OrdinalIgnoreCase))
            && !vendor.ExitAssistance)
        {
            return $"vendor {vendor.VendorId}: Exit clause requires exitAssistance=true";
        }

        return null;
    }

    public static List<string> EvaluateVendors(OwnershipMatrixDocument matrix)
    {
        var failures = new List<string>();
        var required = matrix.RequiredVendorClauses.Count > 0
            ? matrix.RequiredVendorClauses
            : ["RTO", "IncidentDisclosure", "Exit"];

        foreach (var v in matrix.Vendors)
        {
            var err = ValidateVendor(v, required);
            if (err is not null)
                failures.Add(err);
        }

        return failures;
    }
}
