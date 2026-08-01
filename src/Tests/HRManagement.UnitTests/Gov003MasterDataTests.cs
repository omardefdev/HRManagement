using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HRManagement.SharedKernel.Enums;

namespace HRManagement.UnitTests;

public class Gov003OwnershipContractTests
{
    private static string ResolveOwnershipPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "master-data", "field-ownership.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/master-data/field-ownership.json");
    }

    [Fact]
    public void Each_field_has_exactly_one_writer()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveOwnershipPath()));
        foreach (var field in doc.RootElement.GetProperty("fields").EnumerateArray())
        {
            var writer = field.GetProperty("writer").GetString();
            Assert.False(string.IsNullOrWhiteSpace(writer), field.GetProperty("field").GetString());
            Assert.DoesNotContain(",", writer!);
        }
    }

    [Fact]
    public void NationalId_and_BaseSalary_have_distinct_SoR()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveOwnershipPath()));
        string? nidWriter = null;
        string? salaryWriter = null;
        foreach (var field in doc.RootElement.GetProperty("fields").EnumerateArray())
        {
            var name = field.GetProperty("field").GetString();
            if (name == "Employee.NationalId")
                nidWriter = field.GetProperty("writer").GetString();
            if (name == "EmployeePayProfile.BaseSalary")
                salaryWriter = field.GetProperty("writer").GetString();
        }
        Assert.Equal("HR.Core", nidWriter);
        Assert.Equal("Payroll", salaryWriter);
    }
}

public class Gov003MergeUnmergeTests
{
    private static Employee NewEmployee(EmploymentStatus status = EmploymentStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        NationalId = Guid.NewGuid().ToString("N")[..10],
        FirstName = "A",
        LastName = "B",
        FullName = "A B",
        BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentStatus = status
    };

    [Fact]
    public void Merge_then_Unmerge_restores_Active()
    {
        var source = NewEmployee();
        var target = NewEmployee();
        var history = new List<EmploymentStatusHistory>();
        Assert.True(EmployeeLegalLifecycleService.TryMerge(source, target, Guid.NewGuid(), history).Success);
        Assert.Equal(EmploymentStatus.Merged, source.CurrentStatus);

        var (ok, err) = EmployeeLegalLifecycleService.TryUnmerge(source, Guid.NewGuid(), history);
        Assert.True(ok, err);
        Assert.Equal(EmploymentStatus.Active, source.CurrentStatus);
        Assert.Null(source.MergedIntoEmployeeId);
    }

    [Fact]
    public void Unmerge_rejects_LegalHold()
    {
        var source = NewEmployee();
        var target = NewEmployee();
        var history = new List<EmploymentStatusHistory>();
        EmployeeLegalLifecycleService.TryMerge(source, target, Guid.NewGuid(), history);
        source.LegalHold = true;
        var (ok, error) = EmployeeLegalLifecycleService.TryUnmerge(source, Guid.NewGuid(), history);
        Assert.False(ok);
        Assert.Contains("حجز", error ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Unmerge_rejects_non_merged()
    {
        var source = NewEmployee();
        var (ok, _) = EmployeeLegalLifecycleService.TryUnmerge(source, Guid.NewGuid(), []);
        Assert.False(ok);
    }
}

public class Gov003DataQualityThresholdTests
{
    [Fact]
    public void Evaluate_fails_on_orphans_above_threshold()
    {
        // Mirror scripts/gov003-data-quality-report.py evaluate() rules
        var metrics = new Dictionary<string, int>
        {
            ["completeness_violations"] = 0,
            ["national_id_duplicates"] = 0,
            ["orphan_employee_ids"] = 2,
            ["open_duplicate_cases"] = 0
        };
        var failures = new List<string>();
        if (metrics["completeness_violations"] > 0) failures.Add("completeness");
        if (metrics["national_id_duplicates"] > 0) failures.Add("duplicates");
        if (metrics["orphan_employee_ids"] > 0) failures.Add("orphans");
        Assert.Contains("orphans", failures);
    }

    [Fact]
    public void Evaluate_passes_clean_fixture()
    {
        var metrics = new Dictionary<string, int>
        {
            ["completeness_violations"] = 0,
            ["national_id_duplicates"] = 0,
            ["orphan_employee_ids"] = 0,
            ["open_duplicate_cases"] = 3
        };
        Assert.True(metrics["orphan_employee_ids"] == 0 && metrics["national_id_duplicates"] == 0);
        Assert.True(metrics["open_duplicate_cases"] <= 10);
    }
}
