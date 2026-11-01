using System.Text.Json;
using System.Text.Json.Nodes;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-39: renders the three realm profiles once (scripts/keycloak/render-realm.ps1) with an env file whose secret
/// values are distinctive markers, so the tests can prove that none of them reaches a Production or Demo file.
/// </summary>
public static class Ops039RealmRenders
{
    public static readonly string[] Profiles = ["Production", "Demo", "Acceptance"];

    /// <summary>Secret keys of the server .env with marker values; render-realm.ps1 must read none of them for Production and Demo.</summary>
    public static readonly IReadOnlyDictionary<string, string> SecretMarkers = BuildSecretMarkers();

    public const string AppDomain = "hr.ops039.test";
    public const string BootstrapAdmin = "agency-admin-ops039";

    private static readonly Lazy<IReadOnlyDictionary<string, JsonObject>> Rendered = new(RenderAll);

    public static JsonObject Realm(string profile) => (JsonObject)Rendered.Value[profile].DeepClone();

    public static string RawText(string profile) => Rendered.Value[profile].ToJsonString();

    public static IEnumerable<JsonObject> Clients(JsonObject realm) => realm["clients"]!.AsArray().Select(c => c!.AsObject());

    public static JsonObject Client(JsonObject realm, string clientId) =>
        Clients(realm).Single(c => (string?)c["clientId"] == clientId);

    private static Dictionary<string, string> BuildSecretMarkers()
    {
        var example = System.IO.File.ReadAllLines(Path.Combine(RepoPaths.Root, "deploy", "vps", ".env.vps.example"));
        var markers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in example)
        {
            var i = line.IndexOf('=');
            if (i < 1 || line.StartsWith('#')) continue;
            var key = line[..i].Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(key, "(PASSWORD|_PASS|SECRET|_KEY|TOKEN)$"))
                markers[key] = "ops039-marker-" + key.ToLowerInvariant().Replace('_', '-') + "-x9";
        }

        markers["KEYCLOAK_HR_GATEWAY_CLIENT_SECRET"] = "ops039-marker-gateway-secret-x9";
        markers["DEMO_USERS_PASSWORD"] = "ops039-marker-demo-users-x9";
        markers["HRM_BOOTSTRAP_ADMIN_PASSWORD"] = "ops039-marker-bootstrap-admin-x9";
        return markers;
    }

    private static IReadOnlyDictionary<string, JsonObject> RenderAll()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ops039-realm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var envFile = Path.Combine(dir, "test.env");
            var lines = new List<string> { $"APP_DOMAIN={AppDomain}", $"HRM_BOOTSTRAP_ADMIN_USER={BootstrapAdmin}" };
            lines.AddRange(SecretMarkers.Select(kv => $"{kv.Key}={kv.Value}"));
            System.IO.File.WriteAllLines(envFile, lines);

            var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var profile in Profiles)
            {
                var outFile = Path.Combine(dir, profile + ".json");
                var extra = $"-Profile {profile} -EnvFile \"{envFile}\" -OutFile \"{outFile}\"";
                if (profile == "Acceptance") extra += " -NoAcceptanceCredentialFiles";
                var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
                    Path.Combine(RepoPaths.Root, "scripts", "keycloak", "render-realm.ps1"), extra);
                if (exitCode != 0)
                    throw new InvalidOperationException($"render-realm.ps1 -Profile {profile} failed: {stdout}{stderr}");
                result[profile] = JsonNode.Parse(System.IO.File.ReadAllText(outFile))!.AsObject();
            }

            return result;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The realm without what profiles may differ in: users, the gateway's URLs, and the Acceptance exceptions.</summary>
    public static string Normalised(string profile)
    {
        var realm = Realm(profile);
        realm.Remove("users");
        foreach (var client in Clients(realm))
        {
            // Acceptance runs with lab secrets (P-1); Production and Demo placeholders are asserted separately.
            client.Remove("secret");
            if ((string?)client["clientId"] != "hr-gateway-client") continue;
            client.Remove("redirectUris");
            client.Remove("webOrigins");
            client.Remove("attributes");
            if (profile == "Acceptance") client["directAccessGrantsEnabled"] = false;
        }

        return realm.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
