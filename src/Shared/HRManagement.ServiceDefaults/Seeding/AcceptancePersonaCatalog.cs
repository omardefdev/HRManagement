using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.ServiceDefaults.Seeding;

public sealed class AcceptancePersonaFile
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string ReferenceDate { get; set; } = string.Empty;
    public List<AcceptancePersonaDefinition> Personas { get; set; } = [];
}

public sealed class AcceptancePersonaDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordEnv { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];
    public string? TenantCode { get; set; }
    public string? TenantId { get; set; }
    public string? ScopeOrgUnitId { get; set; }
    public string? ScopeSiteCode { get; set; }
    public string? EmployeeSiteCode { get; set; }
    public int? EmployeeOrdinal { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Fixture { get; set; }
    public string? Purpose { get; set; }
    public string[] Scenarios { get; set; } = [];
}

public sealed record AcceptancePersona(
    AcceptancePersonaDefinition Definition,
    Guid? ClaimTenantId,
    Guid StorageTenantId,
    Guid? ScopeOrgUnitId,
    AcceptanceEmployee? Employee)
{
    public string Id => Definition.Id;
    public string Username => Definition.Username;
    public IReadOnlyList<string> Roles => Definition.Roles;
    public bool Enabled => Definition.Enabled;
    public string? Fixture => Definition.Fixture;
}

public static class AcceptanceRoleCatalog
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        HrManagementRoles.Admin,
        HrManagementRoles.HrCore,
        HrManagementRoles.HrCoreSupervisory,
        HrManagementRoles.HrCoreExecutive,
        HrManagementRoles.HrContracts,
        HrManagementRoles.HrHealth,
        HrManagementRoles.HrHealthClinical,
        HrManagementRoles.HrMovements,
        HrManagementRoles.HrTraining,
        HrManagementRoles.Payroll,
        HrManagementRoles.PayrollProcessor,
        HrManagementRoles.PayrollApprover,
        HrManagementRoles.PayrollPoster,
        HrManagementRoles.PayrollReverser,
        HrManagementRoles.IdCard,
        HrManagementRoles.RecordsArchive,
        HrManagementRoles.AdminLogistics,
        HrManagementRoles.ItInfra,
        HrManagementRoles.DevAnalytics,
        HrManagementRoles.Compliance,
        HrManagementRoles.AuthDirectory,
        HrManagementRoles.FileStorage,
        HrManagementRoles.HrManagementApprover,
        HrManagementRoles.BranchManager,
        HrManagementRoles.HrEmployee,
        HrManagementRoles.PlatformAdmin,
        HrManagementRoles.Auditor,
        HrManagementRoles.HrExamMarker
    };
}

/// <summary>Loads and validates the single committed source for Acceptance v2 identities.</summary>
public static class AcceptancePersonaCatalog
{
    private const string ResourceName = "HRManagement.Acceptance.personas.json";
    private static readonly Lazy<IReadOnlyList<AcceptancePersona>> LazyPersonas = new(LoadAndValidate);
    private static readonly Lazy<string> LazyReleaseDigest = new(ComputeReleaseDigest);

    public static IReadOnlyList<AcceptancePersona> Personas => LazyPersonas.Value;
    public static string ReleaseDigest => LazyReleaseDigest.Value;

    public static AcceptancePersona Get(string id) =>
        Personas.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown acceptance persona '{id}'.");

