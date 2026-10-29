namespace HRManagement.ServiceDefaults;

/// <summary>Realm roles expected in Keycloak (create under Realm → Roles).</summary>
public static class HrManagementRoles
{
    public const string Admin = "hr-admin";

    public const string HrCore = "hr-core";

    /// <summary>وصول لقائمة الكادر الإشرافي فقط (مكافئ صف الضباط).</summary>
    public const string HrCoreSupervisory = "hr-core-supervisory";

    /// <summary>وصول لقائمة الموظفين التنفيذيين فقط (مكافئ الأفراد).</summary>
    public const string HrCoreExecutive = "hr-core-executive";
    public const string HrContracts = "hr-contracts";
    public const string HrHealth = "hr-health";

    /// <summary>FUN-004: clinical least-access — diagnosis/history fields.</summary>
    public const string HrHealthClinical = "hr-health-clinical";

    public const string HrMovements = "hr-movements";
    public const string HrTraining = "hr-training";
    public const string Payroll = "payroll";
    public const string PayrollProcessor = "payroll-processor";
    public const string PayrollApprover = "payroll-approver";
    public const string PayrollPoster = "payroll-poster";
    public const string PayrollReverser = "payroll-reverser";
    public const string IdCard = "idcard";
    public const string RecordsArchive = "records-archive";
    public const string AdminLogistics = "admin-logistics";
    public const string ItInfra = "it-infra";
    public const string DevAnalytics = "dev-analytics";
    public const string Compliance = "compliance";
    public const string AuthDirectory = "auth-directory";
    public const string FileStorage = "file-storage";

    /// <summary>اعتماد الإدارة العليا (تعيين، نقل، ...).</summary>
    public const string HrManagementApprover = "hr-management-approver";

    /// <summary>مدير فرع — يرى بيانات وحدته والوحدات التابعة فقط.</summary>
    public const string BranchManager = "branch-manager";

    /// <summary>موظف — بوابة الخدمة الذاتية فقط.</summary>
    public const string HrEmployee = "hr-employee";

    /// <summary>مشغّل المنصة — عبور الجهات مع تدقيق (TEN-001).</summary>
    public const string PlatformAdmin = "platform-admin";

    /// <summary>مدقق مستقل للقراءة والتحقق من الأدلة دون صلاحيات تشغيلية.</summary>
    public const string Auditor = "auditor";

    /// <summary>
    /// HCM-031 (PLAN-31): exam marker — reaches only the anonymous marking screens of HR.Core (exam numbers, never
    /// identities). Not part of the HR.Core module or read roles.
    /// </summary>
    public const string HrExamMarker = "hr-exam-marker";
}

/// <summary>Authorization policy names (one per module).</summary>
public static class HrManagementPolicies
{
    public const string GatewayDashboard = "Gateway.Dashboard";

    public const string ModuleHrCore = "Module.HrCore";
    public const string ModuleHrContracts = "Module.HrContracts";
    public const string ModuleHrHealth = "Module.HrHealth";

    /// <summary>FUN-004: read/write clinical medical fields.</summary>
    public const string HrHealthClinical = "HrHealth.Clinical";

    public const string ModuleHrMovements = "Module.HrMovements";
    public const string ModuleHrTraining = "Module.HrTraining";
    public const string ModulePayroll = "Module.Payroll";
    public const string ModuleIdCard = "Module.IdCard";
    public const string ModuleRecordsArchive = "Module.RecordsArchive";
    public const string ModuleAdminLogistics = "Module.AdminLogistics";
    public const string ModuleItInfra = "Module.ItInfra";
    public const string ModuleDevAnalytics = "Module.DevAnalytics";
    public const string ModuleCompliance = "Module.Compliance";
    public const string ModuleAuthDirectory = "Module.AuthDirectory";
    public const string ModuleFileStorage = "Module.FileStorage";
    public const string ModuleEss = "Module.Ess";

    public const string HrCoreReadOnly = "HrCore.ReadOnly";
    public const string HrCoreWrite = "HrCore.Write";
    public const string HrCoreDelete = "HrCore.Delete";
    public const string HrCoreApprove = "HrCore.Approve";

    /// <summary>Cross-tenant quarantine reconciliation is restricted to platform operators.</summary>
    public const string TenantQuarantineReconcile = "TenantQuarantine.Reconcile";

    /// <summary>Scoped operational actions available to central HR and branch managers.</summary>
    public const string HrCoreBranchOperation = "HrCore.BranchOperation";

    /// <summary>HCM-028 (PLAN-25 slice): recruitment appeal review — central HR and the compliance reviewer.</summary>
    public const string HrCoreAppealReview = "HrCore.AppealReview";

    /// <summary>HCM-028 (PLAN-25 slice): HR.Core work inbox — the read-only roles plus compliance (appeals only).</summary>
    public const string HrCoreWorkInbox = "HrCore.WorkInbox";

    /// <summary>HCM-031 (PLAN-31): the anonymous exam marking screens — the exam marker role only.</summary>
    public const string HrCoreExamMarking = "HrCore.ExamMarking";

    /// <summary>Lookup APIs shared across modules — any authenticated user (no NationalId in responses).</summary>
    public const string HrCoreLookup = "HrCore.Lookup";

    public const string HrContractsApprove = "HrContracts.Approve";
    public const string PayrollProcess = "Payroll.Process";
    public const string PayrollApprove = "Payroll.Approve";
    public const string PayrollPost = "Payroll.Post";
    public const string PayrollReverse = "Payroll.Reverse";

    public const string HrManagementApprove = "HrManagement.Approve";

    public const string HrContractsHireView = "HrContracts.HireView";
    public const string HrContractsHireExecute = "HrContracts.HireExecute";

    /// <summary>Manage temporary approval delegations (IAM-003).</summary>
    public const string ManageDelegations = "SoD.ManageDelegations";

    /// <summary>Admin force-delete / retention / reconcile for File.Storage (FILE-001).</summary>
    public const string FileStorageForceDelete = "FileStorage.ForceDelete";

    /// <summary>EVD-003: read evidence artifacts (independent assessor / assurance).</summary>
    public const string EvidenceRead = "Evidence.Read";

    /// <summary>AC-26: view full national ID (not branch-manager scoped readers).</summary>
    public const string PiiViewNationalId = "Pii.ViewNationalId";
}
