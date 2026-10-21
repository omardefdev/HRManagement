using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-28: live clean/EICAR scan path with script evidence.</summary>
public class Ac28FileScanTests
{
    [SkippableFact]
    public async Task Live_file_scan_script_produces_evidence()
    {
        AcceptanceEnv.RequireEnabled();
        var central = AcceptanceHarness.RequirePersona("a-file-custodian");
        var (accessToken, correlationId) = await AcceptanceHarness.LoginAsync(central);
        var recorder = new EvidenceRecorder("AC-28", correlationId);
        recorder.WriteMetadata("AC-28 file scan E2E (FILE-002)", central.Id);

        Environment.SetEnvironmentVariable("BASE_URL", E2eEnv.BaseUrl.TrimEnd('/'));
        Environment.SetEnvironmentVariable("ACCESS_TOKEN", accessToken);
        var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync("acceptance-file-scan.ps1");
        recorder.WriteDbChecks(new { scriptExitCode = exitCode, scriptOutput = output });

        Assert.True(exitCode == 0, output);
        recorder.WriteAfter(new { scriptExitCode = exitCode });
        recorder.WriteResult("Pass");
    }
}
