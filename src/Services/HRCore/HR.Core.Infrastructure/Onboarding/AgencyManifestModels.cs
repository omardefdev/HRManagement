namespace HR.Core.Infrastructure.Onboarding;

public sealed class AgencyManifest
{
    public string PackId { get; set; } = "";
    public string AgencyId { get; set; } = "";
    public string ReleaseDigest { get; set; } = "";
    public string? Signature { get; set; }
    public List<string> ReviewerRoles { get; set; } = [];
    public string RollbackRef { get; set; } = "";
    public AgencyTenantSection Tenant { get; set; } = new();
    public AgencyIdentitySection Identity { get; set; } = new();
    public AgencyRolesSection Roles { get; set; } = new();
    public AgencyPoliciesSection Policies { get; set; } = new();
    public AgencyWorkflowsSection Workflows { get; set; } = new();
    public AgencyReferenceSubsetSection ReferenceSubset { get; set; } = new();
    public AgencyLanguageSection Language { get; set; } = new();
    public AgencyCalendarSection Calendar { get; set; } = new();
    public AgencyIntegrationsSection Integrations { get; set; } = new();
    public AgencyRetentionSection Retention { get; set; } = new();
    public AgencyMonitoringSection Monitoring { get; set; } = new();
    public AgencyBackupSection Backup { get; set; } = new();
    public AgencySupportSection Support { get; set; } = new();
    public AgencyOrgStructureSection OrgStructure { get; set; } = new();
}

public sealed class AgencyTenantSection
{
    public string DisplayName { get; set; } = "";
    public string Archetype { get; set; } = "";
    public string GovernorateCode { get; set; } = "";
}

public sealed class AgencyIdentitySection
{
    public string Realm { get; set; } = "";
    public string AdminBootstrapRef { get; set; } = "";
}

public sealed class AgencyRolesSection
{
    public string CatalogRef { get; set; } = "";
    public string SodMatrixRef { get; set; } = "";
}

public sealed class AgencyPoliciesSection
{
    public List<string> PolicyPackIds { get; set; } = [];
    public List<string> CodePackIds { get; set; } = [];
    public bool PreserveNationalMandatory { get; set; } = true;
}

public sealed class AgencyWorkflowsSection
{
    public List<string> WorkflowPackIds { get; set; } = [];
}

public sealed class AgencyReferenceSubsetSection
{
    public List<string> CatalogKinds { get; set; } = [];
}

public sealed class AgencyLanguageSection
{
    public string PrimaryLocale { get; set; } = "ar-SY";
    public bool RtlFirst { get; set; } = true;
    public List<string>? SecondaryLocales { get; set; }
}

public sealed class AgencyCalendarSection
{
    public string Timezone { get; set; } = "Asia/Damascus";
    public string HolidayPolicyRef { get; set; } = "";
}

public sealed class AgencyIntegrationsSection
{
    public List<string> ConnectorProfiles { get; set; } = [];
}

public sealed class AgencyRetentionSection
{
    public string MatrixRef { get; set; } = "";
}

public sealed class AgencyMonitoringSection
{
    public string ObservabilityProfile { get; set; } = "";
}

public sealed class AgencyBackupSection
{
    public string ScheduleRef { get; set; } = "";
    public bool RestoreTestRequired { get; set; } = true;
}

public sealed class AgencySupportSection
{
    public string ServiceDeskTier { get; set; } = "";
    public string OnCallRef { get; set; } = "";
}

public sealed class AgencyOrgStructureSection
{
    public List<AgencyOrgUnitDef> Units { get; set; } = [];
}

public sealed class AgencyOrgUnitDef
{
    public string Code { get; set; } = "";
    public string? ParentCode { get; set; }
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
}

public sealed class AgencyProvisioningResult
{
    public bool DryRun { get; init; }
    public bool AlreadyApplied { get; init; }
    public Guid? TenantId { get; init; }
    public string AgencyId { get; init; } = "";
    public string PackId { get; init; } = "";
    public IReadOnlyList<string> Steps { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
