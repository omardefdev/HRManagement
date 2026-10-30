using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HRManagement.E2E;

/// <summary>
/// PLAN-33 (AC-HCM-33c): what the requisition-to-hire journey needs from the acceptance stack. The E2E project cannot
/// reference the acceptance test project (it references this one), so these are small, self-contained equivalents of
/// its helpers: acceptance personas and password-grant tokens, gateway UI clients with antiforgery and manual cookies,
/// <c>docker exec psql</c> checks, the FileDrop OTP reader and the eight-item evidence writer.
/// </summary>
public static class AcceptanceStack
{
    public static string KeycloakBaseUrl =>
        (Environment.GetEnvironmentVariable("KEYCLOAK_BASE_URL") ?? "http://localhost:6388").TrimEnd('/');

    public static string ComposeProject => Environment.GetEnvironmentVariable("COMPOSE_PROJECT_NAME") ?? "hrm-acceptance";

    private static readonly Lazy<IReadOnlyList<E2ePersona>> Personas = new(() =>
    {
        var path = Path.Combine(E2eSeed.RepoRoot, "testdata", "acceptance", "personas.json");
        Assert.True(File.Exists(path), $"Missing acceptance personas at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("personas")
            .Deserialize<List<E2ePersona>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    });

    public static E2ePersona Persona(string id)
    {
        var persona = Personas.Value.FirstOrDefault(p => p.Id == id && p.Enabled);
        Assert.True(persona is not null, $"Acceptance persona {id} is missing (testdata/acceptance/personas.json).");
        return persona!;
    }

    /// <summary>Persona password: its env variable, else the git-ignored <c>.acceptance/credentials.json</c>.</summary>
    public static string Password(E2ePersona persona)
    {
        if (!string.IsNullOrWhiteSpace(persona.PasswordEnv) && Environment.GetEnvironmentVariable(persona.PasswordEnv) is { Length: > 0 } fromEnv)
            return fromEnv;
        var path = Path.Combine(E2eSeed.RepoRoot, ".acceptance", "credentials.json");
        Assert.True(File.Exists(path), $"No password for {persona.Id}: set {persona.PasswordEnv} or run scripts/prepare-keycloak-import.ps1 -Profile Acceptance.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("passwords").TryGetProperty(persona.Username, out var pw), $"No password for {persona.Username}.");
        return pw.GetString()!;
    }

    public static async Task<string> TokenAsync(E2ePersona persona)
    {
        var secret = Environment.GetEnvironmentVariable("KEYCLOAK_HR_GATEWAY_CLIENT_SECRET");
        Assert.False(string.IsNullOrWhiteSpace(secret), "Set KEYCLOAK_HR_GATEWAY_CLIENT_SECRET (from .env) for the acceptance realm.");
        using var http = new HttpClient();
        using var resp = await http.PostAsync($"{KeycloakBaseUrl}/realms/hrmanagement/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "hr-gateway-client",
                ["client_secret"] = secret!,
                ["username"] = persona.Username,
                ["password"] = Password(persona),
                ["scope"] = "openid"
            }));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Token for {persona.Id} failed: {(int)resp.StatusCode}.");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    public static HttpClient NewClient(string correlationId, string? bearer = null)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(E2eEnv.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(2)
        };
        if (bearer is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        return http;
    }

    public static async Task<JourneyUi> SignInAsync(string personaId, string correlationId, JourneyEvidence evidence) =>
        new(NewClient(correlationId, await TokenAsync(Persona(personaId))), evidence, personaId);

    public static JourneyUi Anonymous(string correlationId, JourneyEvidence evidence, string label) =>
        new(NewClient(correlationId), evidence, label);

    // ── docker exec psql (acceptance PostgreSQL) ────────────────────────────────────────────────

    public static async Task<string> SqlAsync(string database, string sql)
    {
        var container = (await RunAsync("docker", "ps", "--filter", $"label=com.docker.compose.project={ComposeProject}",
            "--filter", "label=com.docker.compose.service=postgresql", "--format", "{{.Names}}")).Output.Trim();
        Assert.False(string.IsNullOrWhiteSpace(container), $"No PostgreSQL container for compose project {ComposeProject}.");
        var (exit, output) = await RunAsync("docker", "exec", container.Split('\n')[0].Trim(), "psql", "-X", "-v", "ON_ERROR_STOP=1",
            "-U", "hrm_ci", "-d", database, "-t", "-A", "-c", sql);
        Assert.True(exit == 0, $"psql on {database} failed: {output}");
        return output.Trim();
    }

