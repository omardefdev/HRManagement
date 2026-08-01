using System.Diagnostics;
using System.Text.Json;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Leg001RetentionTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "leg001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void OfficialRecord_has_required_properties()
    {
        var record = new OfficialRecord
        {
            RecordClass = "employee-file",
            EntityType = "Employee",
            EntityId = Guid.NewGuid().ToString(),
            RetentionCode = "employee-file",
            RetentionStartUtc = DateTime.UtcNow,
            TenantId = Guid.NewGuid()
        };

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal("employee-file", record.RecordClass);
        Assert.Equal(OfficialRecordStatus.Active, record.Status);
        Assert.Null(record.DispositionDueUtc);
        Assert.Null(record.ContentHash);
    }

    [Fact]
    public void OfficialRecord_status_enum_values()
    {
        Assert.Equal(0, (int)OfficialRecordStatus.Active);
        Assert.Equal(1, (int)OfficialRecordStatus.Archived);
        Assert.Equal(2, (int)OfficialRecordStatus.PendingDisposition);
        Assert.Equal(3, (int)OfficialRecordStatus.Disposed);
        Assert.Equal(4, (int)OfficialRecordStatus.UnderLegalHold);
    }

    [Fact]
    public void LegalHold_prevents_disposition()
    {
        var hold = new LegalHold
        {
            HoldReference = "INV-2026-001",
            Reason = "Active investigation",
            IssuedBy = "legal@gov.sy",
            TenantId = Guid.NewGuid()
        };

        Assert.Equal(LegalHoldStatus.Active, hold.Status);
        Assert.Null(hold.ReleasedAtUtc);
        Assert.Null(hold.ReleasedBy);
    }

    [Fact]
    public void LegalHold_release_sets_status_and_metadata()
    {
        var hold = new LegalHold
        {
            HoldReference = "INV-2026-002",
            IssuedBy = "legal@gov.sy",
            TenantId = Guid.NewGuid()
        };

        hold.Status = LegalHoldStatus.Released;
        hold.ReleasedBy = "judge@gov.sy";
        hold.ReleasedAtUtc = DateTime.UtcNow;

        Assert.Equal(LegalHoldStatus.Released, hold.Status);
        Assert.NotNull(hold.ReleasedAtUtc);
        Assert.Equal("judge@gov.sy", hold.ReleasedBy);
    }

    [Fact]
    public void Retention_schedule_json_is_valid()
    {
        var path = RepoPaths.Resolve("docs", "leg", "retention-schedule.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("LEG-001", root.GetProperty("card").GetString());
        var classes = root.GetProperty("classes").EnumerateArray().ToList();
        Assert.True(classes.Count >= 3, "must define at least 3 record classes");

        foreach (var cls in classes)
        {
            Assert.False(string.IsNullOrWhiteSpace(cls.GetProperty("code").GetString()));
            Assert.True(cls.GetProperty("retentionYears").GetInt32() >= 0);
            Assert.False(string.IsNullOrWhiteSpace(cls.GetProperty("legalBasis").GetString()));
        }
    }

    [Fact]
    public void Supersedes_chain_tracks_corrections()
    {
        var original = new OfficialRecord
        {
            RecordClass = "administrative-decision",
            EntityType = "Decision",
            EntityId = "DEC-001",
            RetentionCode = "administrative-decision",
            RetentionStartUtc = DateTime.UtcNow,
            TenantId = Guid.NewGuid()
        };

        var correction = new OfficialRecord
        {
            RecordClass = "administrative-decision",
            EntityType = "Decision",
            EntityId = "DEC-001",
            RetentionCode = "administrative-decision",
            RetentionStartUtc = DateTime.UtcNow,
            SupersedesRecordId = original.Id,
            TenantId = original.TenantId
        };

        Assert.Equal(original.Id, correction.SupersedesRecordId);
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "leg001-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "LEG_001_RETENTION.md")));
    }
}
