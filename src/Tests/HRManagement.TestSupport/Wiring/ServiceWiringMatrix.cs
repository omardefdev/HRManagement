using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HRManagement.TestSupport.Wiring;

/// <summary>
/// PLAN-37 (§1): loader and schema check for <c>deploy/wiring/service-wiring.json</c>, the single source of truth for
/// service-to-service wiring (URLs, Keycloak client, scopes, secret variable, calls, legal packs).
/// </summary>
public sealed class ServiceWiringMatrix
{
    public const string RelativePath = "deploy/wiring/service-wiring.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("about")] public string About { get; init; } = "";
    [JsonPropertyName("scopes")] public Dictionary<string, WiringScope> Scopes { get; init; } = [];
    [JsonPropertyName("endpoints")] public Dictionary<string, WiringEndpoint> Endpoints { get; init; } = [];
    [JsonPropertyName("registrations")] public Dictionary<string, WiringRegistration> Registrations { get; init; } = [];
    [JsonPropertyName("registrationsIgnored")] public Dictionary<string, string> RegistrationsIgnored { get; init; } = [];
    [JsonPropertyName("allowListExceptions")] public List<WiringAllowListException> AllowListExceptions { get; init; } = [];
    [JsonPropertyName("legalPacks")] public Dictionary<string, WiringLegalPack> LegalPacks { get; init; } = [];
    [JsonPropertyName("services")] public List<WiringService> Services { get; init; } = [];

    public static ServiceWiringMatrix Load(string? repoRoot = null) =>
        Parse(File.ReadAllText(Path.Combine(repoRoot ?? RepoPaths.Root, RelativePath.Replace('/', Path.DirectorySeparatorChar))));

    public static ServiceWiringMatrix Parse(string json) =>
        JsonSerializer.Deserialize<ServiceWiringMatrix>(json, JsonOptions)
        ?? throw new InvalidDataException($"{RelativePath} is empty.");

    public WiringService Service(string composeService) =>
        Services.SingleOrDefault(s => s.ComposeService == composeService)
        ?? throw new KeyNotFoundException($"{composeService} is not in {RelativePath}.");

    /// <summary>Environment-variable keys the deployment must set for this service (URLs + identity).</summary>
    public static IReadOnlyDictionary<string, string> ExpectedEnvironment(WiringService service)
    {
        var expected = new Dictionary<string, string>(service.Urls, StringComparer.Ordinal);
        if (service.ServiceIdentity)
        {
            expected["ServiceIdentity__ClientId"] = service.ClientId!;
            expected["ServiceIdentity__Scope"] = string.Join(' ', service.Scopes);
        }

        return expected;
    }

    /// <summary>Converts a configuration key (<c>Integration:HrCoreBaseUrl</c>) to its environment form.</summary>
    public static string ToEnvironmentKey(string configurationKey) => configurationKey.Replace(":", "__", StringComparison.Ordinal);

