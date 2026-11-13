using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-67 (P-12 rule 1, by reflection): a <c>&lt;label asp-for="X"&gt;&lt;/label&gt;</c> with no text of its own, or
/// <c>@Html.DisplayNameFor(m =&gt; m.X)</c>, prints the property's display name, which is its English name when the property has no
/// <c>[Display(Name = …)]</c>. The view's <c>@model</c> type is resolved among the loaded assemblies and the property path walked; a
/// missing or non-Arabic name is an <c>english-label</c> finding. A model type that cannot be resolved is counted, never guessed.
/// </summary>
public static partial class InterfaceTextScanner
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<Type>>> TypesBySimpleName = new(() =>
    {
        // The fifteen web assemblies and the project assemblies they reference (domains, view models, the shared kernel).
        foreach (var entry in WebAssemblyCatalog.Entries)
        {
            var web = WebAssemblyCatalog.AssemblyOf(entry.Service);
            foreach (var reference in web.GetReferencedAssemblies())
            {
                try { Assembly.Load(reference); }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException) { }
            }
        }

        var types = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.FullName is { } n && (n.StartsWith("HR", StringComparison.Ordinal) || n.StartsWith("Admin.", StringComparison.Ordinal)
                        || n.StartsWith("Auth.", StringComparison.Ordinal) || n.StartsWith("Compliance", StringComparison.Ordinal)
                        || n.StartsWith("Dev.", StringComparison.Ordinal) || n.StartsWith("File.", StringComparison.Ordinal)
                        || n.StartsWith("IDCard", StringComparison.Ordinal) || n.StartsWith("IT.", StringComparison.Ordinal)
                        || n.StartsWith("Payroll", StringComparison.Ordinal) || n.StartsWith("Records.", StringComparison.Ordinal)))
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).Cast<Type>(); }
            })
            .Where(t => !t.Name.StartsWith('<'))
            .ToList();
        return types.GroupBy(t => t.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (IReadOnlyList<Type>)g.ToList(), StringComparer.Ordinal);
    });

    /// <summary>Views whose <c>@model</c> type the asp-for check could not resolve (reported by the inventory, never a pass by guess).</summary>
    public static readonly List<string> UnresolvedModels = [];

    /// <summary>The asp-for and DisplayNameFor labels of one view whose text is not Arabic.</summary>
    public static IEnumerable<(int Index, string Snippet)> EnglishModelLabels(string relativePath, string text, Type? modelType = null)
    {
        var labels = EmptyAspForLabelRegex().Matches(text).Select(m => (m.Index, Expr: m.Groups["expr"].Value))
            .Concat(DisplayNameForRegex().Matches(text).Select(m => (m.Index, Expr: m.Groups["expr"].Value)))
            .ToList();
        if (labels.Count == 0)
            yield break;

        modelType ??= ResolveModelType(text);
        if (modelType is null)
        {
            lock (UnresolvedModels)
                UnresolvedModels.Add(relativePath);
            yield break;
        }

        foreach (var (index, expr) in labels)
        {
            var property = WalkProperty(modelType, expr);
            if (property is null)
                continue; // a loop variable's path, or a dictionary: not this check's to judge
            var name = property.GetCustomAttribute<DisplayAttribute>()?.GetName()
                       ?? property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            if (name is null || !ArabicLetterRegex().IsMatch(name))
                yield return (index, $"asp-for {expr} → «{name ?? property.Name}» ({property.DeclaringType?.Name}.{property.Name} has no Arabic [Display])");
        }
    }

    private static Type? ResolveModelType(string text)
    {
        var m = ModelDirectiveRegex().Match(text);
        if (!m.Success)
            return null;
        var raw = m.Groups["type"].Value.Trim().TrimEnd('?');
        var generic = raw.IndexOf('<');
        if (generic >= 0)
        {
            // IEnumerable<T>, List<T>…: the labels of such a view bind to T through DisplayNameFor(m => m.X) of the element.
            raw = raw[(generic + 1)..raw.LastIndexOf('>')];
        }

        var simple = raw.Split('.').Last();
        if (!TypesBySimpleName.Value.TryGetValue(simple, out var candidates))
            return null;
        var exact = candidates.Where(t => t.FullName == raw).ToList();
        if (exact.Count == 1)
            return exact[0];
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static PropertyInfo? WalkProperty(Type type, string path)
    {
        PropertyInfo? property = null;
        var current = type;
        foreach (var raw in path.Split('.'))
        {
            var segment = IndexerRegex().Replace(raw, "");
            if (segment.Length == 0)
                return null;
            property = current.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
                return null;
            current = property.PropertyType;
            if (raw.Contains('['))
                current = ElementType(current) ?? current;
            current = Nullable.GetUnderlyingType(current) ?? current;
        }

        return property;
    }

    private static Type? ElementType(Type t) =>
        t.IsArray ? t.GetElementType() : t.GetInterfaces().Concat([t]).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];

    [GeneratedRegex(@"^@model\s+(?<type>[^\r\n]+)", RegexOptions.Multiline)]
    private static partial Regex ModelDirectiveRegex();

    [GeneratedRegex(@"<label\b(?=[^>]*\basp-for=""(?<expr>[^""@]+)"")[^>]*>\s*</label>", RegexOptions.IgnoreCase)]
    private static partial Regex EmptyAspForLabelRegex();

    [GeneratedRegex(@"Html\.DisplayNameFor\(\s*\w+\s*=>\s*\w+\.(?<expr>[\w.]+)\s*\)")]
    private static partial Regex DisplayNameForRegex();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex IndexerRegex();

    [GeneratedRegex(@"\p{IsArabic}")]
    private static partial Regex ArabicLetterRegex();
}
