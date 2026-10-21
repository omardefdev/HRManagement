namespace HRManagement.ServiceDefaults;

/// <summary>Keycloak confidential client IDs for workloads (IAM-004).</summary>
public static class ServiceClientIds
{
    public const string HrCore = "svc-hr-core";
    public const string HrContracts = "svc-hr-contracts";
    public const string HrHealth = "svc-hr-health";
    public const string HrMovements = "svc-hr-movements";
    public const string HrTraining = "svc-hr-training";
    public const string Payroll = "svc-payroll";
    public const string IdCard = "svc-idcard";
    public const string RecordsArchive = "svc-records-archive";
    public const string AdminLogistics = "svc-admin-logistics";
    public const string ItInfra = "svc-it-infra";
    public const string DevAnalytics = "svc-dev-analytics";
    public const string Compliance = "svc-compliance";
    public const string AuthDirectory = "svc-auth-directory";
    public const string FileStorage = "svc-file-storage";
    public const string Gateway = "hr-gateway-client";

    public static readonly IReadOnlyList<string> All =
    [
        HrCore, HrContracts, HrHealth, HrMovements, HrTraining, Payroll,
        IdCard, RecordsArchive, AdminLogistics, ItInfra, DevAnalytics,
        Compliance, AuthDirectory, FileStorage
    ];
}

/// <summary>API resource audiences validated by JwtBearer (IAM-004).</summary>
public static class ServiceApiAudiences
{
    public const string HrCore = "api-hr-core";
    public const string AuthDirectory = "api-auth-directory";
    public const string FileStorage = "api-file-storage";
    public const string Payroll = "api-payroll";
}

/// <summary>OAuth scopes granted to service accounts for S2S calls (IAM-004).</summary>
public static class ServiceScopes
{
    public const string DirectorySync = "directory.sync";
    public const string HrCoreLookup = "hr-core.lookup";
    public const string HrCoreWorkflow = "hr-core.workflow";
    public const string FileStorageReferences = "file-storage.references";
    public const string DsarSubjectRead = "dsar.subject.read";
    public const string EvidenceIngest = "evidence.ingest";
}

/// <summary>Authorization policy names for service-to-service endpoints (IAM-004).</summary>
public static class ServicePolicies
{
    public const string DirectorySync = "Service.DirectorySync";
    public const string HrCoreLookupS2S = "Service.HrCoreLookup";
    public const string FileStorageReferences = "Service.FileStorageReferences";
    public const string DsarSubjectRead = "Service.DsarSubjectRead";
    public const string EvidenceIngest = "Service.EvidenceIngest";
}