    public static Task<string> CoreSql(string sql) => SqlAsync("hr_core_db", sql);
    public static Task<string> ContractsSql(string sql) => SqlAsync("hr_contracts_db", sql);

    public static async Task<(int Exit, string Output)> RunAsync(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, (await stdout) + (await stderr));
    }

    /// <summary>The value of an environment variable inside a running service container of the acceptance project.</summary>
    public static async Task<string?> ContainerEnvAsync(string service, string name)
    {
        var container = (await RunAsync("docker", "ps", "--filter", $"label=com.docker.compose.project={ComposeProject}",
            "--filter", $"label=com.docker.compose.service={service}", "--format", "{{.Names}}")).Output.Trim().Split('\n')[0].Trim();
        if (container.Length == 0) return null;
        var (_, env) = await RunAsync("docker", "inspect", "--format", "{{range .Config.Env}}{{println .}}{{end}}", container);
        return env.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
    }

    public static async Task<string> ContainerLogsAsync(string service, TimeSpan since)
    {
        var container = (await RunAsync("docker", "ps", "--filter", $"label=com.docker.compose.project={ComposeProject}",
            "--filter", $"label=com.docker.compose.service={service}", "--format", "{{.Names}}")).Output.Trim().Split('\n')[0].Trim();
        return container.Length == 0 ? "" : (await RunAsync("docker", "logs", "--since", $"{(int)since.TotalSeconds}s", container)).Output;
    }

    // ── Candidate notifications (FileDrop) ─────────────────────────────────────────────────────

    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();

    /// <summary>The six-digit code of an OTP challenge, read from the FileDrop message (key <c>otp:{challengeId}:Email</c>).</summary>
    public static async Task<string> ReadOtpAsync(string challengeId, TimeSpan timeout)
    {
        var path = Path.Combine(E2eSeed.RepoRoot, "artifacts", "acceptance", "candidate-notifications", Sha256Hex($"otp:{challengeId}:Email") + ".json");
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (File.Exists(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, Encoding.UTF8));
                    Assert.Equal("OTP", doc.RootElement.GetProperty("template").GetString());
                    var match = Regex.Match(doc.RootElement.GetProperty("body").GetString()!, @"(?<!\d)(\d{6})(?!\d)");
                    Assert.True(match.Success, "No six-digit code in the OTP message.");
                    return match.Groups[1].Value;
                }
                catch (IOException)
                {
                    // being moved into place
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.Fail($"No FileDrop OTP message within {timeout.TotalSeconds:0}s ({path}).");
        return null!;
    }

    public static async Task<string> WaitForAsync(Func<Task<string>> query, Func<string, bool> done, TimeSpan timeout, string what)
    {
        var started = DateTime.UtcNow;
        var last = "";
        while (DateTime.UtcNow - started < timeout)
        {
            last = await query();
            if (done(last)) return last;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        Assert.Fail($"{what}: not reached within {timeout.TotalSeconds:0}s (last: '{last}').");
        return last;
    }
}

public sealed record JourneyResponse(HttpStatusCode Status, string? FinalPath, string Body, string? CorrelationHeader);

