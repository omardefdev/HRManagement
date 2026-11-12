using System.Data.Common;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>What the generated scope test needs from a service's in-process host.</summary>
public interface IIam052ScopeHost
{
    Assembly WebAssembly { get; }

    OrgScopeKeyRegistry Registry { get; }

    /// <summary>The restricted user's unit (his scope) and another branch's.</summary>
    Guid OwnUnit { get; }

    Guid ForeignUnit { get; }

    /// <summary>One record of each registered entity type in each branch.</summary>
    IReadOnlyDictionary<Type, (Guid Own, Guid Foreign)> Records { get; }

    HttpClient Restricted();

    HttpClient Central();

    /// <summary>A GET page with a form, to read the antiforgery token a POST needs.</summary>
    string TokenPage { get; }

    Task<T> WithDbAsync<T>(Func<DbContext, Task<T>> work);
}

/// <summary>
/// PLAN-52 (P3, P4): the integration test generated from the registry. For every action of an <see cref="OrgScopedAttribute"/> controller:
/// <list type="bullet">
/// <item>each keyed id (route, query, form, a bound model's property, a list) is sent with another branch's record of its type while every
/// other keyed id is the user's own: the answer is the stance's (404, or the kept 403), and nothing changes in the database (the target
/// record and the row count of every seeded type);</item>
/// <item>a GET without a keyed id is a list: its body holds no foreign record id of the controller's type.</item>
/// </list>
/// A 403 where the scope's answer is not 403 (or a 403 that may come from a policy) is told apart by a control request of a central user with
/// the same roles: if he is refused too, the action is out of the roles' reach and not counted. A controller with no exercised action fails.
/// </summary>
public static class Iam052ScopeHttp
{
    private static readonly Regex TokenPattern = new("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex RouteToken = new("\\{([A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\\??\\}", RegexOptions.Compiled);

    public sealed record Outcome(IReadOnlyList<string> Offenders, IReadOnlyList<string> Exercised, IReadOnlyList<string> Skipped);

    public static async Task<Outcome> RunAsync(IIam052ScopeHost host)
    {
        var offenders = new List<string>();
        var exercised = new List<string>();
        var skipped = new List<string>();
        using var restricted = host.Restricted();
        using var central = host.Central();
        var restrictedToken = await TokenAsync(restricted, host.TokenPage);
        var centralToken = await TokenAsync(central, host.TokenPage);

        foreach (var controller in Controllers(host.WebAssembly))
        {
            var controllerExercised = false;
            foreach (var method in Actions(controller))
            {
                if (OrgScopeActionStance.For(controller, method) is not { Stance: OrgScopedAttribute } stance)
                    continue;
                var verb = Verb(method);
                var name = $"{controller.Name}.{method.Name}";
                var keyedSlots = OrgScopeIds.Slots(controller, method, host.Registry, stance.EntityType).Where(s => s.Rule is { IsKey: true }).ToList();
                var unseeded = keyedSlots
                    .Where(s => s.Rule!.EntityType != typeof(OrgScopeUnit) && !host.Records.ContainsKey(s.Rule.EntityType!))
                    .Select(s => s.Rule!.EntityType!.Name)
                    .Distinct()
                    .ToList();
                if (unseeded.Count > 0)
                {
                    // A random id for an unseeded type would make every other check pass for the wrong reason.
                    offenders.Add($"{name}: no seed for {string.Join(", ", unseeded)}");
                    continue;
                }

                var slots = keyedSlots;

                if (slots.Count == 0)
                {
                    if (verb != "GET" || method.GetParameters().Any(p => OrgScopeIds.IsId(p.ParameterType) && p.Name == "id"))
                        continue;
                    var (status, body) = await SendAsync(restricted, verb, controller, method, host, slots, target: null, restrictedToken);
                    if (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                    {
                        skipped.Add($"{name}: list refused to the roles");
                        continue;
                    }

                    if (stance.EntityType is { } listed && host.Records.TryGetValue(listed, out var pair)
                        && body.Contains(pair.Foreign.ToString("D"), StringComparison.OrdinalIgnoreCase))
                        offenders.Add($"{name}: the list shows another branch's {listed.Name} {pair.Foreign}");
                    exercised.Add($"{name} (list, {(int)status})");
                    controllerExercised = true;
                    continue;
                }

                foreach (var target in slots)
                {
                    var targetType = target.Rule!.EntityType!;
                    var foreign = targetType == typeof(OrgScopeUnit) ? host.ForeignUnit : host.Records[targetType].Foreign;
                    var before = await StateAsync(host, targetType, foreign);
                    var (status, _) = await SendAsync(restricted, verb, controller, method, host, slots, target, restrictedToken);
                    var after = await StateAsync(host, targetType, foreign);
                    var expected = stance.Answer == OrgScopeAnswer.Forbid ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound;
                    var label = $"{name}({target.Path}={targetType.Name})";

                    if (before != after)
                        offenders.Add($"{label}: the database changed");
                    if (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                    {
                        // Is it the roles (a policy before the filter) or the scope? A central user with the same roles and unknown ids tells.
                        var (control, _) = await SendAsync(central, verb, controller, method, host, slots, target: null, centralToken, unknownIds: true);
                        if (control is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                        {
                            skipped.Add($"{label}: refused to the roles ({(int)control} for a central user)");
                            continue;
                        }
                    }

                    if (status != expected)
                        offenders.Add($"{label}: {(int)status}, expected {(int)expected}");
                    exercised.Add($"{label} -> {(int)status}");
                    controllerExercised = true;
                }
            }

            if (!controllerExercised && Actions(controller).Any(m => OrgScopeActionStance.For(controller, m)?.Stance is OrgScopedAttribute))
                offenders.Add($"{controller.Name}: no action could be exercised (a seed or a route the generator lacks)");
        }

        return new Outcome(offenders, exercised, skipped);
    }

    private static IEnumerable<Type> Controllers(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !m.IsDefined(typeof(NonActionAttribute)) && m.Name != nameof(IDisposable.Dispose))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.GetParameters().Length);

    private static string Verb(MethodInfo method)
    {
        var verbs = method.GetCustomAttributes(inherit: true).OfType<IActionHttpMethodProvider>().SelectMany(p => p.HttpMethods).ToList();
        if (verbs.Count == 0)
            return "GET";
        return verbs.FirstOrDefault(v => !string.Equals(v, "GET", StringComparison.OrdinalIgnoreCase))?.ToUpperInvariant() ?? "GET";
    }

    private static async Task<string> TokenAsync(HttpClient client, string page)
    {
        var response = await client.GetAsync(page);
        var html = await response.Content.ReadAsStringAsync();
        var match = TokenPattern.Match(html);
        Assert.True(match.Success, $"No antiforgery token on {page} ({(int)response.StatusCode}).");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>The target record's row and the row count of every seeded type: what "nothing changed" compares.</summary>
    private static Task<string> StateAsync(IIam052ScopeHost host, Type target, Guid id) =>
        host.WithDbAsync(async db =>
        {
            var state = new StringBuilder();
            if (target != typeof(OrgScopeUnit))
                state.Append(await RowAsync(db, target, id));
            foreach (var type in host.Records.Keys.OrderBy(t => t.Name, StringComparer.Ordinal))
                state.Append('|').Append(type.Name).Append('=').Append(await ScalarAsync(db, $"SELECT count(*) FROM \"{Table(db, type)}\""));
            return state.ToString();
        });

    private static string Table(DbContext db, Type type) =>
        db.Model.FindEntityType(type)?.GetTableName() ?? throw new InvalidOperationException($"{type.Name} is not mapped");

    private static async Task<string> RowAsync(DbContext db, Type type, Guid id)
    {
        var key = db.Model.FindEntityType(type)!.FindPrimaryKey()!.Properties[0].GetColumnName();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{Table(db, type)}\" WHERE \"{key}\" = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = db.Database.IsSqlite() ? id.ToString("D").ToUpperInvariant() : id;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        var row = new StringBuilder();
        while (await reader.ReadAsync())
            for (var i = 0; i < reader.FieldCount; i++)
                row.Append(reader.GetName(i)).Append(':').Append(reader.IsDBNull(i) ? "null" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        return row.ToString();
    }

    private static async Task<string?> ScalarAsync(DbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Guid Value(IIam052ScopeHost host, OrgScopeIdSlot slot, OrgScopeIdSlot? target, bool unknownIds)
    {
        if (unknownIds)
            return Guid.NewGuid();
        var type = slot.Rule?.EntityType;
        if (type == typeof(OrgScopeUnit))
            return slot == target ? host.ForeignUnit : host.OwnUnit;
        if (type is not null && host.Records.TryGetValue(type, out var pair))
            return slot == target ? pair.Foreign : pair.Own;
        return Guid.NewGuid();
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpClient client, string verb, Type controller, MethodInfo method, IIam052ScopeHost host,
        IReadOnlyList<OrgScopeIdSlot> keyed, OrgScopeIdSlot? target, string token, bool unknownIds = false)
    {
        var all = OrgScopeIds.Slots(controller, method, host.Registry, OrgScopeActionStance.For(controller, method)?.EntityType);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var fromBody = method.GetParameters().FirstOrDefault(p => p.IsDefined(typeof(FromBodyAttribute)));
        // A route id and a model's Id bind from the same key: every slot of the target's name carries the target's (foreign) value, as a
        // real request would; otherwise the own value of the other slot would win and the check would not test the foreign id at all.
        var targetName = target is null ? null : target.Property?.Name ?? target.Parameter.Name!;
        foreach (var slot in all)
        {
            var bindingName = slot.Property?.Name ?? slot.Parameter.Name!;
            var effectiveTarget = target is not null && string.Equals(bindingName, targetName, StringComparison.OrdinalIgnoreCase) ? slot : null;
            var id = Value(host, slot, effectiveTarget, unknownIds);
            if (effectiveTarget is not null && target!.Rule?.EntityType != slot.Rule?.EntityType)
                id = Value(host, target, target, unknownIds);
            if (slot.Property is not null && slot.Parameter == fromBody)
                body[slot.Property.Name] = slot.IsList ? new[] { id } : id;
            else
                values[slot.Property?.Name ?? slot.Parameter.Name!] = id.ToString("D");
        }

        var (path, used) = Path(controller, method, values);
        foreach (var key in used)
            values.Remove(key);

        HttpResponseMessage response;
        if (verb == "GET")
        {
            var query = string.Join("&", values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            response = await client.GetAsync(query.Length == 0 ? path : $"{path}?{query}");
        }
        else if (fromBody is not null)
        {
            var query = string.Join("&", values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            using var request = new HttpRequestMessage(new HttpMethod(verb), query.Length == 0 ? path : $"{path}?{query}")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("RequestVerificationToken", token);
            response = await client.SendAsync(request);
        }
        else
        {
            var form = values.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)).ToList();
            form.Add(new("__RequestVerificationToken", token));
            using var request = new HttpRequestMessage(new HttpMethod(verb), path) { Content = new FormUrlEncodedContent(form) };
            response = await client.SendAsync(request);
        }

        using (response)
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The URL of the action: its attribute route with its tokens filled, else <c>/{controller}/{action}[/{id}]</c>.</summary>
    private static (string Path, IReadOnlyList<string> Used) Path(Type controller, MethodInfo method, IReadOnlyDictionary<string, string> values)
    {
        var controllerName = controller.Name.EndsWith("Controller", StringComparison.Ordinal) ? controller.Name[..^10] : controller.Name;
        var actionName = method.GetCustomAttribute<ActionNameAttribute>()?.Name ?? method.Name;
        var classRoute = controller.GetCustomAttributes<RouteAttribute>(inherit: true).FirstOrDefault()?.Template;
        var actionRoute = method.GetCustomAttributes(inherit: true).OfType<IRouteTemplateProvider>()
            .Select(r => r.Template).FirstOrDefault(t => t is not null);
        string template;
        if (actionRoute is not null && (actionRoute.StartsWith('/') || actionRoute.StartsWith("~/", StringComparison.Ordinal)))
            template = actionRoute.TrimStart('~');
        else if (classRoute is not null)
            template = "/" + classRoute.TrimEnd('/') + (string.IsNullOrEmpty(actionRoute) ? "" : "/" + actionRoute);
        else if (actionRoute is not null)
            template = "/" + actionRoute;
        else
            template = values.ContainsKey("id") ? $"/{controllerName}/{actionName}/{{id}}" : $"/{controllerName}/{actionName}";

        template = template.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
        var used = new List<string>();
        var path = RouteToken.Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            if (values.TryGetValue(name, out var value))
            {
                used.Add(name);
                return value;
            }

            return Guid.NewGuid().ToString("D");
        });
        return (path, used);
    }
}
