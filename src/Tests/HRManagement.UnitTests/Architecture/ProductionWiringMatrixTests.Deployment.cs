using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Wiring;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-37 (§2 rules 1, 3-6): code -> matrix, matrix -> deployment (compose chains and k8s), prod-like parity, the
/// Keycloak scripts, and the negative check of the checker itself.
/// </summary>
public partial class ProductionWiringMatrixTests
{
    private static readonly Regex RegistrationMethod = new(@"public static [\w<>\.\?]+ (\w+)\(\s*this IServiceCollection", RegexOptions.CultureInvariant);
    private static readonly Regex UrlKeyLiteral = new(@"""(Integration:\w+|Services:\w+:BaseUrl|HrCore:BaseUrl|HrGateway:InternalBaseUrl)""", RegexOptions.CultureInvariant);

    // D-INT-3: the deprecated HR.Core keys are read only by HrCoreEndpoint (with a warning), never by a service.
    private static readonly HashSet<string> LegacyKeys = new(StringComparer.Ordinal) { "Services:HRCore:BaseUrl", "HrCore:BaseUrl" };

    private static IEnumerable<string> SourceFiles(string relativeDirectory) =>
        Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, relativeDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/');

    /// <summary>§2 rule 1: every HTTP client registration in the code is in the matrix, and named as such.</summary>
    [Fact]
    public void Every_http_client_registration_in_the_code_is_in_the_matrix()
    {
        var problems = new List<string>();
        var known = Matrix.Value.Registrations.Keys.Concat(Matrix.Value.RegistrationsIgnored.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var file in SourceFiles("src").Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = System.IO.File.ReadAllText(file);
            if (!text.Contains("AddServiceAuthenticatedClient(", StringComparison.Ordinal) && !text.Contains("AddHttpClient", StringComparison.Ordinal))
                continue;

            var methods = RegistrationMethod.Matches(text);
            for (var i = 0; i < methods.Count; i++)
            {
                var end = i + 1 < methods.Count ? methods[i + 1].Index : text.Length;
                var body = text[methods[i].Index..end];
                var name = methods[i].Groups[1].Value;
                if ((body.Contains("AddServiceAuthenticatedClient(", StringComparison.Ordinal) || body.Contains(".AddHttpClient", StringComparison.Ordinal))
                    && !known.Contains(name))
                    problems.Add($"{Relative(file)}: {name} registers an HTTP client but is not in service-wiring.json (registrations or registrationsIgnored)");
            }

            // A service token client outside a named registration cannot be traced to the matrix.
            var firstMethod = methods.Count > 0 ? methods[0].Index : text.Length;
            if (text[..firstMethod].Contains(".AddServiceAuthenticatedClient(", StringComparison.Ordinal))
                problems.Add($"{Relative(file)}: AddServiceAuthenticatedClient outside a named registration; wrap it in an Add...Client extension listed in the matrix");
        }

        foreach (var (name, registration) in Matrix.Value.Registrations)
        {
            var definition = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, registration.DefinedIn));
            if (!Regex.IsMatch(definition, $@"public static [\w<>\.\?]+ {Regex.Escape(name)}\(\s*this IServiceCollection"))
                problems.Add($"{name}: not defined in {registration.DefinedIn}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>§2 rule 1: each service registers exactly the matrix registrations and reads only matrix URL keys.</summary>
    [Fact]
    public void Each_service_registers_and_reads_exactly_what_the_matrix_lists()
    {
        var problems = new List<string>();
        var registrationNames = Matrix.Value.Registrations.Keys.ToList();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in Matrix.Value.Services)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in service.Projects)
            {
                covered.Add(project);
                foreach (var file in SourceFiles(project))
                {
                    var text = System.IO.File.ReadAllText(file);
                    foreach (var name in registrationNames)
                    {
                        if (Regex.IsMatch(text, $@"\.{Regex.Escape(name)}\("))
                            found.Add(name);
                    }

                    foreach (Match m in UrlKeyLiteral.Matches(text))
                    {
                        if (LegacyKeys.Contains(m.Groups[1].Value))
                            problems.Add($"{Relative(file)}: reads the deprecated {m.Groups[1].Value}; use HrCoreEndpoint (Integration:HrCoreBaseUrl)");
                        else
                            keys.Add(ServiceWiringMatrix.ToEnvironmentKey(m.Groups[1].Value));
                    }
                }
            }

