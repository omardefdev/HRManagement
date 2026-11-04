using System.Reflection;
using System.Runtime.Loader;
using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-42 §8: <c>api/internal/*</c> is exempt from the gateway proof, so it may serve service accounts alone. This walks the controllers
/// of every <c>*.Web</c> assembly and of the gateway, from their build output: a new internal route with a user policy fails here.
/// </summary>
public class S2s042InternalRoutesAreServiceOnlyTests
{
    private const string Prefix = "api/internal";

    private static readonly HashSet<string> ServicePolicyNames = typeof(ServicePolicies)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true })
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> Violations(Type controller)
    {
        var violations = new List<string>();
        var classRoutes = controller.GetCustomAttributes<RouteAttribute>(true).Select(r => r.Template).ToList();
        var classPolicies = controller.GetCustomAttributes<AuthorizeAttribute>(true).Select(a => a.Policy).ToList();

        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var verbs = action.GetCustomAttributes<HttpMethodAttribute>(true).ToList();
            if (verbs.Count == 0)
                continue;

            var templates = classRoutes.Count > 0
                ? classRoutes.SelectMany(route => verbs.Select(v => $"{route}/{v.Template}"))
                : verbs.Select(v => v.Template ?? string.Empty);
            templates = templates.Concat(action.GetCustomAttributes<RouteAttribute>(true).Select(r => r.Template));
            if (!templates.Any(t => t.TrimStart('/').StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            var name = $"{controller.FullName}.{action.Name}";
            if (action.GetCustomAttribute<AllowAnonymousAttribute>(true) is not null
                || controller.GetCustomAttribute<AllowAnonymousAttribute>(true) is not null)
            {
                violations.Add($"{name}: anonymous on an internal route");
                continue;
            }

            var policies = classPolicies.Concat(action.GetCustomAttributes<AuthorizeAttribute>(true).Select(a => a.Policy)).ToList();
            if (!policies.Any(p => p is not null && ServicePolicyNames.Contains(p)))
                violations.Add($"{name}: no ServicePolicies policy ({string.Join(", ", policies.Select(p => p ?? "<none>"))})");
        }

        return violations;
    }

    private static string BuildConfiguration() =>
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";

    /// <summary>Every web project in src, loaded from its own output, with its dependencies probed beside it.</summary>
    private static IEnumerable<Assembly> WebAssemblies()
    {
        var src = Path.Combine(RepoPaths.Root, "src");
        var projects = Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => Path.GetFileNameWithoutExtension(p).EndsWith(".Web", StringComparison.Ordinal)
                        || Path.GetFileNameWithoutExtension(p) == "HRManagement.Gateway")
            .ToList();
        Assert.Equal(15, projects.Count);

        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var dll = Path.Combine(Path.GetDirectoryName(project)!, "bin", BuildConfiguration(), "net10.0", name + ".dll");
            Assert.True(System.IO.File.Exists(dll), $"{dll} is not built: this guard reads every web assembly");

            var context = new AssemblyLoadContext(name, isCollectible: true);
            var directory = Path.GetDirectoryName(dll)!;
            context.Resolving += (loadContext, assemblyName) =>
            {
                var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                return System.IO.File.Exists(candidate) ? loadContext.LoadFromAssemblyPath(candidate) : null;
            };
            yield return context.LoadFromAssemblyPath(dll);
        }
    }

    private static IEnumerable<Type> Controllers(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        return types.OfType<Type>().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));
    }

    [Fact]
    public void Every_internal_route_of_every_web_assembly_is_for_services_only()
    {
        var checkedControllers = 0;
        var violations = new List<string>();
        foreach (var assembly in WebAssemblies())
        {
            foreach (var controller in Controllers(assembly))
            {
                checkedControllers++;
                violations.AddRange(Violations(controller));
            }
        }

        Assert.True(checkedControllers > 50, $"only {checkedControllers} controllers were read: the guard is not looking");
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_guard_sees_the_internal_controllers_it_is_meant_to_see()
    {
        var names = WebAssemblies().SelectMany(Controllers)
            .Where(c => c.GetCustomAttributes<RouteAttribute>(true).Any(r => r.Template.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "PolicyEnforceInternalController", "CodeGenerationInternalController", "EmployeeLifecycleInternalController",
                     "ServiceRegimeInternalController", "DsarInternalController", "PayGradeLookupInternalController", "DirectoryInternalSyncController"
                 })
            Assert.Contains(expected, names);
    }

    [Route("api/internal/sample")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    private sealed class UserPolicySample : ControllerBase
    {
        [HttpGet("x")]
        public IActionResult Get() => Ok();
    }

    [Route("api/internal/sample")]
    [Authorize]
    private sealed class NoPolicySample : ControllerBase
    {
        [HttpGet("x")]
        public IActionResult Get() => Ok();
    }

    [Route("api/internal/sample")]
    [Authorize(Policy = ServicePolicies.CodeGenerate)]
    private sealed class AnonymousSample : ControllerBase
    {
        [HttpGet("x")]
        [AllowAnonymous]
        public IActionResult Get() => Ok();
    }

    [Route("api/sample")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    private sealed class PublicSample : ControllerBase
    {
        [HttpGet("x")]
        public IActionResult Get() => Ok();
    }

    private sealed class ActionRouteSample : ControllerBase
    {
        [HttpPost("api/internal/sample/y")]
        [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
        public IActionResult Post() => Ok();
    }

    [Route("api/internal/sample")]
    [Authorize(Policy = ServicePolicies.CodeGenerate)]
    private sealed class ServiceSample : ControllerBase
    {
        [HttpGet("x")]
        public IActionResult Get() => Ok();
    }

    [Theory]
    [InlineData(typeof(UserPolicySample))]
    [InlineData(typeof(NoPolicySample))]
    [InlineData(typeof(AnonymousSample))]
    [InlineData(typeof(ActionRouteSample))]
    public void A_sample_controller_that_lets_a_user_in_fails_the_guard(Type sample) =>
        Assert.NotEmpty(Violations(sample));

    [Theory]
    [InlineData(typeof(ServiceSample))]
    [InlineData(typeof(PublicSample))]
    public void A_service_route_and_a_public_route_pass(Type sample) =>
        Assert.Empty(Violations(sample));
}
