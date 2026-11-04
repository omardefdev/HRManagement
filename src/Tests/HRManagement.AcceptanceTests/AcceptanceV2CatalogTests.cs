using System.Text.Json.Nodes;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.SeparationOfDuties;

namespace HRManagement.AcceptanceTests;

public class AcceptanceV2CatalogTests
{
    [Fact]
    public void Canonical_topology_has_the_approved_non_uniform_totals()
    {
        Assert.Equal(3, AcceptanceSeedCatalog.Agencies.Count);
        Assert.Equal(98, AcceptanceSeedCatalog.TotalOrgUnits);
        Assert.Equal(1_617, AcceptanceSeedCatalog.TotalEmployees);
        Assert.Equal(1_489, AcceptanceSeedCatalog.TotalPayableEmployees);
        Assert.Equal(1_531, AcceptanceSeedCatalog.TotalCurrentContracts);
        Assert.Equal(86, AcceptanceSeedCatalog.TotalSeparations);

        Assert.Collection(
            AcceptanceSeedCatalog.Agencies,
            agency =>
            {
                Assert.Equal("AGENCY-A", agency.Code);
                Assert.Equal(61, agency.OrgUnitCount);
                Assert.Equal(1_210, agency.EmployeeCount);
            },
            agency =>
            {
                Assert.Equal("AGENCY-B", agency.Code);
                Assert.Equal(24, agency.OrgUnitCount);
                Assert.Equal(271, agency.EmployeeCount);
            },
            agency =>
            {
                Assert.Equal("AGENCY-C", agency.Code);
                Assert.Equal(13, agency.OrgUnitCount);
                Assert.Equal(136, agency.EmployeeCount);
            });

        var siteCounts = AcceptanceSeedCatalog.Agencies
            .SelectMany(a => a.Sites)
            .Select(s => s.EmployeeCount)
            .ToArray();
        Assert.Equal(siteCounts.Length, siteCounts.Distinct().Count());

        Assert.Equal(420, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.LeaveRequests));
        Assert.Equal(180, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.Advances));
        Assert.Equal(120, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.Transfers));
        Assert.Equal(45, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.Secondments));
        Assert.Equal(30, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.Loans));
        Assert.Equal(420, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.TrainingEnrollments));
        Assert.Equal(1_250, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.IssuedCardsAndRequests));
        Assert.Equal(450, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.Assets));
        Assert.Equal(320, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.AssetAssignments));
        Assert.Equal(90, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.ComplianceCases));
        Assert.Equal(24, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.DsarRequests));
        Assert.Equal(120, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.Files));
        Assert.Equal(25, AcceptanceSeedCatalog.Agencies.Sum(a => a.Volume.RecruitmentApplications));
    }

    [Fact]
    public void Every_employee_and_org_unit_identifier_is_unique_and_tenant_consistent()
    {
        var employees = AcceptanceSeedCatalog.Agencies.SelectMany(a => a.Employees).ToArray();
        var units = AcceptanceSeedCatalog.Agencies.SelectMany(a => a.OrgUnits).ToArray();

        Assert.Equal(employees.Length, employees.Select(e => e.Id).Distinct().Count());
        Assert.Equal(units.Length, units.Select(o => o.Id).Distinct().Count());
        Assert.All(employees, employee =>
        {
            var unit = AcceptanceSeedCatalog.GetOrgUnit(employee.OrgUnitId);
            Assert.Equal(employee.TenantId, unit.TenantId);
            Assert.Equal(employee.SiteCode, unit.SiteCode);
        });

        var parentIds = units.Where(unit => unit.ParentId.HasValue)
            .Select(unit => unit.ParentId!.Value)
            .ToHashSet();
        Assert.All(employees, employee => Assert.DoesNotContain(employee.OrgUnitId, parentIds));
    }

    [Fact]
    public void Persona_catalog_has_separated_accounts_in_the_approved_distribution_and_real_employee_links()
    {
        // PLAN-41: every count derives from the approved table in the catalog, so a plan that adds a persona changes one
        // place (AC-QA-41c); the distribution itself is checked by the parity tests below.
        var personas = AcceptancePersonaCatalog.Personas;
        var regular = personas.Where(p => p.Fixture is null).ToArray();
        var fixtures = personas.Where(p => p.Fixture is not null).ToArray();
        Assert.Equal(AcceptancePersonaCatalog.ApprovedPersonaCount, personas.Count);
        Assert.Equal(personas.Count, personas.Select(p => p.Username).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(AcceptancePersonaCatalog.ApprovedNegativePersonas, fixtures.Length);
        Assert.All(regular, persona => Assert.True(persona.Enabled, $"{persona.Id} is a regular persona and disabled."));
        Assert.Equal(
            regular.Count(p => p.Definition.TenantCode != "PLATFORM"),
            regular.Count(p => p.Employee is not null));

        Assert.All(personas.Where(p => p.Employee is not null), persona =>
        {
            Assert.Equal(persona.StorageTenantId, persona.Employee!.TenantId);
            Assert.Contains(
                AcceptanceSeedCatalog.GetAgency(persona.Employee.TenantId).Employees,
                employee => employee.Id == persona.Employee.Id);
        });

        foreach (var (tenant, count) in AcceptancePersonaCatalog.ApprovedRegularPersonas)
            Assert.Equal(count, regular.Count(p => p.Definition.TenantCode == tenant));
    }

    [Fact]
    public void Committed_persona_file_matches_the_catalog()
    {
        var path = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "personas.json");
        Assert.Empty(PersonaFileProblems(File.ReadAllText(path)));
    }

    /// <summary>AC-QA-41c negative: a persona added to the file and not to the catalog turns the parity test red.</summary>
    [Fact]
    public void A_persona_added_to_a_copy_of_the_file_only_is_refused()
    {
        var path = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "personas.json");
        var copy = JsonNode.Parse(File.ReadAllText(path))!;
        var personas = copy["personas"]!.AsArray();
        var extra = personas[2]!.DeepClone();
        extra["id"] = "plan41-extra";
        extra["username"] = "acc-plan41-extra";
        personas.Add(extra);

        var problems = PersonaFileProblems(copy.ToJsonString());

        Assert.Contains(problems, p => p.Contains($"{AcceptancePersonaCatalog.ApprovedPersonaCount + 1} personas", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("plan41-extra", StringComparison.Ordinal));
    }

    private static List<string> PersonaFileProblems(string json)
    {
        var problems = new List<string>();
        var personas = JsonNode.Parse(json)!["personas"]!.AsArray().Select(p => p!).ToArray();
        if (personas.Length != AcceptancePersonaCatalog.ApprovedPersonaCount)
            problems.Add($"The file has {personas.Length} personas, the catalog approves {AcceptancePersonaCatalog.ApprovedPersonaCount}.");

        var known = AcceptancePersonaCatalog.Personas.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(personas.Select(p => p["id"]!.GetValue<string>()).Where(id => !known.Contains(id)).Select(id => $"{id} is not in the catalog."));

        foreach (var (tenant, count) in AcceptancePersonaCatalog.ApprovedRegularPersonas)
        {
            var found = personas.Count(p => p["fixture"] is null && p["tenantCode"]?.GetValue<string>() == tenant);
            if (found != count)
                problems.Add($"{tenant} has {found} regular personas, the catalog approves {count}.");
        }

        return problems;
    }

    [Fact]
    public void Persona_roles_respect_every_configured_separation_of_duties_conflict()
    {
        var conflicts = SodMatrixDefaults.CreateDefaultConflicts();
        var violations = AcceptancePersonaCatalog.Personas
            .SelectMany(persona => conflicts
                .Where(conflict =>
                    persona.Roles.Contains(conflict.RoleA, StringComparer.Ordinal)
                    && persona.Roles.Contains(conflict.RoleB, StringComparer.Ordinal))
                .Select(conflict => $"{persona.Username}:{conflict.RoleA}+{conflict.RoleB}"))
            .ToArray();

        Assert.Empty(violations);
        Assert.NotEqual(
            AcceptancePersonaCatalog.Get("payroll-processor").Username,
            AcceptancePersonaCatalog.Get("payroll-approver").Username);
        Assert.NotEqual(
            AcceptancePersonaCatalog.Get("a-payroll-poster").Username,
            AcceptancePersonaCatalog.Get("a-payroll-reverser").Username);
        Assert.NotEqual(
            AcceptancePersonaCatalog.Get("a-contracts-maker").Username,
            AcceptancePersonaCatalog.Get("a-contracts-checker").Username);
        Assert.NotEqual(
            AcceptancePersonaCatalog.Get("a-logistics-maker").Username,
            AcceptancePersonaCatalog.Get("a-logistics-checker").Username);
    }

    [Fact]
    public void Committed_persona_source_contains_no_password_values()
    {
        var path = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "personas.json");
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("defaultPassword", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Accept-Hr123", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outbox_and_inbox_baselines_are_intentionally_empty()
    {
        using var frame = AcceptanceSeedExecution.Push(AcceptanceSeedCatalog.GetAgency("AGENCY-A"));
        Assert.Empty(HRManagement.ServiceDefaults.DemoIntegrationData.BuildAuditLogs(10, "Acceptance", AcceptanceSeedCatalog.ReferenceDate));
        Assert.Empty(HRManagement.ServiceDefaults.DemoIntegrationData.BuildMessageReceipts(10, "Acceptance", AcceptanceSeedCatalog.ReferenceDate));
    }

    [Fact]
    public void Cross_service_user_references_resolve_to_canonical_auth_users()
    {
        var agency = AcceptanceSeedCatalog.GetAgency("AGENCY-A");
        var persona = AcceptancePersonaCatalog.Personas.First(item =>
            item.Fixture is null && item.Enabled && item.StorageTenantId == agency.Id);
        var expected = AcceptanceSeedCatalog.DeterministicGuid(
            persona.Definition.TenantCode!,
            $"auth-user:{persona.Id}");

        using var frame = AcceptanceSeedExecution.Push(agency);
        Assert.Equal(expected, DemoSeedIds.AuthUserId(0));
        Assert.Equal(64, AcceptanceSeedPackFingerprint.Value.Length);
    }
}
