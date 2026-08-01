using System.Diagnostics;
using System.Reflection;
using HRManagement.SharedKernel.Performance;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>HCM-003: tests for PerformanceCycle and PerformanceReview domain entities.</summary>
public class Hcm003PerformanceTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "hcm003-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void PerformanceCycle_extends_BaseEntity()
    {
        Assert.True(typeof(PerformanceCycle).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void PerformanceCycle_has_required_properties()
    {
        var props = typeof(PerformanceCycle).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("CycleCode", props);
        Assert.Contains("CycleName", props);
        Assert.Contains("Year", props);
        Assert.Contains("Status", props);
        Assert.Contains("PlanningStart", props);
        Assert.Contains("PlanningEnd", props);
        Assert.Contains("ReviewStart", props);
        Assert.Contains("ReviewEnd", props);
        Assert.Contains("CalibrationStart", props);
        Assert.Contains("CalibrationEnd", props);
        Assert.Contains("RatingScaleMax", props);
        Assert.Contains("ObjectivesRequired", props);
        Assert.Contains("CompetenciesIncluded", props);
        Assert.Contains("TemplateVersion", props);
    }

    [Fact]
    public void PerformanceCycle_IsInPhase_planning()
    {
        var cycle = new PerformanceCycle
        {
            PlanningStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PlanningEnd = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            ReviewStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ReviewEnd = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.True(cycle.IsInPhase(CyclePhase.Planning, new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(cycle.IsInPhase(CyclePhase.Planning, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void PerformanceCycle_IsInPhase_calibration_when_set()
    {
        var cycle = new PerformanceCycle
        {
            PlanningStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PlanningEnd = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            ReviewStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ReviewEnd = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
            CalibrationStart = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            CalibrationEnd = new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.True(cycle.IsInPhase(CyclePhase.Calibration, new DateTime(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(cycle.IsInPhase(CyclePhase.Calibration, new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void PerformanceCycle_IsInPhase_calibration_false_when_not_set()
    {
        var cycle = new PerformanceCycle
        {
            PlanningStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PlanningEnd = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            ReviewStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ReviewEnd = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
            CalibrationStart = null,
            CalibrationEnd = null
        };

        Assert.False(cycle.IsInPhase(CyclePhase.Calibration, new DateTime(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void CycleStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(CycleStatus), CycleStatus.Draft));
        Assert.True(Enum.IsDefined(typeof(CycleStatus), CycleStatus.Active));
        Assert.True(Enum.IsDefined(typeof(CycleStatus), CycleStatus.InReview));
        Assert.True(Enum.IsDefined(typeof(CycleStatus), CycleStatus.Calibrating));
        Assert.True(Enum.IsDefined(typeof(CycleStatus), CycleStatus.Closed));
        Assert.True(Enum.IsDefined(typeof(CycleStatus), CycleStatus.Cancelled));
    }

    [Fact]
    public void PerformanceReview_extends_BaseEntity()
    {
        Assert.True(typeof(PerformanceReview).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void PerformanceReview_has_required_properties()
    {
        var props = typeof(PerformanceReview).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("EmployeeId", props);
        Assert.Contains("PerformanceCycleId", props);
        Assert.Contains("ReviewerId", props);
        Assert.Contains("Type", props);
        Assert.Contains("Status", props);
        Assert.Contains("OverallRating", props);
        Assert.Contains("ObjectivesScore", props);
        Assert.Contains("CompetenciesScore", props);
        Assert.Contains("Comments", props);
        Assert.Contains("SelfAssessment", props);
        Assert.Contains("HasAppeal", props);
        Assert.Contains("CalibratedRating", props);
    }

    [Fact]
    public void PerformanceReview_FinalRating_uses_calibrated_when_available()
    {
        var review = new PerformanceReview
        {
            OverallRating = 3.5m,
            CalibratedRating = 4.0m
        };
        Assert.Equal(4.0m, review.FinalRating);

        review.CalibratedRating = null;
        Assert.Equal(3.5m, review.FinalRating);
    }

    [Fact]
    public void PerformanceReview_IsComplete_status_check()
    {
        var review = new PerformanceReview { Status = ReviewStatus.Completed };
        Assert.True(review.IsComplete);

        review.Status = ReviewStatus.Acknowledged;
        Assert.True(review.IsComplete);

        review.Status = ReviewStatus.InProgress;
        Assert.False(review.IsComplete);
    }

    [Fact]
    public void ReviewType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(ReviewType), ReviewType.Self));
        Assert.True(Enum.IsDefined(typeof(ReviewType), ReviewType.Manager));
        Assert.True(Enum.IsDefined(typeof(ReviewType), ReviewType.Matrix));
        Assert.True(Enum.IsDefined(typeof(ReviewType), ReviewType.Peer));
        Assert.True(Enum.IsDefined(typeof(ReviewType), ReviewType.Committee));
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "HCM_003_PERFORMANCE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Performance", "PerformanceCycle.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Performance", "PerformanceReview.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "hcm003-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