            foreach (var name in found.Where(n => !service.Registrations.Contains(n)))
                problems.Add($"{service.ComposeService}: the code calls {name}, which the matrix does not list for it");
            foreach (var name in service.Registrations.Where(n => !found.Contains(n)))
                problems.Add($"{service.ComposeService}: the matrix lists {name}, which the code does not call");
            foreach (var key in keys.Where(k => !service.Urls.ContainsKey(k)))
                problems.Add($"{service.ComposeService}: the code reads {key}, which the matrix does not list in urls");
        }

        foreach (var dir in Directory.GetDirectories(Path.Combine(RepoPaths.Root, "src", "Services")).SelectMany(Directory.GetDirectories))
        {
            var relative = Relative(dir);
            if (Directory.EnumerateFiles(dir, "*.csproj").Any() && !covered.Contains(relative))
                problems.Add($"{relative}: project is not in any matrix service's projects[]");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>§4 / DoD: no production code path falls back to a localhost service URL (only the Development defaults).</summary>
    [Fact]
    public void No_production_code_falls_back_to_a_localhost_service_url()
    {
        var fallback = new Regex(@"\?\?\s*""http://(localhost|127\.0\.0\.1):\d+", RegexOptions.CultureInvariant);
        // Razor views read configuration too (a link built from a fallback points the browser at localhost).
        var offenders = SourceFiles("src")
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src"), "*.cshtml", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => fallback.IsMatch(System.IO.File.ReadAllText(f)))
            .Select(Relative)
            .ToList();
        Assert.True(offenders.Count == 0, "localhost fallback in: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// §4 / D-INT-5: a localhost service URL lives only in appsettings.Development.json; in the base or a Production /
    /// Staging file it would be accepted outside a container and hide a missing deployment value.
    /// </summary>
    [Fact]
    public void No_non_development_appsettings_sets_a_localhost_service_url()
    {
        var localUrl = new Regex(@"""\w*BaseUrl""\s*:\s*""http://(localhost|127\.0\.0\.1)", RegexOptions.CultureInvariant);
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src", "Services"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("appsettings.Development.json", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => localUrl.IsMatch(System.IO.File.ReadAllText(f)))
            .Select(Relative)
            .ToList();
        Assert.True(offenders.Count == 0, "localhost service URL outside appsettings.Development.json: " + string.Join(", ", offenders));
    }

    /// <summary>§6: every service whose code reads ServiceRegime*Rules lists the pack and registers its readiness check.</summary>
    [Fact]
    public void Every_service_that_reads_the_legal_pack_ships_and_checks_it()
    {
        var reader = new Regex(@"\bServiceRegime(Separation|Movement|Promotion|Article159|Transition)Rules\.|\bServiceRegimeSeeder\b", RegexOptions.CultureInvariant);
        var problems = new List<string>();
        foreach (var service in Matrix.Value.Services)
        {
            var files = service.Projects.SelectMany(SourceFiles).ToList();
            var reads = files.Any(f => reader.IsMatch(System.IO.File.ReadAllText(f)));
            var registers = files.Any(f => System.IO.File.ReadAllText(f).Contains(".AddServiceRegimePackCheck(", StringComparison.Ordinal));
            var lists = service.LegalPacks.Contains("syr003-service-regimes-v1");
            if (reads && !lists) problems.Add($"{service.ComposeService}: reads ServiceRegime*Rules but legalPacks does not list syr003-service-regimes-v1");
            if (lists && !registers) problems.Add($"{service.ComposeService}: lists the pack but does not call AddServiceRegimePackCheck");
            if (lists && !reads) problems.Add($"{service.ComposeService}: lists the pack but reads no ServiceRegime*Rules");
        }

        var kernel = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Shared", "HRManagement.SharedKernel", "HRManagement.SharedKernel.csproj"));
        Assert.Contains(@"Link=""seed-packs\syr003-service-regimes-v1\regimes.json""", kernel);
        Assert.Contains(@"CopyToPublishDirectory=""PreserveNewest""", kernel);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    public static TheoryData<string> ProductionChains() => new() { "production", "single-host" };

    /// <summary>§2 rule 3: both production compose chains set the matrix wiring and the mandatory secret.</summary>
    [Theory]
    [MemberData(nameof(ProductionChains))]
    public void Production_compose_chain_sets_the_matrix_wiring(string chain)
    {
        var files = WiringChecks.ComposeChains(RepoPaths.Root)[chain];
        var environment = ComposeEffectiveEnvironment.Load(RepoPaths.Root, files);
        var errors = WiringChecks.CheckComposeChain(Matrix.Value, environment, chain, mandatorySecret: true);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    public static TheoryData<string> Overlays() => new() { "production-ha", "staging-ha", "dr-secondary" };

    /// <summary>§2 rule 3: every k8s overlay sets the matrix wiring per Deployment, the secret from hr-app-secrets.</summary>
    [Theory]
    [MemberData(nameof(Overlays))]
    public void Kubernetes_overlay_sets_the_matrix_wiring(string overlay)
    {
        var errors = WiringChecks.CheckKustomize(Matrix.Value, KustomizeEffectiveEnvironment.Load(RepoPaths.Root, overlay), overlay);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    /// <summary>§2 rule 4 and §3 rule 1: the prod-like stack takes its wiring from the production files.</summary>
    [Fact]
    public void Prod_like_stack_has_the_production_wiring()
    {
        var chains = WiringChecks.ComposeChains(RepoPaths.Root);
        var production = ComposeEffectiveEnvironment.Load(RepoPaths.Root, chains["production"]);
        var prodLike = ComposeEffectiveEnvironment.Load(RepoPaths.Root, chains["prodlike"]);
        var errors = WiringChecks.CheckParity(Matrix.Value, production, prodLike).ToList();
        errors.AddRange(WiringChecks.CheckComposeChain(Matrix.Value, prodLike, "prodlike", mandatorySecret: false));

        foreach (var file in new[] { "docker-compose.acceptance.yml", "docker-compose.acceptance-prodlike.yml" })
        {
            var only = ComposeEffectiveEnvironment.Load(RepoPaths.Root, [file]);
            foreach (var (service, environment) in only)
                foreach (var key in environment.Keys.Where(k => WiringChecks.WiringKey().IsMatch(k)))
                    errors.Add($"{file}: {service} sets {key}; acceptance files may set only the lab {WiringChecks.SecretKey}");
        }

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    /// <summary>§2 rule 6: the checker names the service and the key when a production file loses a URL.</summary>
    [Fact]
    public void Checker_reports_a_missing_url_with_service_and_key()
    {
        var files = WiringChecks.ComposeChains(RepoPaths.Root)["production"];
        var broken = ComposeEffectiveEnvironment.Load(RepoPaths.Root, files, (file, text) =>
            file == "docker-compose.yml"
                ? Regex.Replace(text, @"(?m)^      Integration__HrCoreBaseUrl: http://hr-core-web:8080\r?\n(?=      HrGateway__InternalBaseUrl)", "")
                : text);

        var errors = WiringChecks.CheckComposeChain(Matrix.Value, broken, "production", mandatorySecret: true);

        Assert.Contains("production: hr-contracts-web lacks Integration__HrCoreBaseUrl (expected http://hr-core-web:8080)", errors);
        Assert.DoesNotContain(errors, e => !e.Contains("hr-contracts-web", StringComparison.Ordinal));
    }

    /// <summary>
    /// §2: the fast reader follows compose where it matters for a key-present check: a value-less entry passes the host
    /// variable through (nothing set), and a service-level merge key can bring the environment block.
    /// </summary>
    [Fact]
    public void Compose_reader_treats_value_less_entries_as_unset_and_resolves_service_merges()
    {
        var environment = ComposeEffectiveEnvironment.FromTexts(
        [
            ("base.yml", """
                x-env: &with-env
                  environment:
                    FROM_ANCHOR: anchored
                services:
                  a:
                    environment:
                      KEEP: "1"
                      EMPTY: ""
                      DROPPED: "1"
                  b:
                    <<: *with-env
                  c:
                    environment:
                      - LISTED=x
                      - PASS_THROUGH
                """),
            ("override.yml", """
                services:
                  a:
                    environment:
                      DROPPED:
                """)
        ]);

        Assert.Equal("1", environment["a"]["KEEP"]);
        Assert.Equal("", environment["a"]["EMPTY"]);
        Assert.False(environment["a"].ContainsKey("DROPPED"));
        Assert.Equal("anchored", environment["b"]["FROM_ANCHOR"]);
        Assert.Equal("x", environment["c"]["LISTED"]);
        Assert.False(environment["c"].ContainsKey("PASS_THROUGH"));
    }

    /// <summary>§2 rule 5: the Keycloak scripts take clients and scopes from the matrix.</summary>
    [Fact]
    public void Keycloak_dev_sync_reads_the_matrix()
    {
        var script = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "sync-keycloak-dev-realm.ps1"));
        Assert.Contains("deploy/wiring/service-wiring.json", script);
        Assert.DoesNotMatch(@"Ensure-ServiceClient -ClientId """, script);
        Assert.DoesNotMatch(@"Ensure-ClientScope -Name """, script);
        Assert.Contains("Ensure-ServiceClient -ClientId $service.clientId -OptionalScopes @($service.scopes)", script);
    }

    /// <summary>§2 rule 5 and P-4: the VpsDemo realm (first import on a server) has every matrix client and scope.</summary>
    [Fact]
    public void Keycloak_vps_demo_realm_has_every_matrix_client_and_scope()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "int037-kc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
                Path.Combine(RepoPaths.Root, "scripts", "prepare-keycloak-import.ps1"),
                $"-Profile VpsDemo -AppDomain hr.int037.test -OutDir \"{outDir}\"");
            Assert.True(exitCode == 0, stdout + stderr);

            using var realm = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(outDir, "realm-hrmanagement.json")));
            var clients = realm.RootElement.GetProperty("clients").EnumerateArray()
                .ToDictionary(c => c.GetProperty("clientId").GetString()!, c => c);
            var scopes = realm.RootElement.GetProperty("clientScopes").EnumerateArray()
                .ToDictionary(s => s.GetProperty("name").GetString()!, s => s);
            var problems = new List<string>();

            foreach (var (name, scope) in Matrix.Value.Scopes)
            {
                if (!scopes.TryGetValue(name, out var realmScope))
                {
                    problems.Add($"scope {name} missing");
                    continue;
                }

                var mappers = realmScope.TryGetProperty("protocolMappers", out var m)
                    ? m.EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToHashSet()
                    : [];
                foreach (var audience in scope.Audiences.Where(a => !mappers.Contains("audience-" + a)))
                    problems.Add($"scope {name}: no audience mapper for {audience}");
            }

            foreach (var service in Matrix.Value.Services.Where(s => s.ServiceIdentity))
            {
                if (!clients.TryGetValue(service.ClientId!, out var client))
                {
                    problems.Add($"client {service.ClientId} missing");
                    continue;
                }

                if (!client.GetProperty("enabled").GetBoolean()) problems.Add($"client {service.ClientId} disabled");
                if (client.GetProperty("secret").GetString() != "${" + service.SecretEnv + "}")
                    problems.Add($"client {service.ClientId}: secret is not the ${{{service.SecretEnv}}} placeholder");
                var optional = client.TryGetProperty("optionalClientScopes", out var o)
                    ? o.EnumerateArray().Select(x => x.GetString()!).OrderBy(x => x).ToList()
                    : [];
                if (!optional.SequenceEqual(service.Scopes.OrderBy(x => x)))
                    problems.Add($"client {service.ClientId}: optional scopes [{string.Join(' ', optional)}], matrix [{string.Join(' ', service.Scopes)}]");
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    /// <summary>
    /// P-4: on the single host Keycloak resolves the realm placeholders and keycloak-sync-clients.sh sets each client's
    /// secret from Keycloak's own environment, so it must receive every matrix secret.
    /// </summary>
    [Fact]
    public void Single_host_keycloak_receives_every_service_secret()
    {
        var keycloak = ComposeEffectiveEnvironment.Load(RepoPaths.Root, WiringChecks.ComposeChains(RepoPaths.Root)["single-host"])["keycloak"];
        foreach (var service in Matrix.Value.Services.Where(s => s.ServiceIdentity))
        {
            Assert.True(keycloak.TryGetValue(service.SecretEnv!, out var value), $"keycloak lacks {service.SecretEnv}");
            Assert.StartsWith("${" + service.SecretEnv + ":?", value);
        }

        var sync = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "deploy", "vps", "keycloak-sync-clients.sh"));
        Assert.Contains("deploy/wiring/service-wiring.json", sync);
        Assert.Contains("keycloak-sync-clients.sh", System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "deploy", "vps", "remote-deploy.sh")));
    }

    /// <summary>The server .env template lists every secret the production chain now requires (names only).</summary>
    [Fact]
    public void Vps_env_example_lists_every_service_secret()
    {
        var example = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "deploy", "vps", ".env.vps.example"));
        foreach (var service in Matrix.Value.Services.Where(s => s.ServiceIdentity))
            Assert.Matches($@"(?m)^{Regex.Escape(service.SecretEnv!)}=\s*$", example);
    }
}
