namespace HRManagement.ServiceDefaults.MigFactory;

public static class MigFactoryEvaluator
{
    private static readonly string[] RequiredStages =
        ["extract", "validate", "transform", "quarantine", "load", "purge"];

    private static readonly string[] RequiredLineageFields =
    [
        "sourceId", "sourceRecordKey", "mappingVersion", "disposition",
        "loadBatchId", "loadedAt", "tenantId"
    ];

    private static readonly string[] RequiredPiiFields = ["NationalId", "FullName"];

    public static List<string> Evaluate(MigFactoryMatrixDocument matrix, bool liveEtlWorkersClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "MIG-003", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be MIG-003, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateStages(matrix));
        failures.AddRange(EvaluateStagingZones(matrix));
        failures.AddRange(EvaluateIdempotency(matrix.Idempotency));
        failures.AddRange(EvaluateCheckpoint(matrix.Checkpoint));
        failures.AddRange(EvaluateLineage(matrix.Lineage));
        failures.AddRange(EvaluatePii(matrix.PiiLogging));
        failures.AddRange(EvaluateThroughput(matrix));
        failures.AddRange(EvaluatePipelines(matrix));

        if (liveEtlWorkersClaim)
            failures.Add("LiveEtlWorkersClaim forbidden until MIG-003a-d ETL workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(MigFactoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("MIG-002", "Satisfied"),
                     ("TEN-002", "Satisfied"),
                     ("OPS-005", "Satisfied"),
                     ("agency-extract-access", "Blocked"),
                     ("live-cutover-perf", "Blocked")
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

    public static List<string> EvaluateStages(MigFactoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.Stages.Count < 6)
            failures.Add($"need >= 6 stages (got {matrix.Stages.Count})");

        var ids = matrix.Stages.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in RequiredStages)
        {
            if (!ids.Contains(need))
                failures.Add($"missing stage {need}");
        }

        return failures;
    }

    public static List<string> EvaluateStagingZones(MigFactoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.StagingZones.Count < 3)
            failures.Add($"need >= 3 stagingZones (got {matrix.StagingZones.Count})");

        foreach (var zone in matrix.StagingZones)
        {
            var err = ValidateStagingZone(zone);
            if (err is not null)
                failures.Add(err);
        }

        return failures;
    }

    public static string? ValidateStagingZone(MigStagingZone zone)
    {
        if (string.IsNullOrWhiteSpace(zone.Id))
            return "staging zone missing id";
        if (!zone.EncryptionAtRest)
            return $"staging zone {zone.Id}: encryptionAtRest must be true";
        if (!zone.TenantScoped)
            return $"staging zone {zone.Id}: tenantScoped must be true";
        if (string.IsNullOrWhiteSpace(zone.PurgePolicy))
            return $"staging zone {zone.Id}: purgePolicy missing";
        if (string.IsNullOrWhiteSpace(zone.AccessRole))
            return $"staging zone {zone.Id}: accessRole missing";
        return null;
    }

    public static List<string> EvaluateIdempotency(MigIdempotencyPolicy? policy)
    {
        var err = ValidateIdempotency(policy);
        return err is null ? [] : [err];
    }

    public static string? ValidateIdempotency(MigIdempotencyPolicy? policy)
    {
        if (policy is null)
            return "idempotency missing";
        if (!policy.DuplicateOnReloadForbidden)
            return "duplicateOnReloadForbidden must be true";
        if (!string.Equals(policy.ReloadPolicy, "skip-identical", StringComparison.OrdinalIgnoreCase))
            return "reloadPolicy must be skip-identical";
        if (policy.BatchKeyFields.Count < 1)
            return "batchKeyFields empty";
        if (policy.NaturalKeyFields.Count < 1)
            return "naturalKeyFields empty";
        return null;
    }

    public static List<string> EvaluateCheckpoint(MigCheckpointPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("checkpoint missing");
            return failures;
        }

        if (!policy.ResumeFromStage)
            failures.Add("resumeFromStage must be true");
        if (string.IsNullOrWhiteSpace(policy.MidFailBehavior))
            failures.Add("midFailBehavior missing");
        return failures;
    }

    public static List<string> EvaluateLineage(MigLineagePolicy? policy)
    {
        var err = ValidateLineageFields(policy?.PerRecordFields);
        return err is null ? [] : [err];
    }

    public static string? ValidateLineageFields(IReadOnlyList<string>? fields)
    {
        if (fields is null || fields.Count < 1)
            return "lineage perRecordFields empty";
        foreach (var need in RequiredLineageFields)
        {
            if (!fields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                return $"lineage missing field {need}";
        }

        return null;
    }

    public static List<string> EvaluatePii(MigPiiLoggingPolicy? policy)
    {
        var failures = new List<string>();
        if (policy is null)
        {
            failures.Add("piiLogging missing");
            return failures;
        }

        if (policy.ForbiddenFields.Count < 1)
            failures.Add("piiLogging forbiddenFields empty");
        foreach (var need in RequiredPiiFields)
        {
            if (!policy.ForbiddenFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"piiLogging forbiddenFields missing {need}");
        }

        if (string.IsNullOrWhiteSpace(policy.RedactionMode))
            failures.Add("piiLogging redactionMode missing");
        return failures;
    }

    public static string? ValidatePiiLogSample(IEnumerable<string> sampleKeys, MigPiiLoggingPolicy? policy)
    {
        var forbidden = policy?.ForbiddenFields ?? RequiredPiiFields.ToList();
        foreach (var key in sampleKeys)
        {
            if (forbidden.Any(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase)))
                return $"log sample contains forbidden PII field {key}";
        }

        return null;
    }

    public static List<string> EvaluateThroughput(MigFactoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        var t = matrix.Throughput;
        if (t is null)
        {
            failures.Add("throughput missing");
            return failures;
        }

        if (t.CutoverWindowHours <= 0)
            failures.Add("cutoverWindowHours must be > 0");
        if (t.TargetRowsPerHour <= 0)
            failures.Add("targetRowsPerHour must be > 0");

        var liveBlocked = matrix.Blockers.Any(b =>
            string.Equals(b.Id, "live-cutover-perf", StringComparison.OrdinalIgnoreCase)
            && string.Equals(b.Status, "Blocked", StringComparison.OrdinalIgnoreCase));
        if (t.LiveProofRequired && liveBlocked)
            failures.Add("liveProofRequired must be false while live-cutover-perf is Blocked");

        return failures;
    }

    public static string? ValidateThroughputClaim(bool liveProof, MigFactoryMatrixDocument matrix)
    {
        var liveBlocked = matrix.Blockers.Any(b =>
            string.Equals(b.Id, "live-cutover-perf", StringComparison.OrdinalIgnoreCase)
            && string.Equals(b.Status, "Blocked", StringComparison.OrdinalIgnoreCase));
        if (liveProof && liveBlocked)
            return "liveProof forbidden while live-cutover-perf is Blocked";
        return null;
    }

    public static List<string> EvaluatePipelines(MigFactoryMatrixDocument matrix)
    {
        var failures = new List<string>();
        if (matrix.Pipelines.Count < 1)
            failures.Add("need >= 1 pipeline");

        foreach (var p in matrix.Pipelines)
        {
            if (string.IsNullOrWhiteSpace(p.CodeMapVersionRef))
                failures.Add($"pipeline {p.Id}: codeMapVersionRef missing");
            var stageSet = p.StageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var need in RequiredStages)
            {
                if (!stageSet.Contains(need))
                    failures.Add($"pipeline {p.Id}: missing stageId {need}");
            }
        }

        return failures;
    }
}
