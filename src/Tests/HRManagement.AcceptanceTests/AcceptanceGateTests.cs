using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-33 AC-HCM-33a/33b: a missing precondition is recorded as Blocked and reported as Skipped, never Passed; under
/// <c>ACCEPTANCE_REQUIRE_ALL=true</c> a required scenario fails instead. Runs without the acceptance stack.
/// </summary>
public class AcceptanceGateTests
{
    [Fact]
    public void Missing_precondition_writes_blocked_result_and_skips()
    {
        WithRecorder(recorder =>
        {
            var ex = Assert.ThrowsAny<Exception>(() =>
                AcceptanceGate.Evaluate(recorder, false, "لا بيانات", "No seeded data", requireAll: false, required: true));

            Assert.Equal("Xunit.SkipException", ex.GetType().FullName);
            Assert.Contains("No seeded data", ex.Message, StringComparison.Ordinal);
            AssertBlocked(recorder, "No seeded data");
        });
    }

    [Fact]
    public void Required_scenario_fails_under_require_all()
    {
        WithRecorder(recorder =>
        {
            var ex = Assert.ThrowsAny<Exception>(() =>
                AcceptanceGate.Evaluate(recorder, false, "لا بيانات", "No seeded data", requireAll: true, required: true));

            Assert.IsNotType<SkipException>(ex);
            Assert.Contains("Required acceptance scenario blocked", ex.Message, StringComparison.Ordinal);
            AssertBlocked(recorder, "No seeded data");
        });
    }

    [Fact]
    public void Optional_scenario_still_skips_under_require_all()
    {
        WithRecorder(recorder =>
        {
            Assert.Throws<SkipException>(() =>
                AcceptanceGate.Evaluate(recorder, false, "لا بيانات", "No seeded data", requireAll: true, required: false));
            AssertBlocked(recorder, "No seeded data");
        });
    }

    [Fact]
    public void Satisfied_precondition_writes_nothing_and_continues()
    {
        WithRecorder(recorder =>
        {
            AcceptanceGate.Evaluate(recorder, true, "لا بيانات", "No seeded data", requireAll: true, required: true);
            Assert.False(File.Exists(Path.Combine(recorder.Folder, "result.json")));
        });
    }

    [Fact]
    public void Disabled_acceptance_without_recorder_skips_without_evidence()
    {
        Assert.Throws<SkipException>(() =>
            AcceptanceGate.Evaluate(null, false, "غير مفعّلة", "Set RUN_ACCEPTANCE=true", requireAll: false, required: true));
    }

    [Fact]
    public void Shared_folder_result_reports_the_worst_scenario()
    {
        var root = Path.Combine(Path.GetTempPath(), "hr-acceptance-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            new EvidenceRecorder("AC-HCM-21", "c1", root) { ScenarioKey = "21a" }.WriteResult("Fail", "boom");
            new EvidenceRecorder("AC-HCM-21", "c2", root) { ScenarioKey = "21b" }.WriteResult("Pass");
            var blocked = new EvidenceRecorder("AC-HCM-21", "c3", root) { ScenarioKey = "21c" };
            Assert.Throws<SkipException>(() =>
                AcceptanceGate.Evaluate(blocked, false, "لا بيانات", "No data", requireAll: false, required: false));

            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "AC-HCM-21", "result.json")));
            Assert.Equal("Fail", result.RootElement.GetProperty("status").GetString());
            var scenarios = result.RootElement.GetProperty("scenarios");
            Assert.Equal("Fail", scenarios.GetProperty("21a").GetProperty("status").GetString());
            Assert.Equal("Pass", scenarios.GetProperty("21b").GetProperty("status").GetString());
            Assert.Equal("Blocked", scenarios.GetProperty("21c").GetProperty("status").GetString());

            new EvidenceRecorder("AC-HCM-21", "c4", root) { ScenarioKey = "21a" }.WriteResult("Pass");
            using var after = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "AC-HCM-21", "result.json")));
            Assert.Equal("Blocked", after.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Started_scenario_that_never_records_a_result_counts_as_failed()
    {
        var root = Path.Combine(Path.GetTempPath(), "hr-acceptance-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            new EvidenceRecorder("AC-HCM-22", "c1", root) { ScenarioKey = "22a" }.WriteResult("Pass");
            // A later run of 22a starts and then throws before WriteResult.
            new EvidenceRecorder("AC-HCM-22", "c2", root) { ScenarioKey = "22a" }.WriteMetadata("AC-HCM-22a rerun", "central-a");

            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "AC-HCM-22", "result.json")));
            Assert.Equal("Fail", result.RootElement.GetProperty("status").GetString());
            Assert.Equal("Fail", result.RootElement.GetProperty("scenarios").GetProperty("22a").GetProperty("status").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Required_attribute_on_class_is_found_from_async_test_method()
    {
        var sample = new RequiredSample();
        await sample.DetectAsync();
        Assert.True(sample.AsyncResult);
        sample.DetectSync();
        Assert.True(sample.SyncResult);
    }

    [Fact]
    public async Task Unmarked_test_method_is_not_required()
    {
        await Task.Yield();
        Assert.False(AcceptanceGate.IsRequiredScenario(new StackTrace()));
    }

    [Fact]
    [RequiredAcceptance]
    public async Task Required_is_kept_in_a_helper_after_an_await()
    {
        // As RequireEnabled does at the start of every scenario; the helper's gate runs after a real await, when this
        // method is no longer on the physical stack (PLAN-33 P8 review finding).
        Assert.True(AcceptanceGate.CaptureRequired(new StackTrace()));
        Assert.True(await GateInHelperAsync());
    }

    [Fact]
    public async Task Optional_stays_optional_in_a_helper_after_an_await()
    {
        Assert.False(AcceptanceGate.CaptureRequired(new StackTrace()));
        Assert.False(await GateInHelperAsync());
    }

    private static async Task<bool> GateInHelperAsync()
    {
        await Task.Delay(5);
        return AcceptanceGate.IsRequired(new StackTrace());
    }

    [Fact]
    [RequiredAcceptance]
    public async Task Required_attribute_on_method_is_found()
    {
        await Task.Yield();
        Assert.True(AcceptanceGate.IsRequiredScenario(new StackTrace()));
    }

    private static void AssertBlocked(EvidenceRecorder recorder, string reasonFragment)
    {
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(recorder.Folder, "result.json")));
        Assert.Equal("Blocked", result.RootElement.GetProperty("status").GetString());
        Assert.Contains(reasonFragment, result.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
        foreach (var item in new[] { "metadata.json", "steps.md", "before.json", "after.json", "audit.json", "db-checks.json", "timing.json" })
            Assert.True(File.Exists(Path.Combine(recorder.Folder, item)), $"{item} missing");
    }

    private static void WithRecorder(Action<EvidenceRecorder> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "hr-acceptance-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            body(new EvidenceRecorder("AC-HCM-33", "corr-33", root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Stand-in for a required scenario class: the detection runs inside its own [Fact]-marked methods. Private, so
    /// xUnit does not discover it.
    /// </summary>
    [RequiredAcceptance]
#pragma warning disable xUnit1000 // deliberately private: a fixture, not a test class
    private sealed class RequiredSample
    {
        public bool AsyncResult { get; private set; }
        public bool SyncResult { get; private set; }

        [Fact]
        public async Task DetectAsync()
        {
            await Task.Yield();
            AsyncResult = AcceptanceGate.IsRequiredScenario(new StackTrace());
        }

        [Fact]
        public void DetectSync() => SyncResult = AcceptanceGate.IsRequiredScenario(new StackTrace());
    }
#pragma warning restore xUnit1000
}
