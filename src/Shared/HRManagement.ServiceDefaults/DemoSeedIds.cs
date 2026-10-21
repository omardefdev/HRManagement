using System.Security.Cryptography;
using System.Text;
using HRManagement.ServiceDefaults.Seeding;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Deterministic demo entity IDs so all microservices
/// can independently compute the same set without cross-DB queries.
/// Only used in Development environment seeding.
/// </summary>
public static class DemoSeedIds
{
    // Must be valid hex (the historical "DEMO" segment was invalid and crashed type init).
    private static readonly Guid Namespace = Guid.Parse("DEADBEEF-DE00-4000-8000-000000000000");

    /// <summary>Well-known org unit IDs matching HRCoreDataSeeder / legacy demo seeders.</summary>
    private static readonly Guid[] KnownOrgUnitIds =
    [
        Guid.Parse("11111111-1111-1111-1111-111111111101"), // 0 ROOT
        Guid.Parse("11111111-1111-1111-1111-111111111102"), // 1 HR
        Guid.Parse("11111111-1111-1111-1111-111111111103"), // 2 Finance
        Guid.Parse("11111111-1111-1111-1111-111111111104"), // 3 Legal
        Guid.Parse("11111111-1111-1111-1111-111111111105"), // 4 IT
        Guid.Parse("11111111-1111-1111-1111-111111111106"), // 5 Audit
        Guid.Parse("11111111-1111-1111-1111-111111111107"), // 6 Admin
        Guid.Parse("11111111-1111-1111-1111-111111111108"), // 7 Planning
        Guid.Parse("11111111-1111-1111-1111-111111111109"), // 8 Public Relations
    ];

    public static Guid DefaultContractTypeId => AcceptanceSeedExecution.IsAcceptance
        ? DeterministicGuid("contract-type-default")
        : Guid.Parse("22222222-2222-2222-2222-222222222201");

    public static Guid DefaultSalaryScaleId => AcceptanceSeedExecution.IsAcceptance
        ? DeterministicGuid("salary-scale-default")
        : Guid.Parse("33333333-3333-3333-3333-333333333301");

    public static int EmployeeCount => AcceptanceSeedExecution.EmployeeCount;
    public static int OrgUnitCount => AcceptanceSeedExecution.OrgUnitCount;
    public static int TrainingCourseCount => 30;
    public static int AssetCount => AcceptanceSeedExecution.AssetCount;

    public static Guid EmployeeId(int index) => AcceptanceSeedExecution.CurrentAgency?.EmployeeAt(index).Id
        ?? DeterministicGuid($"employee-{index}");

    public static Guid OrgUnitId(int index) =>
        AcceptanceSeedExecution.CurrentAgency is { } agency
            ? agency.OrgUnitAt(index).Id
            : index >= 0 && index < KnownOrgUnitIds.Length
            ? KnownOrgUnitIds[index]
            : DeterministicGuid($"orgunit-{index}");

    public static Guid EmployeeOrgUnitId(int employeeIndex) =>
        AcceptanceSeedExecution.CurrentAgency?.EmployeeAt(employeeIndex).OrgUnitId
        ?? OrgUnitId(1 + employeeIndex % Math.Max(1, OrgUnitCount - 1));

    public static Guid ContractId(int index) => DeterministicGuid($"contract-{index}");
    public static Guid TrainingCourseId(int index) => DeterministicGuid($"training-course-{index}");
    public static Guid AssetId(int index) => DeterministicGuid($"asset-{index}");
    public static Guid ContractTypeId(int index) =>
        index == 0 ? DefaultContractTypeId : DeterministicGuid($"contract-type-{index}");
    public static Guid TrainingProviderId(int index) => DeterministicGuid($"training-provider-{index}");
    public static Guid CardDesignId(int index) => DeterministicGuid($"card-design-{index}");
    public static Guid RoleId(int index) => DeterministicGuid($"role-{index}");
    public static Guid AuthUserId(int index)
    {
        if (AcceptanceSeedExecution.CurrentAgency is { } agency)
        {
            var personas = AcceptancePersonaCatalog.Personas
                .Where(persona => persona.Fixture is null
                    && persona.Enabled
                    && persona.StorageTenantId == agency.Id)
                .ToArray();
            if (personas.Length == 0)
                throw new InvalidOperationException($"Acceptance tenant '{agency.Code}' has no active persona.");

            var persona = personas[index % personas.Length];
            return AcceptanceSeedCatalog.DeterministicGuid(
                persona.Definition.TenantCode ?? "NO-TENANT",
                $"auth-user:{persona.Id}");
        }

        return DeterministicGuid($"auth-user-{index}");
    }
    public static Guid JobCatalogId(int index) => DeterministicGuid($"job-catalog-{index}");
    public static Guid PayGradeId(int index) =>
        !AcceptanceSeedExecution.IsAcceptance && index is >= 0 and < 6
            ? Guid.Parse($"44444444-4444-4444-4444-44444444440{index + 1}")
            : DeterministicGuid($"pay-grade-{index}");
    public static Guid SalaryScaleId(int index) =>
        index == 0 ? DefaultSalaryScaleId : DeterministicGuid($"salary-scale-{index}");
    public static Guid InsuranceContractId(int index) => DeterministicGuid($"insurance-contract-{index}");
    public static Guid VendorId(int index) => DeterministicGuid($"vendor-{index}");
    public static Guid DocumentClassificationId(int index) => DeterministicGuid($"doc-class-{index}");
    public static Guid Named(string name) => DeterministicGuid(name);

    /// <summary>Roughly top ~10% of workforce (directors / section heads).</summary>
    public static bool IsSupervisor(int index) => AcceptanceSeedExecution.CurrentAgency?.EmployeeAt(index).IsSupervisor
        ?? (index < 40 || index % 12 == 0);

    /// <summary>
    /// Status buckets per 100 employees: 85 Active, 5 OnLeave, 5 Seconded, 3 Terminated, 2 Retired.
    /// </summary>
    public static int StatusBucket(int index)
    {
        var state = AcceptanceSeedExecution.CurrentAgency?.EmployeeAt(index).State;
        return state switch
        {
            AcceptanceEmployeeState.Active => 10,
            AcceptanceEmployeeState.Probation => 20,
            AcceptanceEmployeeState.OnLeave => 87,
            AcceptanceEmployeeState.Seconded => 92,
            AcceptanceEmployeeState.Terminated => 96,
            AcceptanceEmployeeState.Retired => 99,
            _ => index % 100
        };
    }

    public static bool IsActiveEmployee(int index) => AcceptanceSeedExecution.CurrentAgency?.EmployeeAt(index).IsPayable
        ?? StatusBucket(index) < 85;

    private static Guid DeterministicGuid(string name)
    {
        if (AcceptanceSeedExecution.CurrentAgency is { } agency)
            name = $"{AcceptanceSeedCatalog.PackId}|{agency.Code}|{name}";

        var nsBytes = Namespace.ToByteArray();
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var combined = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, combined, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, combined, nsBytes.Length, nameBytes.Length);
        var hash = SHA256.HashData(combined);
        var guidBytes = new byte[16];
        Buffer.BlockCopy(hash, 0, guidBytes, 0, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40); // v4
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variant 1
        return new Guid(guidBytes);
    }
}
