using System.Collections;
using System.Reflection;
using System.Security.Claims;
using HRManagement.SharedKernel.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>The user's org scope in one service: HR.Core reads its org units, the others their local <see cref="OrgUnitSnapshot"/>s.</summary>
public interface IOrgScopeProvider
{
    Task<OrgAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct = default);
}

/// <summary>The scope from the service's own <see cref="OrgUnitSnapshot"/>s (materialized paths). An unrestricted user costs no query.</summary>
public sealed class SnapshotOrgScopeProvider<TContext>(TContext db) : IOrgScopeProvider
    where TContext : DbContext
{
    public Task<OrgAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        OrgScopeResolver.GetAllowedOrgUnitIdsAsync(user, db.Set<OrgUnitSnapshot>(), ct: ct);
}

/// <summary>The scope of the current request, computed once per request.</summary>
public static class OrgScopeHttpContextExtensions
{
    private const string ItemKey = "HRManagement.OrgScope.AccessScope";

    /// <summary>The caller's scope (cached in the request); unrestricted when the request has no provider (a controller built by hand).</summary>
    public static async Task<OrgAccessScope> GetOrgScopeAsync(this HttpContext? http)
    {
        if (http is null)
            return OrgAccessScope.Unrestricted();
        if (http.Items.TryGetValue(ItemKey, out var cached) && cached is OrgAccessScope scope)
            return scope;
        var provider = http.RequestServices?.GetService<IOrgScopeProvider>();
        if (provider is null)
            return OrgAccessScope.Unrestricted();
        scope = await provider.GetScopeAsync(http.User, http.RequestAborted);
        http.Items[ItemKey] = scope;
        return scope;
    }

    public static Task<OrgAccessScope> GetOrgScopeAsync(this ControllerBase controller) => controller.HttpContext.GetOrgScopeAsync();
}

/// <summary>An action's effective stance: its own, else its class's (an action-level <see cref="OrgScopedAttribute"/> without a type keeps the
/// class's type).</summary>
public sealed record OrgScopeActionStance(IOrgScopeStance Stance, Type? EntityType, OrgScopeAnswer Answer)
{
    public static OrgScopeActionStance? For(Type controller, MethodInfo method)
    {
        var own = method.GetCustomAttributes(inherit: true).OfType<IOrgScopeStance>().FirstOrDefault();
        var classStance = controller.GetCustomAttributes(inherit: true).OfType<IOrgScopeStance>().FirstOrDefault();
        var stance = own ?? classStance;
        return stance switch
        {
            null => null,
            OrgScopedAttribute scoped => new OrgScopeActionStance(
                scoped, scoped.EntityType ?? (classStance as OrgScopedAttribute)?.EntityType, scoped.OutOfScope),
            _ => new OrgScopeActionStance(stance, null, OrgScopeAnswer.NotFound),
        };
    }
}

/// <summary>One place an action takes an id: a parameter, or a public settable property of a bound model; a single Guid or a list.</summary>
public sealed record OrgScopeIdSlot(string Name, ParameterInfo Parameter, PropertyInfo? Property, bool IsList, OrgScopeIdRule? Rule)
{
    /// <summary><c>parameter</c> or <c>parameter.Property</c>.</summary>
    public string Path => Property is null ? Parameter.Name! : $"{Parameter.Name}.{Property.Name}";

    /// <summary>PLAN-52 (§6, P-11 rule 2): an id by name: <c>id</c> or <c>…Id</c>, or a list <c>ids</c> or <c>…Ids</c>.</summary>
    public bool IsConventional => IsList
        ? Name.Equals("ids", StringComparison.OrdinalIgnoreCase) || Name.EndsWith("Ids", StringComparison.Ordinal)
        : Name.Equals("id", StringComparison.OrdinalIgnoreCase) || Name.EndsWith("Id", StringComparison.Ordinal);
}

