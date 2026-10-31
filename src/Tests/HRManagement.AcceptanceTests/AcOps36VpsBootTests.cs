using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-OPS-36a–k (PLAN-36): the production profile on one host behind the Caddy TLS edge. One design, two targets:
/// <list type="bullet">
/// <item>local (no <c>VPS_SSH_HOST</c>): the chain deployed by <c>scripts/vps/deploy.ps1 -Local</c> (project
/// <c>hrm-vps-local</c>, <c>hr.hrm.test</c>/<c>sso.hrm.test</c>); docker commands run through Git Bash; TLS trusts only
/// Caddy's exported internal root; evidence in <c>acceptance-evidence/AC-OPS-36/</c>;</item>
/// <item>server (<c>VPS_SSH_HOST=hrm-vps</c>): the same commands over <c>ssh</c>; TLS uses the system trust store (or,
/// with <c>VPS_ALLOW_STAGING_CA=true</c>, only a Let's Encrypt staging certificate); evidence in
/// <c>acceptance-evidence/AC-OPS-36/vps/</c>; the owner's public IP is masked to two octets.</item>
/// </list>
/// Both host names are mapped to <c>VPS_TARGET_IP</c> with <see cref="SocketsHttpHandler.ConnectCallback"/>; TLS
/// validation is never switched off. The demo users' password is read from the target's .env into memory for the
/// sign-ins and is never logged or written to evidence. Negatives that need another configuration run a one-off
/// container (<c>docker compose run</c>) next to the live service, which keeps its configuration; the test then proves
/// the live service is still healthy.
/// </summary>
[RequiredAcceptance]
public class AcOps36VpsBootTests
{
    private const string Ac = "AC-OPS-36";
    private const string DemoBanner = "بيئة عرض — بيانات اصطناعية فقط";

    private static readonly string[] UiModules =
    [
        "hr-core", "hr-contracts", "hr-health", "hr-movements", "hr-training", "payroll", "idcard", "records-archive",
        "admin-logistics", "it-infra", "dev-analytics", "compliance", "auth-directory"
    ];

    private static readonly string[] Applications =
    [
        "hr-gateway", "hr-core-web", "hr-contracts-web", "hr-health-web", "hr-movements-web", "hr-training-web",
        "payroll-web", "idcard-web", "records-archive-web", "admin-logistics-web", "it-infra-web", "dev-analytics-web",
        "compliance-web", "auth-directory-web", "file-storage-web"
    ];

    private static readonly int[] ClosedPorts = [5000, 8080, 5432, 5672, 15672, 5341];

    // ---------------------------------------------------------------- target

    private static string? SshHost => Environment.GetEnvironmentVariable("VPS_SSH_HOST") is { Length: > 0 } h ? h : null;
    private static bool Remote => SshHost is not null;
    private static string AppDomain => Environment.GetEnvironmentVariable("VPS_APP_DOMAIN") ?? "hr.hrm.test";
    private static string SsoDomain => Environment.GetEnvironmentVariable("VPS_SSO_DOMAIN") ?? "sso.hrm.test";
    private static string TargetIp => Environment.GetEnvironmentVariable("VPS_TARGET_IP") ?? "127.0.0.1";
    private static bool AllowStagingCa =>
        string.Equals(Environment.GetEnvironmentVariable("VPS_ALLOW_STAGING_CA"), "true", StringComparison.OrdinalIgnoreCase);

