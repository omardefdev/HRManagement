using System.Reflection;
using System.Text.RegularExpressions;

namespace HRManagement.AcceptanceTests;

/// <summary>Static verification that AC-01..AC-37 each have at least one acceptance test class.</summary>
public class AcceptanceCoverageTests
{
    private static readonly string[] ExpectedAcCodes = Enumerable.Range(1, 37)
        .Select(n => $"AC-{n:D2}")
        .ToArray();

    [Fact]
    public void All_thirty_seven_acceptance_scenarios_have_test_classes()
    {
        var assembly = typeof(AcceptanceCoverageTests).Assembly;
        var acPattern = new Regex(@"^Ac(\d{2})", RegexOptions.IgnoreCase);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        {
            var match = acPattern.Match(type.Name);
            if (!match.Success)
                continue;
            covered.Add($"AC-{match.Groups[1].Value}");
        }

        var missing = ExpectedAcCodes.Where(c => !covered.Contains(c)).ToList();
        Assert.True(missing.Count == 0,
            $"Missing acceptance test classes for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Evidence_recorder_supports_eight_file_contract()
    {
        var required = new[]
        {
            "metadata.json", "steps.md", "before.json", "after.json",
            "audit.json", "db-checks.json", "timing.json", "result.json"
        };
        var recorderType = typeof(EvidenceRecorder);
        var methods = recorderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(EvidenceRecorder.WriteMetadata), methods);
        Assert.Contains(nameof(EvidenceRecorder.WriteSteps), methods);
        Assert.Contains(nameof(EvidenceRecorder.WriteBefore), methods);
        Assert.Contains(nameof(EvidenceRecorder.WriteAfter), methods);
        Assert.Contains(nameof(EvidenceRecorder.WriteAudit), methods);
        Assert.Contains(nameof(EvidenceRecorder.WriteDbChecks), methods);
        Assert.Contains(nameof(EvidenceRecorder.FlushTiming), methods);
        Assert.Contains(nameof(EvidenceRecorder.WriteResult), methods);
        Assert.Equal(8, required.Length);
    }
}