/// <summary>Reads the ids of an action and the rule of each: the attribute on the parameter or property, the action's attribute naming it,
/// <c>id</c> (and a model's <c>Id</c>, and <c>ids</c>) as the stance's entity type, else the registry's mapping by name (a list also by its
/// singular name).</summary>
public static class OrgScopeIds
{
    public static IReadOnlyList<OrgScopeIdSlot> Slots(Type controller, MethodInfo method, OrgScopeKeyRegistry? registry, Type? stanceEntity)
    {
        var slots = new List<OrgScopeIdSlot>();
        foreach (var parameter in method.GetParameters())
        {
            if (IsId(parameter.ParameterType) || IsIdList(parameter.ParameterType))
            {
                slots.Add(new OrgScopeIdSlot(parameter.Name!, parameter, null, IsIdList(parameter.ParameterType),
                    RuleOf(controller, method, parameter.Name!, parameter.GetCustomAttributes(inherit: true), registry, stanceEntity)));
                continue;
            }

            if (!IsModel(parameter.ParameterType))
                continue;
            foreach (var property in parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true
                                     && !p.IsDefined(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute), inherit: true)))
            {
                if (!IsId(property.PropertyType) && !IsIdList(property.PropertyType))
                    continue;
                slots.Add(new OrgScopeIdSlot(property.Name, parameter, property, IsIdList(property.PropertyType),
                    RuleOf(controller, method, property.Name, property.GetCustomAttributes(inherit: true), registry, stanceEntity)));
            }
        }

        return slots;
    }

    private static OrgScopeIdRule? RuleOf(
        Type controller, MethodInfo method, string name, object[] attributes, OrgScopeKeyRegistry? registry, Type? stanceEntity)
    {
        if (attributes.OfType<NotOrgScopeKeyAttribute>().FirstOrDefault() is { } notKey)
            return new OrgScopeIdRule(null, notKey.Reason);
        if (attributes.OfType<OrgScopeIdAttribute>().FirstOrDefault() is { } own)
            return new OrgScopeIdRule(own.EntityType, null);

        var isId = name.Equals("id", StringComparison.OrdinalIgnoreCase) || name.Equals("ids", StringComparison.OrdinalIgnoreCase);
        var methodAttributes = method.GetCustomAttributes(inherit: true);
        if (methodAttributes.OfType<NotOrgScopeKeyAttribute>().FirstOrDefault(a => Names(a.Name, name, isId)) is { } actionNotKey)
            return new OrgScopeIdRule(null, actionNotKey.Reason);
        if (methodAttributes.OfType<OrgScopeIdAttribute>().FirstOrDefault(a => Names(a.Name, name, isId)) is { } actionId)
            return new OrgScopeIdRule(actionId.EntityType, null);
        if (isId && stanceEntity is not null)
            return new OrgScopeIdRule(stanceEntity, null);

        var rule = registry?.RuleFor(controller, name);
        if (rule is null && registry is not null && name.EndsWith('s'))
            rule = registry.RuleFor(controller, name[..^1]);
        return rule;
    }

    private static bool Names(string? attributeName, string name, bool isId) =>
        attributeName is null ? isId : string.Equals(attributeName, name, StringComparison.OrdinalIgnoreCase);

    public static bool IsId(Type t) => t == typeof(Guid) || t == typeof(Guid?);

    public static bool IsIdList(Type t) =>
        t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t)
        && (t.IsArray
            ? IsId(t.GetElementType()!)
            : t.IsGenericType && t.GetGenericArguments().Length == 1 && IsId(t.GetGenericArguments()[0]));

    /// <summary>A model the binder fills property by property: a class of the application (not the framework's, not a collection).</summary>
    public static bool IsModel(Type t) =>
        t.IsClass && t != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(t)
        && t.Namespace?.StartsWith("System", StringComparison.Ordinal) != true
        && t.Namespace?.StartsWith("Microsoft", StringComparison.Ordinal) != true;

    /// <summary>The non-empty Guids a slot holds in the bound arguments.</summary>
    public static IEnumerable<Guid> Values(OrgScopeIdSlot slot, IDictionary<string, object?> arguments)
    {
        if (!arguments.TryGetValue(slot.Parameter.Name!, out var argument) || argument is null)
            yield break;
        var value = slot.Property is null ? argument : slot.Property.GetValue(argument);
        switch (value)
        {
            case Guid g when g != Guid.Empty:
                yield return g;
                break;
            case IEnumerable list and not string:
                foreach (var item in list)
                    if (item is Guid element && element != Guid.Empty)
                        yield return element;
                break;
        }
    }
}

