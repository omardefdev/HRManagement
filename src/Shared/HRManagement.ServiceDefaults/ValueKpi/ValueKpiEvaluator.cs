namespace HRManagement.ServiceDefaults.ValueKpi;

public static class ValueKpiEvaluator
{
    private static readonly string[] DefaultForbidden = ["screenCount", "pageCount", "widgetCount"];
    private static readonly string[] DefaultRequiredFields =
    [
        "id", "name", "definition", "unit", "formula", "sourceRef",
        "ownerRole", "auditorRole", "target", "baselineRequired",
        "antiGamingRules", "benefitCategory", "moduleIds"
    ];

    public static List<string> Evaluate(ValueKpiMatrixDocument matrix, bool liveDashboardClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "STR-007", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be STR-007, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateKpis(matrix));
        failures.AddRange(EvaluateBaselines(matrix));
        failures.AddRange(EvaluateForbidden(matrix));
        failures.AddRange(EvaluatePrivacy(matrix));

        if (liveDashboardClaim)
        {
            var privacyErr = EvaluatePrivacy(matrix);
            if (privacyErr.Count > 0)
                failures.Add("LiveDashboardClaim requires valid tenant privacy rules");
            failures.Add("LiveDashboardClaim forbidden until PROD-006c / live-bi-dashboard");
        }

        return failures;
    }

    public static List<string> EvaluateBlockers(ValueKpiMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("STR-001", "Satisfied"),
                     ("STR-003", "Satisfied"),
                     ("GOV-003", "Satisfied"),
                     ("FIN-001", "NotStarted"),
                     ("PROD-003", "Satisfied")
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

    public static List<string> EvaluateKpis(ValueKpiMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.Kpis.Count < 9)
            failures.Add($"need >= 9 kpis (got {matrix.Kpis.Count})");

        var required = matrix.RequiredKpiFields.Count > 0
            ? matrix.RequiredKpiFields
            : DefaultRequiredFields.ToList();

        foreach (var k in matrix.Kpis)
        {
            foreach (var field in required)
            {
                var missing = field switch
                {
                    "id" => string.IsNullOrWhiteSpace(k.Id),
                    "name" => string.IsNullOrWhiteSpace(k.Name),
                    "definition" => string.IsNullOrWhiteSpace(k.Definition),
                    "unit" => string.IsNullOrWhiteSpace(k.Unit),
                    "formula" => string.IsNullOrWhiteSpace(k.Formula),
                    "sourceRef" => string.IsNullOrWhiteSpace(k.SourceRef),
                    "ownerRole" => string.IsNullOrWhiteSpace(k.OwnerRole),
                    "auditorRole" => string.IsNullOrWhiteSpace(k.AuditorRole),
                    "target" => k.Target is null,
                    "baselineRequired" => false,
                    "antiGamingRules" => k.AntiGamingRules.Count < 1,
                    "benefitCategory" => string.IsNullOrWhiteSpace(k.BenefitCategory),
                    "moduleIds" => false,
                    _ => false
                };
                if (missing)
                    failures.Add($"kpi {k.Id}: missing or empty '{field}'");
            }

            var pairErr = ValidateKpiPair(k);
            if (pairErr is not null)
                failures.Add(pairErr);
        }

        return failures;
    }

    public static string? ValidateKpiPair(ValueKpiDefinition kpi)
    {
        if (kpi.AntiGamingRules.Count < 1)
            return $"kpi {kpi.Id}: empty antiGamingRules";
        if (string.Equals(kpi.OwnerRole, kpi.AuditorRole, StringComparison.OrdinalIgnoreCase))
            return $"kpi {kpi.Id}: ownerRole must differ from auditorRole";
        return null;
    }

    public static List<string> EvaluateBaselines(ValueKpiMatrixDocument matrix)
    {
        var failures = new List<string>();
        var byKpi = matrix.Baselines
            .GroupBy(b => b.KpiId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var k in matrix.Kpis.Where(x => x.BaselineRequired))
        {
            if (!byKpi.TryGetValue(k.Id, out var baseline))
            {
                failures.Add($"kpi {k.Id}: missing baseline");
                continue;
            }

            if (!baseline.PrePilotFreeze)
                failures.Add($"kpi {k.Id}: baseline not prePilotFreeze");
        }

        return failures;
    }

    public static string? ValidateBaseline(ValueKpiMatrixDocument matrix, string kpiId)
    {
        var kpi = matrix.Kpis.FirstOrDefault(k =>
            string.Equals(k.Id, kpiId, StringComparison.OrdinalIgnoreCase));
        if (kpi is null)
            return $"unknown kpi '{kpiId}'";
        if (!kpi.BaselineRequired)
            return null;

        var baseline = matrix.Baselines.FirstOrDefault(b =>
            string.Equals(b.KpiId, kpiId, StringComparison.OrdinalIgnoreCase));
        if (baseline is null)
            return $"kpi {kpiId}: missing baseline";
        if (!baseline.PrePilotFreeze)
            return $"kpi {kpiId}: baseline not prePilotFreeze";
        return null;
    }

    public static List<string> EvaluateForbidden(ValueKpiMatrixDocument matrix)
    {
        var failures = new List<string>();
        var forbidden = matrix.ForbiddenBenefitMeasures.Count > 0
            ? matrix.ForbiddenBenefitMeasures
            : DefaultForbidden.ToList();

        foreach (var need in DefaultForbidden)
        {
            if (!forbidden.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"forbiddenBenefitMeasures missing {need}");
        }

        foreach (var k in matrix.Kpis)
        {
            if (forbidden.Any(f =>
                    string.Equals(f, k.BenefitCategory, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(f, k.Id, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"kpi {k.Id}: uses forbidden benefit measure");
            }
        }

        return failures;
    }

    public static string? ValidateBenefitMeasure(
        string measureId,
        IReadOnlyList<string>? forbidden = null)
    {
        var list = forbidden is { Count: > 0 } ? forbidden : DefaultForbidden;
        if (list.Any(f => string.Equals(f, measureId, StringComparison.OrdinalIgnoreCase)))
            return $"forbidden benefit measure '{measureId}'";
        return null;
    }

    public static List<string> EvaluatePrivacy(ValueKpiMatrixDocument matrix)
    {
        var failures = new List<string>();
        var p = matrix.PrivacyRules;
        if (p is null)
        {
            failures.Add("privacyRules missing");
            return failures;
        }

        if (!p.TenantScoped)
            failures.Add("privacyRules.tenantScoped must be true");
        if (!p.ForbidCrossTenantRollupWithoutWaiver)
            failures.Add("forbidCrossTenantRollupWithoutWaiver must be true");
        if (p.MinCellSize < 1)
            failures.Add("minCellSize must be >= 1");

        return failures;
    }

    public static bool PrivacyIsValid(ValueKpiMatrixDocument matrix) =>
        EvaluatePrivacy(matrix).Count == 0;
}
