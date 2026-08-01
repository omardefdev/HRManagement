using System.Text.RegularExpressions;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// BOLA: employee-scoped GET actions must call org-scope guard helpers (IAM-002).
/// </summary>
public class BolaArchitectureTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
        }
    }

    private static readonly Regex EmployeeScopedAction = new(
        @"public\s+(?:async\s+)?Task<IActionResult>\s+\w+\s*\([^)]*\bGuid\?\s+employeeId\b[^)]*\)|public\s+(?:async\s+)?Task<IActionResult>\s+\w+\s*\([^)]*\bGuid\s+employeeId\b[^)]*\)",
        RegexOptions.Compiled);

    private static readonly string[] GuardMarkers =
    [
        "ForbidIfEmployeeOutOfScopeAsync",
        "ForbidIfRecordEmployeeOutOfScopeAsync",
        "ForbidIfRecordOutOfScopeAsync",
        "ForbidIfOrgUnitOutOfScope",
        "ForbidIfOrgScopeDenied",
        "GetAllowedOrgUnitIdsAsync",
        "GetEmployeeId",
        "WhereInOrgScope"
    ];

    [Fact]
    public void Employee_scoped_controller_actions_apply_org_scope_guard()
    {
        var controllers = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src", "Services"), "*Controller*.cs", SearchOption.AllDirectories)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}Controllers{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();
        foreach (var file in controllers)
        {
            var text = global::System.IO.File.ReadAllText(file);
            foreach (Match match in EmployeeScopedAction.Matches(text))
            {
                var segmentStart = Math.Max(0, match.Index - 400);
                var segment = text[segmentStart..match.Index];
                if (segment.Contains("[HttpPost", StringComparison.Ordinal))
                    continue;

                var start = Math.Max(0, match.Index - 200);
                var end = Math.Min(text.Length, match.Index + 800);
                var window = text[start..end];
                if (GuardMarkers.Any(marker => window.Contains(marker, StringComparison.Ordinal)))
                    continue;

                violations.Add($"{Path.GetRelativePath(RepoRoot, file)}: {match.Value.Trim()}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Employee-scoped actions must enforce org scope before returning data. Offenders:\n"
            + string.Join("\n", violations));
    }
}
