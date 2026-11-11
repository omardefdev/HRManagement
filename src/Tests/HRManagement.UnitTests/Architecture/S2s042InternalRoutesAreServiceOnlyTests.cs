using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-42 §8: <c>api/internal/*</c> is exempt from the gateway proof, so it may serve service accounts alone. This reads the controllers
/// of every <c>*.Web</c> assembly and of the gateway from their build output: a new internal route with a user policy fails here.
/// The assemblies are read as metadata, not loaded: a loaded assembly would show up in the scans of other architecture tests.
/// </summary>
public class S2s042InternalRoutesAreServiceOnlyTests
{
    private const string Prefix = "api/internal";

    private static readonly HashSet<string> ServicePolicyNames = typeof(ServicePolicies)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true })
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    public sealed record ActionModel(string Name, IReadOnlyList<string> VerbTemplates, IReadOnlyList<string> Routes, IReadOnlyList<string?> Policies, bool Anonymous);

    public sealed record ControllerModel(string Name, IReadOnlyList<string> Routes, IReadOnlyList<string?> Policies, bool Anonymous, IReadOnlyList<ActionModel> Actions);

    public static IReadOnlyList<string> Violations(ControllerModel controller)
    {
        var violations = new List<string>();
        foreach (var action in controller.Actions.Where(a => a.VerbTemplates.Count > 0))
        {
            var templates = controller.Routes.Count > 0
                ? controller.Routes.SelectMany(route => action.VerbTemplates.Select(verb => $"{route}/{verb}"))
                : action.VerbTemplates;
            templates = templates.Concat(action.Routes);
            if (!templates.Any(t => t.TrimStart('/').StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            var name = $"{controller.Name}.{action.Name}";
            if (action.Anonymous || controller.Anonymous)
            {
                violations.Add($"{name}: anonymous on an internal route");
                continue;
            }

            var policies = controller.Policies.Concat(action.Policies).ToList();
            if (!policies.Any(p => p is not null && ServicePolicyNames.Contains(p)))
                violations.Add($"{name}: no ServicePolicies policy ({string.Join(", ", policies.Select(p => p ?? "<none>"))})");
        }

        return violations;
    }

    private static ControllerModel FromType(Type controller) => new(
        controller.FullName!,
        controller.GetCustomAttributes<RouteAttribute>(true).Select(r => r.Template).ToList(),
        controller.GetCustomAttributes<AuthorizeAttribute>(true).Select(a => a.Policy).ToList(),
        controller.GetCustomAttribute<AllowAnonymousAttribute>(true) is not null,
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => new ActionModel(
                m.Name,
                m.GetCustomAttributes<HttpMethodAttribute>(true).Select(v => v.Template ?? string.Empty).ToList(),
                m.GetCustomAttributes<RouteAttribute>(true).Select(r => r.Template).ToList(),
                m.GetCustomAttributes<AuthorizeAttribute>(true).Select(a => a.Policy).ToList(),
                m.GetCustomAttribute<AllowAnonymousAttribute>(true) is not null))
            .ToList());

    private sealed class AttributeTypes : ICustomAttributeTypeProvider<object?>
    {
        public object? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;
        public object? GetSystemType() => null;
        public object? GetSZArrayType(object? elementType) => null;
        public object? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => null;
        public object? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => null;
        public object? GetTypeFromSerializedName(string name) => null;
        public PrimitiveTypeCode GetUnderlyingEnumType(object? type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(object? type) => false;
    }

    private static string AttributeName(MetadataReader reader, CustomAttribute attribute)
    {
        var parent = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default
        };
        return parent.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
            _ => string.Empty
        };
    }

    private sealed record Read(List<string> VerbTemplates, List<string> Routes, List<string?> Policies, bool Anonymous);

    private static Read ReadAttributes(MetadataReader reader, CustomAttributeHandleCollection handles)
    {
        var verbs = new List<string>();
        var routes = new List<string>();
        var policies = new List<string?>();
        var anonymous = false;
        foreach (var handle in handles)
        {
            var attribute = reader.GetCustomAttribute(handle);
            var name = AttributeName(reader, attribute);
            if (name == "AllowAnonymousAttribute")
            {
                anonymous = true;
                continue;
            }

            if (name is not ("RouteAttribute" or "AuthorizeAttribute")
                && !(name.StartsWith("Http", StringComparison.Ordinal) && name.EndsWith("Attribute", StringComparison.Ordinal)))
                continue;

            var value = attribute.DecodeValue(new AttributeTypes());
            var first = value.FixedArguments.Length > 0 ? value.FixedArguments[0].Value as string : null;
            switch (name)
            {
                case "RouteAttribute":
                    routes.Add(first ?? string.Empty);
                    break;
                case "AuthorizeAttribute":
                    var named = value.NamedArguments.FirstOrDefault(a => a.Name == "Policy");
                    policies.Add(named.Value as string ?? first);
                    break;
                default:
                    verbs.Add(first ?? string.Empty);
                    break;
            }
        }

        return new Read(verbs, routes, policies, anonymous);
    }

    private static IEnumerable<ControllerModel> ReadControllers(string dll)
    {
        using var stream = System.IO.File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var controllers = new List<ControllerModel>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            if (!name.EndsWith("Controller", StringComparison.Ordinal) || type.Attributes.HasFlag(TypeAttributes.Abstract))
                continue;

            var read = ReadAttributes(reader, type.GetCustomAttributes());
            var actions = type.GetMethods()
                .Select(reader.GetMethodDefinition)
                .Where(m => m.Attributes.HasFlag(MethodAttributes.Public))
                .Select(m =>
                {
                    var action = ReadAttributes(reader, m.GetCustomAttributes());
                    return new ActionModel(reader.GetString(m.Name), action.VerbTemplates, action.Routes, action.Policies, action.Anonymous);
                })
                .ToList();
            controllers.Add(new ControllerModel($"{reader.GetString(type.Namespace)}.{name}", read.Routes, read.Policies, read.Anonymous, actions));
        }

        return controllers;
    }

    private static string BuildConfiguration() =>
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";

    /// <summary>Every web project in src, from its own output.</summary>
    private static List<ControllerModel> AllControllers()
    {
        var projects = Directory.GetFiles(Path.Combine(RepoPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => Path.GetFileNameWithoutExtension(p).EndsWith(".Web", StringComparison.Ordinal)
                        || Path.GetFileNameWithoutExtension(p) == "HRManagement.Gateway")
            .ToList();
        Assert.Equal(15, projects.Count);

        var controllers = new List<ControllerModel>();
        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var dll = Path.Combine(Path.GetDirectoryName(project)!, "bin", BuildConfiguration(), "net10.0", name + ".dll");
            Assert.True(System.IO.File.Exists(dll), $"{dll} is not built: this guard reads every web assembly");
            controllers.AddRange(ReadControllers(dll));
        }

        return controllers;
    }

    [Fact]
    public void Every_internal_route_of_every_web_assembly_is_for_services_only()
    {
        var controllers = AllControllers();

        Assert.True(controllers.Count > 50, $"only {controllers.Count} controllers were read: the guard is not looking");
        var violations = controllers.SelectMany(Violations).ToList();
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_guard_sees_the_internal_controllers_it_is_meant_to_see()
    {
        var names = AllControllers()
            .Where(c => c.Routes.Any(r => r.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Name.Split('.')[^1])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "PolicyEnforceInternalController", "CodeGenerationInternalController", "EmployeeLifecycleInternalController",
                     "ServiceRegimeInternalController", "DsarInternalController", "PayGradeLookupInternalController", "DirectoryInternalSyncController",
                     // PLAN-50: the active approval delegations of a user.
                     "ApprovalDelegationsInternalController"
                 })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void The_metadata_reader_and_reflection_agree_on_a_real_controller()
    {
        var dll = typeof(HR.Core.Web.Controllers.PolicyEnforceInternalController).Assembly.Location;
        var read = ReadControllers(dll).Single(c => c.Name.EndsWith(".PolicyEnforceInternalController", StringComparison.Ordinal));
        var reflected = FromType(typeof(HR.Core.Web.Controllers.PolicyEnforceInternalController));

        Assert.Equal(reflected.Routes, read.Routes);
        Assert.Equal(reflected.Policies, read.Policies);
        Assert.Equal(
            reflected.Actions.Single(a => a.Name == "Enforce").VerbTemplates,
            read.Actions.Single(a => a.Name == "Enforce").VerbTemplates);
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
        Assert.NotEmpty(Violations(FromType(sample)));

    [Theory]
    [InlineData(typeof(ServiceSample))]
    [InlineData(typeof(PublicSample))]
    public void A_service_route_and_a_public_route_pass(Type sample) =>
        Assert.Empty(Violations(FromType(sample)));
}
