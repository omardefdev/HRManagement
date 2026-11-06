using System.Text.Json;
using System.Text.Json.Nodes;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-39 (section 1, rules 1-3; P-1, P-5; D-39-5, D-39-6): the rendered realms carry no secret value, differ only in
/// users, redirect URIs and the Acceptance exceptions, give no service client a browser or password flow, and carry
/// the production security settings in every profile.
/// </summary>
public class Ops039RenderRealmTests
{
    public static TheoryData<string> AllProfiles() => new(Ops039RealmRenders.Profiles);

    public static TheoryData<string> ServerProfiles() => new("Production", "Demo");

    [Theory]
    [MemberData(nameof(ServerProfiles))]
    public void Server_profiles_contain_no_secret_value(string profile)
    {
        var text = Ops039RealmRenders.RawText(profile);
        foreach (var (key, marker) in Ops039RealmRenders.SecretMarkers)
            Assert.False(text.Contains(marker, StringComparison.Ordinal), $"{profile}: the value of {key} is in the rendered realm");
        Assert.DoesNotContain("dev-svc-secret-local-only", text, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ServerProfiles))]
    public void Server_profiles_use_placeholders_for_every_secret(string profile)
    {
        var realm = Ops039RealmRenders.Realm(profile);
        foreach (var client in Ops039RealmRenders.Clients(realm))
        {
            var secret = (string?)client["secret"];
            Assert.Matches(@"^\$\{[A-Z][A-Z0-9_]*\}$", secret);
        }

        foreach (var user in realm["users"]!.AsArray())
            foreach (var credential in user!["credentials"]!.AsArray())
                Assert.Matches(@"^\$\{(DEMO_USERS_PASSWORD|HRM_BOOTSTRAP_ADMIN_PASSWORD)\}$", (string?)credential!["value"]);
    }

    [Fact]
    public void Acceptance_profile_reads_no_server_secret()
    {
        var text = Ops039RealmRenders.RawText("Acceptance");
        foreach (var (key, marker) in Ops039RealmRenders.SecretMarkers.Where(kv => kv.Key != "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET"))
            Assert.False(text.Contains(marker, StringComparison.Ordinal), $"Acceptance: the value of {key} is in the rendered realm");
    }

    /// <summary>Review F3: the reconcile plan names its profile (remote-deploy.sh compares it with HRM_DEPLOYMENT_MODE) and holds no value.</summary>
    [Theory]
    [MemberData(nameof(ServerProfiles))]
    public void The_reconcile_plan_names_its_profile_and_holds_no_secret(string profile)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ops039-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (exitCode, stdout, stderr) = HRManagement.TestSupport.RepoScriptRunner.RunScript(
                Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "keycloak", "render-realm.ps1"),
                $"-Profile {profile} -AppDomain {Ops039RealmRenders.AppDomain} -OutFile \"{Path.Combine(dir, "realm-hrmanagement.json")}\"");
            Assert.True(exitCode == 0, stdout + stderr);
            var plan = System.IO.File.ReadAllLines(Path.Combine(dir, "reconcile-plan.tsv"));
            Assert.Contains($"profile\t{profile}", plan);
            Assert.DoesNotContain(plan, l => l.Contains("dev-svc-secret-local-only", StringComparison.Ordinal) || l.Contains("ops039-marker", StringComparison.Ordinal));
            Assert.Contains(plan, l => l.StartsWith("client\tsvc-payroll\tservice\tSVC_PAYROLL_CLIENT_SECRET\t", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Profiles_differ_only_in_users_redirect_uris_and_the_acceptance_exceptions()
    {
        var production = Ops039RealmRenders.Normalised("Production");
        Assert.Equal(production, Ops039RealmRenders.Normalised("Demo"));
        Assert.Equal(production, Ops039RealmRenders.Normalised("Acceptance"));
    }

    [Fact]
    public void Normalisation_still_sees_a_difference_outside_the_allowed_fields()
    {
        // Negative control of the comparison: a changed security field is not hidden by the normalisation.
        var realm = Ops039RealmRenders.Realm("Demo");
        realm["sslRequired"] = "none";
        realm.Remove("users");
        foreach (var client in Ops039RealmRenders.Clients(realm))
        {
            client.Remove("secret");
            if ((string?)client["clientId"] != "hr-gateway-client") continue;
            client.Remove("redirectUris");
            client.Remove("webOrigins");
            client.Remove("attributes");
        }

        Assert.NotEqual(Ops039RealmRenders.Normalised("Production"), realm.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void No_service_client_has_a_browser_or_password_flow(string profile)
    {
        var realm = Ops039RealmRenders.Realm(profile);
        foreach (var client in Ops039RealmRenders.Clients(realm).Where(c => (string?)c["clientId"] != "hr-gateway-client"))
        {
            var id = (string?)client["clientId"];
            Assert.False((bool)client["standardFlowEnabled"]!, $"{profile} {id}: standardFlowEnabled");
            Assert.False((bool)client["directAccessGrantsEnabled"]!, $"{profile} {id}: directAccessGrantsEnabled");
            Assert.False((bool)client["implicitFlowEnabled"]!, $"{profile} {id}: implicitFlowEnabled");
            Assert.False((bool)client["publicClient"]!, $"{profile} {id}: publicClient");
            Assert.True((bool)client["serviceAccountsEnabled"]!, $"{profile} {id}: serviceAccountsEnabled");
        }
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Every_profile_has_the_production_security_settings(string profile)
    {
        var realm = Ops039RealmRenders.Realm(profile);
        Assert.Equal("external", (string?)realm["sslRequired"]);
        Assert.True((bool)realm["bruteForceProtected"]!);
        Assert.False((bool)realm["permanentLockout"]!);
        Assert.Equal(5, (int)realm["failureFactor"]!);
        Assert.Equal(900, (int)realm["waitIncrementSeconds"]!);
        Assert.Equal(900, (int)realm["maxFailureWaitSeconds"]!);
        Assert.Equal("length(12) and notUsername and passwordHistory(3)", (string?)realm["passwordPolicy"]);
    }

    [Fact]
    public void Gateway_password_grant_is_off_except_in_acceptance()
    {
        Assert.False((bool)Ops039RealmRenders.Client(Ops039RealmRenders.Realm("Production"), "hr-gateway-client")["directAccessGrantsEnabled"]!);
        Assert.False((bool)Ops039RealmRenders.Client(Ops039RealmRenders.Realm("Demo"), "hr-gateway-client")["directAccessGrantsEnabled"]!);
        Assert.True((bool)Ops039RealmRenders.Client(Ops039RealmRenders.Realm("Acceptance"), "hr-gateway-client")["directAccessGrantsEnabled"]!);
    }

    [Theory]
    [MemberData(nameof(ServerProfiles))]
    public void Server_profiles_redirect_to_the_application_domain_only(string profile)
    {
        var gateway = Ops039RealmRenders.Client(Ops039RealmRenders.Realm(profile), "hr-gateway-client");
        Assert.Equal([$"https://{Ops039RealmRenders.AppDomain}/signin-oidc"], gateway["redirectUris"]!.AsArray().Select(x => (string?)x));
        Assert.Equal([$"https://{Ops039RealmRenders.AppDomain}"], gateway["webOrigins"]!.AsArray().Select(x => (string?)x));
        Assert.Equal($"https://{Ops039RealmRenders.AppDomain}/signout-callback-oidc", (string?)gateway["attributes"]!["post.logout.redirect.uris"]);
    }

    [Fact]
    public void Production_has_one_bootstrap_administrator_who_must_change_the_password()
    {
        var users = Ops039RealmRenders.Realm("Production")["users"]!.AsArray();
        var admin = Assert.Single(users)!;
        Assert.Equal(Ops039RealmRenders.BootstrapAdmin, (string?)admin["username"]);
        Assert.Equal(["UPDATE_PASSWORD"], admin["requiredActions"]!.AsArray().Select(x => (string?)x));
        var credential = Assert.Single(admin["credentials"]!.AsArray())!;
        Assert.True((bool)credential["temporary"]!);
        Assert.Contains("hr-admin", admin["realmRoles"]!.AsArray().Select(x => (string?)x));
    }

    [Fact]
    public void Demo_and_acceptance_carry_their_own_users()
    {
        var demo = Ops039RealmRenders.Realm("Demo")["users"]!.AsArray().Select(u => (string?)u!["username"]).ToList();
        Assert.Contains("hradmin", demo);
        Assert.DoesNotContain(Ops039RealmRenders.BootstrapAdmin, demo);
        var acceptance = Ops039RealmRenders.Realm("Acceptance")["users"]!.AsArray();
        Assert.Equal(HRManagement.ServiceDefaults.Seeding.AcceptancePersonaCatalog.ApprovedPersonaCount, acceptance.Count);
        Assert.DoesNotContain(acceptance, u => (string?)u!["username"] == "hradmin" || (string?)u!["username"] == Ops039RealmRenders.BootstrapAdmin);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void User_claims_are_managed_attributes_users_cannot_edit(string profile)
    {
        var provider = Ops039RealmRenders.Realm(profile)["components"]!["org.keycloak.userprofile.UserProfileProvider"]!.AsArray().Single()!;
        var config = JsonNode.Parse((string)provider["config"]!["kc.user.profile.config"]!.AsArray().Single()!)!;
        var attributes = config["attributes"]!.AsArray().ToDictionary(a => (string)a!["name"]!, a => a!);
        foreach (var claim in new[] { "tenant_id", "employee_id", "scope_org_unit_id" })
        {
            Assert.True(attributes.ContainsKey(claim), $"{profile}: {claim} is not a managed attribute");
            Assert.Equal(["admin"], attributes[claim]["permissions"]!["edit"]!.AsArray().Select(x => (string?)x));
        }

        var gateway = Ops039RealmRenders.Client(Ops039RealmRenders.Realm(profile), "hr-gateway-client");
        var mapped = gateway["protocolMappers"]!.AsArray().Select(m => (string?)m!["config"]!["claim.name"]).ToList();
        Assert.Contains("tenant_id", mapped);
        Assert.Contains("employee_id", mapped);
        Assert.Contains("scope_org_unit_id", mapped);
    }
}
