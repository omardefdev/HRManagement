using System.Reflection;
using System.Text.Json;
using HRManagement.TestSupport;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-41 (AC-QA-41f): tests/acceptance/ci-scope.json lists every class exactly once, and the guard of the Acceptance job
/// refuses what the scope does not allow. The guard runs on the fixture trx files in testdata/acceptance-ci. No stack needed.
/// </summary>
public class AcceptanceCiScopeTests
{
    private static string Fixture(string name) => Path.Combine(RepoPaths.Root, "testdata", "acceptance-ci", name);

    private static JsonElement Scope(out JsonDocument document)
    {
        document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "acceptance", "ci-scope.json")));
        return document.RootElement;
    }

    private static IEnumerable<string> ClassesOf(JsonElement scope, string part) =>
        scope.GetProperty(part).EnumerateArray().Select(e => e.GetProperty("class").GetString()!);

    [Fact]
    public void Every_test_class_of_the_project_is_listed_exactly_once()
    {
        var discovered = typeof(AcceptanceCiScopeTests).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsClass: true, IsAbstract: false })
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(m => m.IsDefined(typeof(FactAttribute), inherit: true)))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var scope = Scope(out var document);
        using (document)
        {
            var excludedClasses = ClassesOf(scope, "excluded").Where(c => !c.Contains('.')).ToList();
            var listed = ClassesOf(scope, "lab").Concat(ClassesOf(scope, "prodLike")).Concat(excludedClasses).ToList();

            Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
            Assert.Empty(discovered.Except(listed, StringComparer.Ordinal));
            var journeys = new[] { "RecruitmentToHireJourneyE2ETests", "RecruitmentToHireJourneyProdLikeE2ETests" };
            Assert.Empty(listed.Except(discovered, StringComparer.Ordinal).Except(journeys, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Every_exclusion_has_a_reason_an_owner_and_a_review_date()
    {
        var scope = Scope(out var document);
        using (document)
        {
            Assert.All(scope.GetProperty("excluded").EnumerateArray(), entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("reason").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("ownerPlan").GetString()));
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", entry.GetProperty("reviewBy").GetString()!);
            });
        }
    }

    [Fact]
    public void Every_group_of_the_scope_has_a_stage()
    {
        var scope = Scope(out var document);
        using (document)
        {
            var stages = scope.GetProperty("order").EnumerateArray()
                .Select(s => $"{s.GetProperty("part").GetString()}/{s.GetProperty("group").GetString()}")
                .ToHashSet(StringComparer.Ordinal);
            foreach (var part in new[] { "lab", "prodLike" })
                Assert.All(scope.GetProperty(part).EnumerateArray(), e => Assert.Contains($"{part}/{e.GetProperty("group").GetString()}", stages));
        }
    }

    [Fact]
    public void No_shared_persona_password_is_left_in_scripts_workflows_or_tests()
    {
        var variable = "ACCEPTANCE_PERSONA" + "_PASSWORD";
        var roots = new[] { "scripts", ".github", Path.Combine("src", "Tests") };
        var hits = roots.SelectMany(root => Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, root), "*", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Path.GetExtension(f) is ".ps1" or ".sh" or ".yml" or ".yaml" or ".cs" or ".json")
            .Where(f => File.ReadAllText(f).Contains(variable, StringComparison.Ordinal))
            .ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Guard_passes_a_run_where_only_a_class_that_is_not_required_failed()
    {
        var (exitCode, output) = Guard("passing.trx");
        Assert.True(exitCode == 0, output);
    }

    /// <summary>AC-QA-41f negative.</summary>
    [Fact]
    public void Guard_fails_on_a_class_the_scope_does_not_list()
    {
        var (exitCode, output) = Guard("passing.trx", "unlisted-class.trx");
        Assert.Equal(1, exitCode);
        Assert.Contains("BrandNewTests ran and is not in the scope file", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_fails_on_a_skipped_scenario_of_a_required_class()
    {
        var (exitCode, output) = Guard("passing.trx", "skipped-required.trx");
        Assert.Equal(1, exitCode);
        Assert.Contains("It_was_skipped", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_fails_when_a_required_class_did_not_run()
    {
        var (exitCode, output) = Guard();
        Assert.Equal(1, exitCode);
        Assert.Contains("RequiredPassingTests did not run", output, StringComparison.Ordinal);
    }

    /// <summary>An aborted host keeps the finished tests in the trx: the class looks complete, the run was not.</summary>
    [Fact]
    public void Guard_fails_on_an_aborted_test_run()
    {
        var (exitCode, output) = Guard("aborted.trx");
        Assert.Equal(1, exitCode);
        Assert.Contains("aborted.trx: the test run did not finish", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_fails_on_a_result_file_it_cannot_read()
    {
        var evidence = Path.Combine(Path.GetTempPath(), "hr-guard-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(evidence, "AC-T"));
            File.WriteAllText(Path.Combine(evidence, "AC-T", "result.json"), """{"status":"Pass","scen""");
            var (exitCode, output) = Guard(["passing.trx"], $"-EvidenceDir \"{evidence}\"");
            Assert.Equal(1, exitCode);
            Assert.Contains("cannot be read", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(evidence, recursive: true);
        }
    }

    [Fact]
    public void Guard_fails_on_a_blocked_folder_of_a_required_class()
    {
        var evidence = Path.Combine(Path.GetTempPath(), "hr-guard-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(evidence, "AC-T"));
            File.WriteAllText(Path.Combine(evidence, "AC-T", "result.json"), """
                {"status":"Blocked","scenarios":{"s":{"status":"Blocked","reason":"no data","testClass":"RequiredPassingTests"}}}
                """);
            var (exitCode, output) = Guard(["passing.trx"], $"-EvidenceDir \"{evidence}\"");
            Assert.Equal(1, exitCode);
            Assert.Contains("AC-T/s (RequiredPassingTests): Blocked", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(evidence, recursive: true);
        }
    }

    [Fact]
    public void Guard_shows_a_blocked_folder_of_a_class_that_is_not_required_without_failing()
    {
        var evidence = Path.Combine(Path.GetTempPath(), "hr-guard-evidence-" + Guid.NewGuid().ToString("N"));
        var report = Path.Combine(Path.GetTempPath(), "hr-guard-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(evidence, "AC-T"));
            File.WriteAllText(Path.Combine(evidence, "AC-T", "result.json"), """
                {"status":"Blocked","scenarios":{"s":{"status":"Blocked","reason":"no data","testClass":"OptionalFailingTests"}}}
                """);
            var (exitCode, output) = Guard(["passing.trx"], $"-EvidenceDir \"{evidence}\" -OutDir \"{report}\"");
            Assert.True(exitCode == 0, output);
            Assert.Contains("AC-T/s (OptionalFailingTests): Blocked", File.ReadAllText(Path.Combine(report, "summary.md")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(evidence, recursive: true);
            if (Directory.Exists(report))
                Directory.Delete(report, recursive: true);
        }
    }

    // Built from parts, so a scanner of the repository does not take the sample for a real token.
    private const string SampleToken = "eyJ" + "hbGciOiJSUzI1NiIsInR5cCI6" + ".eyJ" + "zdWIiOiIxMjM0NTY3ODkwIiwibmFt" + ".c2lnbmF0dXJl";

    /// <summary>P-5: nothing generated on the runner reaches the artifact, and the report names the file, never the value.</summary>
    [Theory]
    [InlineData("result.json", "generated-persona-value-0123456789")]
    [InlineData("result.json", "generated-role-value-0123456789abcd")]
    [InlineData("log.txt", SampleToken)]
    [InlineData("credentials.json", "{}")]
    [InlineData("testhost_hangdump.dmp", "{}")]
    public void Artifact_scan_refuses_a_generated_value_a_token_or_a_secrets_file(string fileName, string content)
    {
        var (exitCode, output) = ArtifactScan(fileName, content);
        Assert.True(exitCode == 1, output);
        Assert.Contains(fileName, output, StringComparison.Ordinal);
        if (content != "{}")
            Assert.DoesNotContain(content, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Artifact_scan_passes_a_folder_without_secrets()
    {
        var (exitCode, output) = ArtifactScan("result.json", """{"status":"Pass"}""");
        Assert.True(exitCode == 0, output);
    }

    private static (int ExitCode, string Output) ArtifactScan(string fileName, string content)
    {
        var root = Path.Combine(Path.GetTempPath(), "hr-artifact-scan-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "run");
        Directory.CreateDirectory(target);
        try
        {
            var credentials = Path.Combine(root, "secrets.json");
            var environment = Path.Combine(root, "leastpriv.txt");
            File.WriteAllText(credentials, """{"passwords":{"central-a2":"generated-persona-value-0123456789"}}""");
            File.WriteAllText(environment, "POSTGRES_HR_CORE_APP_PASSWORD=generated-role-value-0123456789abcd\n");
            File.WriteAllText(Path.Combine(target, fileName), content);
            var script = Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-artifact-scan.ps1");
            var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
                script, $"-Target \"{target}\" -CredentialsFile \"{credentials}\" -LeastPrivEnv \"{environment}\"");
            return (exitCode, stdout + stderr);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int ExitCode, string Output) Guard(params string[] trxFiles) => Guard(trxFiles, "");

    private static (int ExitCode, string Output) Guard(string[] trxFiles, string extra)
    {
        var folder = Path.Combine(Path.GetTempPath(), "hr-guard-trx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var file in trxFiles)
                File.Copy(Fixture(file), Path.Combine(folder, file));
            var script = Path.Combine(RepoPaths.Root, "scripts", "ci", "acceptance-guard.ps1");
            var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
                script, $"-Scope \"{Fixture("scope.json")}\" -TrxDir \"{folder}\" {extra}");
            return (exitCode, stdout + stderr);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
