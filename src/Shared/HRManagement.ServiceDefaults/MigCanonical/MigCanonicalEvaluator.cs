namespace HRManagement.ServiceDefaults.MigCanonical;

public static class MigCanonicalEvaluator
{
    private static readonly string[] DefaultDispositions = ["Map", "Transform", "Quarantine"];
    private static readonly string[] DefaultCatchAlls = ["Other", "OTHER", "misc", "unknown-dump"];
    private static readonly string[] RequiredEntities = ["employee", "org", "job", "payroll", "document"];

    public static List<string> Evaluate(MigCanonicalMatrixDocument matrix, bool liveCodeMapEngineClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "MIG-002", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be MIG-002, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateEntities(matrix));
        failures.AddRange(EvaluateCodeMaps(matrix));
        failures.AddRange(EvaluateUnmappedPolicy(matrix));
        failures.AddRange(EvaluateLegacyIdPolicy(matrix));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.MappingVersion));
        failures.AddRange(EvaluateLocale(matrix));

        if (liveCodeMapEngineClaim)
            failures.Add("LiveCodeMapEngineClaim forbidden until MIG-003b / LiveEtlWorkersClaim");

        return failures;
    }

    public static List<string> EvaluateBlockers(MigCanonicalMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("MIG-001", "Satisfied"),
                     ("TEN-001", "Satisfied"),
                     ("GOV-003", "Satisfied"),
                     ("agency-extract-access", "Blocked"),
                     ("SYR-003", "Blocked"),
                     ("MDM-001", "Blocked")
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

    public static List<string> EvaluateEntities(MigCanonicalMatrixDocument matrix)
    {
        var failures = new List<string>();
        var ids = matrix.CanonicalEntities
            .Select(e => e.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in RequiredEntities)
        {
            if (!ids.Contains(need))
                failures.Add($"missing canonical entity {need}");
        }

        return failures;
    }

    public static List<string> EvaluateCodeMaps(MigCanonicalMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.CodeMaps.Count < 10)
            failures.Add($"need >= 10 codeMaps (got {matrix.CodeMaps.Count})");

        var allowed = matrix.AllowedDispositions.Count > 0
            ? matrix.AllowedDispositions
            : DefaultDispositions.ToList();
        var catchAlls = matrix.UnmappedPolicy?.ForbiddenCatchAllValues.Count > 0
            ? matrix.UnmappedPolicy.ForbiddenCatchAllValues
            : DefaultCatchAlls.ToList();

        foreach (var cm in matrix.CodeMaps)
        {
            var err = ValidateDisposition(cm, allowed, catchAlls);
            if (err is not null)
                failures.Add(err);
        }

        return failures;
    }

    public static string? ValidateDisposition(
        MigCodeMap map,
        IReadOnlyList<string>? allowed = null,
        IReadOnlyList<string>? catchAlls = null)
    {
        var dispositions = allowed is { Count: > 0 } ? allowed : DefaultDispositions;
        var forbidden = catchAlls is { Count: > 0 } ? catchAlls : DefaultCatchAlls;

        if (string.IsNullOrWhiteSpace(map.Disposition))
            return $"codeMap {map.MapId}: missing disposition";
        if (!dispositions.Any(d => string.Equals(d, map.Disposition, StringComparison.OrdinalIgnoreCase)))
            return $"codeMap {map.MapId}: bad disposition {map.Disposition}";
        if (string.Equals(map.Disposition, "Map", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(map.TargetValue))
        {
            return $"codeMap {map.MapId}: Map without targetValue";
        }

        if (!string.IsNullOrWhiteSpace(map.TargetValue)
            && forbidden.Any(f => string.Equals(f, map.TargetValue, StringComparison.OrdinalIgnoreCase)))
        {
            return $"codeMap {map.MapId}: uses forbidden catch-all target {map.TargetValue}";
        }

        return null;
    }

    public static List<string> EvaluateUnmappedPolicy(MigCanonicalMatrixDocument matrix)
    {
        var failures = new List<string>();
        var p = matrix.UnmappedPolicy;
        if (p is null)
        {
            failures.Add("unmappedPolicy missing");
            return failures;
        }

        if (!p.ForbidOther)
            failures.Add("forbidOther must be true");
        if (p.OtherBucketMaxRate < 0)
            failures.Add("otherBucketMaxRate must be >= 0");

        foreach (var need in DefaultCatchAlls)
        {
            if (!p.ForbiddenCatchAllValues.Any(f =>
                    string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"forbiddenCatchAllValues missing {need}");
            }
        }

        return failures;
    }

    public static List<string> EvaluateLegacyIdPolicy(MigCanonicalMatrixDocument matrix)
    {
        var failures = new List<string>();
        var p = matrix.LegacyIdPolicy;
        if (p is null)
        {
            failures.Add("legacyIdPolicy missing");
            return failures;
        }

        if (!p.StoreAsReferenceOnly)
            failures.Add("storeAsReferenceOnly must be true");
        if (!p.NeverUseAsAuthOrTenantKey)
            failures.Add("neverUseAsAuthOrTenantKey must be true");
        if (p.ReferenceFields.Count < 1)
            failures.Add("legacyIdPolicy referenceFields empty");

        return failures;
    }

    public static string? ValidateLegacyIdUse(string proposedKey, MigLegacyIdPolicy? policy)
    {
        if (string.IsNullOrWhiteSpace(proposedKey))
            return "proposed auth key empty";
        var p = policy ?? new MigLegacyIdPolicy { NeverUseAsAuthOrTenantKey = true, ReferenceFields = ["EmpNo", "NAT_ID"] };
        if (!p.NeverUseAsAuthOrTenantKey)
            return "neverUseAsAuthOrTenantKey must be true";
        if (p.ReferenceFields.Any(f => string.Equals(f, proposedKey, StringComparison.OrdinalIgnoreCase)))
            return $"legacy id '{proposedKey}' must not be used as auth/tenant key";
        return null;
    }

    public static List<string> EvaluateSigning(MigCodeMapSigning? sig, string? mappingVersion)
    {
        var failures = new List<string>();
        var err = ValidateSigning(sig, mappingVersion);
        if (err is not null)
            failures.Add(err);
        return failures;
    }

    public static string? ValidateSigning(MigCodeMapSigning? sig, string? mappingVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.MappingVersion ?? mappingVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }

    public static List<string> EvaluateLocale(MigCanonicalMatrixDocument matrix)
    {
        var failures = new List<string>();
        var tz = matrix.LocaleCalendar?.Timezone;
        if (tz is not null
            && !string.Equals(tz, "Asia/Damascus", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("timezone must be Asia/Damascus");
        }

        return failures;
    }
}