/// <summary>A gateway client of one persona (or anonymous visitor): manual cookie jar, antiforgery form posts.</summary>
public sealed class JourneyUi(HttpClient http, JourneyEvidence evidence, string label)
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public bool HasCookie(string name) => _cookies.ContainsKey(name);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (_cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        var response = await http.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var header in setCookies)
            {
                var pair = header.Split(';', 2)[0];
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var name = pair[..eq].Trim();
                var value = pair[(eq + 1)..].Trim();
                if (string.IsNullOrEmpty(value) || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)) _cookies.Remove(name);
                else _cookies[name] = value;
            }
        }

        return response;
    }

    private async Task<HttpResponseMessage> FollowAsync(HttpResponseMessage resp)
    {
        for (var hops = 0; hops < 4 && (int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is not null; hops++)
        {
            var location = resp.Headers.Location;
            resp.Dispose();
            resp = await SendAsync(HttpMethod.Get, location.IsAbsoluteUri ? location.PathAndQuery.TrimStart('/') : location.OriginalString.TrimStart('/'));
        }

        return resp;
    }

    public async Task<JourneyResponse> GetAsync(string step, string path, bool requireOk = true)
    {
        var resp = await FollowAsync(await SendAsync(HttpMethod.Get, path));
        var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        evidence.Http($"{step}-{label}", "GET", "/" + path, (int)resp.StatusCode, Snippet(body));
        E2eHttp.EnsureNotServerError((int)resp.StatusCode, $"GET {path} as {label}");
        if (requireOk)
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} as {label} returned {(int)resp.StatusCode}.");
        return new(resp.StatusCode, resp.RequestMessage?.RequestUri?.AbsolutePath, body, Correlation(resp));
    }

    /// <summary>Antiforgery post: token from <paramref name="tokenPage"/> (or the given HTML); 429 from the public window is retried.</summary>
    public async Task<JourneyResponse> PostAsync(string step, string path, string tokenPage, List<KeyValuePair<string, string>> fields,
        IReadOnlyList<(string Field, string FileName, string ContentType, byte[] Bytes)>? files = null, string? tokenHtml = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            var html = tokenHtml;
            if (html is null)
            {
                var page = await FollowAsync(await SendAsync(HttpMethod.Get, tokenPage));
                html = await page.Content.ReadAsStringAsync();
                Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {tokenPage} as {label} returned {(int)page.StatusCode}.");
            }

            var token = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""", RegexOptions.IgnoreCase);
            Assert.True(token.Success, $"No antiforgery token on {tokenPage}.");
            var form = new List<KeyValuePair<string, string>>(fields) { new("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value)) };
            HttpContent content;
            if (files is { Count: > 0 })
            {
                var multipart = new MultipartFormDataContent();
                foreach (var f in form) multipart.Add(new StringContent(f.Value), f.Key);
                foreach (var file in files)
                {
                    var part = new ByteArrayContent(file.Bytes);
                    part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                    multipart.Add(part, file.Field, file.FileName);
                }

                content = multipart;
            }
            else
            {
                content = new FormUrlEncodedContent(form);
            }

            var posted = await SendAsync(HttpMethod.Post, path, content);
            var postStatus = (int)posted.StatusCode;
            var resp = await FollowAsync(posted);
            var body = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
            var masked = fields.Select(f => f.Key is "NationalId" or "Code" ? $"{f.Key}=***" : $"{f.Key}={f.Value}");
            evidence.Http($"{step}-{label}", "POST", "/" + path, (int)resp.StatusCode,
                new { fields = masked, files = files?.Select(f => f.FileName), postStatus, final = resp.RequestMessage?.RequestUri?.AbsolutePath });
            if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                continue;
            }

            E2eHttp.EnsureNotServerError((int)resp.StatusCode, $"POST {path} as {label}");
            Assert.True(resp.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"POST {path} as {label} was denied.");
            return new(resp.StatusCode, resp.RequestMessage?.RequestUri?.AbsolutePath, body, Correlation(resp));
        }
    }

    private static string? Correlation(HttpResponseMessage resp) =>
        resp.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.FirstOrDefault() : null;

    private static string Snippet(string body) => body.Length > 600 ? body[..600] : body;
}

/// <summary>
/// The eight-item evidence contract of <c>docs/acceptance-plans/README.md</c> for the journey (same layout as the
/// acceptance project's EvidenceRecorder: one folder, per-scenario <c>steps-</c>/<c>db-checks-</c> files, and a
/// <c>result.json</c> whose status is the worst of its scenarios).
/// </summary>
public sealed class JourneyEvidence
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _key;
    private readonly string _correlationId;
    private readonly Dictionary<string, object?> _timing = new();
    private readonly List<string> _steps = [];
    private int _http;

    public JourneyEvidence(string relativeFolder, string key, string correlationId, string scenario)
    {
        _key = key;
        _correlationId = correlationId;
        var root = Environment.GetEnvironmentVariable("ACCEPTANCE_EVIDENCE_DIR") ?? Path.Combine(E2eSeed.RepoRoot, "acceptance-evidence");
        Folder = Path.Combine(root, relativeFolder);
        Directory.CreateDirectory(Path.Combine(Folder, "http"));
        Write("metadata.json", new
        {
            scenario,
            correlationId,
            capturedAtUtc = DateTime.UtcNow.ToString("o"),
            environment = new { gateway = E2eEnv.BaseUrl, keycloak = AcceptanceStack.KeycloakBaseUrl, composeProject = AcceptanceStack.ComposeProject },
            gitSha = AcceptanceStack.RunAsync("git", "rev-parse", "HEAD").GetAwaiter().GetResult().Output.Trim()
        });
    }

    public string Folder { get; }
    public string Stage { get; private set; } = "start";

    public void Enter(string stage)
    {
        Stage = stage;
        _steps.Add($"- {DateTime.UtcNow:HH:mm:ss} {stage}");
        _timing[stage] = DateTime.UtcNow.ToString("o");
    }

    public void Note(string line) => _steps.Add($"  - {line}");

    public void Http(string step, string method, string url, int status, object? body)
    {
        var safe = string.Concat($"{_key}-{++_http:D3}-{step}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        Write(Path.Combine("http", safe + ".json"), new { step, method, url, statusCode = status, capturedAtUtc = DateTime.UtcNow.ToString("o"), body });
    }

    public void Before(object o) => Write("before.json", o);
    public void After(object o) => Write("after.json", o);
    public void Audit(object o) => Write("audit.json", o);
    public void DbChecks(object o)
    {
        Write($"db-checks-{_key}.json", o);
        Write("db-checks.json", new { lastScenario = _key, checks = o });
    }

    public void Result(string status, string? reason, string plan)
    {
        File.WriteAllText(Path.Combine(Folder, $"steps-{_key}.md"), plan + "\n\n## Executed\n\n" + string.Join('\n', _steps) + "\n");
        File.WriteAllText(Path.Combine(Folder, "steps.md"), string.Join("\n\n", Directory.GetFiles(Folder, "steps-*.md")
            .OrderBy(f => f, StringComparer.Ordinal).Select(f => $"## {Path.GetFileNameWithoutExtension(f)[6..]}\n\n{File.ReadAllText(f)}")));
        _timing["finishedAtUtc"] = DateTime.UtcNow.ToString("o");
        _timing["stageReached"] = Stage;
        Write("timing.json", _timing);
        foreach (var (name, placeholder) in new (string, object)[]
                 {
                     ("before.json", new { explicitDomainSnapshot = false, note = "No pre-action snapshot captured." }),
                     ("after.json", new { explicitDomainSnapshot = false, outcome = status, stageReached = Stage }),
                     ("audit.json", new { explicitAuditQuery = false, note = "No audit query captured." }),
                     ("db-checks.json", new { explicitDatabaseQuery = false })
                 })
        {
            if (!File.Exists(Path.Combine(Folder, name))) Write(name, placeholder);
        }

        var path = Path.Combine(Folder, "result.json");
        var result = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [] : [];
        var scenarios = result["scenarios"] as JsonObject ?? [];
        scenarios[_key] = new JsonObject
        {
            ["status"] = status,
            ["reason"] = reason,
            ["stageReached"] = Stage,
            ["correlationId"] = _correlationId,
            ["recordedAtUtc"] = DateTime.UtcNow.ToString("o")
        };
        result["scenarios"] = scenarios;
        var statuses = scenarios.Select(s => s.Value?["status"]?.GetValue<string>() ?? "Unknown").ToList();
        result["status"] = statuses.Contains("Fail") ? "Fail" : statuses.Contains("Blocked") ? "Blocked" : statuses.All(s => s == "Pass") ? "Pass" : "Unknown";
        var open = scenarios.Where(s => s.Value?["status"]?.GetValue<string>() != "Pass")
            .Select(s => $"{s.Key}: {s.Value?["status"]?.GetValue<string>()} — {s.Value?["reason"]?.GetValue<string>()}").ToList();
        result["reason"] = open.Count == 0 ? null : string.Join("; ", open);
        result["correlationId"] = _correlationId;
        result["recordedAtUtc"] = DateTime.UtcNow.ToString("o");
        File.WriteAllText(path, result.ToJsonString(Json));
    }

    private void Write(string name, object payload) => File.WriteAllText(Path.Combine(Folder, name), JsonSerializer.Serialize(payload, Json));
}
