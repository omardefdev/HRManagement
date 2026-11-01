using System.Text.Json;
using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-38 (section 3, rule 2): every <c>${VAR:?}</c> of the production and single-host chains is in the env examples
/// and in what init-secrets fills (the same chain files plus the role and broker matrices), each such variable is
/// either generated (a secret name) or a named non-secret, and no appsettings.Production.json keeps a placeholder.
/// </summary>
public class Ops038EnvParityTests
{
    private static readonly string[] SingleHostChain =
    [
        "docker-compose.yml", "docker-compose.services.yml", "docker-compose.images.yml", "docker-compose.production.yml",
        "docker-compose.vps.yml", "docker-compose.vps-demo.yml",
    ];

    /// <summary>Required non-secrets: init-secrets takes them from the example's default or from --set, never generates them.</summary>
    private static readonly string[] NonSecretRequired =
    [
        "APP_DOMAIN", "SSO_DOMAIN", "HRM_IMAGE_REPO", "IMAGE_TAG", "POSTGRES_USER", "RABBITMQ_DEFAULT_USER",
        "KEYCLOAK_ADMIN", "GRAFANA_ADMIN_USER",
    ];

    private static readonly Regex SecretName = new("(PASSWORD|_PASS|SECRET|_KEY|TOKEN)$", RegexOptions.CultureInvariant);

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Read(string relative) =>
        System.IO.File.ReadAllText(Path.Combine(ResolveRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static SortedSet<string> Required(IEnumerable<string> files) =>
        new(files.SelectMany(f => Regex.Matches(Read(f), @"\$\{([A-Z_][A-Z0-9_]*):\?").Select(m => m.Groups[1].Value)),
            StringComparer.Ordinal);

    private static HashSet<string> ExampleKeys(string file) =>
        Read(file).Split('\n')
            .Select(l => Regex.Match(l, @"^([A-Z][A-Z0-9_]*)="))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> MatrixPasswordVariables()
    {
        foreach (var line in Read("deploy/bootstrap/postgres-roles.conf").Split('\n').Select(l => l.Trim()))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var prefix = line.Split('|')[3];
            yield return $"{prefix}_APP_PASSWORD";
            yield return $"{prefix}_MIGRATOR_PASSWORD";
        }
        yield return "POSTGRES_BACKUP_PASSWORD";
        using var doc = JsonDocument.Parse(Read("deploy/bootstrap/rabbitmq-users.json"));
        foreach (var u in doc.RootElement.GetProperty("users").EnumerateArray())
            yield return u.GetProperty("passwordEnv").GetString()!;
    }

    [Fact]
    public void Every_required_variable_of_the_single_host_chain_is_in_the_vps_example()
    {
        var example = ExampleKeys("deploy/vps/.env.vps.example");
        var missing = Required(SingleHostChain.Append("docker-compose.posture-mq-admin.yml")).Where(k => !example.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "missing in deploy/vps/.env.vps.example: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_required_variable_of_the_production_chains_is_in_the_env_example()
    {
        var example = ExampleKeys(".env.example");
        var files = SingleHostChain.Append("docker-compose.posture-mq-admin.yml").Append("docker-compose.observability.production.yml");
        var missing = Required(files).Where(k => !example.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "missing in .env.example: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_role_and_broker_password_is_required_by_compose_and_listed_in_both_examples()
    {
        var production = Required(["docker-compose.production.yml"]);
        var vps = ExampleKeys("deploy/vps/.env.vps.example");
        var env = ExampleKeys(".env.example");
        foreach (var variable in MatrixPasswordVariables())
        {
            Assert.Contains(variable, production);
            Assert.Contains(variable, vps);
            Assert.Contains(variable, env);
        }
    }

    [Fact]
    public void Every_required_variable_is_a_generated_secret_or_a_named_non_secret()
    {
        var unknown = Required(SingleHostChain.Append("docker-compose.observability.production.yml"))
            .Where(k => !SecretName.IsMatch(k) && !NonSecretRequired.Contains(k))
            .ToList();
        Assert.True(unknown.Count == 0, "neither a secret name nor a known non-secret: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Init_secrets_scripts_read_the_same_chain_and_matrices_and_never_print_or_take_secrets()
    {
        var sh = Read("deploy/vps/init-secrets.sh");
        var ps = Read("scripts/vps/init-secrets.ps1");
        foreach (var file in SingleHostChain)
        {
            Assert.Contains(file, sh);
            Assert.Contains(file, ps);
        }
        foreach (var script in new[] { sh, ps })
        {
            Assert.Contains("postgres-roles.conf", script);
            Assert.Contains("rabbitmq-users.json", script);
            Assert.Contains(".env.vps.example", script);
            Assert.Contains("check-ignore", script);
            Assert.Contains("refuses secret key", script);
        }
        // The same secret rule in both generators.
        Assert.Contains("*PASSWORD|*_PASS|*SECRET|*_KEY|*TOKEN", sh);
        Assert.Contains("(PASSWORD|_PASS|SECRET|_KEY|TOKEN)$", ps);
        Assert.Contains("openssl rand -base64 32", sh);
        Assert.Contains("RandomNumberGenerator", ps);
        Assert.Contains("chmod 600", sh);
        Assert.All(ps, c => Assert.True(c < 128, $"non-ASCII character U+{(int)c:X4} in init-secrets.ps1"));
        // The server bundle carries the generator and its inputs.
        var bundle = Read("scripts/vps/make-deploy-bundle.ps1");
        Assert.Contains("'deploy/vps/init-secrets.sh'", bundle);
        Assert.Contains("'deploy/bootstrap/rabbitmq-users.json'", bundle);
        Assert.Contains("'deploy/bootstrap/postgres-roles.conf'", bundle);
    }

    [Fact]
    public void The_local_lab_env_file_is_git_ignored()
    {
        Assert.Contains(".env.leastpriv", Read(".gitignore"));
    }

    [Fact]
    public void No_production_settings_file_keeps_a_placeholder_and_the_caller_proof_secret_comes_only_from_the_environment()
    {
        var root = ResolveRepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src"), "appsettings.Production.json", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.Equal(15, files.Count); // the gateway and the 14 modules (File.Storage never had the placeholder)
        foreach (var file in files)
        {
            var text = System.IO.File.ReadAllText(file);
            Assert.DoesNotMatch("YOUR_|USE_ENV|USE_SECRET_MANAGER|CHANGE_ME|example\\.com", text);
            using var doc = JsonDocument.Parse(text);
            Assert.True(doc.RootElement.TryGetProperty("GatewayCallerProof", out var proof), file);
            Assert.False(proof.TryGetProperty("Secret", out _), $"{file}: GatewayCallerProof:Secret must come from the environment");
            Assert.True(proof.GetProperty("Enforce").GetBoolean(), file);
        }

        // PLAN-36 defect 3: no FileStorage:SecretKey placeholder in any base settings either.
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src"), "appsettings.json", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                 && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            Assert.DoesNotMatch("\"SecretKey\":\\s*\"USE_ENV", System.IO.File.ReadAllText(file));
        }
    }
}
