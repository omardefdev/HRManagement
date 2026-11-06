using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-42 §4 (G-IAM-13), read as text as in Hard001HardeningTests: there is no cluster here. Each Deployment sees its own Secret
/// and nothing else, and the keys that make a proof or manage Keycloak sit in exactly one Secret.
/// </summary>
public class S2s042K8sPerServiceSecretsTests
{
    private static readonly string K8s = Path.Combine(RepoPaths.Root, "deploy", "k8s");

    private static readonly string[] Deployments =
    [
        "hr-gateway", "hr-core-web", "hr-contracts-web", "hr-health-web", "hr-movements-web", "hr-training-web", "payroll-web", "idcard-web",
        "records-archive-web", "admin-logistics-web", "it-infra-web", "dev-analytics-web", "compliance-web", "auth-directory-web", "file-storage-web"
    ];

    public static TheoryData<string> Overlays() => new() { "production-ha", "staging-ha" };

    private static string Read(params string[] parts) => System.IO.File.ReadAllText(Path.Combine([K8s, .. parts]));

    private static string[] SecretKeys(string overlay, string secret) =>
        Regex.Matches(Read("overlays", overlay, "external-secrets", secret + ".yaml"), @"secretKey: (\S+)").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void Every_deployment_is_covered_and_there_are_fifteen()
    {
        var found = Directory.GetDirectories(Path.Combine(K8s, "base"))
            .Where(d => System.IO.File.Exists(Path.Combine(d, "deployment.yaml")))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);

        Assert.Equal(Deployments.Order(StringComparer.Ordinal), found);
    }

    [Fact]
    public void Each_deployment_references_only_its_own_secret()
    {
        foreach (var deployment in Deployments)
        {
            foreach (var file in new[] { "deployment.yaml", "wiring-patch.yaml" })
            {
                var path = Path.Combine(K8s, "base", deployment, file);
                if (!System.IO.File.Exists(path))
                    continue;

                var names = Regex.Matches(System.IO.File.ReadAllText(path), @"(?m)^\s+name: (\S+-secrets)\s*$").Select(m => m.Groups[1].Value).Distinct().ToList();
                Assert.All(names, name => Assert.Equal($"{deployment}-secrets", name));
                if (file == "deployment.yaml")
                    Assert.NotEmpty(names);
            }
        }
    }

    [Fact]
    public void The_shared_secret_is_gone()
    {
        var hits = Directory.GetFiles(Path.Combine(RepoPaths.Root, "deploy"), "*", SearchOption.AllDirectories)
            .Where(f => System.IO.File.ReadAllText(f).Contains("hr-app-secrets", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(hits);
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void The_overlay_has_one_external_secret_per_service_and_the_migration_job(string overlay)
    {
        var kustomization = Read("overlays", overlay, "external-secrets", "kustomization.yaml");

        foreach (var deployment in Deployments)
        {
            Assert.Contains($"- {deployment}-secrets.yaml", kustomization);
            Assert.Contains($"name: {deployment}-secrets", Read("overlays", overlay, "external-secrets", $"{deployment}-secrets.yaml"));
        }

        Assert.Contains("- hr-migrate-secrets.yaml", kustomization);
        Assert.Contains("- external-secrets", Read("overlays", overlay, "kustomization.yaml"));
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void What_makes_a_proof_or_manages_Keycloak_sits_in_one_secret(string overlay)
    {
        var holders = (string key) => Deployments.Where(d => SecretKeys(overlay, $"{d}-secrets").Contains(key)).ToList();

        Assert.Equal(["hr-gateway"], holders("GatewayCallerProof__PrivateKeyPem"));
        Assert.Equal(["auth-directory-web"], holders("Keycloak__DirectorySync__AdminClientSecret"));
        Assert.Equal(["hr-gateway"], holders("Keycloak__ClientSecret"));
        Assert.Equal(["file-storage-web"], holders("FileStorage__SecretKey"));
        Assert.Equal(["file-storage-web"], holders("FileStorage__AccessKey"));
        Assert.Equal(["hr-core-web"], holders("Recruitment__Portal__OtpHmacKey"));
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void No_service_holds_the_administrators_or_a_connection_string_of_another(string overlay)
    {
        foreach (var deployment in Deployments)
        {
            var keys = SecretKeys(overlay, $"{deployment}-secrets");
            Assert.DoesNotContain("POSTGRES_PASSWORD", keys);
            Assert.DoesNotContain("RABBITMQ_DEFAULT_PASS", keys);
            Assert.Single(keys, k => k.StartsWith("ConnectionStrings__", StringComparison.Ordinal));
            Assert.True(keys.Count(k => k.StartsWith("SVC_", StringComparison.Ordinal)) <= 1, deployment);
        }
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void Every_module_has_its_broker_user_and_the_public_key_and_the_gateway_has_neither(string overlay)
    {
        foreach (var deployment in Deployments.Where(d => d != "hr-gateway"))
        {
            var keys = SecretKeys(overlay, $"{deployment}-secrets");
            Assert.Contains("RabbitMQ__Username", keys);
            Assert.Contains("RabbitMQ__Password", keys);
            Assert.Contains("GatewayCallerProof__PublicKeyPem", keys);
        }

        var gateway = SecretKeys(overlay, "hr-gateway-secrets");
        Assert.DoesNotContain("RabbitMQ__Password", gateway);
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void Each_broker_user_is_read_from_its_own_path(string overlay)
    {
        var paths = Deployments.Where(d => d != "hr-gateway")
            .Select(d => Regex.Match(Read("overlays", overlay, "external-secrets", $"{d}-secrets.yaml"), @"rabbitmq/([a-z-]+)").Groups[1].Value)
            .ToList();

        Assert.Equal(paths.Count, paths.Distinct().Count());
    }

    [Fact]
    public void The_migration_job_alone_reads_every_connection_string()
    {
        Assert.Contains("name: hr-migrate-secrets", Read("base", "migrate-job.yaml"));
        Assert.Equal(15, SecretKeys("production-ha", "hr-migrate-secrets").Count(k => k.StartsWith("ConnectionStrings__", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void The_dev_placeholders_are_removed_by_label(string overlay)
    {
        var kustomization = Read("overlays", overlay, "kustomization.yaml");

        Assert.Contains("labelSelector: hrmanagement.io/secret-source=dev-only-placeholder", kustomization);
        Assert.Contains("$patch: delete", Read("overlays", overlay, "external-secrets", "delete-placeholder.yaml"));
    }

    [Fact]
    public void Dev_placeholders_hold_replace_me_only_so_the_guard_refuses_them()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(K8s, "base"), "*secret-placeholder.yaml", SearchOption.AllDirectories))
        {
            var text = System.IO.File.ReadAllText(file);
            Assert.Contains("hrmanagement.io/secret-source: dev-only-placeholder", text);
            Assert.All(
                Regex.Matches(text, @"(?m)^  [A-Za-z0-9_]+: ""(.*)""\s*$").Select(m => m.Groups[1].Value),
                value => Assert.Equal("replace-me", value));
        }
    }

    [Fact]
    public void No_key_material_is_in_a_manifest()
    {
        foreach (var file in Directory.GetFiles(K8s, "*.yaml", SearchOption.AllDirectories))
            Assert.DoesNotContain("BEGIN PRIVATE KEY", System.IO.File.ReadAllText(file));
    }
}
