using System.Diagnostics;
using HR.Core.Domain.Entities;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Org001DatedStructureTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "org001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void OrgUnitVersion_has_required_properties()
    {
        var version = new OrgUnitVersion
        {
            OrgUnitId = Guid.NewGuid(),
            ValidFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidTo = null,
            Name = "وزارة التنمية الإدارية",
            ParentId = Guid.NewGuid(),
            OperationType = OrgOperationType.Create,
            TenantId = Guid.NewGuid()
        };

        Assert.NotEqual(Guid.Empty, version.Id);
        Assert.NotEqual(Guid.Empty, version.OrgUnitId);
        Assert.Equal("وزارة التنمية الإدارية", version.Name);
        Assert.Null(version.ValidTo);
        Assert.Equal(OrgOperationType.Create, version.OperationType);
    }

    [Fact]
    public void OperationType_enum_covers_all_operations()
    {
        Assert.Equal(0, (int)OrgOperationType.Create);
        Assert.Equal(1, (int)OrgOperationType.Rename);
        Assert.Equal(2, (int)OrgOperationType.Move);
        Assert.Equal(3, (int)OrgOperationType.Merge);
        Assert.Equal(4, (int)OrgOperationType.Split);
        Assert.Equal(5, (int)OrgOperationType.Close);
    }

    [Fact]
    public void Future_effective_version_has_future_ValidFrom()
    {
        var futureDate = DateTime.UtcNow.AddMonths(3);
        var version = new OrgUnitVersion
        {
            OrgUnitId = Guid.NewGuid(),
            ValidFrom = futureDate,
            Name = "مديرية جديدة",
            OperationType = OrgOperationType.Create,
            DecisionReference = "DEC-2026-1234",
            TenantId = Guid.NewGuid()
        };

        Assert.True(version.ValidFrom > DateTime.UtcNow);
        Assert.Equal("DEC-2026-1234", version.DecisionReference);
    }

    [Fact]
    public void OrgUnitVersion_has_HeadEmployeeId()
    {
        var version = new OrgUnitVersion { HeadEmployeeId = Guid.NewGuid() };
        Assert.NotNull(version.HeadEmployeeId);
    }

    [Fact]
    public void Rename_closes_prior_version_and_opens_new_unit_test()
    {
        var orgUnitId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var original = new OrgUnitVersion
        {
            OrgUnitId = orgUnitId,
            ValidFrom = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidTo = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Name = "الاسم القديم",
            OperationType = OrgOperationType.Create,
            TenantId = tenantId
        };

        var renamed = new OrgUnitVersion
        {
            OrgUnitId = orgUnitId,
            ValidFrom = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Name = "الاسم الجديد",
            ParentId = original.ParentId,
            OperationType = OrgOperationType.Rename,
            TenantId = tenantId
        };

        Assert.Equal(orgUnitId, renamed.OrgUnitId);
        Assert.Equal(OrgOperationType.Rename, renamed.OperationType);
        Assert.NotEqual(original.Name, renamed.Name);
        Assert.Equal(original.ValidTo, renamed.ValidFrom);
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "org001-validate.ps1");
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

    [Fact]
    public void Docs_and_migration_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ORG_001_DATED_STRUCTURE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Infrastructure", "Migrations", "20260731170000_ORG001_DatedOrgStructure.cs")));
    }
}
