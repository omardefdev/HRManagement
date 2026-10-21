using System.Text.Json;

namespace HRManagement.AcceptanceTests;

public class AcceptanceIdentityPopulationTests
{
    [SkippableFact]
    public async Task All_55_personas_have_the_expected_login_and_claim_outcome()
    {
        AcceptanceEnv.RequireEnabled();
        var personas = AcceptanceHarness.LoadPersonas();
        Assert.Equal(56, personas.Count);

        var successful = 0;
        var expectedDenials = 0;
        var failures = new List<string>();

        foreach (var persona in personas)
        {
            try
            {
                using var response = await AcceptanceHarness.FetchPasswordGrantResponseAsync(persona);
                var body = await response.Content.ReadAsStringAsync();

                if (!persona.Enabled)
                {
                    expectedDenials++;
                    if (response.IsSuccessStatusCode)
                        failures.Add($"{persona.Username}: disabled fixture unexpectedly obtained a token.");
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    failures.Add($"{persona.Username}: token grant returned {(int)response.StatusCode} {body}");
                    continue;
                }

                successful++;
                using var tokenDocument = JsonDocument.Parse(body);
                var accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString();
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    failures.Add($"{persona.Username}: response did not contain an access token.");
                    continue;
                }

                var jwt = AcceptanceHarness.ParseJwt(accessToken);
                var tenantClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
                if (!string.Equals(tenantClaim, persona.TenantId, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"{persona.Username}: tenant claim '{tenantClaim}' != '{persona.TenantId}'.");

                var scopeClaim = jwt.Claims.FirstOrDefault(c => c.Type == "scope_org_unit_id")?.Value;
                if (!string.Equals(scopeClaim, persona.ScopeOrgUnitId, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"{persona.Username}: scope claim '{scopeClaim}' != '{persona.ScopeOrgUnitId}'.");

                var realmAccess = jwt.Claims.FirstOrDefault(c => c.Type == "realm_access")?.Value;
                var tokenRoles = ReadRealmRoles(realmAccess);
                foreach (var role in persona.Roles)
                {
                    if (!tokenRoles.Contains(role))
                        failures.Add($"{persona.Username}: token is missing role '{role}'.");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{persona.Username}: {exception.Message}");
            }
        }

        Assert.Equal(53, successful);
        Assert.Equal(2, expectedDenials);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IReadOnlySet<string> ReadRealmRoles(string? realmAccess)
    {
        if (string.IsNullOrWhiteSpace(realmAccess))
            return new HashSet<string>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(realmAccess);
        if (!document.RootElement.TryGetProperty("roles", out var roles))
            return new HashSet<string>(StringComparer.Ordinal);

        return roles.EnumerateArray()
            .Select(role => role.GetString())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
