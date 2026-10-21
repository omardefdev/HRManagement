using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-34: all acceptance databases are backed up and critical databases restore cleanly.</summary>
public class Ac34BackupRestoreLiveTests
{
    [SkippableFact]
    public async Task Backup_and_restore_drill_preserves_critical_data_and_relationships()
    {
        AcceptanceEnv.RequireEnabled();
        var recorder = new EvidenceRecorder("AC-34", Guid.NewGuid().ToString("D"));
        recorder.WriteMetadata("AC-34 backup restore drill", "ops");
        recorder.WriteSteps("""
            1. Create custom-format PostgreSQL dumps for all 16 acceptance databases.
            2. Compute and record a SHA-256 digest for every dump.
            3. Restore HR.Core, Payroll, Logistics, Gateway Audit, and File Storage into isolated databases.
            4. Verify critical tables and HR.Core references from payroll and custody data.
            5. Drop only the temporary drill databases and retain the backup evidence.
            """);

        var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-backup-restore.ps1",
            ("OutputDir", recorder.Folder));
        recorder.WriteDbChecks(new { scriptOutput = output });

        Assert.True(exitCode == 0, $"AC-34 backup/restore drill failed: {output}");
        var resultPath = Path.Combine(recorder.Folder, "drill-result.json");
        Assert.True(File.Exists(resultPath));
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
        var root = result.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(16, root.GetProperty("databaseCount").GetInt32());
        Assert.Equal(5, root.GetProperty("restoredCriticalCount").GetInt32());
        var relationships = root.GetProperty("relationshipChecks");
        Assert.True(relationships.GetProperty("pass").GetBoolean());
        Assert.Equal(0, relationships.GetProperty("payrollOrphanEmployeeReferences").GetInt32());
        Assert.Equal(0, relationships.GetProperty("custodyOrphanEmployeeReferences").GetInt32());

        recorder.WriteAfter(root.Clone());
        recorder.WriteResult("Pass");
    }
}
