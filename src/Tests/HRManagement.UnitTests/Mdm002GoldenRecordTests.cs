using System.Diagnostics;
using System.Text.Json;
using HRManagement.SharedKernel.MasterData;
using HRManagement.ServiceDefaults.MasterData;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Mdm002GoldenRecordTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "mdm002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void GoldenPersonRecord_has_required_properties()
    {
        var record = new GoldenPersonRecord
        {
            PersonId = Guid.NewGuid(),
            NationalIdHash = "abc123",
            Status = GoldenPersonStatus.Active,
            TenantId = Guid.NewGuid()
        };

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.NotEqual(Guid.Empty, record.PersonId);
        Assert.Equal("abc123", record.NationalIdHash);
        Assert.Equal(GoldenPersonStatus.Active, record.Status);
        Assert.Null(record.SurvivorPersonId);
        Assert.Null(record.MergeHistory);
        Assert.Null(record.ProvenanceJson);
    }

    [Fact]
    public void Status_enum_has_Active_Merged_Deceased()
    {
        Assert.Equal(0, (int)GoldenPersonStatus.Active);
        Assert.Equal(1, (int)GoldenPersonStatus.Merged);
        Assert.Equal(2, (int)GoldenPersonStatus.Deceased);
    }

    [Fact]
    public void Merge_marks_duplicate_as_merged_and_records_history()
    {
        var service = new PersonMergeService();
        var survivor = new GoldenPersonRecord
        {
            PersonId = Guid.NewGuid(),
            NationalIdHash = "hash-survivor",
            TenantId = Guid.NewGuid()
        };
        var duplicate = new GoldenPersonRecord
        {
            PersonId = Guid.NewGuid(),
            NationalIdHash = "hash-duplicate",
            TenantId = survivor.TenantId
        };

        var result = service.Merge(survivor, duplicate, "admin@gov.sy");

        Assert.True(result.Success);
        Assert.Equal(GoldenPersonStatus.Merged, duplicate.Status);
        Assert.Equal(survivor.PersonId, duplicate.SurvivorPersonId);
        Assert.NotNull(survivor.MergeHistory);
        Assert.NotNull(duplicate.MergeHistory);
        Assert.Contains("merge", survivor.MergeHistory);
    }

    [Fact]
    public void Merge_self_returns_failure()
    {
        var service = new PersonMergeService();
        var record = new GoldenPersonRecord
        {
            PersonId = Guid.NewGuid(),
            NationalIdHash = "hash",
            TenantId = Guid.NewGuid()
        };

        var result = service.Merge(record, record, "admin@gov.sy");

        Assert.False(result.Success);
        Assert.Contains("itself", result.Error!);
    }

    [Fact]
    public void Merge_already_merged_returns_failure()
    {
        var service = new PersonMergeService();
        var survivor = new GoldenPersonRecord { PersonId = Guid.NewGuid(), NationalIdHash = "h1", TenantId = Guid.NewGuid() };
        var dup = new GoldenPersonRecord { PersonId = Guid.NewGuid(), NationalIdHash = "h2", TenantId = survivor.TenantId, Status = GoldenPersonStatus.Merged };

        var result = service.Merge(survivor, dup, "admin@gov.sy");

        Assert.False(result.Success);
        Assert.Contains("already merged", result.Error!);
    }

    [Fact]
    public void Unmerge_restores_and_records_history()
    {
        var service = new PersonMergeService();
        var survivor = new GoldenPersonRecord { PersonId = Guid.NewGuid(), NationalIdHash = "h1", TenantId = Guid.NewGuid() };
        var duplicate = new GoldenPersonRecord { PersonId = Guid.NewGuid(), NationalIdHash = "h2", TenantId = survivor.TenantId };

        service.Merge(survivor, duplicate, "admin@gov.sy");
        var unmergeResult = service.Unmerge(survivor, duplicate.PersonId, "reviewer@gov.sy");

        Assert.True(unmergeResult.Success);
        Assert.Contains("unmerge", survivor.MergeHistory!);
    }

    [Fact]
    public void Unmerge_without_prior_merge_returns_failure()
    {
        var service = new PersonMergeService();
        var record = new GoldenPersonRecord { PersonId = Guid.NewGuid(), NationalIdHash = "h1", TenantId = Guid.NewGuid() };

        var result = service.Unmerge(record, Guid.NewGuid(), "admin@gov.sy");

        Assert.False(result.Success);
        Assert.Contains("No merge record", result.Error!);
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mdm002-validate.ps1");
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
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MDM_002_GOLDEN_RECORD.md")));
    }
}
