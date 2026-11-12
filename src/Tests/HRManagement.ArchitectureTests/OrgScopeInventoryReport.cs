using System.Collections;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-52 (P0, P-3): writes the reach matrix and the ids of every action to the folder named by <c>PLAN52_INVENTORY_OUT</c>
/// (<c>reach-matrix.tsv</c>, <c>controllers.tsv</c>, <c>ids.tsv</c>). Without the variable it does nothing: the report is generated on demand,
/// and <c>OrgScopeCoverageTests</c> holds the rules.
/// </summary>
public sealed class OrgScopeInventoryReport
{
    [Fact]
    public void Writes_the_reach_matrix_and_the_ids_when_asked()
    {
        var folder = Environment.GetEnvironmentVariable("PLAN52_INVENTORY_OUT");
        if (string.IsNullOrWhiteSpace(folder))
            return;
        Directory.CreateDirectory(folder);

        var reach = OrgScopeReach.Actions;
        var matrix = new StringBuilder("service\tcontroller\taction\tverb\tpath\tgateway\tdownstream\treachedBy\n");
        foreach (var r in reach)
            matrix.Append($"{r.Action.Service}\t{r.Action.ControllerName}\t{r.Action.Method.Name}\t{r.Verb}\t{r.GatewayPath}\t{r.GatewayPolicy}\t"
                          + $"{string.Join(",", r.DownstreamPolicies)}\t{string.Join(",", r.ReachedBy)}\n");
        global::System.IO.File.WriteAllText(Path.Combine(folder, "reach-matrix.tsv"), matrix.ToString());

        var controllers = new StringBuilder("service\tcontroller\tstatus\treachedBy\tactions\treachedActions\texample\tclassPolicies\tscopeDependencies\n");
        foreach (var group in reach.GroupBy(r => (r.Action.Service, r.Action.Controller)).OrderBy(g => g.Key.Service).ThenBy(g => g.Key.Controller.Name))
        {
            var reached = group.Where(r => r.ReachedBy.Count > 0).ToList();
            var users = reached.SelectMany(r => r.ReachedBy).Distinct().OrderBy(u => u).ToList();
            var example = reached.FirstOrDefault() is { } e
                ? $"{e.Verb} {e.GatewayPath ?? e.Action.Key} via {e.GatewayPolicy ?? "-"} + {string.Join("+", e.DownstreamPolicies)}"
                : "";
            var classPolicies = group.First().Action.ClassPolicies;
            var deps = group.Key.Controller.GetConstructors().SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType.Name)
                .Where(n => n.Contains("Scope", StringComparison.OrdinalIgnoreCase) || n.Contains("Snapshot", StringComparison.OrdinalIgnoreCase))
                .Distinct();
            controllers.Append($"{group.Key.Service}\t{group.First().Action.ControllerName}\t{(reached.Count > 0 ? "actual" : "latent")}\t"
                               + $"{string.Join(",", users)}\t{group.Select(r => r.Action.Method.Name).Distinct().Count()}\t"
                               + $"{reached.Select(r => r.Action.Method.Name).Distinct().Count()}\t{example}\t{string.Join(",", classPolicies)}\t{string.Join(",", deps)}\n");
        }

        global::System.IO.File.WriteAllText(Path.Combine(folder, "controllers.tsv"), controllers.ToString());

        var ids = new StringBuilder("service\tcontroller\taction\tverbs\tname\ttype\twhere\n");
        foreach (var action in ActionCatalog.Actions)
            foreach (var (name, type, where) in IdsOf(action.Method))
                ids.Append($"{action.Service}\t{action.ControllerName}\t{action.Method.Name}\t{string.Join(",", action.Verbs)}\t{name}\t{type}\t{where}\n");
        global::System.IO.File.WriteAllText(Path.Combine(folder, "ids.tsv"), ids.ToString());
    }

    /// <summary>Every Guid, Guid? or list of Guid an action takes: a parameter, or a public settable property of a bound model.</summary>
    public static IEnumerable<(string Name, string Type, string Where)> IdsOf(MethodInfo method)
    {
        foreach (var p in method.GetParameters())
        {
            if (IsId(p.ParameterType) || IsIdList(p.ParameterType))
            {
                yield return (p.Name!, Short(p.ParameterType), "parameter");
                continue;
            }

            if (!IsModel(p.ParameterType))
                continue;
            foreach (var prop in p.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(x => x.CanWrite && x.SetMethod?.IsPublic == true))
                if (IsId(prop.PropertyType) || IsIdList(prop.PropertyType))
                    yield return ($"{p.Name}.{prop.Name}", Short(prop.PropertyType), "model");
        }
    }

    public static bool IsId(Type t) => t == typeof(Guid) || t == typeof(Guid?);

    public static bool IsIdList(Type t) =>
        t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t)
        && (t.IsArray ? IsId(t.GetElementType()!) : t.IsGenericType && t.GetGenericArguments().Length == 1 && IsId(t.GetGenericArguments()[0]));

    public static bool IsModel(Type t) =>
        t.IsClass && t != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(t)
        && t.Namespace?.StartsWith("System", StringComparison.Ordinal) != true
        && t.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) != true;

    private static string Short(Type t) =>
        t == typeof(Guid) ? "Guid" : t == typeof(Guid?) ? "Guid?" : t.IsArray ? "Guid[]" : $"{t.Name.Split('`')[0]}<Guid>";
}
