using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Wiring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-37 (§2): <c>deploy/wiring/service-wiring.json</c> is the single source of truth for service-to-service
/// wiring. These checks keep the code, the server policies, the deployment files and the Keycloak scripts in step
/// with it. A plan that adds an S2S client, call or scope updates the matrix in the same commit.
/// </summary>
public partial class ProductionWiringMatrixTests
{
    private static readonly Lazy<ServiceWiringMatrix> Matrix = new(() => ServiceWiringMatrix.Load());

    [Fact]
    public void Matrix_schema_is_valid()
    {
        var errors = Matrix.Value.Validate();
        Assert.True(errors.Count == 0, "service-wiring.json:\n  " + string.Join("\n  ", errors));
    }

    [Fact]
    public void Matrix_lists_every_known_service_client()
    {
        var matrixClients = Matrix.Value.Services.Where(s => s.ServiceIdentity).Select(s => s.ClientId!).ToHashSet();
        // svc-file-storage exists in the realm but File.Storage makes no S2S call.
        var expected = ServiceClientIds.All.Where(c => c != ServiceClientIds.FileStorage).ToHashSet();
        Assert.Equal(expected.OrderBy(x => x), matrixClients.OrderBy(x => x));
    }

    [Fact]
    public void Validate_reports_a_scope_without_a_call()
    {
        var broken = ServiceWiringMatrix.Load();
        broken.Service("hr-movements-web").Scopes.Add("hr-core.workflow");
        Assert.Contains(broken.Validate(), e => e == "hr-movements-web: scope hr-core.workflow has no call");
    }

    [Fact]
    public void Rendered_matrix_document_is_up_to_date()
    {
        var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoPaths.Root, "scripts", "int037-render-wiring-doc.ps1"), "-Check");
        Assert.True(exitCode == 0, stdout + stderr);
    }

    public static TheoryData<string, string, string> Calls()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var service in Matrix.Value.Services)
            foreach (var call in service.Calls)
                data.Add(service.ComposeService, call.Endpoint, call.Client);
        return data;
    }

    /// <summary>§2 rule 2: every call passes its server policies with the matrix identity, and fails without the scope.</summary>
    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Call_is_allowed_with_its_scope_and_refused_without_it(string composeService, string endpointId, string client)
    {
        var service = Matrix.Value.Service(composeService);
        var endpoint = Matrix.Value.Endpoints[endpointId];
        var authorization = BuildAuthorization();

        foreach (var policy in endpoint.ServerPolicies)
        {
            var policyName = ResolvePolicyName(policy);
            var allowed = await authorization.AuthorizeAsync(ServicePrincipal(service.ClientId!, service.Scopes), policyName);
            Assert.True(allowed.Succeeded, $"{composeService} -> {endpointId} via {client}: policy {policy} refused {service.ClientId} with [{string.Join(' ', service.Scopes)}]");

            var noScope = await authorization.AuthorizeAsync(ServicePrincipal(service.ClientId!, []), policyName);
            Assert.False(noScope.Succeeded, $"{composeService} -> {endpointId}: policy {policy} allowed {service.ClientId} with no scope");
        }

        // Negative: the same identity without the endpoint's scope fails at least one of its policies.
        var withoutScope = ServicePrincipal(service.ClientId!, service.Scopes.Where(s => s != endpoint.Scope));
        var passesAll = true;
        foreach (var policy in endpoint.ServerPolicies)
            passesAll &= (await authorization.AuthorizeAsync(withoutScope, ResolvePolicyName(policy))).Succeeded;
        Assert.False(passesAll, $"{composeService} -> {endpointId}: allowed without {endpoint.Scope}");

        // Every policy of the endpoint must actually be declared on its controller.
        var controller = System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, endpoint.Controller));
        foreach (var policy in endpoint.ServerPolicies)
            Assert.Contains($"[Authorize(Policy = {policy})]", controller);
    }

    /// <summary>
    /// §1 rule 3: the allow-listed S2S policies admit exactly the clients that call them, plus the documented
    /// exceptions. A client added to an allow-list without a matrix call fails here.
    /// </summary>
    [Fact]
    public async Task Allow_listed_policies_admit_only_matrix_callers()
    {
        var authorization = BuildAuthorization();
        var allowListed = new[]
        {
            "ServicePolicies.DirectorySync", "ServicePolicies.HrCoreLookupS2S", "ServicePolicies.FileStorageReferences",
            "ServicePolicies.DsarSubjectRead", "ServicePolicies.PayrollGradeLookup", "ServicePolicies.FileStorageServiceUpload"
        };
        var allClients = ServiceClientIds.All.Append(ServiceClientIds.Gateway).ToArray();
        var problems = new List<string>();

        foreach (var policy in allowListed)
        {
            var endpoints = Matrix.Value.Endpoints.Where(e => e.Value.ServerPolicies.Contains(policy)).ToList();
            var scope = endpoints.Select(e => e.Value.Scope).Distinct().Single();
            var callers = Matrix.Value.Services
                .Where(s => s.Calls.Any(c => endpoints.Any(e => e.Key == c.Endpoint)))
                .Select(s => s.ClientId!)
                .ToHashSet();
            var exceptions = Matrix.Value.AllowListExceptions.Where(x => x.Policy == policy).SelectMany(x => x.Clients).ToHashSet();

            foreach (var client in allClients)
            {
                var result = await authorization.AuthorizeAsync(ServicePrincipal(client, [scope]), ResolvePolicyName(policy));
                var expected = callers.Contains(client) || exceptions.Contains(client);
                if (result.Succeeded && !expected) problems.Add($"{policy} admits {client}, which has no call in the matrix");
                if (!result.Succeeded && callers.Contains(client)) problems.Add($"{policy} refuses {client}, a matrix caller");
                if (!result.Succeeded && exceptions.Contains(client)) problems.Add($"{policy}: exception {client} is no longer admitted; drop it from allowListExceptions");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    internal static IAuthorizationService BuildAuthorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        services.AddServiceToServiceAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    internal static ClaimsPrincipal ServicePrincipal(string clientId, IEnumerable<string> scopes)
    {
        var claims = new List<Claim>
        {
            new("azp", clientId),
            new("preferred_username", "service-account-" + clientId),
            new("scope", string.Join(' ', scopes))
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    internal static string ResolvePolicyName(string constant)
    {
        var parts = constant.Split('.');
        Assert.Equal(2, parts.Length);
        var type = parts[0] switch
        {
            "HrManagementPolicies" => typeof(HrManagementPolicies),
            "ServicePolicies" => typeof(ServicePolicies),
            _ => throw new InvalidOperationException($"unknown policy class in {constant}")
        };
        var field = type.GetField(parts[1], BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{constant} does not exist");
        return (string)field.GetRawConstantValue()!;
    }
}
