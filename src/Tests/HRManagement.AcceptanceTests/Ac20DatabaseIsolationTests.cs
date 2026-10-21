using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-20: PostgreSQL service roles deny cross-DB CONNECT.</summary>
public class Ac20DatabaseIsolationTests
{
    [SkippableFact]
    public async Task Verify_postgres_service_roles_isolation()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("platform-admin");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-20", correlationId);
        recorder.WriteMetadata("AC-20 database isolation verify script", persona.Id);

        var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync("acceptance-verify-db-isolation.ps1");
        recorder.WriteDbChecks(new { exitCode, output });

        var evidenceRoot = recorder.Folder;
        var logPath = Path.Combine(evidenceRoot, "verify-roles.log");
        if (File.Exists(logPath))
        {
            var log = await File.ReadAllTextAsync(logPath);
            recorder.CaptureHttp("verify-roles-log", "SCRIPT", "verify-postgres-service-roles.sh", exitCode == 0 ? 200 : 500,
                new { log });
            Assert.Contains("All cross-DB CONNECT checks passed", log, StringComparison.Ordinal);
        }

        var crossDbPath = Path.Combine(evidenceRoot, "cross-db-attempt.json");
        if (File.Exists(crossDbPath))
        {
            var crossDbJson = await File.ReadAllTextAsync(crossDbPath);
            using var doc = JsonDocument.Parse(crossDbJson);
            Assert.NotEqual(0, doc.RootElement.GetProperty("actualExitCode").GetInt32());
            recorder.WriteAfter(JsonSerializer.Deserialize<object>(crossDbJson)!);
        }

        Assert.Equal(0, exitCode);
        recorder.WriteResult("Pass");
    }
}
