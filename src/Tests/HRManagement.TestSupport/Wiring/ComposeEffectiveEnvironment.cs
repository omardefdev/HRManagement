using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace HRManagement.TestSupport.Wiring;

/// <summary>
/// PLAN-37 (§2): the effective <c>environment</c> of every service across a chain of compose files, merged in order
/// (the last file wins per key), with YAML anchors and merge keys resolved. Merge keys (<c>&lt;&lt;: *anchor</c>,
/// <c>&lt;&lt;: [*a, *b]</c>) are resolved here with YAML merge semantics (explicit keys win, then earlier anchors): YamlDotNet's
/// <c>MergingParser</c> reports an explicit key that overrides a merged one as a duplicate key. The compose tags
/// <c>!reset</c> and <c>!override</c> on an <c>environment</c> block clear or replace it; elsewhere they are ignored.
/// Values are the raw text before interpolation (<c>${SVC_X:?...}</c> stays as written). The rendered
/// <c>docker compose config</c> in <c>scripts/int037-wiring-check.ps1</c> remains the final judge.
/// </summary>
public static class ComposeEffectiveEnvironment
{
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load(
        string repoRoot,
        IEnumerable<string> files,
        Func<string, string, string>? transform = null) =>
        FromTexts(files.Select(f =>
        {
            var text = File.ReadAllText(Path.Combine(repoRoot, f.Replace('/', Path.DirectorySeparatorChar)));
            return (f, transform is null ? text : transform(f, text));
        }));

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FromTexts(
        IEnumerable<(string Name, string Yaml)> documents)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (name, yaml) in documents)
        {
            var stream = new YamlStream();
            try
            {
                stream.Load(new StringReader(yaml));
            }
            catch (YamlException ex)
            {
                throw new InvalidDataException($"{name}: {ex.Message}", ex);
            }

            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
                continue;
            if (!root.Children.TryGetValue(new YamlScalarNode("services"), out var servicesNode) || servicesNode is not YamlMappingNode services)
                continue;

            foreach (var (serviceKey, serviceNode) in services.Children)
            {
                var service = ((YamlScalarNode)serviceKey).Value!;
                if (serviceNode is not YamlMappingNode serviceMap)
                    continue;
                if (!serviceMap.Children.TryGetValue(new YamlScalarNode("environment"), out var envNode))
                    continue;

                if (!result.TryGetValue(service, out var env))
                    result[service] = env = new Dictionary<string, string>(StringComparer.Ordinal);

                var tag = envNode.Tag.IsEmpty ? null : envNode.Tag.Value;
                if (tag is "!reset" or "!override")
                    env.Clear();

                foreach (var (key, value) in Entries(envNode))
                    env[key] = value;
            }
        }

        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, string>)kv.Value,
            StringComparer.Ordinal);
    }

    private static IEnumerable<(string Key, string Value)> Entries(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
                foreach (var entry in MergedMapping(map))
                    yield return entry;

                break;
            case YamlSequenceNode list:
                foreach (var item in list.Children.OfType<YamlScalarNode>())
                {
                    var text = item.Value ?? "";
                    var eq = text.IndexOf('=');
                    yield return eq < 0 ? (text, "") : (text[..eq], text[(eq + 1)..]);
                }

                break;
        }
    }

    private static IEnumerable<(string Key, string Value)> MergedMapping(YamlMappingNode map)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        void Put(string key, string value, bool overwrite)
        {
            if (merged.ContainsKey(key) && !overwrite)
                return;
            if (!merged.ContainsKey(key))
                order.Add(key);
            merged[key] = value;
        }

        if (map.Children.TryGetValue(new YamlScalarNode("<<"), out var mergeNode))
        {
            IEnumerable<YamlMappingNode> sources = mergeNode is YamlSequenceNode seq
                ? seq.Children.OfType<YamlMappingNode>()
                : mergeNode is YamlMappingNode single ? [single] : [];
            foreach (var source in sources)
            foreach (var (k, v) in MergedMapping(source))
                Put(k, v, overwrite: false);
        }

        foreach (var (k, v) in map.Children)
        {
            var key = ((YamlScalarNode)k).Value!;
            if (key != "<<")
                Put(key, v is YamlScalarNode s ? s.Value ?? "" : "", overwrite: true);
        }

        return order.Select(k => (k, merged[k]));
    }
}
