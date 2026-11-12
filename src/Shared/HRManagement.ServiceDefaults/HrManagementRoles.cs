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

    /// <summary>PLAN-43 (D-WF-4): writes and publishes workflow definitions from the national tenant. Reaches no HR.Core data.</summary>
    public const string WorkflowAdmin = "workflow-admin";

    /// <summary>PLAN-47 (D-POL-3): writes policy drafts. No default holder; hr-core and admin author too.</summary>
    public const string PolicyAuthor = "policy-author";

    /// <summary>PLAN-47 (D-POL-3): publishes policy versions it did not edit. No default holder; hr-management-approver and admin publish too.</summary>
    public const string PolicyApprover = "policy-approver";

    /// <summary>Every role above: what a workflow step may name (PLAN-43).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Admin, HrCore, HrCoreSupervisory, HrCoreExecutive, HrContracts, HrHealth, HrHealthClinical, HrMovements, HrTraining,
        Payroll, PayrollProcessor, PayrollApprover, PayrollPoster, PayrollReverser, IdCard, RecordsArchive, AdminLogistics,
        ItInfra, DevAnalytics, Compliance, AuthDirectory, FileStorage, HrManagementApprover, BranchManager, HrEmployee,
        PlatformAdmin, Auditor, HrExamMarker, WorkflowAdmin, PolicyAuthor, PolicyApprover
    ];
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

    /// <summary>
    /// PLAN-51 (D-51-2, decision 3): HR.Core's sensitive decisions that are not approvals of another's request (publish a requisition or its
    /// internal posting, publish competition results, revoke an offer, approve a workforce plan). Roles from configuration, by default
    /// today's: hr-admin and hr-core. <see cref="HrCoreApprove"/> would widen them to the management approver and the platform.
    /// </summary>
    public const string HrCoreDecision = "HrCore.Decision";

    // PLAN-51 (§4, D-51-2): the write and approval policy of each module, roles from Authorization:Policies:{name}:Roles; the defaults are
    // today's (the module's roles), so a single-role module changes for nobody. The module filter applies {Module}.Write to a write that
    // names no policy of its own; every write names one anyway.
    public const string HrContractsWrite = "HrContracts.Write";
    public const string FileStorageWrite = "FileStorage.Write";
    public const string PayrollWrite = "Payroll.Write";
    public const string HrMovementsApprove = "HrMovements.Approve";
    public const string HrTrainingWrite = "HrTraining.Write";
    public const string HrTrainingApprove = "HrTraining.Approve";
    public const string HrHealthWrite = "HrHealth.Write";
    public const string HrHealthApprove = "HrHealth.Approve";
    public const string ComplianceWrite = "Compliance.Write";
    public const string ComplianceApprove = "Compliance.Approve";
    public const string IdCardWrite = "IdCard.Write";
    public const string IdCardApprove = "IdCard.Approve";
    public const string AdminLogisticsWrite = "AdminLogistics.Write";
    public const string AdminLogisticsApprove = "AdminLogistics.Approve";
    public const string ItInfraWrite = "ItInfra.Write";
    public const string ItInfraApprove = "ItInfra.Approve";
    public const string RecordsArchiveWrite = "RecordsArchive.Write";
    public const string RecordsArchiveApprove = "RecordsArchive.Approve";
    public const string DevAnalyticsWrite = "DevAnalytics.Write";
    public const string DevAnalyticsApprove = "DevAnalytics.Approve";
    public const string AuthDirectoryWrite = "AuthDirectory.Write";
    public const string AuthDirectoryApprove = "AuthDirectory.Approve";

    /// <summary>PLAN-51 (decision 4): the DSAR requests of Compliance, formerly <c>[Authorize(Roles = compliance,platform-admin)]</c>.</summary>
    public const string ComplianceDsar = "Compliance.Dsar";

    /// <summary>PLAN-51 (decision 4): the platform operator's own gateway actions (tenant entry, platform audit), formerly <c>Roles = platform-admin</c>.</summary>
    public const string PlatformOperator = "Platform.Operator";

    /// <summary>PLAN-51 (§5, G-IAM-09): agency onboarding through the API: platform-admin only, in the service and at the gateway.</summary>
    public const string PlatformAgencyOnboarding = "Platform.AgencyOnboarding";

    /// <summary>
    /// PLAN-51 (§5, D-51-5): publishing evidence artifacts (<c>Service.EvidenceIngest</c>) is open to the service clients listed in
    /// <c>Evidence:IngestClients</c>, empty by default (nobody).
    /// </summary>
    public const string EvidenceIngestClientsKey = "Evidence:IngestClients";

    /// <summary>PLAN-43 (D-WF-4): write and publish workflow definitions: hr-admin or workflow-admin, never a service account.</summary>
    public const string WorkflowDefinitionAdmin = "Workflow.DefinitionAdmin";

    /// <summary>PLAN-43 (G-WF-05): a service account with hr-core.workflow. Which type it owns is checked per request.</summary>
    public const string WorkflowExternalStart = "Workflow.ExternalStart";

    /// <summary>PLAN-43 (G-WF-05): approve or reject a step through the API. PLAN-45 and PLAN-46 add roles; never a service account.</summary>
    public const string WorkflowStepAction = "Workflow.StepAction";

    /// <summary>PLAN-43: read workflow definitions: the HR.Core readers, and workflow-admin, which reads nothing else in HR.Core.</summary>
    public const string WorkflowDefinitionRead = "Workflow.DefinitionRead";

    /// <summary>PLAN-40: the replay of lost events (MessagingRepairController): an administrator or a platform operator.</summary>
    public const string MessagingRepair = "Messaging.Repair";
    public const string HrCoreApprove = "HrCore.Approve";

    /// <summary>PLAN-47 (D-POL-3): write policy drafts: PolicyEngine:Roles:Author (admin, hr-core, policy-author); never a service account.</summary>
    public const string PolicyAuthor = "Policy.Author";

    /// <summary>PLAN-47 (D-POL-3): publish policy versions: PolicyEngine:Roles:Approver (admin, hr-management-approver, policy-approver).</summary>
    public const string PolicyApprover = "Policy.Approver";

    /// <summary>PLAN-47: read policy templates and versions: the HR.Core readers, the authors and the approvers.</summary>
    public const string PolicyReader = "Policy.Reader";

    /// <summary>Cross-tenant quarantine reconciliation is restricted to platform operators.</summary>
    public const string TenantQuarantineReconcile = "TenantQuarantine.Reconcile";

    /// <summary>HCM-028 (PLAN-25 slice): recruitment appeal review — central HR and the compliance reviewer.</summary>
    public const string HrCoreAppealReview = "HrCore.AppealReview";

    /// <summary>HCM-028 (PLAN-25 slice): HR.Core work inbox — the read-only roles plus compliance (appeals only) and admin-logistics (external approvals only).</summary>
    public const string HrCoreWorkInbox = "HrCore.WorkInbox";

    /// <summary>HCM-031 (PLAN-31): the anonymous exam marking screens — the exam marker role only.</summary>
    public const string HrCoreExamMarking = "HrCore.ExamMarking";

    /// <summary>
    /// Lookup APIs shared across modules (no NationalId in responses). PLAN-51 (D-51-3): an interactive staff user (PLAN-50's definition,
    /// <c>Authorization:NonStaffRoles</c>) or a service account with <c>hr-core.lookup</c>; never <c>hr-employee</c> or the exam marker alone.
    /// </summary>
    public const string HrCoreLookup = "HrCore.Lookup";

    /// <summary>PLAN-51 (§2, P-4): any signed-in user, for files that carry no data (HR.Core's static files, those of the self-service segment).</summary>
    public const string GatewayAuthenticated = "Gateway.Authenticated";

    /// <summary>
    /// PLAN-51 (§3, D-IAM-7): the gateway's audit pages (operations, audit, audit search). Roles from <c>Gateway:Audit:Roles</c>, by default
    /// platform-admin (every agency), auditor and compliance (their own agency, filtered in <c>GatewayAuditQueryService</c>); not hr-admin.
    /// </summary>
    public const string GatewayAudit = "Gateway.Audit";

    public const string HrContractsApprove = "HrContracts.Approve";
    public const string PayrollProcess = "Payroll.Process";
    public const string PayrollApprove = "Payroll.Approve";
    public const string PayrollPost = "Payroll.Post";
    public const string PayrollReverse = "Payroll.Reverse";

    public const string HrManagementApprove = "HrManagement.Approve";

    /// <summary>
    /// PLAN-50 (D-IAM-1, D-MOV-2): the four approval screens of the five movement types (list, details, approve, reject): the module role,
    /// the management approver, and an active delegate of the management approver (D-50-3). Used through <see cref="ModulePolicyOverrideAttribute"/>.
    /// </summary>
    public const string HrMovementsApprovalAccess = "HrMovements.ApprovalAccess";

    /// <summary>PLAN-50: create, edit, execute, activate, close and cancel a movement: Admin or hr-movements, explicitly.</summary>
    public const string HrMovementsWrite = "HrMovements.Write";

    /// <summary>
    /// PLAN-50 (§4): the gateway's coarse layer for the named approval paths only: an interactive user with a valid tenant_id and at least one
    /// role of the platform that is not a non-staff role (<c>Authorization:NonStaffRoles</c>). The service decides.
    /// </summary>
    public const string GatewayApprovalReach = "Gateway.ApprovalReach";

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
