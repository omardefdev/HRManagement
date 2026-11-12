using System.Reflection;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace HRManagement.ArchitectureTests;

/// <summary>One MVC action as the architecture tests see it: its verbs, its policies (action and class) and its overrides.</summary>
public sealed record ActionInfo(
    string Service,
    Type Controller,
    MethodInfo Method,
    IReadOnlyList<string> Verbs,
    IReadOnlyList<string> ActionPolicies,
    IReadOnlyList<string> ClassPolicies,
    bool RolesOnly,
    bool Anonymous,
    string? ActionOverride,
    string? ClassOverride,
    IReadOnlyList<string> RouteTemplates)
{
    public static readonly IReadOnlySet<string> WriteVerbs = new HashSet<string>(["POST", "PUT", "PATCH", "DELETE"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The controller's name without the <c>Controller</c> suffix.</summary>
    public string ControllerName => Controller.Name.EndsWith("Controller", StringComparison.Ordinal)
        ? Controller.Name[..^"Controller".Length]
        : Controller.Name;

    /// <summary>The key of the ceiling lists: <c>Controller.Method</c>.</summary>
    public string Key => $"{ControllerName}.{Method.Name}";

    public bool IsWrite => Verbs.Any(WriteVerbs.Contains);

    /// <summary>A policy named on the action or its class, or <c>[AllowAnonymous]</c>.</summary>
    public bool HasExplicitPolicy => Anonymous || ActionPolicies.Count > 0 || ClassPolicies.Count > 0;

    public IEnumerable<string> AllPolicies => ClassPolicies.Concat(ActionPolicies);

    /// <summary>The override the module filter applies: the action's own, else its class's.</summary>
    public string? EffectiveOverride => ActionOverride ?? ClassOverride;
}

/// <summary>Reads the actions of the web assemblies by reflection, the way MVC discovers them.</summary>
public static class ActionCatalog
{
    private static readonly Lazy<IReadOnlyList<ActionInfo>> All = new(() =>
        WebAssemblyCatalog.Entries.SelectMany(e => Read(e.Service, WebAssemblyCatalog.Controllers(e.Service))).ToList());

    public static IReadOnlyList<ActionInfo> Actions => All.Value;

    public static IEnumerable<ActionInfo> Of(string service) => Actions.Where(a => a.Service == service);

    public static IReadOnlyList<ActionInfo> Read(string service, IEnumerable<Type> controllers) =>
        controllers.SelectMany(c => ReadController(service, c)).ToList();

    public static IEnumerable<ActionInfo> ReadController(string service, Type controller)
    {
        var classAuthorize = controller.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().ToList();
        var classAnonymous = controller.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any();
        var classOverride = controller.GetCustomAttributes<ModulePolicyOverrideAttribute>(inherit: true).LastOrDefault()?.Policy;
        var classRoutes = controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(r => r.Template).ToList();

        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(IsAction)
                     .OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var attributes = method.GetCustomAttributes(inherit: true);
            var verbs = attributes.OfType<IActionHttpMethodProvider>().SelectMany(p => p.HttpMethods)
                .Select(v => v.ToUpperInvariant()).Distinct().ToList();
            var authorize = attributes.OfType<IAuthorizeData>().ToList();
            var actionTemplates = attributes.OfType<IRouteTemplateProvider>().Select(r => r.Template ?? string.Empty).ToList();
            var templates = classRoutes.Count > 0 && actionTemplates.All(t => !t.StartsWith('/') && !t.StartsWith("~/", StringComparison.Ordinal))
                ? classRoutes.SelectMany(r => actionTemplates.Count == 0 ? [r] : actionTemplates.Select(t => Combine(r, t))).ToList()
                : actionTemplates.Select(t => t.TrimStart('~').TrimStart('/')).ToList();

            yield return new ActionInfo(
                service,
                controller,
                method,
                verbs,
                Policies(authorize),
                Policies(classAuthorize),
                RolesOnly: authorize.Concat(classAuthorize).Any(a => string.IsNullOrWhiteSpace(a.Policy) && !string.IsNullOrWhiteSpace(a.Roles))
                           && Policies(authorize).Count == 0 && Policies(classAuthorize).Count == 0,
                Anonymous: classAnonymous || attributes.OfType<IAllowAnonymous>().Any(),
                ActionOverride: method.GetCustomAttributes<ModulePolicyOverrideAttribute>(inherit: true).LastOrDefault()?.Policy,
                ClassOverride: classOverride,
                RouteTemplates: templates);
        }
    }

    private static string Combine(string classRoute, string actionTemplate) =>
        string.IsNullOrEmpty(actionTemplate) ? classRoute : $"{classRoute.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";

    private static IReadOnlyList<string> Policies(IEnumerable<IAuthorizeData> authorize) =>
        authorize.Select(a => a.Policy).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>MVC's rule: a public instance method that is not a property accessor, not [NonAction], and not declared by the framework.</summary>
    private static bool IsAction(MethodInfo method)
    {
        if (method.IsSpecialName || method.IsGenericMethodDefinition || method.IsStatic)
            return false;
        var declaring = method.GetBaseDefinition().DeclaringType;
        if (declaring is null || declaring == typeof(object) || declaring == typeof(ControllerBase) || declaring == typeof(Controller))
            return false;
        if (declaring.Assembly == typeof(ControllerBase).Assembly || declaring.Assembly == typeof(Controller).Assembly)
            return false;
        if (method.IsDefined(typeof(NonActionAttribute), inherit: true))
            return false;
        // IDisposable.Dispose on a controller is not an action.
        return !(method.Name == nameof(IDisposable.Dispose) && method.GetParameters().Length == 0);
    }
}
