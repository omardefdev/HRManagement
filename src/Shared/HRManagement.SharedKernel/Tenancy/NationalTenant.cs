namespace HRManagement.SharedKernel.Tenancy;

/// <summary>
/// PLAN-48 (D-TEN-1, D-TEN-2): the national tenant and the one reference list of the national shared tables.
/// <para>
/// <see cref="TableNames"/> must equal the tables of the entities marked <see cref="INationalShared"/> in the EF model and the
/// tables of <c>scripts/sql/ten048-national-shared-read.sql</c>; a unit test proves the three are one list.
/// </para>
/// </summary>
public static class NationalTenant
{
    /// <summary>The national tenant (PLATFORM), also the trial agency (D-OPS-3).</summary>
    public static readonly Guid Id = TenantDefaults.DefaultTenantId;

    public const string Code = TenantDefaults.DefaultCode;

    /// <summary>The ten national shared tables (D-TEN-2). No table with personal data may join this list.</summary>
    public static readonly IReadOnlyList<string> TableNames =
    [
        "PolicyTemplates",
        "PolicyTemplateVersions",
        "PolicyRules",
        "PolicyFactsSchemas",
        "PolicyWorkflowBindings",
        "WorkflowDefinitions",
        "CodePolicyTemplates",
        "CodePolicyVersions",
        "CodePolicyRules",
        "ReferenceCatalogEntries"
    ];

    public static bool IsNational(Guid? tenantId) => tenantId == Id;
}