    private static string Project => Remote ? "hrm" : "hrm-vps-local";
    private static string HrmHome => Remote ? "/opt/hrm" : LocalHome.Replace('\\', '/');
    private static string LocalHome => Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "vps", "local-home");
    private static string EnvFile => $"{HrmHome}/shared/.env";

    private static string EvidenceRoot =>
        Remote ? Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence", Ac) : Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence");

    private static string EvidenceFolder => Remote ? "vps" : Ac;

    private static EvidenceRecorder Start(string key, string scenario)
    {
        var recorder = new EvidenceRecorder(EvidenceFolder, $"ops36-{key}-{Guid.NewGuid():N}", EvidenceRoot) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, Remote ? "demo users on the server (password read into memory only)" : "demo users (lab-only)");
        recorder.RecordTiming("target", Remote ? "server" : "local");
        recorder.RecordTiming("appDomain", AppDomain);
        AcceptanceGate.RequireEnabled(recorder);
        var (code, output) = Sh($"docker ps -q --filter label=com.docker.compose.project={Project} --filter label=com.docker.compose.service=edge");
        AcceptanceGate.RequireData(recorder, code == 0 && output.Trim().Length > 0,
            "الخادم الواحد غير منشور", $"The single-host chain (project {Project}) is not running: deploy it with scripts/vps/deploy.ps1{(Remote ? "" : " -Local")}.");
        return recorder;
    }

    // ---------------------------------------------------------------- shell

    private static string Bash =>
        OperatingSystem.IsWindows() && File.Exists(@"C:\Program Files\Git\bin\bash.exe") ? @"C:\Program Files\Git\bin\bash.exe" : "bash";

    /// <summary>Runs a POSIX shell command on the target: Git Bash locally, ssh on the server. Output stays in memory.</summary>
    private static (int ExitCode, string Output) Sh(string command, int timeoutMs = 300_000)
    {
        ProcessStartInfo psi;
        if (Remote)
        {
            psi = new ProcessStartInfo("ssh");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("BatchMode=yes");
            psi.ArgumentList.Add(SshHost!);
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi = new ProcessStartInfo(Bash);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
            psi.Environment["MSYS_NO_PATHCONV"] = "1";
        }

        psi.RedirectStandardOutput = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(entireProcessTree: true);
            return (-1, "timeout");
        }

        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    private static string Q(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static string ReleaseDir()
    {
        // remote-deploy.sh records the running release in current-tag (the current symlink exists on Linux only).
        var tag = Sh($"cat {Q(HrmHome + "/current-tag")}").Output.Trim();
        Assert.Matches("^[0-9a-f]{12}$", tag);
        return $"{HrmHome}/releases/{tag}";
    }

    /// <summary>The compose invocation of remote-deploy.sh, run in the release directory.</summary>
    private static string Compose(string args)
    {
        var extra = Remote ? "" : " -f docker-compose.vps-local.yml";
        return $"cd {Q(ReleaseDir())} && IMAGE_TAG=$(basename {Q(ReleaseDir())}) docker compose -p {Project} --env-file {Q(EnvFile)} --profile full-stack " +
               "-f docker-compose.yml -f docker-compose.services.yml -f docker-compose.images.yml -f docker-compose.production.yml " +
               $"-f docker-compose.vps.yml -f docker-compose.vps-demo.yml{extra} {args}";
    }

    private static string ContainerOf(string service) =>
        Sh($"docker ps -q --filter label=com.docker.compose.project={Project} --filter label=com.docker.compose.service={service} | head -1").Output.Trim();

    /// <summary>A non-secret value of the target's .env (domains, repository, mode).</summary>
    private static string EnvValue(string key) =>
        Sh($"grep -E '^{key}=' {Q(EnvFile)} | tail -1 | cut -d= -f2-").Output.Trim().Trim('"');

    /// <summary>The demo users' password: into memory, never into evidence or logs.</summary>
    private static string DemoPassword()
    {
        var (code, output) = Sh($"grep -E '^DEMO_USERS_PASSWORD=' {Q(EnvFile)} | tail -1 | cut -d= -f2-");
        var value = output.Trim().Trim('"');
        Assert.True(code == 0 && value.Length >= 16, "DEMO_USERS_PASSWORD could not be read from the target's .env (value not shown)");
        return value;
    }

    private static string Mask(string text) =>
        Remote ? Regex.Replace(text, @"\b(\d{1,3}\.\d{1,3})\.\d{1,3}\.\d{1,3}\b", "$1.x.x") : text;

    // ---------------------------------------------------------------- TLS + HTTP

    private static X509Certificate2? _localRoot;

    private static X509Certificate2 LocalRoot()
    {
        if (_localRoot is not null)
            return _localRoot;
        var edge = ContainerOf("edge");
        var (code, pem) = Sh($"docker exec {edge} cat /data/caddy/pki/authorities/local/root.crt");
        Assert.True(code == 0 && pem.Contains("BEGIN CERTIFICATE"), "Caddy's internal root could not be exported");
        _localRoot = X509Certificate2.CreateFromPem(pem.Trim());
        return _localRoot;
    }

    private sealed record TlsObservation(string Host, bool Valid, string Issuer, DateTime NotAfterUtc, string Detail);

    private static TlsObservation ValidateCertificate(string host, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null)
            return new TlsObservation(host, false, "", default, "no certificate");
        var cert = new X509Certificate2(certificate);
        var issuer = cert.Issuer;
        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            return new TlsObservation(host, false, issuer, cert.NotAfter.ToUniversalTime(), "name mismatch");

        if (!Remote)
        {
            // Trust exactly Caddy's internal root for the local variant.
            using var custom = new X509Chain();
            custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            custom.ChainPolicy.CustomTrustStore.Add(LocalRoot());
            custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            if (chain is not null)
                foreach (var element in chain.ChainElements.Skip(1))
                    custom.ChainPolicy.ExtraStore.Add(element.Certificate);
            var ok = custom.Build(cert);
            return new TlsObservation(host, ok, issuer, cert.NotAfter.ToUniversalTime(), ok ? "chains to Caddy's internal root" : "does not chain to Caddy's root");
        }

        if (AllowStagingCa)
        {
            var staging = issuer.Contains("(STAGING)", StringComparison.Ordinal);
            return new TlsObservation(host, staging, issuer, cert.NotAfter.ToUniversalTime(),
                staging ? "Let's Encrypt staging (accepted with VPS_ALLOW_STAGING_CA)" : "VPS_ALLOW_STAGING_CA accepts only a Let's Encrypt staging certificate");
        }

        return new TlsObservation(host, errors == SslPolicyErrors.None, issuer, cert.NotAfter.ToUniversalTime(),
            errors == SslPolicyErrors.None ? "publicly trusted" : $"untrusted: {errors}");
    }

    private static readonly Dictionary<string, TlsObservation> TlsSeen = new(StringComparer.OrdinalIgnoreCase);

    private static HttpClient NewClient(CookieContainer? cookies = null, bool followRedirects = false)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = followRedirects,
            MaxAutomaticRedirections = 10,
            UseCookies = cookies is not null,
            CookieContainer = cookies ?? new CookieContainer(),
            ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                if (!string.Equals(host, AppDomain, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(host, SsoDomain, StringComparison.OrdinalIgnoreCase))
                    throw new HttpRequestException($"unexpected host {host}");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await socket.ConnectAsync(IPAddress.Parse(TargetIp), context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    var host = sender is SslStream s ? s.TargetHostName : AppDomain;
                    var observation = ValidateCertificate(host, certificate, chain, errors);
                    lock (TlsSeen)
                        TlsSeen[host] = observation;
                    return observation.Valid;
                }
            }
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    private static readonly Regex FormAction = new(@"<form[^>]*action=""(?<a>[^""]+)""", RegexOptions.IgnoreCase);
    private static readonly Regex HiddenInput = new(@"<input[^>]*type=""hidden""[^>]*name=""(?<n>[^""]+)""[^>]*value=""(?<v>[^""]*)""", RegexOptions.IgnoreCase);

    /// <summary>Code flow through the edge: the gateway's challenge, Keycloak's login form, the form_post back.</summary>
    private static async Task<(HttpClient Client, CookieContainer Cookies)> SignInAsync(string user, string password)
    {
        var cookies = new CookieContainer();
        var client = NewClient(cookies, followRedirects: true);
        var login = await client.GetAsync($"https://{AppDomain}/ui/hr-core/");
        var html = await login.Content.ReadAsStringAsync();
        Assert.Equal(SsoDomain, login.RequestMessage!.RequestUri!.Host, ignoreCase: true);
        var action = WebUtility.HtmlDecode(FormAction.Match(html).Groups["a"].Value);
        Assert.Contains("login-actions/authenticate", action, StringComparison.Ordinal);

        var posted = await client.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = user,
            ["password"] = password,
            ["credentialId"] = ""
        }));
        var body = await posted.Content.ReadAsStringAsync();
        if (body.Contains("signin-oidc", StringComparison.Ordinal))
        {
            var target = WebUtility.HtmlDecode(FormAction.Match(body).Groups["a"].Value);
            var fields = HiddenInput.Matches(body).ToDictionary(m => m.Groups["n"].Value, m => WebUtility.HtmlDecode(m.Groups["v"].Value));
            posted = await client.PostAsync(target, new FormUrlEncodedContent(fields));
        }

        Assert.True(posted.IsSuccessStatusCode, $"sign-in of {user} ended with {(int)posted.StatusCode}");
        Assert.Equal(AppDomain, posted.RequestMessage!.RequestUri!.Host, ignoreCase: true);
        return (client, cookies);
    }

    private static async Task<(int Status, string? Location)> GetNoRedirectAsync(string url, Action<HttpRequestMessage>? configure = null)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        configure?.Invoke(request);
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, response.Headers.Location?.ToString());
    }

    private static bool TcpOpen(string address, int port)
    {
        using var client = new TcpClient();
        try
        {
            return client.ConnectAsync(address, port).Wait(TimeSpan.FromSeconds(3)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- AC-OPS-36a

    [SkippableFact]
    public async Task AC_OPS_36a_Boots_from_the_registry_with_every_container_healthy()
    {
        var recorder = Start("36a", "AC-OPS-36a deploy pulls from the registry and all containers become healthy within 10 minutes");
        var repo = EnvValue("HRM_IMAGE_REPO");
        var tag = Path.GetFileName(ReleaseDir());
        var (_, ps) = Sh($"for id in $(docker ps -aq --filter label=com.docker.compose.project={Project}); do " +
                         "docker inspect -f '{{index .Config.Labels \"com.docker.compose.service\"}}|{{.Config.Image}}|{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{.State.ExitCode}}' $id; done");
        var rows = ps.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().Split('|')).Where(p => p.Length == 5).ToList();
        recorder.WriteAfter(rows.Select(r => new { service = r[0], image = r[1], state = r[2], health = r[3], exit = r[4] }));

        foreach (var app in Applications)
        {
            var row = rows.SingleOrDefault(r => r[0] == app);
            Assert.True(row is not null, $"{app} is not running");
            Assert.Equal($"{repo}/{app}:{tag}", row![1]);
        }

        foreach (var infra in new[] { "postgresql", "rabbitmq", "keycloak", "clamav", "seq", "edge" })
            Assert.Contains(rows, r => r[0] == infra);
        foreach (var r in rows)
        {
            if (r[0] == "volume-init")
                Assert.True(r[2] == "exited" && r[4] == "0", "volume-init did not exit 0");
            else
                Assert.True(r[2] == "running" && r[3] is "healthy" or "none", $"{r[0]} is {r[2]}/{r[3]}");
        }

        Assert.DoesNotContain(rows, r => r[0] is "otel-collector" or "prometheus" or "grafana" or "tempo");

        // Boot time: the deploy record written by scripts/vps/deploy.ps1 (healthy_seconds from remote-deploy.sh).
        var records = Directory.GetFiles(Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "vps"), $"deploy-{(Remote ? SshHost : "local")}-deploy-*.json");
        AcceptanceGate.RequireData(recorder, records.Length > 0, "لا سجل نشر", "No deploy record under artifacts/vps (run scripts/vps/deploy.ps1).");
        using var record = JsonDocument.Parse(File.ReadAllText(records.OrderByDescending(File.GetLastWriteTimeUtc).First()));
        // up -d already waits for the health-gated dependencies, so the boot time is up + the final wait.
        var timings = record.RootElement.GetProperty("timings");
        var healthy = timings.GetProperty("up_seconds").GetInt32() + timings.GetProperty("healthy_seconds").GetInt32();
        recorder.RecordTiming("upSeconds", timings.GetProperty("up_seconds").GetInt32());
        recorder.RecordTiming("bootToHealthySeconds", healthy);
        if (record.RootElement.GetProperty("timings").TryGetProperty("pull_seconds", out var pull))
            recorder.RecordTiming("pullSeconds", pull.GetInt32());
        Assert.True(healthy <= 600, $"healthy after {healthy} s (limit 600)");

        // Negatives: no registry path, or the latest tag, fail before any pull.
        // A second env file that only empties HRM_IMAGE_REPO (later --env-file wins; no copy of the secrets is made, and
        // an empty process variable cannot exist on Windows).
        var emptyRepo = Remote ? "/tmp/ops36a-empty-repo.env" : $"{HrmHome}/ops36a-empty-repo.env";
        Sh($"printf 'HRM_IMAGE_REPO=\\n' > {Q(emptyRepo)}");
        var (noRepoCode, noRepo) = Sh(Compose("config -q").Replace($"--env-file {Q(EnvFile)}", $"--env-file {Q(EnvFile)} --env-file {Q(emptyRepo)}"));
        Sh($"rm -f {Q(emptyRepo)}");
        Assert.NotEqual(0, noRepoCode);
        Assert.Contains("HRM_IMAGE_REPO required", noRepo, StringComparison.Ordinal);
        var watch = Stopwatch.StartNew();
        var (latestCode, latest) = Sh($"cd {Q(ReleaseDir())} && HRM_HOME={Q(HrmHome)} HRM_PROJECT={Project} bash deploy/vps/remote-deploy.sh deploy latest");
        watch.Stop();
        Assert.NotEqual(0, latestCode);
        Assert.Contains("not a 12-character commit SHA", latest, StringComparison.Ordinal);
        recorder.WriteDbChecks(new
        {
            withoutRegistry = new { exitCode = noRepoCode, message = "HRM_IMAGE_REPO required" },
            latestTag = new { exitCode = latestCode, refusedInSeconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }
        });
        recorder.FlushTiming();
        recorder.WriteResult("Pass", $"{rows.Count} containers from {repo}:{tag}, healthy after {healthy} s; no registry / latest refused");
        await Task.CompletedTask;
    }

    // ---------------------------------------------------------------- AC-OPS-36b

    [SkippableFact]
    public async Task AC_OPS_36b_Sign_in_over_tls_shows_the_modules()
    {
        var recorder = Start("36b", "AC-OPS-36b sign-in over https redirects to the SSO host and back; 14 modules; /ui/<slug>/ 200");

        // The redirect chain from the gateway to Keycloak: absolute https Locations, never localhost.
        var url = $"https://{AppDomain}/";
        var locations = new List<string>();
        for (var hop = 0; hop < 6; hop++)
        {
            var (status, location) = await GetNoRedirectAsync(url);
            recorder.CaptureHttp($"redirect-{hop}", "GET", Mask(url), status, location is null ? null : new { location });
            if (location is null)
                break;
            locations.Add(location);
            url = location.StartsWith('/') ? $"https://{AppDomain}{location}" : location;
            if (url.StartsWith($"https://{SsoDomain}/", StringComparison.OrdinalIgnoreCase))
                break;
        }

        Assert.DoesNotContain(locations, l => l.Contains("localhost", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith($"https://{SsoDomain}/realms/hrmanagement/protocol/openid-connect/auth", url, StringComparison.OrdinalIgnoreCase);

        var (client, _) = await SignInAsync("hradmin", DemoPassword());
        using (client)
        {
            var home = await client.GetStringAsync($"https://{AppDomain}/");
            var cards = Regex.Matches(home, "gateway-module-card-title").Count;
            recorder.CaptureHttp("home", "GET", $"https://{AppDomain}/", 200, new { moduleCards = cards });
            Assert.Equal(14, cards);

            var statuses = new Dictionary<string, int>();
            foreach (var module in UiModules)
            {
                using var response = await client.GetAsync($"https://{AppDomain}/ui/{module}/");
                statuses[module] = (int)response.StatusCode;
                Assert.Equal(AppDomain, response.RequestMessage!.RequestUri!.Host, ignoreCase: true);
            }

            recorder.WriteAfter(statuses);
            Assert.All(statuses, s => Assert.True(s.Value == 200, $"/ui/{s.Key}/ answered {s.Value}"));
        }

        // Negative: plain HTTP gives no session (port 80 is not published at all).
        var http80 = TcpOpen(TargetIp, 80);
        recorder.RecordTiming("port80Open", http80);
        Assert.False(http80, "port 80 must not be reachable");
        recorder.FlushTiming();
        recorder.WriteResult("Pass", $"redirect to https://{SsoDomain} without localhost; signed in; 14 module cards; 13 module UIs 200; no plain HTTP");
    }

    // ---------------------------------------------------------------- AC-OPS-36c

    [SkippableFact]
    public async Task AC_OPS_36c_Only_443_is_open_with_a_valid_certificate()
    {
        var recorder = Start("36c", "AC-OPS-36c 443 with a valid certificate for both names; other ports closed; admin 403; health/metrics 404");
        foreach (var host in new[] { AppDomain, SsoDomain })
        {
            await GetNoRedirectAsync($"https://{host}/");
            TlsObservation seen;
            lock (TlsSeen)
                seen = TlsSeen[host];
            recorder.CaptureHttp($"tls-{host}", "TLS", host, seen.Valid ? 200 : 495,
                new { seen.Issuer, notAfterUtc = seen.NotAfterUtc.ToString("o"), seen.Detail });
            Assert.True(seen.Valid, $"{host}: {seen.Detail} ({seen.Issuer})");
        }

        Assert.True(TcpOpen(TargetIp, 443));
        var open = ClosedPorts.Where(p => TcpOpen(TargetIp, p)).ToList();
        Assert.Empty(open);

        string listeners = "n/a (local Docker Desktop: published ports checked from this machine)";
        if (Remote)
        {
            // An ISP can block outbound ports, so confirm on the server which sockets listen on public addresses.
            listeners = Sh("ss -Htln | awk '{print $4}' | grep -vE '^(127\\.|\\[::1\\]|::1)' | sed -E 's/.*:([0-9]+)$/\\1/' | sort -un | tr '\\n' ' '").Output.Trim();
            Assert.Equal("22 443", listeners);
        }

        var edges = new List<object>();
        foreach (var (path, expected) in new[]
                 {
                     ($"https://{SsoDomain}/admin/", 403), ($"https://{SsoDomain}/realms/master/", 403),
                     ($"https://{AppDomain}/metrics", 404), ($"https://{AppDomain}/health/ready", 404), ($"https://{AppDomain}/health/live", 404)
                 })
        {
            var (status, _) = await GetNoRedirectAsync(path);
            edges.Add(new { path, status });
            Assert.True(status == expected, $"{path} answered {status}, expected {expected}");
        }

        recorder.WriteAfter(new { closedPorts = ClosedPorts, openUnexpected = open, serverPublicListeners = listeners, edges });
        recorder.WriteResult("Pass", "443 valid for both names; 5000/8080/5432/5672/15672/5341 closed; /admin and /realms/master 403; /metrics and /health 404");
    }

    // ---------------------------------------------------------------- AC-OPS-36d

    [SkippableFact]
    public async Task AC_OPS_36d_Gateway_audit_records_the_address_caddy_saw()
    {
        var recorder = Start("36d", "AC-OPS-36d gateway audit carries the caller IP as Caddy saw it; a forged X-Forwarded-For is ignored");
        var (client, _) = await SignInAsync("hradmin", DemoPassword());
        var genuine = $"ops36d-{Guid.NewGuid():N}";
        var forged = $"ops36d-forged-{Guid.NewGuid():N}";
        using (client)
        {
            using var r1 = new HttpRequestMessage(HttpMethod.Get, $"https://{AppDomain}/ui/hr-core/?probe={genuine}");
            r1.Headers.Add("X-Correlation-Id", genuine);
            (await client.SendAsync(r1)).Dispose();
            using var r2 = new HttpRequestMessage(HttpMethod.Get, $"https://{AppDomain}/ui/hr-core/?probe={forged}");
            r2.Headers.Add("X-Correlation-Id", forged);
            r2.Headers.Add("X-Forwarded-For", "1.2.3.4");
            (await client.SendAsync(r2)).Dispose();
        }

        // What Caddy saw: the remote_ip of its JSON access log line for the genuine request.
        await Task.Delay(TimeSpan.FromSeconds(5));
        var edge = ContainerOf("edge");
        var accessLine = Sh($"docker logs --since 10m {edge} 2>&1 | grep {Q(genuine)} | head -1").Output;
        var caddyIp = Regex.Match(accessLine, "\"remote_ip\":\"(?<ip>[^\"]+)\"").Groups["ip"].Value;
        Assert.False(string.IsNullOrEmpty(caddyIp), "the edge access log has no line for the probe");

        string AuditIp(string correlation)
        {
            for (var i = 0; i < 12; i++)
            {
                var pg = ContainerOf("postgresql");
                var sql = $"select \"ClientIp\" from gateway_audit_entries where \"CorrelationId\" = '{correlation}' order by 1 limit 1;";
                var (_, output) = Sh($"docker exec {pg} sh -c {Q($"psql -U \"$POSTGRES_USER\" -d gateway_audit_db -tAc {Q(sql)}")}");
                var value = output.Trim();
                if (value.Length > 0 && !value.Contains("ERROR", StringComparison.Ordinal))
                    return value;
                Thread.Sleep(5000);
            }

            return "";
        }

        var genuineIp = AuditIp(genuine);
        var forgedIp = AuditIp(forged);
        recorder.WriteDbChecks(new
        {
            table = "gateway_audit_entries",
            caddyRemoteIp = Mask(caddyIp),
            genuineAuditIp = Mask(genuineIp),
            forgedRequestAuditIp = Mask(forgedIp),
            forgedHeader = "X-Forwarded-For: 1.2.3.4"
        });
        Assert.Equal(caddyIp, genuineIp);
        Assert.NotEqual("1.2.3.4", forgedIp);
        Assert.Equal(caddyIp, forgedIp);
        recorder.WriteResult("Pass", "audit ClientIp equals Caddy's remote_ip (compared unmasked in memory); forged X-Forwarded-For ignored");
    }

    // ---------------------------------------------------------------- AC-OPS-36e

    [SkippableFact]
    public async Task AC_OPS_36e_A_demo_room_behind_one_address_is_not_throttled()
    {
        var recorder = Start("36e", "AC-OPS-36e two users behind one IP: 400 assets + 50 pages without 429; 400 anonymous: 429 after 300 with Retry-After");
        var password = DemoPassword();
        var asset = await FindAssetPathAsync();
        var rejected = 0;
        foreach (var user in new[] { "hradmin", "e2e-hr-admin" })
        {
            var (client, _) = await SignInAsync(user, password);
            using (client)
            {
                var tasks = new List<Task<HttpStatusCode>>();
                for (var i = 0; i < 200; i++)
                    tasks.Add(Status(client, $"https://{AppDomain}{asset}?n={i}"));
                for (var i = 0; i < 25; i++)
                    tasks.Add(Status(client, $"https://{AppDomain}/ui/{UiModules[i % UiModules.Length]}/"));
                var results = await Task.WhenAll(tasks);
                rejected += results.Count(s => s == HttpStatusCode.TooManyRequests);
            }
        }

        recorder.RecordTiming("authenticatedRequests", 450);
        recorder.RecordTiming("authenticated429", rejected);
        Assert.Equal(0, rejected);

        // Anonymous: one address, one window of 300 per minute.
        var (succeeded, retryAfter, sent) = await AnonymousBurstAsync();
        if (succeeded > 300)
            (succeeded, retryAfter, sent) = await AnonymousBurstAsync(); // the window turned over during the burst
        recorder.RecordTiming("anonymousSent", sent);
        recorder.RecordTiming("anonymousBeforeFirst429", succeeded);
        recorder.RecordTiming("retryAfter", retryAfter ?? "missing");
        Assert.True(succeeded <= 300, $"{succeeded} anonymous requests passed before a 429");
        Assert.True(sent < 400 || succeeded < 400, "no 429 within 400 anonymous requests");
        Assert.False(string.IsNullOrEmpty(retryAfter), "429 without Retry-After");

        await WaitForAnonymousWindowAsync();
        recorder.FlushTiming();
        recorder.WriteResult("Pass", $"two signed-in users: 400 assets + 50 pages, no 429; anonymous: first 429 after {succeeded} with Retry-After {retryAfter}");
    }

    private static async Task<HttpStatusCode> Status(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        return response.StatusCode;
    }

    private static async Task<string> FindAssetPathAsync()
    {
        var (client, _) = await SignInAsync("hradmin", DemoPassword());
        using (client)
        {
            var html = await client.GetStringAsync($"https://{AppDomain}/ui/hr-core/");
            var href = Regex.Matches(html, @"(?:href|src)=""(?<p>/ui/hr-core/[^""?]+\.(?:css|js))")
                .Select(m => m.Groups["p"].Value).FirstOrDefault();
            Assert.False(string.IsNullOrEmpty(href), "no /ui/hr-core/ stylesheet or script in the page");
            return href!;
        }
    }

    private static async Task<(int Succeeded, string? RetryAfter, int Sent)> AnonymousBurstAsync()
    {
        using var client = NewClient();
        var succeeded = 0;
        for (var i = 0; i < 400; i++)
        {
            using var response = await client.GetAsync($"https://{AppDomain}/?burst={i}");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return (succeeded, response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0"), i + 1);
            succeeded++;
        }

        return (succeeded, null, 400);
    }

    /// <summary>Leaves the anonymous window usable for the other scenarios (tests in a class run one after another).</summary>
    private static async Task WaitForAnonymousWindowAsync()
    {
        using var client = NewClient();
        for (var i = 0; i < 15; i++)
        {
            using var response = await client.GetAsync($"https://{AppDomain}/");
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
                return;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    // ---------------------------------------------------------------- AC-OPS-36f

    [SkippableFact]
    public async Task AC_OPS_36f_Placeholder_or_short_secret_stops_the_service()
    {
        var recorder = Start("36f", "AC-OPS-36f placeholder AllowedHosts stops hr-core-web; a 10-character caller-proof secret stops the gateway; the live services stay healthy");

        var placeholder = Sh(Compose("run --rm --no-deps -T -e AllowedHosts=YOUR_MODULE_HOST hr-core-web"), 420_000);
        var shortSecret = Sh(Compose("run --rm --no-deps -T -e GatewayCallerProof__Secret=0123456789 hr-gateway"), 420_000);
        var placeholderLine = placeholder.Output.Split('\n').FirstOrDefault(l => l.Contains("will not start", StringComparison.Ordinal)) ?? "";
        var shortLine = shortSecret.Output.Split('\n').FirstOrDefault(l => l.Contains("will not start", StringComparison.Ordinal)) ?? "";
        recorder.WriteDbChecks(new
        {
            placeholderAllowedHosts = new { exitCode = placeholder.ExitCode, message = placeholderLine.Trim() },
            shortCallerProofSecret = new { exitCode = shortSecret.ExitCode, message = shortLine.Trim() }
        });

        Assert.NotEqual(0, placeholder.ExitCode);
        Assert.Contains("AllowedHosts", placeholderLine, StringComparison.Ordinal);
        Assert.DoesNotContain("YOUR_MODULE_HOST", placeholderLine, StringComparison.Ordinal);
        Assert.NotEqual(0, shortSecret.ExitCode);
        Assert.Contains("GatewayCallerProof:Secret", shortLine, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789", shortLine.Replace("0123456789abcdef", ""), StringComparison.Ordinal);

        // The live services kept their configuration: still healthy, still serving.
        foreach (var service in new[] { "hr-core-web", "hr-gateway" })
        {
            var health = Sh($"docker inspect -f '{{{{.State.Health.Status}}}}' {ContainerOf(service)}").Output.Trim();
            Assert.Equal("healthy", health);
        }

        var (client, _) = await SignInAsync("hradmin", DemoPassword());
        using (client)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"https://{AppDomain}/ui/hr-core/")).StatusCode);
        recorder.WriteAfter(new { liveHrCore = "healthy", liveGateway = "healthy", uiHrCore = 200 });
        recorder.WriteResult("Pass", "one-off containers with a placeholder AllowedHosts and a short secret exited naming the key (no value); live services unchanged and healthy");
    }

    // ---------------------------------------------------------------- AC-OPS-36g

    [SkippableFact]
    public async Task AC_OPS_36g_File_upload_through_the_gateway_and_no_proof_403()
    {
        var recorder = Start("36g", "AC-OPS-36g upload through the gateway to /api/files 201; hr-core-web without the gateway proof 403");
        var (client, _) = await SignInAsync("hradmin", DemoPassword());
        using (client)
        {
            using var form = new MultipartFormDataContent();
            var pdf = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));
            pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(pdf, "file", "ops36g.pdf");
            using var upload = await client.PostAsync($"https://{AppDomain}/api/files", form);
            var body = await upload.Content.ReadAsStringAsync();
            recorder.CaptureHttp("upload", "POST", $"https://{AppDomain}/api/files", (int)upload.StatusCode, body.Length > 400 ? body[..400] : body);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"https://{AppDomain}/ui/hr-core/")).StatusCode);
        }

        // A container on the application network (the gateway's own, which has curl) calls hr-core-web without the proof.
        var gateway = ContainerOf("hr-gateway");
        var direct = Sh($"docker exec {gateway} curl -s -o /dev/null -w '%{{http_code}}' http://hr-core-web:8080/").Output.Trim();
        recorder.WriteAfter(new { directCallWithoutProof = direct });
        Assert.Equal("403", direct);
        recorder.WriteResult("Pass", $"/api/files upload 201 through the edge and the gateway; direct hr-core-web call without proof {direct}");
    }

    // ---------------------------------------------------------------- AC-OPS-36h

    [SkippableFact]
    public async Task AC_OPS_36h_Memory_stays_within_the_budget()
    {
        var recorder = Start("36h", "AC-OPS-36h after the self-test and a 10-minute tour: no OOMKilled, every container under its limit, total under 75 % of host memory");

        // The tour: keep a signed-in user browsing until the stack has run for 10 minutes.
        var started = Sh($"docker inspect -f '{{{{.State.StartedAt}}}}' {ContainerOf("hr-gateway")}").Output.Trim();
        var startedAt = DateTime.Parse(started, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        var (client, _) = await SignInAsync("hradmin", DemoPassword());
        var tourRequests = 0;
        using (client)
        {
            var i = 0;
            while (DateTime.UtcNow - startedAt < TimeSpan.FromMinutes(10))
            {
                (await client.GetAsync($"https://{AppDomain}/ui/{UiModules[i++ % UiModules.Length]}/")).Dispose();
                tourRequests++;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        recorder.RecordTiming("tourRequests", tourRequests);
        var stats = Sh($"docker stats --no-stream --format '{{{{.Name}}}}|{{{{.MemUsage}}}}' $(docker ps -q --filter label=com.docker.compose.project={Project})").Output;
        var oom = Sh($"for id in $(docker ps -aq --filter label=com.docker.compose.project={Project}); do docker inspect -f '{{{{.Name}}}}|{{{{.State.OOMKilled}}}}' $id; done").Output;
        var hostBytes = long.Parse(Sh("docker info --format '{{.MemTotal}}'").Output.Trim());

        var rows = new List<object>();
        double totalMiB = 0;
        foreach (var line in stats.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length != 2)
                continue;
            var usage = parts[1].Split('/');
            var used = ToMiB(usage[0]);
            var limit = ToMiB(usage[1]);
            totalMiB += used;
            rows.Add(new { container = parts[0], usedMiB = Math.Round(used, 1), limitMiB = Math.Round(limit, 1), percent = Math.Round(100 * used / limit, 1) });
            Assert.True(used < limit, $"{parts[0]} uses {used:0} MiB of {limit:0} MiB");
        }

        var oomKilled = oom.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.Trim().EndsWith("|true", StringComparison.Ordinal)).ToList();
        var hostMiB = hostBytes / 1048576.0;
        recorder.WriteAfter(new { hostMemoryMiB = Math.Round(hostMiB), totalUsedMiB = Math.Round(totalMiB), percentOfHost = Math.Round(100 * totalMiB / hostMiB, 1), containers = rows, oomKilled });
        recorder.FlushTiming();
        Assert.Empty(oomKilled);
        Assert.True(totalMiB < 0.75 * hostMiB, $"total {totalMiB:0} MiB is not under 75 % of {hostMiB:0} MiB");
        recorder.WriteResult("Pass", $"{rows.Count} containers, {totalMiB:0} MiB of {hostMiB:0} MiB ({100 * totalMiB / hostMiB:0.0} %), none over its limit, no OOMKilled");
    }

    private static double ToMiB(string text)
    {
        var m = Regex.Match(text.Trim(), @"^(?<n>[\d.]+)\s*(?<u>[KMGT]?i?B)$");
        var n = double.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups["u"].Value switch
        {
            "B" => n / 1048576,
            "KiB" or "kB" or "KB" => n / 1024,
            "MiB" or "MB" => n,
            "GiB" or "GB" => n * 1024,
            "TiB" => n * 1048576,
            _ => n
        };
    }

    // ---------------------------------------------------------------- AC-OPS-36i

    [SkippableFact]
    public async Task AC_OPS_36i_Seq_receives_every_service_and_telemetry_is_off()
    {
        var recorder = Start("36i", "AC-OPS-36i Seq receives logs from all 15 services over the loopback port; no OTLP collector, no OTLP errors");
        var expected = new Dictionary<string, string>
        {
            ["HRManagement.Gateway"] = "hr-gateway", ["HR.Core.Web"] = "hr-core-web", ["HR.Contracts.Web"] = "hr-contracts-web",
            ["HR.Health.Web"] = "hr-health-web", ["HR.Movements.Web"] = "hr-movements-web", ["HR.Training.Web"] = "hr-training-web",
            ["Payroll.Web"] = "payroll-web", ["IDCard.Web"] = "idcard-web", ["Records.Archive.Web"] = "records-archive-web",
            ["Admin.Logistics.Web"] = "admin-logistics-web", ["IT.Infra.Web"] = "it-infra-web", ["Dev.Analytics.Web"] = "dev-analytics-web",
            ["Compliance.Web"] = "compliance-web", ["Auth.Directory.Web"] = "auth-directory-web", ["File.Storage.Web"] = "file-storage-web"
        };
        var seen = new Dictionary<string, int>();
        foreach (var application in expected.Keys)
        {
            var filter = Uri.EscapeDataString($"Application = '{application}'");
            var (_, json) = Sh($"curl -s 'http://127.0.0.1:5380/api/events?count=1&filter={filter}'");
            var count = json.TrimStart().StartsWith('[') ? JsonDocument.Parse(json).RootElement.GetArrayLength() : 0;
            seen[application] = count;
        }

        recorder.WriteAfter(new { seqApi = "http://127.0.0.1:5380/api/events (loopback)", eventsPerApplication = seen });
        Assert.All(seen, s => Assert.True(s.Value > 0, $"Seq has no event from {s.Key}"));

        var containers = Sh($"docker ps --format '{{{{.Names}}}}' --filter label=com.docker.compose.project={Project}").Output;
        Assert.DoesNotContain("otel-collector", containers, StringComparison.Ordinal);
        Assert.DoesNotContain("prometheus", containers, StringComparison.Ordinal);
        var otlpErrors = new Dictionary<string, int>();
        foreach (var service in expected.Values)
        {
            var (_, count) = Sh($"docker logs --since 24h {ContainerOf(service)} 2>&1 | grep -ciE 'otlp|otel-collector|:4317' || true");
            otlpErrors[service] = int.TryParse(count.Trim(), out var n) ? n : -1;
        }

        recorder.WriteDbChecks(new { otlpLogLines = otlpErrors });
        Assert.All(otlpErrors, e => Assert.True(e.Value == 0, $"{e.Key} logged {e.Value} OTLP line(s)"));
        recorder.WriteResult("Pass", "Seq has events from all 15 applications; no otel-collector/prometheus container; no OTLP lines in any log");
        await Task.CompletedTask;
    }

    // ---------------------------------------------------------------- AC-OPS-36j

    [SkippableFact]
    public async Task AC_OPS_36j_Redeploy_keeps_the_session_and_rollback_restores_the_previous_tag()
    {
        var recorder = Start("36j", "AC-OPS-36j redeploy with a new tag and force-recreate of the gateway keep the session and data; rollback restores the previous tag");
        var log = Sh($"cat {HrmHome}/deployments.log").Output;
        var deploys = Regex.Matches(log, @"\|tag=(?<t>[0-9a-f]{12})\|action=deploy\|result=healthy").Select(m => m.Groups["t"].Value).ToList();
        var rollbacks = Regex.Matches(log, @"\|tag=(?<t>[0-9a-f]{12})\|action=rollback").Select(m => m.Groups["t"].Value).ToList();
        recorder.WriteBefore(new { deployments = log.Split('\n', StringSplitOptions.RemoveEmptyEntries) });
        AcceptanceGate.RequireData(recorder, deploys.Distinct().Count() >= 2 && rollbacks.Count >= 1,
            "لم يجرِ نشر بوسمين وتراجع", "deployments.log needs two deployed tags and a rollback (scripts/vps/deploy.ps1, then -Rollback).");
        var smokes = Directory.GetFiles(Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "vps"), $"smoke-{(Remote ? SshHost : "local")}-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        AcceptanceGate.RequireData(recorder, smokes.Count > 0, "لا تقرير اختبار ذاتي", "No smoke report under artifacts/vps.");
        using (var smoke = JsonDocument.Parse(File.ReadAllText(smokes[0])))
            Assert.Equal(0, smoke.RootElement.GetProperty("summary").GetProperty("fail").GetInt32());
        Assert.Equal(rollbacks[^1], Path.GetFileName(ReleaseDir()));

        // Session survives a recreated gateway (shared Data Protection key ring on a volume).
        var (client, _) = await SignInAsync("hradmin", DemoPassword());
        using (client)
        {
            var before = await client.GetAsync($"https://{AppDomain}/ui/hr-core/");
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            var recreate = Sh(Compose("up -d --no-deps --force-recreate hr-gateway"), 300_000);
            Assert.True(recreate.ExitCode == 0, "force-recreate of hr-gateway failed");
            var healthy = false;
            for (var i = 0; i < 60 && !healthy; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                healthy = Sh($"docker inspect -f '{{{{.State.Health.Status}}}}' {ContainerOf("hr-gateway")}").Output.Trim() == "healthy";
            }

            Assert.True(healthy, "hr-gateway did not become healthy after force-recreate");
            using var after = await client.GetAsync($"https://{AppDomain}/ui/hr-core/");
            var html = await after.Content.ReadAsStringAsync();
            recorder.CaptureHttp("after-recreate", "GET", $"https://{AppDomain}/ui/hr-core/", (int)after.StatusCode);
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
            Assert.Equal(AppDomain, after.RequestMessage!.RequestUri!.Host, ignoreCase: true);
            Assert.DoesNotContain("kc-form-login", html, StringComparison.Ordinal);
        }

        recorder.WriteAfter(new { distinctDeployedTags = deploys.Distinct().ToList(), rollbacks, current = Path.GetFileName(ReleaseDir()), latestSmoke = Path.GetFileName(smokes[0]) });
        recorder.WriteResult("Pass", "two tags deployed, rollback to the previous tag with a green smoke; session valid after the gateway was recreated");
    }

    // ---------------------------------------------------------------- AC-OPS-36k

    [SkippableFact]
    public async Task AC_OPS_36k_Header_shows_the_organization_and_the_demo_banner_and_license_spares_health()
    {
        var recorder = Start("36k", "AC-OPS-36k header shows HRM_ORGANIZATION_NAME and the demo banner; an expired license gives 402 but /health/live 200");
        var organization = EnvValue("HRM_ORGANIZATION_NAME");
        var (client, _) = await SignInAsync("hradmin", DemoPassword());
        using (client)
        {
            // Razor encodes non-Latin text as character references; compare the decoded page.
            var home = WebUtility.HtmlDecode(await client.GetStringAsync($"https://{AppDomain}/"));
            // HR.Core: an internal page (shared layout) and its root, the public careers portal (public layout).
            var module = WebUtility.HtmlDecode(await client.GetStringAsync($"https://{AppDomain}/ui/hr-core/Employees"));
            var portal = WebUtility.HtmlDecode(await client.GetStringAsync($"https://{AppDomain}/ui/hr-core/"));
            recorder.WriteAfter(new
            {
                organization,
                gatewayHeaderHasName = home.Contains(organization, StringComparison.Ordinal),
                gatewayBanner = home.Contains(DemoBanner, StringComparison.Ordinal),
                hrCoreBanner = module.Contains(DemoBanner, StringComparison.Ordinal),
                careersPortalBanner = portal.Contains(DemoBanner, StringComparison.Ordinal)
            });
            Assert.False(string.IsNullOrWhiteSpace(organization), "HRM_ORGANIZATION_NAME is empty");
            Assert.Contains(organization, home, StringComparison.Ordinal);
            Assert.Contains(DemoBanner, home, StringComparison.Ordinal);
            Assert.Contains(DemoBanner, module, StringComparison.Ordinal);
            Assert.Contains(DemoBanner, portal, StringComparison.Ordinal);
        }

        // Expired license on a one-off gateway next to the live one.
        var name = $"ops36k-{Guid.NewGuid():N}"[..20];
        var run = Sh(Compose($"run -d --rm --no-deps --name {name} -e Deployment__LicenseExpiresUtc=2020-01-01T00:00:00Z hr-gateway"), 300_000);
        Assert.True(run.ExitCode == 0, "could not start the one-off gateway");
        try
        {
            string ui = "", live = "";
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                live = Sh($"docker exec {name} curl -s -o /dev/null -w '%{{http_code}}' http://localhost:8080/health/live").Output.Trim();
                if (live == "200")
                {
                    ui = Sh($"docker exec {name} curl -s -o /dev/null -w '%{{http_code}}' http://localhost:8080/").Output.Trim();
                    break;
                }
            }

            recorder.WriteDbChecks(new { expiredLicense = "2020-01-01T00:00:00Z", ui, healthLive = live });
            Assert.Equal("200", live);
            Assert.Equal("402", ui);
        }
        finally
        {
            Sh($"docker rm -f {name}");
        }

        Assert.Equal("healthy", Sh($"docker inspect -f '{{{{.State.Health.Status}}}}' {ContainerOf("hr-gateway")}").Output.Trim());
        recorder.WriteResult("Pass", "organization name and demo banner in the gateway and HR.Core headers; expired license: UI 402, /health/live 200; live gateway unchanged");
    }
}
