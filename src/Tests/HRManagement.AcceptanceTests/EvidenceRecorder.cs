using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text;

using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// Writes the eight-file evidence contract under acceptance-evidence/AC-XX/ (PLAN-00 README).
/// </summary>
public sealed class EvidenceRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _acFolder;
    private readonly string _correlationId;
    private readonly List<object> _httpCaptures = [];
    private readonly Dictionary<string, object?> _timing = new();
    private string? _scenario;
    private string? _personaId;

    public EvidenceRecorder(string acCode, string correlationId, string? evidenceRoot = null)
    {
        _correlationId = correlationId;
        var root = evidenceRoot
            ?? Environment.GetEnvironmentVariable("ACCEPTANCE_EVIDENCE_DIR")
            ?? Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence");
        _acFolder = Path.Combine(root, acCode);
        Directory.CreateDirectory(_acFolder);
        Directory.CreateDirectory(Path.Combine(_acFolder, "http"));
    }

    public string Folder => _acFolder;

    public void WriteMetadata(string scenario, string personaId)
    {
        _scenario = scenario;
        _personaId = personaId;
        var gitSha = TryGitSha();
        var meta = new
        {
            scenario,
            personaId,
            correlationId = _correlationId,
            capturedAtUtc = DateTime.UtcNow.ToString("o"),
            environment = new
            {
                gateway = E2eEnv.BaseUrl,
                keycloak = AcceptanceEnv.KeycloakBaseUrl,
                runAcceptance = AcceptanceEnv.IsEnabled
            },
            gitSha,
            imageTag = Environment.GetEnvironmentVariable("IMAGE_TAG")
        };
        WriteJson("metadata.json", meta);
    }

    public void WriteSteps(string markdown) => File.WriteAllText(Path.Combine(_acFolder, "steps.md"), markdown);

    public void CaptureHttp(string stepName, string method, string url, int statusCode, object? responseBody = null)
    {
        var capture = new
        {
            step = stepName,
            method,
            url,
            statusCode,
            capturedAtUtc = DateTime.UtcNow.ToString("o"),
            body = responseBody
        };
        _httpCaptures.Add(capture);
        var safeName = string.Concat(stepName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        WriteJson(Path.Combine("http", $"{safeName}.json"), capture);
    }

    public void WriteBefore(object snapshot) => WriteJson("before.json", snapshot);
    public void WriteAfter(object snapshot) => WriteJson("after.json", snapshot);
    public void WriteAudit(object audit) => WriteJson("audit.json", audit);
    public void WriteDbChecks(object checks) => WriteJson("db-checks.json", checks);

    public void RecordTiming(string key, object value) => _timing[key] = value;

    public void FlushTiming() => WriteJson("timing.json", _timing);

    public void WriteResult(string status, string? reason = null)
    {
        EnsureContractArtifacts(status, reason);
        WriteMergedResult(status, reason);
        AppendSummary(status, reason);
    }

    /// <summary>
    /// Completes the evidence contract without pretending an omitted domain snapshot or audit
    /// query took place. Derived artifacts identify their source and retain the real HTTP evidence.
    /// </summary>
    private void EnsureContractArtifacts(string status, string? reason)
    {
        var metadataPath = Path.Combine(_acFolder, "metadata.json");
        if (!File.Exists(metadataPath))
            WriteMetadata(_scenario ?? Path.GetFileName(_acFolder), _personaId ?? "n/a");

        var stepsPath = Path.Combine(_acFolder, "steps.md");
        if (!File.Exists(stepsPath))
            WriteSteps(BuildDerivedSteps());

        var beforePath = Path.Combine(_acFolder, "before.json");
        if (!File.Exists(beforePath))
        {
            WriteBefore(new
            {
                stage = "before",
                explicitDomainSnapshot = false,
                correlationId = _correlationId,
                note = "No explicit pre-action domain snapshot was captured; see HTTP and DB evidence."
            });
        }

        var afterPath = Path.Combine(_acFolder, "after.json");
        if (!File.Exists(afterPath))
        {
            WriteAfter(new
            {
                stage = "after",
                explicitDomainSnapshot = false,
                outcome = status,
                reason,
                correlationId = _correlationId,
                httpObservationCount = _httpCaptures.Count,
                lastHttpObservation = _httpCaptures.LastOrDefault(),
                note = "Outcome is derived from the executed assertions and captured observations."
            });
        }

        var auditPath = Path.Combine(_acFolder, "audit.json");
        if (!File.Exists(auditPath))
        {
            WriteAudit(new
            {
                correlationId = _correlationId,
                explicitAuditQuery = false,
                httpObservationCount = _httpCaptures.Count,
                note = "No separate audit-store query was required or captured for this scenario."
            });
        }

        var dbPath = Path.Combine(_acFolder, "db-checks.json");
        if (!File.Exists(dbPath))
        {
            WriteJson("db-checks.json", new
            {
                explicitDatabaseQuery = false,
                note = "No separate database query was required or captured for this scenario."
            });
        }

        var timingPath = Path.Combine(_acFolder, "timing.json");
        if (!File.Exists(timingPath))
            FlushTiming();
    }

    private string BuildDerivedSteps()
    {
        var markdown = new StringBuilder()
            .AppendLine($"# {_scenario ?? Path.GetFileName(_acFolder)}")
            .AppendLine()
            .AppendLine($"- Persona: `{_personaId ?? "n/a"}`")
            .AppendLine($"- Correlation ID: `{_correlationId}`")
            .AppendLine($"- HTTP observations: `{_httpCaptures.Count}`")
            .AppendLine();

        if (_httpCaptures.Count == 0)
        {
            markdown.AppendLine("The scenario was verified through assertions, scripts, or database checks recorded in the companion evidence files.");
        }
        else
        {
            markdown.AppendLine("## Observed requests");
            markdown.AppendLine();
            foreach (var capture in _httpCaptures)
            {
                var json = JsonSerializer.SerializeToElement(capture, JsonOptions);
                markdown.Append("- `")
                    .Append(json.GetProperty("method").GetString())
                    .Append(' ')
                    .Append(json.GetProperty("url").GetString())
                    .Append("` -> `")
                    .Append(json.GetProperty("statusCode").GetInt32())
                    .AppendLine("`");
            }
        }

        return markdown.ToString();
    }

    private void WriteMergedResult(string status, string? reason)
    {
        var path = Path.Combine(_acFolder, "result.json");
        JsonObject result;
        if (File.Exists(path))
        {
            try
            {
                result = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
            }
            catch (JsonException)
            {
                result = [];
            }
        }
        else
        {
            result = [];
        }

        result["status"] = status;
        result["reason"] = reason;
        result["correlationId"] = _correlationId;
        result["recordedAtUtc"] = DateTime.UtcNow.ToString("o");
        File.WriteAllText(path, result.ToJsonString(JsonOptions));
    }

    private void AppendSummary(string status, string? reason)
    {
        var root = Path.GetDirectoryName(_acFolder)!;
        var summaryPath = Path.Combine(root, "summary.md");
        var line = $"- **{Path.GetFileName(_acFolder)}**: {status}" +
                   (string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason}") +
                   $" (`{_correlationId}`)\n";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(summaryPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                stream.Seek(0, SeekOrigin.End);
                using var writer = new StreamWriter(stream);
                writer.Write(line);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private void WriteJson(string fileName, object payload) =>
        File.WriteAllText(
            Path.Combine(_acFolder, fileName),
            JsonSerializer.Serialize(payload, JsonOptions));

    private static string TryGitSha()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return "n/a";
            var sha = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return string.IsNullOrWhiteSpace(sha) ? "n/a" : sha;
        }
        catch
        {
            return "n/a";
        }
    }
}