/// <summary>
/// PLAN-52 (§1, P-4): the one org-scope filter of a service. For a restricted user only (<see cref="OrgAccessScope.MustFilter"/>); an
/// unrestricted user costs no query. A denied scope, or a central-only action, is refused (403). On an <see cref="OrgScopedAttribute"/> action,
/// every id it takes (route, query, form, a bound model's properties, each element of a list) is resolved through the registry before the
/// action runs, so before any read in the action and any change by a POST: out of scope or no key gives the stance's answer (404 by
/// default, D-52-5). It runs before <c>[ApiController]</c>'s automatic 400 (<see cref="FilterOrder"/>), so a foreign id gets the same answer
/// whatever the body. A recruitment controller keeps its own filter (<c>[RecruitmentOrgScope]</c>); lists are filtered by the actions.
/// </summary>
public sealed class OrgScopeActionFilter<TContext>(TContext db, OrgScopeKeyRegistry registry) : IAsyncActionFilter, IOrderedFilter
    where TContext : DbContext
{
    /// <summary>Below <c>ModelStateInvalidFilter</c>'s -2000.</summary>
    public const int FilterOrder = -3000;

    public int Order => FilterOrder;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || OrgScopeActionStance.For(action.ControllerTypeInfo.AsType(), action.MethodInfo) is not { } stance
            || stance.Stance is not (OrgScopedAttribute or OrgScopeCentralOnlyAttribute))
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        var scope = await http.GetOrgScopeAsync();
        if (!scope.MustFilter)
        {
            await next();
            return;
        }

        if (scope.IsDenied || stance.Stance is OrgScopeCentralOnlyAttribute)
        {
            OrgScopeDenialContext.Record(http, http.User, reason: scope.IsDenied ? "org-scope-denied" : "org-scope-central-only");
            context.Result = new ForbidResult();
            return;
        }

        foreach (var slot in OrgScopeIds.Slots(action.ControllerTypeInfo.AsType(), action.MethodInfo, registry, stance.EntityType))
        {
            if (slot.Rule is not { IsKey: true } rule)
                continue;
            foreach (var id in OrgScopeIds.Values(slot, context.ActionArguments))
            {
                if (await IsInScopeAsync(rule.EntityType!, id, scope, http.RequestAborted))
                    continue;
                OrgScopeDenialContext.Record(http, http.User, reason: "org-scope-denied", resourceType: rule.EntityType!.Name,
                    resourceId: id.ToString("D"));
                context.Result = stance.Answer == OrgScopeAnswer.Forbid ? new ForbidResult() : new NotFoundResult();
                return;
            }
        }

        await next();
    }

    private Task<bool> IsInScopeAsync(Type entityType, Guid id, OrgAccessScope scope, CancellationToken ct)
    {
        if (entityType == typeof(OrgScopeUnit))
            return Task.FromResult(scope.CanAccess(id));
        // An entity type with no key is a configuration defect the coverage test catches; at run time it is out of scope (fail-closed).
        return registry.Keys.TryGetValue(entityType, out var key)
            ? key.IsInScopeAsync(db, id, scope.AllowedOrgUnitIds, ct)
            : Task.FromResult(false);
    }
}

public static class OrgScopeFilterRegistration
{
    /// <summary>
    /// Registers the service's registry (filled by its <c>{Service}OrgScopeKeys.Configure</c>), the filter for every MVC action and, unless
    /// the service registered its own, the scope provider over <typeparamref name="TContext"/>'s org unit snapshots.
    /// </summary>
    public static IMvcBuilder AddOrgScopeFilter<TContext>(this IMvcBuilder mvc, string service, Action<OrgScopeKeyRegistry> configure)
        where TContext : DbContext
    {
        var registry = new OrgScopeKeyRegistry(service);
        configure(registry);
        mvc.Services.AddSingleton(registry);
        mvc.Services.AddScoped<OrgScopeActionFilter<TContext>>();
        mvc.Services.TryAddScopedOrgScopeProvider<TContext>();
        mvc.Services.Configure<MvcOptions>(o => o.Filters.AddService<OrgScopeActionFilter<TContext>>(OrgScopeActionFilter<TContext>.FilterOrder));
        return mvc;
    }

    private static void TryAddScopedOrgScopeProvider<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        if (services.Any(d => d.ServiceType == typeof(IOrgScopeProvider)))
            return;
        services.AddScoped<IOrgScopeProvider, SnapshotOrgScopeProvider<TContext>>();
    }
}
