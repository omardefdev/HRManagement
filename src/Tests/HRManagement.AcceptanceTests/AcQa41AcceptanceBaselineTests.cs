using System.Text.Json;
using HRManagement.E2E;
using HRManagement.TestSupport;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-41, the live evidence of the job itself (AC-QA-41a/e/f/g), on the prod-like stack near the end of the job: it reads the
/// metadata of the folders this run wrote, runs the one summarizer and the validator on them, and signs in the file scan with a
/// generated persona password. The negatives are static (AcceptanceEvidenceSummaryTests, AcceptanceCiScopeTests) or remote
/// (the dispatched run with ACCEPTANCE_SIMULATE_MISSING).
/// </summary>
[RequiredAcceptance]
public class AcQa41AcceptanceBaselineTests
{
    private const string Ac = "AC-QA-41";

    private static EvidenceRecorder Start(string key, string scenario)
    {
        var recorder = new EvidenceRecorder(Ac, $"qa41-{key}-{Guid.NewGuid():N}") { ScenarioKey = key };
        AcceptanceGate.RequireEnabled(recorder);
        recorder.WriteMetadata(scenario, "n/a");
        AcceptanceGate.RequireData(recorder, !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCEPTANCE_EVIDENCE_DIR")),
            "يعمل هذا الصنف داخل وظيفة القبول فقط", "This class reads the folders of a run of scripts/ci/acceptance-job.ps1.");
        return recorder;
    }

    /// <summary>The folders this run wrote: those that carry its code tree.</summary>
    private static List<(string Name, JsonDocument Metadata)> RunFolders()
    {
        var tree = AcceptanceRunContext.Current.CodeTree;
        var folders = new List<(string, JsonDocument)>();
        foreach (var folder in Directory.GetDirectories(AcceptanceHarness.EvidenceRoot))
        {
            var path = Path.Combine(folder, "metadata.json");
            if (!File.Exists(path) || Path.GetFileName(folder) == Ac)
                continue;
            var metadata = JsonDocument.Parse(File.ReadAllText(path));
            if (metadata.RootElement.TryGetProperty("codeTree", out var value) && value.GetString() == tree)
                folders.Add((Path.GetFileName(folder), metadata));
            else
                metadata.Dispose();
        }

        return folders;
    }

