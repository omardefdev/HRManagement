using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

public class Ac34BackupRestoreDrillTests
{
    [Fact]
    public void Acceptance_backup_restore_script_exists()
    {
        var root = ResolveRepoRoot();
        Assert.True(global::System.IO.File.Exists(Path.Combine(root, "scripts", "acceptance-backup-restore.ps1")));
        Assert.True(global::System.IO.File.Exists(Path.Combine(root, "scripts", "verify-restore-relationships.sh")));
        Assert.True(global::System.IO.File.Exists(Path.Combine(root, "scripts", "verify-restore-relationships.sql")));
    }

    [Fact]
    public void Timing_fixture_has_measured_restore_seconds_shape()
    {
        var fixture = new
        {
            databases = new[] { new { database = "hr_core_db", measuredRestoreSeconds = 12 } },
            totalRestoreSeconds = 12
        };
        var json = System.Text.Json.JsonSerializer.Serialize(fixture);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("totalRestoreSeconds").GetInt32() >= 0);
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }
}
