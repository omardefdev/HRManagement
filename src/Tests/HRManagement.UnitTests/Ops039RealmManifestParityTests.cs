using System.Text.Json.Nodes;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Wiring;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-39 (section 1; PLAN-37 section 2, rule 5 of ProductionWiringMatrixTests): the realm rendered for every profile
/// holds every client and scope of deploy/wiring/service-wiring.json exactly as the matrix assigns them, every scope has
/// an audience, and every ServiceIdentity__ApiAudience of the production chain is the target of a scope. The realm
/// manifest defines no service client of its own.
/// </summary>
public class Ops039RealmManifestParityTests
{
    private static readonly Lazy<ServiceWiringMatrix> Matrix = new(() => ServiceWiringMatrix.Load());

    public static TheoryData<string> AllProfiles() => new(Ops039RealmRenders.Profiles);

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Every_matrix_client_is_present_with_exactly_its_matrix_scopes(string profile)
    {
        var realm = Ops039RealmRenders.Realm(profile);
        var problems = new List<string>();
        foreach (var service in Matrix.Value.Services.Where(s => s.ServiceIdentity))
        {
            var client = Ops039RealmRenders.Clients(realm).SingleOrDefault(c => (string?)c["clientId"] == service.ClientId);
            if (client is null)
            {
                problems.Add($"client {service.ClientId} missing");
                continue;
            }

            if (!(bool)client["enabled"]!) problems.Add($"client {service.ClientId} disabled");
            var optional = client["optionalClientScopes"]!.AsArray().Select(x => (string)x!).Order().ToList();
            if (!optional.SequenceEqual(service.Scopes.Order()))
                problems.Add($"client {service.ClientId}: optional [{string.Join(' ', optional)}], matrix [{string.Join(' ', service.Scopes)}]");
            if (client["defaultClientScopes"]!.AsArray().Count != 0)
                problems.Add($"client {service.ClientId}: has default scopes (a service scope must be requested)");
        }

        // Only matrix clients, the gateway and directory-sync: no service client outside the matrix (svc-file-storage left).
        var allowed = Matrix.Value.Services.Where(s => s.ServiceIdentity).Select(s => s.ClientId!).Concat(["hr-gateway-client", "directory-sync"]).ToHashSet();
        problems.AddRange(Ops039RealmRenders.Clients(realm).Select(c => (string)c["clientId"]!).Where(id => !allowed.Contains(id)).Select(id => $"client {id} is not in the matrix"));
        Assert.True(problems.Count == 0, profile + ":\n" + string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Every_matrix_scope_has_its_audiences(string profile)
    {
        var scopes = Ops039RealmRenders.Realm(profile)["clientScopes"]!.AsArray().ToDictionary(s => (string)s!["name"]!, s => s!);
        var problems = new List<string>();
        foreach (var (name, scope) in Matrix.Value.Scopes)
        {
            if (scope.Audiences.Count == 0) problems.Add($"scope {name}: no audience in the matrix");
            if (!scopes.TryGetValue(name, out var realmScope))
            {
                problems.Add($"scope {name} missing");
                continue;
            }

            var audiences = realmScope["protocolMappers"]!.AsArray()
                .Where(m => (string?)m!["protocolMapper"] == "oidc-audience-mapper")
                .Select(m => (string)m!["config"]!["included.client.audience"]!).Order().ToList();
            if (!audiences.SequenceEqual(scope.Audiences.Order()))
                problems.Add($"scope {name}: audiences [{string.Join(' ', audiences)}], matrix [{string.Join(' ', scope.Audiences)}]");
        }

        Assert.True(problems.Count == 0, profile + ":\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Every_production_api_audience_is_the_target_of_a_scope()
    {
        var targets = Matrix.Value.Scopes.Values.SelectMany(s => s.Audiences).ToHashSet(StringComparer.Ordinal);
        var chain = ComposeEffectiveEnvironment.Load(RepoPaths.Root, WiringChecks.ComposeChains(RepoPaths.Root)["single-host"]);
        var audiences = chain.Values
            .Where(env => env.TryGetValue("ServiceIdentity__ApiAudience", out var value) && !string.IsNullOrWhiteSpace(value))
            .Select(env => env["ServiceIdentity__ApiAudience"]!)
            .Distinct()
            .ToList();
        Assert.NotEmpty(audiences);
        Assert.All(audiences, audience => Assert.True(targets.Contains(audience), $"{audience} is no scope's audience"));
    }

    [Fact]
    public void Manifest_defines_no_service_client_and_no_service_scope()
    {
        var manifest = JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "docker", "keycloak", "realm-manifest.json")))!;
        var text = manifest.ToJsonString();
        foreach (var service in Matrix.Value.Services.Where(s => s.ServiceIdentity))
            Assert.DoesNotContain("\"" + service.ClientId + "\"", text, StringComparison.Ordinal);
        var builtins = manifest["builtinClientScopes"]!.AsArray().Select(s => (string)s!["name"]!).ToList();
        Assert.Empty(builtins.Intersect(Matrix.Value.Scopes.Keys));
        Assert.DoesNotContain("\"secret\"", text, StringComparison.Ordinal);
    }
}