    /// <summary>Schema and internal consistency; returns every problem (empty when valid).</summary>
    public IReadOnlyList<string> Validate(string? repoRoot = null)
    {
        var root = repoRoot ?? RepoPaths.Root;
        var errors = new List<string>();
        if (SchemaVersion != 1) errors.Add($"schemaVersion must be 1 (was {SchemaVersion})");
        if (Services.Count == 0) errors.Add("services is empty");

        foreach (var (name, scope) in Scopes)
        {
            if (!Regex.IsMatch(name, "^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$")) errors.Add($"scope '{name}': bad name");
            if (scope.Audiences.Count == 0) errors.Add($"scope '{name}': no audience");
        }

        foreach (var (id, endpoint) in Endpoints)
        {
            if (Services.All(s => s.ComposeService != endpoint.Target)) errors.Add($"endpoint '{id}': unknown target {endpoint.Target}");
            if (!Scopes.ContainsKey(endpoint.Scope)) errors.Add($"endpoint '{id}': unknown scope {endpoint.Scope}");
            if (endpoint.ServerPolicies.Count == 0) errors.Add($"endpoint '{id}': no server policy");
            if (!File.Exists(Path.Combine(root, endpoint.Controller))) errors.Add($"endpoint '{id}': controller {endpoint.Controller} not found");
            if (endpoint.Method is not ("GET" or "POST" or "PUT" or "DELETE")) errors.Add($"endpoint '{id}': bad method {endpoint.Method}");
        }

        foreach (var (name, registration) in Registrations)
        {
            if (!File.Exists(Path.Combine(root, registration.DefinedIn))) errors.Add($"registration '{name}': {registration.DefinedIn} not found");
            if (registration.UrlKeys.Count == 0) errors.Add($"registration '{name}': no urlKeys");
            foreach (var key in registration.UrlKeys.Where(k => k.Contains(':'))) errors.Add($"registration '{name}': key {key} must use the __ form");
        }

        foreach (var pack in LegalPacks.Values)
        {
            if (!File.Exists(Path.Combine(root, pack.Source))) errors.Add($"legal pack source {pack.Source} not found");
        }

        foreach (var exception in AllowListExceptions)
        {
            if (string.IsNullOrWhiteSpace(exception.Reason)) errors.Add($"allow-list exception {exception.Policy}: no reason");
        }

        var seenClients = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in Services)
        {
            var label = service.ComposeService;
            if (string.IsNullOrWhiteSpace(service.Name)) errors.Add($"{label}: no name");
            if (string.IsNullOrWhiteSpace(service.K8sDeployment)) errors.Add($"{label}: no k8sDeployment");
            if (service.Projects.Count == 0) errors.Add($"{label}: no projects");
            foreach (var project in service.Projects.Where(p => !Directory.Exists(Path.Combine(root, p))))
                errors.Add($"{label}: project {project} not found");
            if (service.ServiceIdentity)
            {
                if (string.IsNullOrWhiteSpace(service.ClientId)) errors.Add($"{label}: serviceIdentity without clientId");
                if (string.IsNullOrWhiteSpace(service.SecretEnv) || !Regex.IsMatch(service.SecretEnv, "^SVC_[A-Z_]+_CLIENT_SECRET$"))
                    errors.Add($"{label}: secretEnv must be SVC_<NAME>_CLIENT_SECRET");
            }
            else if (service.Scopes.Count > 0 || service.Calls.Count > 0)
            {
                errors.Add($"{label}: scopes or calls without a service identity");
            }

            if (service.ClientId is not null && !seenClients.Add(service.ClientId)) errors.Add($"{label}: duplicate clientId {service.ClientId}");
            foreach (var (key, value) in service.Urls)
            {
                if (key.Contains(':')) errors.Add($"{label}: url key {key} must use the __ form");
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.IsLoopback || uri.Host == "localhost")
                    errors.Add($"{label}: url {key}={value} must be an absolute service DNS URL");
            }

            if (service.Scopes.Distinct(StringComparer.Ordinal).Count() != service.Scopes.Count) errors.Add($"{label}: duplicate scope");
            foreach (var scope in service.Scopes.Where(s => !Scopes.ContainsKey(s))) errors.Add($"{label}: unknown scope {scope}");

            // §1 rule 2: every granted scope is used by at least one call (least privilege).
            foreach (var scope in service.Scopes.Where(s => service.Calls.All(c => c.Scope != s)))
                errors.Add($"{label}: scope {scope} has no call");

            foreach (var registration in service.Registrations)
            {
                if (!Registrations.TryGetValue(registration, out var definition))
                {
                    errors.Add($"{label}: registration {registration} is not in the registrations section");
                    continue;
                }

                foreach (var key in definition.UrlKeys.Where(k => !service.Urls.ContainsKey(k)))
                    errors.Add($"{label}: registration {registration} needs url {key}");
            }

