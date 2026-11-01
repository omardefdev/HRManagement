using YamlDotNet.RepresentationModel;

namespace HRManagement.TestSupport.Wiring;

/// <summary>One container environment entry of a Deployment: a literal value or a Secret / ConfigMap reference.</summary>
public sealed record K8sEnvValue(string? Value, string? SecretName = null, string? SecretKey = null, string? ConfigMapName = null, string? ConfigMapKey = null)
{
    public override string ToString() => Value ?? (SecretKey is not null ? $"secret:{SecretName}/{SecretKey}" : $"configmap:{ConfigMapName}/{ConfigMapKey}");
}

/// <summary>
/// PLAN-37 (§2): the effective environment of the <c>app</c> container of each Deployment under
/// <c>deploy/k8s/base/&lt;svc&gt;/</c>: <c>deployment.yaml</c> env, overlaid by <c>wiring-patch.yaml</c> (strategic merge on
/// the env name), plus the keys of the shared <c>hr-app-config</c> ConfigMap and of <c>hr-app-secrets</c> for an
/// overlay (ExternalSecret <c>spec.data[].secretKey</c>, or the base placeholder's <c>stringData</c>).
/// <c>scripts/int037-wiring-check.ps1</c> checks the real <c>kubectl kustomize</c> output.
/// </summary>
public sealed class KustomizeEffectiveEnvironment
{
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, K8sEnvValue>> Deployments { get; init; }
    public required IReadOnlyDictionary<string, string> SharedConfigMap { get; init; }
    public required IReadOnlySet<string> SecretKeys { get; init; }
    public required IReadOnlyList<string> Patches { get; init; }

    public static KustomizeEffectiveEnvironment Load(string repoRoot, string overlay)
    {
        var k8s = Path.Combine(repoRoot, "deploy", "k8s");
        var baseDir = Path.Combine(k8s, "base");
        var deployments = new Dictionary<string, IReadOnlyDictionary<string, K8sEnvValue>>(StringComparer.Ordinal);
        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var deploymentFile = Path.Combine(dir, "deployment.yaml");
            if (!File.Exists(deploymentFile))
                continue;
            var root = LoadMapping(deploymentFile);
            if (Scalar(root, "kind") != "Deployment")
                continue;
            var name = Scalar((YamlMappingNode)root.Children[new YamlScalarNode("metadata")], "name")!;
            var env = ContainerEnv(root);
            var patchFile = Path.Combine(dir, "wiring-patch.yaml");
            if (File.Exists(patchFile))
            {
                foreach (var (key, value) in ContainerEnv(LoadMapping(patchFile)))
                    env[key] = value;
            }

            deployments[name] = env;
        }

        var configMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var cm = LoadMapping(Path.Combine(baseDir, "configmap-app.yaml"));
        if (cm.Children.TryGetValue(new YamlScalarNode("data"), out var data) && data is YamlMappingNode dataMap)
        {
            foreach (var (k, v) in dataMap.Children)
                configMap[((YamlScalarNode)k).Value!] = ((YamlScalarNode)v).Value ?? "";
        }

        var secretKeys = new HashSet<string>(StringComparer.Ordinal);
        var external = Path.Combine(k8s, "overlays", overlay, "external-secrets", "hr-app-secrets.yaml");
        if (File.Exists(external))
        {
            var es = LoadMapping(external);
            var spec = (YamlMappingNode)es.Children[new YamlScalarNode("spec")];
            foreach (var item in ((YamlSequenceNode)spec.Children[new YamlScalarNode("data")]).Children.OfType<YamlMappingNode>())
                secretKeys.Add(Scalar(item, "secretKey")!);
        }
        else
        {
            var placeholder = LoadMapping(Path.Combine(baseDir, "secret-placeholder.yaml"));
            foreach (var (k, _) in ((YamlMappingNode)placeholder.Children[new YamlScalarNode("stringData")]).Children)
                secretKeys.Add(((YamlScalarNode)k).Value!);
        }

        var kustomization = LoadMapping(Path.Combine(baseDir, "kustomization.yaml"));
        var patches = ((YamlSequenceNode)kustomization.Children[new YamlScalarNode("patches")]).Children
            .OfType<YamlMappingNode>()
            .Select(p => Scalar(p, "path")!)
            .ToList();

        return new KustomizeEffectiveEnvironment
        {
            Deployments = deployments,
            SharedConfigMap = configMap,
            SecretKeys = secretKeys,
            Patches = patches
        };
    }

    private static YamlMappingNode LoadMapping(string path)
    {
        var stream = new YamlStream();
        using var reader = new StreamReader(path);
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s ? s.Value : null;

    private static YamlMappingNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v as YamlMappingNode : null;

    private static Dictionary<string, K8sEnvValue> ContainerEnv(YamlMappingNode root)
    {
        var env = new Dictionary<string, K8sEnvValue>(StringComparer.Ordinal);
        var podSpec = Child(Child(Child(root, "spec")!, "template")!, "spec")!;
        var containers = (YamlSequenceNode)podSpec.Children[new YamlScalarNode("containers")];
        var app = containers.Children.OfType<YamlMappingNode>().Single(c => Scalar(c, "name") == "app");
        if (!app.Children.TryGetValue(new YamlScalarNode("env"), out var envNode) || envNode is not YamlSequenceNode list)
            return env;

        foreach (var item in list.Children.OfType<YamlMappingNode>())
        {
            var name = Scalar(item, "name")!;
            var valueFrom = Child(item, "valueFrom");
            if (valueFrom is null)
            {
                env[name] = new K8sEnvValue(Scalar(item, "value") ?? "");
                continue;
            }

            var secret = Child(valueFrom, "secretKeyRef");
            var configMap = Child(valueFrom, "configMapKeyRef");
            env[name] = secret is not null
                ? new K8sEnvValue(null, Scalar(secret, "name"), Scalar(secret, "key"))
                : new K8sEnvValue(null, ConfigMapName: configMap is null ? null : Scalar(configMap, "name"), ConfigMapKey: configMap is null ? null : Scalar(configMap, "key"));
        }

        return env;
    }
}
