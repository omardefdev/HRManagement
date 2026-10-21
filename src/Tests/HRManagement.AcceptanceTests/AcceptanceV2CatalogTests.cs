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
    public void Persona_catalog_has_55_separated_accounts_and_real_employee_links()
    {
        var personas = AcceptancePersonaCatalog.Personas;
        Assert.Equal(55, personas.Count);
        Assert.Equal(55, personas.Select(p => p.Username).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(49, personas.Count(p => p.Employee is not null));
        Assert.Equal(4, personas.Count(p => p.Fixture is not null));
        Assert.Equal(53, personas.Count(p => p.Enabled));

        Assert.All(personas.Where(p => p.Employee is not null), persona =>
        {
            Assert.Equal(persona.StorageTenantId, persona.Employee!.TenantId);
            Assert.Contains(
                AcceptanceSeedCatalog.GetAgency(persona.Employee.TenantId).Employees,
                employee => employee.Id == persona.Employee.Id);
        });

        Assert.Equal(37, personas.Count(p => p.Fixture is null && p.Definition.TenantCode == "AGENCY-A"));
        Assert.Equal(8, personas.Count(p => p.Fixture is null && p.Definition.TenantCode == "AGENCY-B"));
        Assert.Equal(4, personas.Count(p => p.Fixture is null && p.Definition.TenantCode == "AGENCY-C"));
        Assert.Equal(2, personas.Count(p => p.Fixture is null && p.Definition.TenantCode == "PLATFORM"));
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
