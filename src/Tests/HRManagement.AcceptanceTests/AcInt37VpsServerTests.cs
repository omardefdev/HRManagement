using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-37 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>, as AcOps36VpsBootTests): the mandatory wiring smoke,
/// the legal-pack fingerprint in the three reading services, and the realm in step with the wiring matrix. Evidence goes
/// to acceptance-evidence/AC-INT-37/vps/. Scenarios that need acceptance-only personas (37a, 37c) are not run on the
/// server: the VpsDemo realm has the demo users only; their S2S paths are covered by the smoke's token and call checks.
/// Nothing secret is read: the smoke and the sync script print client ids and statuses, never a value.
/// </summary>
[Collection(AcInt37Collection.Name)]
public class AcInt37VpsServerTests
{
    private static string? SshHost => Environment.GetEnvironmentVariable("VPS_SSH_HOST") is { Length: > 0 } h ? h : null;
    private static string Root => AcceptanceHarness.RepoRoot;

    private static EvidenceRecorder Start(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"int37-{key}-{Guid.NewGuid():N}", Path.Combine(Root, "acceptance-evidence", "AC-INT-37")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, "no persona: smoke, logs and Keycloak over ssh (no secret read)");
        return recorder;
    }

    private static (int Exit, string Output) Run(string file, string arguments, int timeoutMs = 900_000)
    {
        var psi = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(timeoutMs), $"{file} {arguments} timed out");
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    private static (int Exit, string Output) Ssh(string command) => Run("ssh", $"-o BatchMode=yes {SshHost} \"{command.Replace("\"", "\\\"")}\"");

    [SkippableFact]
    public void Server_smoke_with_mandatory_wiring_is_green()
    {
        Skip.If(SshHost is null, "VPS_SSH_HOST is not set (server proof only).");
        var recorder = Start("vps-smoke", "Demo server: mandatory wiring smoke (URLs, identities, tokens, S2S call, legal pack)");
        var outFile = Path.Combine(Root, "artifacts", "vps", $"smoke-int037-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.json");
        var (exit, output) = Run(OperatingSystem.IsWindows() ? "powershell" : "pwsh",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(Root, "scripts", "vps", "smoke.ps1")}\" -SshHost {SshHost} -RequireIntegrationWiring -OutFile \"{outFile}\"");
        Assert.True(exit == 0, output);

        using var doc = JsonDocument.Parse(File.ReadAllText(outFile));
        var checks = doc.RootElement.GetProperty("checks").EnumerateArray()
            .Select(c => (Id: c.GetProperty("id").GetString()!, Status: c.GetProperty("status").GetString()!, Detail: c.GetProperty("detail").GetString()!))
            .ToList();
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("fail").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("deferred").GetInt32());
        var wiring = checks.Where(c => c.Id.StartsWith("wiring", StringComparison.Ordinal) || c.Id.StartsWith("legal-pack", StringComparison.Ordinal)).ToList();
        Assert.All(wiring, c => Assert.True(c.Status == "Pass", $"{c.Id}: {c.Status} {c.Detail}"));
        Assert.Equal(14, wiring.Count(c => c.Id.StartsWith("wiring-env ", StringComparison.Ordinal)));
        Assert.Equal(13, wiring.Count(c => c.Id.StartsWith("wiring-token ", StringComparison.Ordinal)));
        Assert.Equal(3, wiring.Count(c => c.Id.StartsWith("legal-pack ", StringComparison.Ordinal)));
        Assert.Contains(wiring, c => c.Id == "wiring-s2s-candidate-upload");

        recorder.WriteScenarioSteps("1. scripts/vps/smoke.ps1 -SshHost hrm-vps -RequireIntegrationWiring (the wiring checks are mandatory).\n" +
            "2. fail=0, deferred=0; every wiring-env (14), wiring-token (13), legal-pack (3) and the S2S candidate-upload call Pass.\n" +
            "3. Not run on the server: 37a and 37c (they need acceptance personas; the VpsDemo realm has the demo users only).");
        recorder.WriteScenarioDbChecks(new
        {
            summary = JsonSerializer.Deserialize<object>(doc.RootElement.GetProperty("summary").GetRawText()),
            wiring = wiring.Select(c => new { c.Id, c.Status, c.Detail }),
            notRun = new[] { "37a (server): acceptance persona a-contracts-maker absent in the VpsDemo realm", "37c (server): acceptance personas a-idcard / a-it / a-compliance / a-logistics-maker absent" }
        });
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public void Server_services_log_the_repository_legal_pack_fingerprint()
    {
        Skip.If(SshHost is null, "VPS_SSH_HOST is not set (server proof only).");
        var recorder = Start("vps-37e", "Demo server: LegalPack loaded ... sha256 in hr-core-web, hr-contracts-web, hr-movements-web");
        // The committed blob is the reference (.gitattributes keeps docs/syr/*.json LF in every checkout).
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "docs", "syr", "service-regimes.json")))).ToLowerInvariant();
        var lines = new Dictionary<string, string>();
        foreach (var service in new[] { "hr-core-web", "hr-contracts-web", "hr-movements-web" })
        {
            var (exit, logs) = Ssh($"docker logs $(docker ps -q --filter label=com.docker.compose.project=hrm --filter label=com.docker.compose.service={service}) 2>&1 | grep 'LegalPack loaded' | tail -1");
            Assert.True(exit == 0, logs);
            var m = Regex.Match(logs, @"LegalPack loaded pack=syr003-service-regimes-v1 source=\w+ sha256=([0-9a-f]{64})[^\r\n]*");
            Assert.True(m.Success, $"{service}: no start-up line");
            Assert.Equal(sha, m.Groups[1].Value);
            lines[service] = m.Value;
        }

        recorder.WriteScenarioSteps("1. docker logs of the three services on the server: the 'LegalPack loaded' start-up line.\n2. Its sha256 equals the repository file's.");
        recorder.WriteScenarioDbChecks(new { sha256 = sha, lines });
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public void Server_realm_has_every_matrix_client_and_the_sync_is_idempotent()
    {
        Skip.If(SshHost is null, "VPS_SSH_HOST is not set (server proof only).");
        var recorder = Start("vps-keycloak", "Demo server: realm in step with deploy/wiring/service-wiring.json, sync idempotent");
        var (checkExit, check) = Ssh("cd /opt/hrm/current && bash deploy/vps/keycloak-sync-clients.sh --check | tail -1");
        Assert.True(checkExit == 0 && check.Contains("changes=0", StringComparison.Ordinal), check);

        var (exit, clients) = Ssh("kc=$(docker ps -q --filter label=com.docker.compose.project=hrm --filter label=com.docker.compose.service=keycloak); " +
            "docker exec $kc /opt/keycloak/bin/kcadm.sh get clients -r hrmanagement --fields clientId,enabled --format csv --noquotes");
        Assert.True(exit == 0, clients);
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "deploy", "wiring", "service-wiring.json")));
        var expected = matrix.RootElement.GetProperty("services").EnumerateArray()
            .Where(s => s.GetProperty("serviceIdentity").GetBoolean())
            .Select(s => s.GetProperty("clientId").GetString()!)
            .ToList();
        var enabled = clients.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(','))
            .Where(p => p.Length == 2 && p[1] == "true")
            .Select(p => p[0])
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(expected, c => Assert.True(enabled.Contains(c), $"client {c} missing or disabled in the server realm"));

        recorder.WriteScenarioSteps("1. keycloak-sync-clients.sh --check on the server: changes=0 (a second run changes nothing).\n" +
            "2. kcadm get clients: every matrix service client exists and is enabled.");
        recorder.WriteScenarioDbChecks(new { check = check.Trim(), clients = expected });
        recorder.WriteResult("Pass");
    }
}