            foreach (var call in service.Calls)
            {
                if (!service.Registrations.Contains(call.Client)) errors.Add($"{label}: call via {call.Client} which the service does not register");
                if (!Endpoints.TryGetValue(call.Endpoint, out var endpoint))
                {
                    errors.Add($"{label}: call to unknown endpoint {call.Endpoint}");
                    continue;
                }

                if (endpoint.Scope != call.Scope) errors.Add($"{label}: call {call.Endpoint} uses scope {call.Scope}, endpoint requires {endpoint.Scope}");
                if (!service.Scopes.Contains(call.Scope)) errors.Add($"{label}: call {call.Endpoint} needs scope {call.Scope} which is not granted");
                if (call.Authenticated == false && string.IsNullOrWhiteSpace(call.Owner))
                    errors.Add($"{label}: unauthenticated call {call.Endpoint} has no owner plan");
            }

            foreach (var registration in service.UnusedRegistrations.Keys.Where(r => !service.Registrations.Contains(r)))
                errors.Add($"{label}: unusedRegistrations lists {registration} which is not registered");
            foreach (var pack in service.LegalPacks.Where(p => !LegalPacks.ContainsKey(p)))
                errors.Add($"{label}: unknown legal pack {pack}");
        }

        return errors;
    }
}

public sealed class WiringScope
{
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("audiences")] public List<string> Audiences { get; init; } = [];
    [JsonPropertyName("note")] public string? Note { get; init; }
}

public sealed class WiringEndpoint
{
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("via")] public string? Via { get; init; }
    [JsonPropertyName("method")] public string Method { get; init; } = "";
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("controller")] public string Controller { get; init; } = "";
    [JsonPropertyName("serverPolicies")] public List<string> ServerPolicies { get; init; } = [];
    [JsonPropertyName("scope")] public string Scope { get; init; } = "";
}

public sealed class WiringRegistration
{
    [JsonPropertyName("definedIn")] public string DefinedIn { get; init; } = "";
    [JsonPropertyName("urlKeys")] public List<string> UrlKeys { get; init; } = [];
}

public sealed class WiringAllowListException
{
    [JsonPropertyName("policy")] public string Policy { get; init; } = "";
    [JsonPropertyName("clients")] public List<string> Clients { get; init; } = [];
    [JsonPropertyName("reason")] public string Reason { get; init; } = "";
}

public sealed class WiringLegalPack
{
    [JsonPropertyName("source")] public string Source { get; init; } = "";
    [JsonPropertyName("publishPath")] public string PublishPath { get; init; } = "";
}

public sealed class WiringService
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("composeService")] public string ComposeService { get; init; } = "";
    [JsonPropertyName("k8sDeployment")] public string K8sDeployment { get; init; } = "";
    [JsonPropertyName("projects")] public List<string> Projects { get; init; } = [];
    [JsonPropertyName("serviceIdentity")] public bool ServiceIdentity { get; init; }
    [JsonPropertyName("clientId")] public string? ClientId { get; init; }
    [JsonPropertyName("secretEnv")] public string? SecretEnv { get; init; }
    [JsonPropertyName("urls")] public Dictionary<string, string> Urls { get; init; } = [];
    [JsonPropertyName("scopes")] public List<string> Scopes { get; init; } = [];
    [JsonPropertyName("registrations")] public List<string> Registrations { get; init; } = [];
    [JsonPropertyName("calls")] public List<WiringCall> Calls { get; init; } = [];
    [JsonPropertyName("unusedRegistrations")] public Dictionary<string, string> UnusedRegistrations { get; init; } = [];
    [JsonPropertyName("note")] public string? Note { get; init; }
    [JsonPropertyName("legalPacks")] public List<string> LegalPacks { get; init; } = [];
}

public sealed class WiringCall
{
    [JsonPropertyName("client")] public string Client { get; init; } = "";
    [JsonPropertyName("endpoint")] public string Endpoint { get; init; } = "";
    [JsonPropertyName("scope")] public string Scope { get; init; } = "";
    [JsonPropertyName("authenticated")] public bool? Authenticated { get; init; }
    [JsonPropertyName("owner")] public string? Owner { get; init; }
    [JsonPropertyName("note")] public string? Note { get; init; }
}
