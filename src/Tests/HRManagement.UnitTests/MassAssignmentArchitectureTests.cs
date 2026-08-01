using System.Reflection;
using HRManagement.SharedKernel.Entities;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.UnitTests;

/// <summary>
/// Controller actions must not model-bind persistence entities.
/// <para>
/// <see cref="BaseEntity"/> exposes <c>Id</c>, <c>TenantId</c>, <c>CreatedAt</c> and
/// <c>UpdatedAt</c> as public settable properties, and entities add their own privileged fields on
/// top: approval status, approver identity, leave entitlement, monetary amounts, role permissions.
/// Binding the entity hands all of them to the caller. That is one defect class, not many: the
/// <c>Status</c>/<c>ApprovedBy</c> bypass and the cross-tenant write were both instances of it.
/// </para>
/// <para>
/// Actions that need to accept data should take a view model carrying only the fields a user may
/// supply. Where an entity type is still bound, the action must be listed in
/// <see cref="KnownEntityBindingActions"/> with the reason, so the debt is visible and finite
/// rather than silently growing.
/// </para>
/// </summary>
public class MassAssignmentArchitectureTests
{
    /// <summary>
    /// Actions still binding an entity, each pinned deliberately. The list may shrink, never grow:
    /// a new entry means a new way for a client to set a field the server owns.
    /// </summary>
    private static readonly HashSet<string> KnownEntityBindingActions = new(StringComparer.Ordinal)
    {
    };

    private static IEnumerable<Assembly> WebAssemblies()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        return loaded.Where(a => a.GetName().Name?.EndsWith(".Web", StringComparison.Ordinal) == true);
    }

    /// <summary>Forces the web assemblies into the load context so reflection sees the controllers.</summary>
    private static void EnsureWebAssembliesLoaded()
    {
        var probes = new[]
        {
            "HR.Core.Web", "HR.Contracts.Web", "Payroll.Web", "Compliance.Web",
            "HR.Movements.Web", "IDCard.Web", "Admin.Logistics.Web", "IT.Infra.Web",
            "Auth.Directory.Web", "HR.Health.Web"
        };

        foreach (var name in probes)
        {
            try
            {
                Assembly.Load(name);
            }
            catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException)
            {
                // Assembly is not referenced by the test project; the scan simply skips it.
            }
        }
    }

    [Fact]
    public void No_controller_action_binds_a_persistence_entity()
    {
        EnsureWebAssembliesLoaded();

        var controllers = WebAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(controllers);

        var violations = new List<string>();

        foreach (var controller in controllers)
        {
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialService());

            foreach (var action in actions)
            {
                foreach (var parameter in action.GetParameters())
                {
                    if (!typeof(BaseEntity).IsAssignableFrom(parameter.ParameterType))
                        continue;

                    var key = $"{controller.FullName}.{action.Name}";
                    if (KnownEntityBindingActions.Contains(key))
                        continue;

                    violations.Add($"{key}({parameter.ParameterType.Name} {parameter.Name})");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Controller actions must bind a view model, not a persistence entity. "
            + "Entities expose Id/TenantId/CreatedAt plus their own privileged fields, all of which "
            + "become client-settable once bound. Offending actions:\n"
            + string.Join("\n", violations.Distinct().OrderBy(v => v, StringComparer.Ordinal)));
    }

    [Fact]
    public void Pinned_exceptions_all_still_exist()
    {
        EnsureWebAssembliesLoaded();

        var actual = WebAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetParameters().Any(p => typeof(BaseEntity).IsAssignableFrom(p.ParameterType)))
                .Select(m => $"{t.FullName}.{m.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        // A stale entry hides the fact that the debt was paid down, so the list stays honest.
        var stale = KnownEntityBindingActions.Where(k => !actual.Contains(k)).ToList();

        Assert.True(
            stale.Count == 0,
            "These pinned exceptions no longer bind an entity and should be removed from the list:\n"
            + string.Join("\n", stale.OrderBy(s => s, StringComparer.Ordinal)));
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}

internal static class MethodInfoExtensions
{
    /// <summary>Property accessors and compiler-generated members are not actions.</summary>
    public static bool IsSpecialService(this MethodInfo method) =>
        method.IsSpecialName || method.DeclaringType == typeof(object);
}