    [SkippableFact]
    public void Ac41_a_folders_of_the_run_say_their_profile_and_source()
    {
        var r = Start("41a", "AC-QA-41a: the job ran both profiles under ACCEPTANCE_REQUIRE_ALL, and every folder of the run names its profile and its source");
        var run = AcceptanceRunContext.Current;
        var folders = RunFolders();
        AcceptanceGate.RequireData(r, folders.Count > 0, "لا مجلدات لهذا التشغيل", "No evidence folder carries the code tree of this run.");

        var profiles = folders.Select(f => f.Metadata.RootElement.GetProperty("profile").GetString()).Distinct().ToList();
        var sources = folders.Select(f => f.Metadata.RootElement.GetProperty("source").GetString()).Distinct().ToList();
        r.WriteScenarioSteps("""
            1. Read metadata.json of every folder this run wrote (its code tree).
            2. Each names lab or prodlike, and the source of the run: ci with the run id, or local.
            3. ACCEPTANCE_REQUIRE_ALL=true, so a blocked required scenario fails instead of skipping.
            Negative: the dispatched run with ACCEPTANCE_SIMULATE_MISSING is red and names the scenario (reports/plan41-final-report.md).
            """);
        r.WriteScenarioDbChecks(new { folders = folders.Count, profiles, sources, run.CiRunId, run.CiEvent, requireAll = AcceptanceGate.RequireAll });

        Assert.True(AcceptanceGate.RequireAll);
        Assert.Contains("lab", profiles);
        Assert.Contains("prodlike", profiles);
        Assert.Equal([run.Source], sources);
        if (run.Source == "ci")
            Assert.All(folders, f => Assert.Equal(run.CiRunId, f.Metadata.RootElement.GetProperty("ciRunId").GetString()));
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public void Ac41_e_every_service_image_comes_from_the_commit_under_test()
    {
        var r = Start("41e", "AC-QA-41e: imageRevisions of every folder of the run: each service image was built from sourceSha");
        var run = AcceptanceRunContext.Current;
        var folders = RunFolders();
        AcceptanceGate.RequireData(r, folders.Count > 0, "لا مجلدات لهذا التشغيل", "No evidence folder carries the code tree of this run.");

        // A local run with a changed tracked file builds <sha>-dirty on purpose; only a CI run can be fresh.
        var allowed = run.Source == "ci" ? new[] { run.SourceSha } : [run.SourceSha, run.SourceSha + "-dirty"];
        var wrong = folders
            .SelectMany(f => f.Metadata.RootElement.GetProperty("imageRevisions").EnumerateObject()
                .Where(image => !allowed.Contains(image.Value.GetString()))
                .Select(image => $"{f.Name}: {image.Name} = {image.Value.GetString()}"))
            .ToList();
        var counts = folders.Select(f => f.Metadata.RootElement.GetProperty("imageRevisions").EnumerateObject().Count()).Distinct().ToList();
        r.WriteScenarioSteps("""
            1. docker inspect of the running stack, label org.opencontainers.image.revision, recorded in metadata.json by the recorder.
            2. Every service of every folder equals sourceSha; 15 services.
            Negative: AcceptanceEvidenceSummaryTests, a folder with an image of another source is Stale.
            """);
        r.WriteScenarioDbChecks(new { run.SourceSha, services = counts, wrong });

        Assert.Empty(wrong);
        Assert.All(counts, count => Assert.True(count >= 15, $"only {count} service images recorded"));
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac41_f_the_file_scan_signs_in_with_the_generated_persona_password()
    {
        var r = Start("41f", "AC-QA-41f: acceptance-file-scan.ps1 with no token reads the persona's password from .acceptance/credentials.json");
        var persona = AcceptanceHarness.RequirePersona("a-file-custodian");
        var saved = Environment.GetEnvironmentVariable("ACCEPTANCE_EVIDENCE_DIR");
        Environment.SetEnvironmentVariable("ACCEPTANCE_EVIDENCE_DIR", Path.Combine(r.Folder, "file-scan"));
        try
        {
            var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync(
                "acceptance-file-scan.ps1", ("Username", persona.Username), ("AccessToken", ""));
            r.WriteScenarioSteps("""
                1. scripts/acceptance-file-scan.ps1 -Username <persona> with no token: the script reads credentials.json.
                2. Clean upload downloadable, EICAR refused.
                Negative: AcceptanceCiScopeTests, no shared persona password in scripts, workflows or tests; a class the scope does not list fails the guard.
                """);
            r.WriteScenarioDbChecks(new { persona = persona.Username, exitCode });
            Assert.True(exitCode == 0, output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ACCEPTANCE_EVIDENCE_DIR", saved);
        }

        r.WriteResult("Pass");
    }

    [SkippableFact]
    public void Ac41_g_the_summary_of_the_run_passes_the_validator()
    {
        var r = Start("41g", "AC-QA-41g: the one summarizer on the folders of this run, then scripts/acceptance-baseline-validate.ps1");
        var run = AcceptanceRunContext.Current;
        var root = AcceptanceHarness.EvidenceRoot;
        var head = run.SourceSha!;
        var scripts = Path.Combine(RepoPaths.Root, "scripts");
        var (summaryExit, summaryOut, summaryErr) = RepoScriptRunner.RunScript(
            Path.Combine(scripts, "acceptance-evidence-summarize.ps1"), $"-EvidenceDir \"{root}\" -Head {head}");
        var (validatorExit, validatorOut, validatorErr) = RepoScriptRunner.RunScript(
            Path.Combine(scripts, "acceptance-baseline-validate.ps1"), $"-EvidenceDir \"{root}\" -Head {head}");

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "summary.json")));
        r.WriteScenarioSteps("""
            1. scripts/acceptance-evidence-summarize.ps1 -Head <sourceSha> on the evidence of this run.
            2. scripts/acceptance-baseline-validate.ps1: every AC has an owner, summary.json agrees with the folders, nothing is
               counted as proven against the rules.
            Negative: AcceptanceEvidenceSummaryTests, a local summary in -Committed mode, an AC without an owner and a Pass with an
            incomplete contract all fail the validator.
            """);
        r.WriteScenarioDbChecks(new
        {
            summaryExit,
            validatorExit,
            source = summary.RootElement.GetProperty("source").GetString(),
            totals = JsonSerializer.Deserialize<object>(summary.RootElement.GetProperty("totals").GetRawText())
        });

        Assert.True(summaryExit == 0, summaryOut + summaryErr);
        Assert.True(validatorExit == 0, validatorOut + validatorErr);
        Assert.Equal(run.Source, summary.RootElement.GetProperty("source").GetString());
        r.WriteResult("Pass");
    }
}
