using System.Text.RegularExpressions;

namespace HRManagement.TestSupport.Wiring;

/// <summary>
/// PLAN-37 (§2 rule 3, 4): compares a deployment's effective environment with the wiring matrix. Every message names
/// the chain (or overlay), the service and the key.
/// </summary>
public static partial class WiringChecks
{
    /// <summary>Environment keys that are service wiring (URLs and identity, not the secret).</summary>
    [GeneratedRegex(@"^(Integration__\w+|Services__\w+__BaseUrl|HrCore__BaseUrl|HrGateway__InternalBaseUrl|ServiceIdentity__ClientId|ServiceIdentity__Scope)$")]
    public static partial Regex WiringKey();

    public const string SecretKey = "ServiceIdentity__ClientSecret";

    public static IReadOnlyDictionary<string, string> WiringOf(IReadOnlyDictionary<string, string> environment) =>
        environment.Where(kv => WiringKey().IsMatch(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>
    /// A production compose chain sets exactly the matrix wiring for every service and the secret from
    /// <c>${&lt;secretEnv&gt;:?...}</c> (when <paramref name="mandatorySecret"/>).
    /// </summary>
    public static IReadOnlyList<string> CheckComposeChain(
        ServiceWiringMatrix matrix,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> chain,
        string chainName,
        bool mandatorySecret)
    {
        var errors = new List<string>();
        foreach (var service in matrix.Services)
        {
            if (!chain.TryGetValue(service.ComposeService, out var environment))
            {
                errors.Add($"{chainName}: {service.ComposeService} has no environment in the chain");
                continue;
            }

            CompareWiring(errors, $"{chainName}: {service.ComposeService}", ServiceWiringMatrix.ExpectedEnvironment(service), WiringOf(environment));

            if (service.ServiceIdentity && mandatorySecret)
            {
                environment.TryGetValue(SecretKey, out var secret);
                if (secret is null || !secret.StartsWith("${" + service.SecretEnv + ":?", StringComparison.Ordinal))
                    errors.Add($"{chainName}: {service.ComposeService} {SecretKey} must be ${{{service.SecretEnv}:?...}} (was '{secret ?? "<missing>"}')");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> CheckKustomize(ServiceWiringMatrix matrix, KustomizeEffectiveEnvironment k8s, string overlay)
    {
        var errors = new List<string>();
        foreach (var key in k8s.SharedConfigMap.Keys.Where(k => WiringKey().IsMatch(k) || k == SecretKey))
            errors.Add($"{overlay}: hr-app-config must not carry service wiring ({key}); use the service's wiring-patch.yaml");

        foreach (var service in matrix.Services)
        {
            var label = $"{overlay}: {service.K8sDeployment}";
            if (!k8s.Deployments.TryGetValue(service.K8sDeployment, out var environment))
            {
                errors.Add($"{label}: no Deployment");
                continue;
            }

            var expected = ServiceWiringMatrix.ExpectedEnvironment(service);
            if (expected.Count > 0 && !k8s.Patches.Contains($"{service.K8sDeployment}/wiring-patch.yaml"))
                errors.Add($"{label}: wiring-patch.yaml is not listed in base/kustomization.yaml patches");

            var literal = environment
                .Where(kv => WiringKey().IsMatch(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Value ?? $"<{kv.Value}>", StringComparer.Ordinal);
            CompareWiring(errors, label, expected, literal);

            if (service.ServiceIdentity)
            {
                if (!environment.TryGetValue(SecretKey, out var secret) || secret.SecretName != "hr-app-secrets" || secret.SecretKey != service.SecretEnv)
                    errors.Add($"{label}: {SecretKey} must be secretKeyRef hr-app-secrets/{service.SecretEnv} (was {secret?.ToString() ?? "<missing>"})");
                if (!k8s.SecretKeys.Contains(service.SecretEnv!))
                    errors.Add($"{label}: hr-app-secrets has no key {service.SecretEnv}");
            }
        }

        return errors;
    }

    /// <summary>§2 rule 4: the prod-like stack's wiring equals the production chain's, service by service.</summary>
    public static IReadOnlyList<string> CheckParity(
        ServiceWiringMatrix matrix,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> production,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> prodLike)
    {
        var errors = new List<string>();
        foreach (var service in matrix.Services)
        {
            var prod = production.TryGetValue(service.ComposeService, out var p) ? WiringOf(p) : new Dictionary<string, string>();
            var like = prodLike.TryGetValue(service.ComposeService, out var l) ? WiringOf(l) : new Dictionary<string, string>();
            foreach (var key in prod.Keys.Union(like.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                prod.TryGetValue(key, out var a);
                like.TryGetValue(key, out var b);
                if (a != b)
                    errors.Add($"prod-like: {service.ComposeService} {key}='{b ?? "<missing>"}' but production has '{a ?? "<missing>"}'");
            }
        }

        return errors;
    }

    private static void CompareWiring(List<string> errors, string label, IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        foreach (var (key, value) in expected)
        {
            if (!actual.TryGetValue(key, out var found))
            {
                errors.Add($"{label} lacks {key} (expected {value})");
                continue;
            }

            var same = key == "ServiceIdentity__Scope"
                ? ScopeSet(found).SetEquals(ScopeSet(value)) && ScopeSet(found).Count == found.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
                : found == value;
            if (!same)
                errors.Add($"{label} {key}='{found}', expected '{value}'");
        }

        foreach (var key in actual.Keys.Where(k => !expected.ContainsKey(k)))
            errors.Add($"{label} sets {key}={actual[key]}, which the matrix does not list");
    }

    private static HashSet<string> ScopeSet(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The chain file lists from <c>scripts/vps/compose-chains.ps1</c> (the definitions <c>lint-compose.ps1</c> and
    /// <c>int037-wiring-check.ps1</c> use), so the unit tests never keep their own copy.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ComposeChains(string repoRoot)
    {
        var text = File.ReadAllText(Path.Combine(repoRoot, "scripts", "vps", "compose-chains.ps1"));
        static List<string> Files(string body) =>
            Regex.Matches(body, @"'(docker-compose[\w.-]*\.yml)'").Select(m => m.Groups[1].Value).ToList();
        var production = Regex.Match(text, @"(?s)function Get-HrmProductionChains \{(.*?)\n\}");
        var prodLike = Regex.Match(text, @"(?s)function Get-HrmProdLikeChain \{(.*?)\n\}");
        if (!production.Success || !prodLike.Success)
            throw new InvalidDataException("compose-chains.ps1: chain functions not found");

        var prodBody = production.Groups[1].Value;
        var baseFiles = Files(Regex.Match(prodBody, @"\$production = @\(([^)]*)\)").Groups[1].Value);
        var singleHostExtra = Files(Regex.Match(prodBody, @"single-host'[^\n]*").Value).Where(f => !baseFiles.Contains(f)).ToList();
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["production"] = baseFiles,
            ["single-host"] = baseFiles.Concat(singleHostExtra).ToList(),
            ["prodlike"] = Files(prodLike.Groups[1].Value)
        };
    }
}
