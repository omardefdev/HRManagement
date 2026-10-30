using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-35 (D-QA-1/2/16, AC-QA-35d): <c>scripts/scm001-branch-protection-validate.ps1</c> accepts a protection value
/// below the SCM-001 policy only through <c>exceptionRefs</c> naming an Open, unexpired entry of
/// <c>docs/qa/governance-exceptions.json</c>; hard rules (no force-push, no deletion, admins enforced) take no exception;
/// every required context is a job name of ci.yml; the live comparison reports each difference.
/// Fixtures: <c>testdata/qa/branch-protection/</c>.
/// </summary>
public class Scm001BranchProtectionExceptionTests
{
    private const string Fixtures = "testdata/qa/branch-protection";
    private const string AsOf = "2026-11-01";

    private static (int Exit, string Output) Validate(string extra)
    {
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "scm001-branch-protection-validate.ps1"), $"-AsOfDate {AsOf} {extra}");
        return (exit, stdout + stderr);
    }

    [Fact]
    public void Live_file_passes_with_its_open_exceptions()
    {
        var (exit, output) = Validate("");
        Assert.True(exit == 0, output);
    }

    [Fact]
    public void Relaxed_values_without_exception_refs_fail()
    {
        var (exit, output) = Validate($"-Protection {Fixtures}/relaxed-no-ref.json");
        Assert.Equal(1, exit);
        Assert.Contains("required_pull_request_reviews.required_approving_review_count is below policy (>= 2) without exceptionRefs", output, StringComparison.Ordinal);
        Assert.Contains("required_signatures is below policy (true) without exceptionRefs", output, StringComparison.Ordinal);
        Assert.Contains("required_linear_history is below policy (true) without exceptionRefs", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Expired_exceptions_do_not_cover_the_relaxations()
    {
        var (exit, output) = Validate($"-Exceptions {Fixtures}/exceptions-expired.json");
        Assert.Equal(1, exit);
        Assert.Contains("exceptionRefs 'SCM-REVIEW-COUNT-0' is not an Open, unexpired entry", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_closed_exception_does_not_cover_its_relaxation()
    {
        var (exit, output) = Validate($"-Exceptions {Fixtures}/exceptions-closed.json");
        Assert.Equal(1, exit);
        Assert.Contains("required_signatures is below policy (true); exceptionRefs 'SCM-UNSIGNED-COMMITS'", output, StringComparison.Ordinal);
        Assert.DoesNotContain("'SCM-REVIEW-COUNT-0' is not", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Force_push_takes_no_exception()
    {
        var (exit, output) = Validate($"-Protection {Fixtures}/force-push.json");
        Assert.Equal(1, exit);
        Assert.Contains("allow_force_pushes must be false", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-gitleaks.json", "missing required check Gitleaks")]
    [InlineData("wrong-context-name.json", "required check 'CodeQL (SUP-001-C)' is not a job name")]
    public void Required_checks_are_the_ci_job_names(string fixture, string expected)
    {
        var (exit, output) = Validate($"-Protection {Fixtures}/{fixture}");
        Assert.Equal(1, exit);
        Assert.Contains(expected, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_settings_equal_to_the_file_pass()
    {
        var (exit, output) = Validate($"-LiveProtectionFile {Fixtures}/live-match.json");
        Assert.True(exit == 0, output);
        Assert.Contains("live protection of main matches", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_settings_from_before_PLAN_35_are_reported_field_by_field()
    {
        var (exit, output) = Validate($"-LiveProtectionFile {Fixtures}/live-before-plan35.json");
        Assert.Equal(1, exit);
        Assert.Contains("live required_signatures is 'True', file says 'False'", output, StringComparison.Ordinal);
        Assert.Contains("live required_pull_request_reviews.required_approving_review_count is '2', file says '0'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Codeowners_names_only_the_repository_owner()
    {
        var text = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "CODEOWNERS"));
        var tokens = text.Split('\n')
            .Where(l => !l.TrimStart().StartsWith('#'))
            .SelectMany(l => System.Text.RegularExpressions.Regex.Matches(l, @"@[A-Za-z0-9_./-]+").Select(m => m.Value))
            .Distinct()
            .ToList();
        Assert.Equal(["@omardefdev"], tokens);
    }
}