    private static IReadOnlyList<AcceptancePersona> LoadAndValidate()
    {
        using var stream = typeof(AcceptancePersonaCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded acceptance persona resource '{ResourceName}' was not found.");
        var source = JsonSerializer.Deserialize<AcceptancePersonaFile>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Acceptance persona catalog is empty.");

        if (!string.Equals(source.SchemaVersion, AcceptanceSeedCatalog.PackId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected persona schema '{AcceptanceSeedCatalog.PackId}', found '{source.SchemaVersion}'.");
        if (source.Personas.Count != 61)
            throw new InvalidOperationException($"Acceptance v2 requires exactly 61 personas; found {source.Personas.Count}.");
        EnsureUnique(source.Personas.Select(p => p.Id), "persona id");
        EnsureUnique(source.Personas.Select(p => p.Username), "username");
        EnsureUnique(source.Personas.Select(p => p.PasswordEnv), "password environment key");

        var conflicts = SodMatrixDefaults.CreateDefaultConflicts();
        var result = new List<AcceptancePersona>(source.Personas.Count);
        foreach (var definition in source.Personas)
        {
            if (definition.Roles.Length == 0)
                throw new InvalidOperationException($"Persona '{definition.Id}' has no role.");
            var unknown = definition.Roles.Where(r => !AcceptanceRoleCatalog.All.Contains(r)).ToArray();
            if (unknown.Length > 0)
                throw new InvalidOperationException($"Persona '{definition.Id}' has unknown roles: {string.Join(", ", unknown)}.");

            foreach (var conflict in conflicts)
            {
                if (definition.Roles.Contains(conflict.RoleA, StringComparer.Ordinal)
                    && definition.Roles.Contains(conflict.RoleB, StringComparer.Ordinal))
                    throw new InvalidOperationException(
                        $"Persona '{definition.Id}' violates SoD by combining '{conflict.RoleA}' and '{conflict.RoleB}'.");
            }

            var tenantId = ResolveTenant(definition);
            var scopeId = ResolveScope(definition, tenantId);
            var employee = ResolveEmployee(definition, tenantId);
            result.Add(new AcceptancePersona(
                definition,
                tenantId,
                tenantId ?? TenantDefaults.DefaultTenantId,
                scopeId,
                employee));
        }

        ValidateDistribution(result);
        var duplicateEmployees = result.Where(p => p.Employee is not null)
            .GroupBy(p => p.Employee!.Id).Where(g => g.Count() > 1).ToArray();
        if (duplicateEmployees.Length > 0)
            throw new InvalidOperationException("Every acceptance account must map to a distinct employee.");

        return result.AsReadOnly();
    }

    private static string ComputeReleaseDigest()
    {
        using var stream = typeof(AcceptancePersonaCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded acceptance persona resource '{ResourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant();
    }

    private static Guid? ResolveTenant(AcceptancePersonaDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.TenantCode))
        {
            if (!string.Equals(definition.Fixture, "missing-tenant", StringComparison.Ordinal))
                throw new InvalidOperationException($"Persona '{definition.Id}' is missing a tenant outside the negative fixture.");
            return null;
        }

        var resolved = string.Equals(definition.TenantCode, TenantDefaults.DefaultCode, StringComparison.OrdinalIgnoreCase)
            ? TenantDefaults.DefaultTenantId
            : AcceptanceSeedCatalog.GetAgency(definition.TenantCode).Id;
        if (!string.IsNullOrWhiteSpace(definition.TenantId) && Guid.Parse(definition.TenantId) != resolved)
            throw new InvalidOperationException($"Persona '{definition.Id}' tenant code and id disagree.");
        return resolved;
    }

    private static Guid? ResolveScope(AcceptancePersonaDefinition definition, Guid? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(definition.ScopeOrgUnitId))
        {
            var explicitId = Guid.Parse(definition.ScopeOrgUnitId);
            if (!string.Equals(definition.Fixture, "invalid-scope", StringComparison.Ordinal))
            {
                var unit = AcceptanceSeedCatalog.GetOrgUnit(explicitId);
                if (unit.TenantId != tenantId)
                    throw new InvalidOperationException($"Persona '{definition.Id}' scope belongs to another tenant.");
            }
            return explicitId;
        }

        if (string.IsNullOrWhiteSpace(definition.ScopeSiteCode))
            return null;
        if (tenantId is null || tenantId == TenantDefaults.DefaultTenantId)
            throw new InvalidOperationException($"Persona '{definition.Id}' cannot resolve a site scope without an agency.");
        var agency = AcceptanceSeedCatalog.GetAgency(tenantId.Value);
        return agency.Sites.Single(s => string.Equals(s.Code, definition.ScopeSiteCode, StringComparison.OrdinalIgnoreCase)).RootOrgUnitId;
    }

    private static AcceptanceEmployee? ResolveEmployee(AcceptancePersonaDefinition definition, Guid? tenantId)
    {
        if (string.IsNullOrWhiteSpace(definition.EmployeeSiteCode))
            return null;
        if (tenantId is null || tenantId == TenantDefaults.DefaultTenantId)
            throw new InvalidOperationException($"Persona '{definition.Id}' cannot map an employee without an agency.");

        var candidates = AcceptanceSeedCatalog.GetAgency(tenantId.Value).Employees
            .Where(e => string.Equals(e.SiteCode, definition.EmployeeSiteCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var ordinal = definition.EmployeeOrdinal ?? 0;
        if (ordinal < 0 || ordinal >= candidates.Length)
            throw new InvalidOperationException($"Persona '{definition.Id}' employee ordinal is outside site '{definition.EmployeeSiteCode}'.");
        return candidates[ordinal];
    }

    private static void ValidateDistribution(IReadOnlyList<AcceptancePersona> personas)
    {
        var regular = personas.Where(p => p.Fixture is null).ToArray();
        var fixtures = personas.Where(p => p.Fixture is not null).ToArray();
        if (fixtures.Length != 4)
            throw new InvalidOperationException("Acceptance v2 requires exactly four negative personas.");
        if (regular.Count(p => p.Definition.TenantCode == TenantDefaults.DefaultCode) != 2
            || regular.Count(p => p.Definition.TenantCode == "AGENCY-A") != 43
            || regular.Count(p => p.Definition.TenantCode == "AGENCY-B") != 8
            || regular.Count(p => p.Definition.TenantCode == "AGENCY-C") != 4)
            throw new InvalidOperationException("Acceptance persona distribution does not match the approved 2/43/8/4 plan (PLAN-25 added central-a2).");
    }

    private static void EnsureUnique(IEnumerable<string> values, string label)
    {
        var duplicate = values.GroupBy(v => v, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate {label}: '{duplicate.Key}'.");
    }
}
