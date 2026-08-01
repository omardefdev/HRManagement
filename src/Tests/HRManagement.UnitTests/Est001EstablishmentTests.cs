using System.Diagnostics;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Est001EstablishmentTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "est001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    private static FundedPosition MakePosition(FundedPositionStatus status = FundedPositionStatus.Vacant) => new()
    {
        TenantId = Guid.NewGuid(),
        PositionCode = "POS-001",
        GradeCode = "G5",
        FTE = 1.0m,
        BudgetYear = 2026,
        Status = status,
        OrgUnitId = Guid.NewGuid()
    };

    [Fact]
    public void Assign_to_vacant_position_succeeds()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition();
        var empId = Guid.NewGuid();

        var result = svc.AssignEmployee(pos, empId);

        Assert.True(result.Success);
        Assert.Equal(FundedPositionStatus.Occupied, pos.Status);
        Assert.Equal(empId, pos.OccupantEmployeeId);
    }

    [Fact]
    public void Assign_to_frozen_position_fails()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition(FundedPositionStatus.Frozen);

        var result = svc.AssignEmployee(pos, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Contains("frozen", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assign_to_occupied_position_fails()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition(FundedPositionStatus.Occupied);
        pos.OccupantEmployeeId = Guid.NewGuid();

        var result = svc.AssignEmployee(pos, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Contains("occupied", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assign_to_zero_fte_position_fails()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition();
        pos.FTE = 0;

        var result = svc.AssignEmployee(pos, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Contains("FTE", result.Error!);
    }

    [Fact]
    public void Vacate_occupied_position_succeeds()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition(FundedPositionStatus.Occupied);
        pos.OccupantEmployeeId = Guid.NewGuid();

        var result = svc.VacatePosition(pos);

        Assert.True(result.Success);
        Assert.Equal(FundedPositionStatus.Vacant, pos.Status);
        Assert.Null(pos.OccupantEmployeeId);
    }

    [Fact]
    public void Vacate_vacant_position_fails()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition();

        var result = svc.VacatePosition(pos);

        Assert.False(result.Success);
    }

    [Fact]
    public void Freeze_vacant_position_succeeds()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition();

        var result = svc.FreezePosition(pos, "Budget cut");

        Assert.True(result.Success);
        Assert.Equal(FundedPositionStatus.Frozen, pos.Status);
        Assert.Equal("Budget cut", pos.FreezeReason);
        Assert.NotNull(pos.FreezeDate);
    }

    [Fact]
    public void Freeze_occupied_position_fails()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition(FundedPositionStatus.Occupied);

        var result = svc.FreezePosition(pos, "Budget cut");

        Assert.False(result.Success);
        Assert.Contains("Vacate", result.Error!);
    }

    [Fact]
    public void Unfreeze_frozen_position_succeeds()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition(FundedPositionStatus.Frozen);
        pos.FreezeReason = "Budget cut";
        pos.FreezeDate = DateTime.UtcNow;

        var result = svc.UnfreezePosition(pos);

        Assert.True(result.Success);
        Assert.Equal(FundedPositionStatus.Vacant, pos.Status);
        Assert.Null(pos.FreezeReason);
    }

    [Fact]
    public void HasFundedPosition_returns_false_for_null()
    {
        var svc = new EstablishmentService();
        Assert.False(svc.HasFundedPosition(null, Guid.NewGuid()));
    }

    [Fact]
    public void HasFundedPosition_returns_false_for_frozen()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition(FundedPositionStatus.Frozen);
        Assert.False(svc.HasFundedPosition(pos, Guid.NewGuid()));
    }

    [Fact]
    public void HasFundedPosition_returns_false_for_occupied_by_other()
    {
        var svc = new EstablishmentService();
        var pos = MakePosition(FundedPositionStatus.Occupied);
        pos.OccupantEmployeeId = Guid.NewGuid();
        Assert.False(svc.HasFundedPosition(pos, Guid.NewGuid()));
    }

    [Fact]
    public void HasFundedPosition_returns_true_for_occupied_by_same()
    {
        var svc = new EstablishmentService();
        var empId = Guid.NewGuid();
        var pos = MakePosition(FundedPositionStatus.Occupied);
        pos.OccupantEmployeeId = empId;
        Assert.True(svc.HasFundedPosition(pos, empId));
    }

    [Fact]
    public void JobCatalogEntry_has_required_fields()
    {
        var entry = new JobCatalogEntry
        {
            TenantId = Guid.NewGuid(),
            JobCode = "IT-DEV-01",
            TitleAr = "مطور برمجيات",
            GradeCode = "G7",
            IsActive = true
        };

        Assert.Equal("IT-DEV-01", entry.JobCode);
        Assert.Equal("مطور برمجيات", entry.TitleAr);
        Assert.True(entry.IsActive);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EST_001_ESTABLISHMENT.md")));
    }

    [Fact]
    public void Migration_exists()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("src", "Services", "HRCore",
            "HR.Core.Infrastructure", "Migrations", "20260731171000_EST001_Establishment.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "est001-validate.ps1");
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
