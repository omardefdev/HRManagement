# PLAN-52 baseline — the reach matrix, the ids and keys per controller, workflow types, catalogues, the server

Branch `feature/plan-52-iam-branch-scope-bola` from `origin/main` = `e6a93705` (PLAN-51). Taken 2026-11-12, 20:25Z..22:30Z. The long
generated files are under `artifacts/plan52/inventory/` (not committed): `reach-matrix.tsv`, `controllers.tsv`, `ids.tsv`,
`hrcore-A-part1.md`, `hrcore-A-part2.md`, `acceptance-restricted-requests.md`.

## 0.2 Build and tests

| Step | Result |
|------|--------|
| `dotnet build HRManagement.slnx -warnaserror` | 0 errors, 0 warnings (1 min 38 s) |
| `dotnet test src/Tests/HRManagement.ArchitectureTests` | 132/132 (PLAN-51's 131 and the new report fact, which does nothing unless `PLAN52_INVENTORY_OUT` is set) |
| Unit filter `FullyQualifiedName~Bola\|FullyQualifiedName~OrgScope\|FullyQualifiedName~Hcm025` | 196/196 (BOLA v1 green since PLAN-34) |

## 0.3 The reach matrix

**How it is made.** `OrgScopeReach` (architecture project) evaluates the real policies of `AddHrManagementAuthorizationPolicies` for each
action of the fifteen web assemblies and the shared controllers, the way a request meets them:
1. the gateway: `GatewayPathAuthorizationMiddleware`'s segment policy, `TryGetFineGrainedPolicy` (with PLAN-50's
   `GatewayHrCoreActionPolicies` and `Gateway.ApprovalReach`) and PLAN-51's self-service rule, on `/ui/{segment}/{route}`;
2. the module filter of the service (`AddHrModuleAuthorizeFilter`), its `[ModulePolicyOverride]` and its write default;
3. the class and action policies (`[Authorize(Policy)]`, `[Authorize(Roles)]`);
4. **delegation** (P-3, PLAN-50): a `RoleOrDelegationRequirement` is met by a user with an employee and an active delegation of
   `hr-management-approver`, in the services that register a delegation lookup (HR.Core's database, HR.Movements' client); the gateway and
   HR.Contracts decide by roles only.

`OrgScopeInventoryReport` writes `reach-matrix.tsv` (one row per action and verb), `controllers.tsv` and `ids.tsv` when
`PLAN52_INVENTORY_OUT` names a folder. (The first run had a defect: a verb attribute without a template gave an empty route, so a
conventional POST lost its controller segment and met the wrong gateway rule. Fixed before this table.)

**The restricted users** (D-IAM-3: `branch-manager` and not `hr-admin`):

| User | Personas | Roles |
|------|----------|-------|
| `bm-core-logistics` | `br1-mgr`, `br2-mgr` | branch-manager, hr-core, admin-logistics |
| `bm-core` | `a-homs-mgr`, `a-hama-mgr`, `a-lat-mgr`, `a-tar-mgr`, `b-branch-manager`, `c-branch-manager` | branch-manager, hr-core |
| `bm-only` | `negative-invalid-scope` | branch-manager (invalid scope) |
| `bm-delegate` | synthetic: no persona has an employee | `bm-core-logistics`'s roles, an employee, an active delegation of hr-management-approver |
| `bm-employee` | synthetic: a branch manager who is also an employee | `bm-core-logistics`'s roles and hr-employee, with an employee |

**Per service** (controllers: actual = some restricted user reaches one action; latent = none):

| Service | Actual | Latent | Actual controllers and the path that decides |
|---------|-------:|-------:|----------------------------------------------|
| hr-core | 66 | 15 | `Module.HrCore` and `HrCore.ReadOnly` admit branch-manager; `HrCore.Write` admits hr-core; 2 self-service controllers only for `bm-employee` |
| hr-contracts | 1 | 21 | `HireWizard` (12 of 14 actions): `HrContracts.HireView`/`HireExecute` admit hr-core, `[ModulePolicyOverride(HireView)]`; `Step3Approve` needs `HrManagement.Approve` (no lookup in HR.Contracts) |
| admin-logistics | 8 | 0 | `Module.AdminLogistics` and `AdminLogistics.Write` admit admin-logistics (`br1-mgr`, `br2-mgr`) |
| hr-movements | 5 | 8 | **delegation only** (`bm-delegate`): `TransferRequests`, `LoanAssignmentRequests`, `SecondmentRequests`, `PromotionRequests`, `DelegationRequests` — `Index`, `Details`, `Approve` via `Gateway.ApprovalReach` + `HrMovements.ApprovalAccess` (P-9) |
| payroll | 1 | 24 | `Ess` (3/3) for `bm-employee` via `Module.Ess` (self-service) |
| file-storage | 1 | 2 | `FilesApi`: only the anonymous health probe |
| gateway | 2 | 3 | `Account` (sign-in, anonymous), `Home` (9/13: `Gateway.Dashboard` and authenticated pages) |
| shared | 1 | 0 | `Culture` (`Gateway.Authenticated`) |
| hr-health, hr-training, compliance, idcard, it-infra, records-archive, dev-analytics, auth-directory | 0 | 7..13 each | no restricted role reaches the module (D-IAM-3) |

The HR.Core approval paths a delegate meets (`WorkflowApprovals`, `WorkflowInstances` approve/reject, the recruitment approve actions on
`HrManagement.Approve`) already apply both scopes (PLAN-50); `bm-delegate` adds no HR.Core controller that `bm-core-logistics` does not
already reach. The full table follows (generated from `controllers.tsv`).

<details><summary>Reach per controller (235 rows)</summary>

| Service | Controller | Reach | Restricted users that reach it | Reached actions / all | The path that decides (first reached action) |
|---|---|---|---|---:|---|
| admin-logistics | AssetAssignments | actual | bm-core-logistics, bm-delegate, bm-employee | 6/6 | GET /ui/admin-logistics/AssetAssignments/Create via Module.AdminLogistics + Module.AdminLogistics |
| admin-logistics | Assets | actual | bm-core-logistics, bm-delegate, bm-employee | 14/14 | POST /ui/admin-logistics/Assets/AssignToEmployee via Module.AdminLogistics + Module.AdminLogistics+AdminLogistics.Write |
| admin-logistics | Home | actual | bm-core-logistics, bm-delegate, bm-employee | 3/3 | GET /ui/admin-logistics/Home/Error via Module.AdminLogistics + Module.AdminLogistics |
| admin-logistics | InventoryItems | actual | bm-core-logistics, bm-delegate, bm-employee | 5/5 | GET /ui/admin-logistics/InventoryItems/Create via Module.AdminLogistics + Module.AdminLogistics |
| admin-logistics | MaintenanceRecords | actual | bm-core-logistics, bm-delegate, bm-employee | 4/4 | GET /ui/admin-logistics/MaintenanceRecords/Create via Module.AdminLogistics + Module.AdminLogistics |
| admin-logistics | ReceiptRecords | actual | bm-core-logistics, bm-delegate, bm-employee | 6/6 | GET /ui/admin-logistics/ReceiptRecords/Create via Module.AdminLogistics + Module.AdminLogistics |
| admin-logistics | SupplyOrders | actual | bm-core-logistics, bm-delegate, bm-employee | 9/9 | POST /ui/admin-logistics/SupplyOrders/AddLine via Module.AdminLogistics + Module.AdminLogistics+AdminLogistics.Write |
| admin-logistics | Vendors | actual | bm-core-logistics, bm-delegate, bm-employee | 5/5 | GET /ui/admin-logistics/Vendors/Create via Module.AdminLogistics + Module.AdminLogistics |
| auth-directory | AuthUsers | latent | - | 0/4 | - |
| auth-directory | DirectoryEntries | latent | - | 0/2 | - |
| auth-directory | DirectoryInternalSync | latent | - | 0/1 | - |
| auth-directory | DirectorySyncRecords | latent | - | 0/1 | - |
| auth-directory | Home | latent | - | 0/3 | - |
| auth-directory | PermissionPolicies | latent | - | 0/2 | - |
| auth-directory | Roles | latent | - | 0/4 | - |
| auth-directory | StatsApi | latent | - | 0/1 | - |
| compliance | AuditFindings | latent | - | 0/7 | - |
| compliance | Complaints | latent | - | 0/13 | - |
| compliance | ComplianceRecommendations | latent | - | 0/4 | - |
| compliance | Dashboard | latent | - | 0/1 | - |
| compliance | DisciplinaryActions | latent | - | 0/8 | - |
| compliance | DsarRequests | latent | - | 0/7 | - |
| compliance | Export | latent | - | 0/2 | - |
| compliance | ExportJobs | latent | - | 0/3 | - |
| compliance | Home | latent | - | 0/3 | - |
| compliance | LegalCases | latent | - | 0/7 | - |
| compliance | LegalReferrals | latent | - | 0/4 | - |
| dev-analytics | AnalyticsEventsApi | latent | - | 0/1 | - |
| dev-analytics | AnalyticsReconciliationApi | latent | - | 0/2 | - |
| dev-analytics | AnalyticsTestHooks | latent | - | 0/1 | - |
| dev-analytics | Dashboards | latent | - | 0/4 | - |
| dev-analytics | DataRefreshSchedules | latent | - | 0/5 | - |
| dev-analytics | EtlJobs | latent | - | 0/5 | - |
| dev-analytics | ExportJobs | latent | - | 0/4 | - |
| dev-analytics | Home | latent | - | 0/3 | - |
| dev-analytics | Kpis | latent | - | 0/4 | - |
| dev-analytics | ReadModels | latent | - | 0/5 | - |
| dev-analytics | Reports | latent | - | 0/4 | - |
| dev-analytics | WorkforcePlanning | latent | - | 0/2 | - |
| file-storage | DsarInternal | latent | - | 0/1 | - |
| file-storage | FilesServiceUpload | latent | - | 0/3 | - |
| file-storage | FilesApi | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 1/19 | GET /ui/files/api/files/health via Module.FileStorage + Module.FileStorage |
| gateway | AuditSecurity | latent | - | 0/6 | - |
| gateway | EvidenceArtifacts | latent | - | 0/4 | - |
| gateway | PlatformTenant | latent | - | 0/2 | - |
| gateway | Account | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET Account.Login via - +  |
| gateway | Home | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 9/13 | GET Home.Error via - +  |
| hr-contracts | AdministrativeDecisions | latent | - | 0/4 | - |
| hr-contracts | AdvanceRequests | latent | - | 0/8 | - |
| hr-contracts | CommencementRecords | latent | - | 0/4 | - |
| hr-contracts | ContractRenewals | latent | - | 0/1 | - |
| hr-contracts | ContractSignatures | latent | - | 0/2 | - |
| hr-contracts | ContractTypeRegimeMaps | latent | - | 0/4 | - |
| hr-contracts | ContractTypes | latent | - | 0/4 | - |
| hr-contracts | Contracts | latent | - | 0/6 | - |
| hr-contracts | Dashboard | latent | - | 0/1 | - |
| hr-contracts | Export | latent | - | 0/4 | - |
| hr-contracts | ExportJobs | latent | - | 0/3 | - |
| hr-contracts | Home | latent | - | 0/3 | - |
| hr-contracts | LeaveBalances | latent | - | 0/3 | - |
| hr-contracts | LeavePolicies | latent | - | 0/4 | - |
| hr-contracts | LeaveRequests | latent | - | 0/9 | - |
| hr-contracts | ProbationRecords | latent | - | 0/4 | - |
| hr-contracts | ProcessInterventions | latent | - | 0/4 | - |
| hr-contracts | SeparationRecords | latent | - | 0/8 | - |
| hr-contracts | SeparationWizard | latent | - | 0/7 | - |
| hr-contracts | ServiceExtensions | latent | - | 0/1 | - |
| hr-contracts | ServiceTransactions | latent | - | 0/3 | - |
| hr-contracts | HireWizard | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee | 12/14 | POST /ui/hr-contracts/HireWizard/CompleteMedicalStep via HrContracts.HireExecute + HrContracts.HireView+HrContracts.HireView+HrContracts.HireExecute |
| hr-core | ApprovalDelegationsInternal | latent | - | 0/1 | - |
| hr-core | CandidateLifecycle | latent | - | 0/7 | - |
| hr-core | CodeGenerationInternal | latent | - | 0/2 | - |
| hr-core | DsarInternal | latent | - | 0/4 | - |
| hr-core | EmployeeLifecycle | latent | - | 0/4 | - |
| hr-core | EmployeeLifecycleInternal | latent | - | 0/1 | - |
| hr-core | ExamMarking | latent | - | 0/4 | - |
| hr-core | MasterDataDuplicates | latent | - | 0/4 | - |
| hr-core | MessagingRepair | latent | - | 0/3 | - |
| hr-core | Onboarding | latent | - | 0/1 | - |
| hr-core | PolicyEnforceInternal | latent | - | 0/1 | - |
| hr-core | RecruitmentAdmin | latent | - | 0/1 | - |
| hr-core | ServiceRegimeInternal | latent | - | 0/2 | - |
| hr-core | TenantQuarantine | latent | - | 0/3 | - |
| hr-core | WorkflowExternalInstances | latent | - | 0/3 | - |
| hr-core | Approvals | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 6/6 | POST /ui/hr-core/Approvals/Approve via HrCore.Approve + Module.HrCore+HrCore.ReadOnly+HrCore.Approve |
| hr-core | ApprovedCVs | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 5/5 | GET /ui/hr-core/ApprovedCVs/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | BenefitPlans | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 1/1 | GET /ui/hr-core/BenefitPlans/Index via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly |
| hr-core | CandidatePortal | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 13/13 | POST /ui/hr-core/my/offer/accept via HrCore.Write +  |
| hr-core | CareerPaths | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 6/6 | POST /ui/hr-core/CareerPaths/AddStep via HrCore.Write + Module.HrCore+HrCore.Write |
| hr-core | CodeGeneration | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/4 | GET /ui/hr-core/api/codes/policies/a0000001-0000-4000-8000-0000000000f1 via HrCore.ReadOnly + HrCore.ReadOnly+HrCore.ReadOnly |
| hr-core | CodePolicies | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee | 3/3 | GET /ui/hr-core/CodePolicies/Index via HrCore.ReadOnly + Module.HrCore+HrCore.Write |
| hr-core | Competitions | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 39/39 | POST /ui/hr-core/Competitions/AddExamSession via HrCore.Write + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | Contacts | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/4 | GET /ui/hr-core/Contacts/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | DailyAttendanceRecords | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 7/7 | GET /ui/hr-core/DailyAttendanceRecords/BulkEntry via HrCore.ReadOnly + Module.HrCore |
| hr-core | Dashboard | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 1/1 | GET /ui/hr-core/Dashboard/Index via HrCore.ReadOnly + Module.HrCore |
| hr-core | DataImport | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee | 3/3 | POST /ui/hr-core/DataImport/ImportEmployees via HrCore.Write + Module.HrCore+HrCore.Write |
| hr-core | Delegations | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee | 3/3 | GET /ui/hr-core/Delegations/Create via HrCore.ReadOnly + Module.HrCore+SoD.ManageDelegations |
| hr-core | DiwanCorrespondences | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET /ui/hr-core/DiwanCorrespondences/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | Documents | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/4 | GET /ui/hr-core/Documents/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | EmployeeCorrections | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 5/5 | POST /ui/hr-core/EmployeeCorrections/Approve via HrCore.Approve + Module.HrCore+HrCore.ReadOnly+HrCore.Approve |
| hr-core | EmployeeProfiles | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET /ui/hr-core/EmployeeProfiles/Edit via HrCore.ReadOnly + Module.HrCore |
| hr-core | Employees | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/5 | GET /ui/hr-core/Employees/Create via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | EmployeesApi | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 3/3 | GET /ui/hr-core/api/employees/by-national-id-hash via HrCore.ReadOnly + Module.HrCore+HrCore.Lookup |
| hr-core | EmploymentRecords | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/5 | POST /ui/hr-core/EmploymentRecords/Approve via HrCore.Approve + Module.HrCore+HrCore.ReadOnly+HrCore.Approve |
| hr-core | EssPortal | actual | bm-employee | 6/6 | GET /ui/ess/EssPortal/Advance via Module.Ess + Module.Ess+Module.Ess |
| hr-core | EssTalent | actual | bm-employee | 4/4 | POST /ui/ess/EssTalent/Apply via Module.Ess + Module.Ess+Module.Ess |
| hr-core | EstablishmentChanges | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 7/7 | POST /ui/hr-core/EstablishmentChanges/ApproveWorkflow via Gateway.ApprovalReach + Module.HrCore+HrCore.ReadOnly+Workflow.StepAction |
| hr-core | EstablishmentFinance | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET /ui/hr-core/EstablishmentFinance/Import via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | EstablishmentPlanningApi | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 6/6 | GET /ui/hr-core/api/establishment/gap-analysis via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly |
| hr-core | EstablishmentTemporalApi | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 5/5 | GET /ui/hr-core/api/establishment/catalog/a0000001-0000-4000-8000-0000000000f1/as-of via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly |
| hr-core | Evaluations | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 3/4 | GET /ui/hr-core/Evaluations/Create via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | ExportJobs | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 3/3 | POST /ui/hr-core/export-jobs/a0000001-0000-4000-8000-0000000000f1/cancel via HrCore.Write + Module.HrCore+HrCore.ReadOnly |
| hr-core | FieldInspectionVisits | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET /ui/hr-core/FieldInspectionVisits/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | FundedPositions | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 5/5 | GET /ui/hr-core/FundedPositions/Create via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | Home | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 3/3 | GET /ui/hr-core/Home/Error via HrCore.ReadOnly + Module.HrCore |
| hr-core | HrStatisticReports | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET /ui/hr-core/HrStatisticReports/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | JobCatalog | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 7/7 | GET /ui/hr-core/JobCatalog/Create via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | KeyPositions | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/4 | GET /ui/hr-core/KeyPositions/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | LookupApi | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 11/11 | GET /ui/hr-core/api/lookup/employees/a0000001-0000-4000-8000-0000000000f1 via HrCore.Lookup + HrCore.Lookup+HrCore.Lookup |
| hr-core | OfferLetters | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 11/11 | POST /ui/hr-core/OfferLetters/AcceptOffer via HrCore.Write + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | OrgStructureApi | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET /ui/hr-core/api/org-structure/units/a0000001-0000-4000-8000-0000000000f1/as-of via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly |
| hr-core | OrgUnits | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 5/6 | GET /ui/hr-core/OrgUnits/Create via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | Policies | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 16/17 | POST /ui/hr-core/api/policies/versions/a0000001-0000-4000-8000-0000000000f1/rules via Policy.Author + Policy.Reader+Policy.Reader+Policy.Author |
| hr-core | PolicyTemplates | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 14/15 | POST /ui/hr-core/PolicyTemplates/AddRule via Policy.Author + Policy.Reader+Policy.Reader+Policy.Author |
| hr-core | PublicCareers | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 10/10 | POST /ui/hr-core/apply via HrCore.Write +  |
| hr-core | PublicJobPostings | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 6/7 | POST /ui/hr-core/PublicJobPostings/ChangeApplicationStatus via HrCore.Write + Module.HrCore+HrCore.Write |
| hr-core | Qualifications | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/4 | GET /ui/hr-core/Qualifications/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | RecruitmentAppeals | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee | 6/6 | POST /ui/hr-core/RecruitmentAppeals/AssignReviewer via HrCore.AppealReview + HrCore.AppealReview+HrCore.AppealReview+HrCore.Write |
| hr-core | RecruitmentApplications | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 17/17 | POST /ui/hr-core/RecruitmentApplications/AssignRank via HrCore.Write + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | RecruitmentAssessmentCriteria | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | POST /ui/hr-core/RecruitmentAssessmentCriteria/Create via HrCore.Write + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | RecruitmentReports | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 5/5 | GET /ui/hr-core/RecruitmentReports/ExportApplicationsCsv via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly |
| hr-core | RecruitmentRequisitions | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 17/17 | POST /ui/hr-core/RecruitmentRequisitions/ApplyCommitteeRanking via HrCore.Write + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | ReferenceCatalog | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 6/6 | GET /ui/hr-core/ReferenceCatalog/Create via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | ReferenceData | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 7/8 | GET /ui/hr-core/api/reference-data/a0000001-0000-4000-8000-0000000000f1/impact via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly |
| hr-core | Reports | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 6/6 | POST /ui/hr-core/Reports/ExportEmployeesCsv via HrCore.Write + Module.HrCore+HrCore.ReadOnly |
| hr-core | Rosters | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 6/6 | POST /ui/hr-core/Rosters/AddAssignment via HrCore.Write + Module.HrCore+HrCore.Write |
| hr-core | SelectionCommittees | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 14/14 | POST /ui/hr-core/SelectionCommittees/AddMember via HrCore.Write + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | ServiceRegimeExceptions | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/4 | POST /ui/hr-core/ServiceRegimeExceptions/Dismiss via HrCore.Write + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-core | Shifts | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/4 | GET /ui/hr-core/Shifts/Create via HrCore.ReadOnly + Module.HrCore |
| hr-core | Signatures | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 4/4 | GET /ui/hr-core/Signatures/Details via HrCore.ReadOnly + Module.HrCore |
| hr-core | SignaturesApi | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 1/1 | GET /ui/hr-core/api/signatures via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly |
| hr-core | StatsApi | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 1/1 | GET /ui/hr-core/api/stats/pending-approvals via HrCore.ReadOnly + Module.HrCore |
| hr-core | StatusHistory | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET /ui/hr-core/StatusHistory/FullLog via HrCore.ReadOnly + Module.HrCore+HrCore.ReadOnly |
| hr-core | SuccessionPlans | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 6/6 | POST /ui/hr-core/SuccessionPlans/AddCandidate via HrCore.Write + Module.HrCore+HrCore.Write |
| hr-core | TalentMarketplace | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 5/6 | POST /ui/hr-core/TalentMarketplace/ChangeApplicationStatus via HrCore.Write + Module.HrCore+HrCore.Write |
| hr-core | WorkInbox | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 1/1 | GET /ui/hr-core/WorkInbox/Index via HrCore.WorkInbox + HrCore.WorkInbox+HrCore.WorkInbox |
| hr-core | WorkflowApprovals | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 3/3 | POST /ui/hr-core/WorkflowApprovals/ApproveWorkflow via Gateway.ApprovalReach + HrCore.WorkInbox+HrCore.WorkInbox+Workflow.StepAction |
| hr-core | WorkflowDefinitions | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/10 | GET /ui/hr-core/api/workflow-definitions/a0000001-0000-4000-8000-0000000000f1 via Workflow.DefinitionRead + Workflow.DefinitionRead+Workflow.DefinitionRead |
| hr-core | WorkflowInstances | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 5/5 | POST /ui/hr-core/api/workflow-instances/a0000001-0000-4000-8000-0000000000f1/approve via Gateway.ApprovalReach + Module.HrCore+HrCore.ReadOnly+Workflow.StepAction |
| hr-core | WorkforcePlans | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 7/7 | POST /ui/hr-core/WorkforcePlans/AddLine via HrCore.Write + Module.HrCore+HrCore.ReadOnly+HrCore.Write |
| hr-health | Dashboard | latent | - | 0/1 | - |
| hr-health | HealthRetirementRecords | latent | - | 0/4 | - |
| hr-health | HealthStatusEntries | latent | - | 0/4 | - |
| hr-health | Home | latent | - | 0/3 | - |
| hr-health | InsuranceCards | latent | - | 0/9 | - |
| hr-health | InsuranceContracts | latent | - | 0/5 | - |
| hr-health | MedicalCommitteeReports | latent | - | 0/4 | - |
| hr-health | MedicalRecommendations | latent | - | 0/4 | - |
| hr-health | MedicalRecords | latent | - | 0/7 | - |
| hr-health | PeriodicMedicalChecks | latent | - | 0/5 | - |
| hr-health | WorkplaceSafety | latent | - | 0/9 | - |
| hr-movements | Dashboard | latent | - | 0/1 | - |
| hr-movements | DistributionPlans | latent | - | 0/6 | - |
| hr-movements | Home | latent | - | 0/3 | - |
| hr-movements | MessagingRepair | latent | - | 0/3 | - |
| hr-movements | MovementExecutions | latent | - | 0/2 | - |
| hr-movements | MovementNotices | latent | - | 0/5 | - |
| hr-movements | ProcessInterventions | latent | - | 0/3 | - |
| hr-movements | TransferFinancialImpacts | latent | - | 0/2 | - |
| hr-movements | DelegationRequests | actual | bm-delegate | 3/6 | POST /ui/hr-movements/DelegationRequests/Approve via Gateway.ApprovalReach + HrMovements.ApprovalAccess+HrManagement.Approve |
| hr-movements | LoanAssignmentRequests | actual | bm-delegate | 3/7 | POST /ui/hr-movements/LoanAssignmentRequests/Approve via Gateway.ApprovalReach + HrMovements.ApprovalAccess+HrManagement.Approve |
| hr-movements | PromotionRequests | actual | bm-delegate | 3/5 | POST /ui/hr-movements/PromotionRequests/Approve via Gateway.ApprovalReach + HrMovements.ApprovalAccess+HrManagement.Approve |
| hr-movements | SecondmentRequests | actual | bm-delegate | 3/8 | POST /ui/hr-movements/SecondmentRequests/Approve via Gateway.ApprovalReach + HrMovements.ApprovalAccess+HrManagement.Approve |
| hr-movements | TransferRequests | actual | bm-delegate | 4/8 | POST /ui/hr-movements/TransferRequests/Approve via Gateway.ApprovalReach + HrMovements.ApprovalAccess+HrManagement.Approve |
| hr-training | Certificates | latent | - | 0/2 | - |
| hr-training | Dashboard | latent | - | 0/1 | - |
| hr-training | ELearningCourses | latent | - | 0/8 | - |
| hr-training | EssLearning | latent | - | 0/5 | - |
| hr-training | Home | latent | - | 0/3 | - |
| hr-training | MissionFiles | latent | - | 0/7 | - |
| hr-training | QualificationCodes | latent | - | 0/6 | - |
| hr-training | TrainingCourses | latent | - | 0/5 | - |
| hr-training | TrainingEnrollments | latent | - | 0/5 | - |
| hr-training | TrainingNominations | latent | - | 0/5 | - |
| hr-training | TrainingPlans | latent | - | 0/5 | - |
| hr-training | TrainingProviders | latent | - | 0/5 | - |
| hr-training | TrainingResults | latent | - | 0/2 | - |
| idcard | AccessPermissions | latent | - | 0/5 | - |
| idcard | CardDesigns | latent | - | 0/5 | - |
| idcard | CardPermissionSets | latent | - | 0/5 | - |
| idcard | CardRequests | latent | - | 0/8 | - |
| idcard | Home | latent | - | 0/3 | - |
| idcard | IssuedCards | latent | - | 0/11 | - |
| idcard | OfficialSealRecords | latent | - | 0/5 | - |
| it-infra | BackupJobs | latent | - | 0/6 | - |
| it-infra | EnvironmentConfigurations | latent | - | 0/4 | - |
| it-infra | Home | latent | - | 0/3 | - |
| it-infra | SecurityIncidents | latent | - | 0/6 | - |
| it-infra | ServerDevices | latent | - | 0/5 | - |
| it-infra | SupportTickets | latent | - | 0/10 | - |
| it-infra | SystemPermissionRecords | latent | - | 0/5 | - |
| it-infra | UserDevices | latent | - | 0/4 | - |
| payroll | AllowanceRecords | latent | - | 0/2 | - |
| payroll | Arrears | latent | - | 0/4 | - |
| payroll | CompensationTypes | latent | - | 0/4 | - |
| payroll | Dashboard | latent | - | 0/1 | - |
| payroll | DeductionRecords | latent | - | 0/2 | - |
| payroll | DeductionTypes | latent | - | 0/4 | - |
| payroll | DsarInternal | latent | - | 0/1 | - |
| payroll | EmployeePayProfiles | latent | - | 0/3 | - |
| payroll | EndOfService | latent | - | 0/5 | - |
| payroll | ExportJobs | latent | - | 0/3 | - |
| payroll | FraudAlerts | latent | - | 0/4 | - |
| payroll | Home | latent | - | 0/3 | - |
| payroll | PayGradeLookupInternal | latent | - | 0/1 | - |
| payroll | PayGrades | latent | - | 0/4 | - |
| payroll | PaymentBatches | latent | - | 0/1 | - |
| payroll | PaymentHolds | latent | - | 0/3 | - |
| payroll | PaymentHoldsApi | latent | - | 0/1 | - |
| payroll | PayrollAdjustments | latent | - | 0/2 | - |
| payroll | PayrollApi | latent | - | 0/19 | - |
| payroll | PayrollAuditRecords | latent | - | 0/3 | - |
| payroll | PayrollConfigurations | latent | - | 0/7 | - |
| payroll | PayrollReports | latent | - | 0/12 | - |
| payroll | PayrollRuns | latent | - | 0/16 | - |
| payroll | SalaryScales | latent | - | 0/4 | - |
| payroll | Ess | actual | bm-employee | 3/3 | GET /ui/payroll/Ess/Arrears via Module.Ess + Module.Ess+Module.Ess |
| records-archive | ArchiveIndexEntries | latent | - | 0/4 | - |
| records-archive | ArchiveItems | latent | - | 0/14 | - |
| records-archive | DigitalFileRecords | latent | - | 0/5 | - |
| records-archive | DocumentClassifications | latent | - | 0/4 | - |
| records-archive | Home | latent | - | 0/3 | - |
| records-archive | LoanRecords | latent | - | 0/5 | - |
| records-archive | PhysicalFileRecords | latent | - | 0/4 | - |
| shared | Culture | actual | bm-core, bm-core-logistics, bm-delegate, bm-employee, bm-only | 2/2 | GET Culture.GetCulture via - +  |

</details>

## 0.4 What PLAN-34, QA-034 and PLAN-50 did (recorded, not redone)

- **PLAN-34 (QA-034, P4): the twelve `Index(employeeId)` actions** answer 403 for an employee outside the scope and list only the scope
  (`EmployeeIdsInScope` over the snapshots; Payroll's `PayrollEmployeeScope` over the active pay profile): HR.Contracts `Contracts`,
  `CommencementRecords`, `HireWizard`, `ProbationRecords`, `SeparationRecords`, `ServiceTransactions`; Payroll `EndOfService`,
  `FraudAlerts`, `PaymentHolds` and three in `PayrollExtendedControllers`. `BolaArchitectureTests` (v1, a regex over the files) went
  green without an allow-list. The six Payroll lists are latent (no restricted role reaches Payroll); the six HR.Contracts lists keep
  their 403 under `[OrgScoped(..., OutOfScope = Forbid)]` (P-4).
- **QA-034 since:** `HireWizard.Index` and `Continue(employeeId)` (403; an employee with no snapshot is outside); `LeaveRequests`,
  `AdvanceRequests`, `LeaveBalances` lists filtered the same way; HR.Core's export jobs capture the requester's scope
  (`HRCoreExportJobService`: employees CSV and the recruitment reports).
- **PLAN-50:** the delegation stores `PrincipalScopeOrgUnitId`; the inbox, `WorkflowActionActor` and HR.Movements' transfers and loans
  apply the delegate's scope and the principal's. **PLAN-25:** `RecruitmentOrgScopeFilter` (404) on the recruitment screens.
- **The kept answers** (P-4): AC-06 pins 403 on `api/lookup/employees/{id}`; AC-32 pins 403 on `Assets/Details`; QA-034 pins 403 on
  `HireWizard.Index`/`Continue`; `Plan034ScopedIndexTests` pins the lookup's 403; `Hcm032RecruitmentReportsControllerTests` (and
  AC-HCM-32b) pin an empty report (200) for an out-of-scope unit filter; `Wf045WorkflowApprovalsControllerTests` pins 403 (not a permitted
  reader) and 404 (unregistered type).

## Inventory A — per controller: stance, entity and key, ids, kept answers, lists

Notation: **404** = D-52-5 (the scenario's answer, or no older answer to keep); **403 kept** = `OutOfScope = Forbid` because the
controller answers 403 today (its §2 rule 2 area, or an older test pins it). "Lists" = each `Index`, export and pick-list to filter
with `WhereInOrgScope`. The id-by-id tables are in `hrcore-A-part1.md`, `hrcore-A-part2.md` (HR.Core) and `ids.tsv`; the decisions
below are the ones P1–P4 apply.

### HR.Core (81 controllers)

| Controller | Reach | Stance | Entity → unit | Answer | Lists, exports, pick-lists to filter |
|---|---|---|---|---|---|
| ApprovalDelegationsInternal | latent | NotApplicable(service-to-service) | — | — | — |
| Approvals | actual | OrgScoped(ApprovalRequest) | ApprovalRequest → its ApprovedCV → Employee.PrimaryOrgUnitId (another entity type: no key) | 404 | Index, Pending, and the inbox's legacy items (unfiltered today) |
| ApprovedCVs | actual | OrgScoped(ApprovedCV) | ApprovedCV → Employee | 403 kept | (per employee, checked) |
| BenefitPlans | actual | NotApplicable(reference data) | no unit | — | — |
| CandidateLifecycle | latent | CentralOnly | candidate applications | 403 | — |
| CandidatePortal | actual | NotApplicable(self-service: the candidate's own applications) | — | — | — |
| CareerPaths | actual | NotApplicable(reference data) — inventory C | no unit | — | — |
| CodeGeneration | actual | NotApplicable(reference data); `ListIssuances` CentralOnly | issuances carry no unit | 403 on the issuance log | — |
| CodeGenerationInternal | latent | NotApplicable(service-to-service) | — | — | — |
| CodePolicies | actual | NotApplicable(reference data); `Issuances` CentralOnly | as above | 403 on the issuance log | — |
| Competitions | actual | RecruitmentOrgScope(CentralOnly) (unchanged) | a competition spans requisitions | 403 | — |
| Contacts | actual | OrgScoped(ContactInfo) | ContactInfo → Employee | 403 kept | Create/Edit/Delete unchecked today |
| DailyAttendanceRecords | actual | OrgScoped(DailyAttendanceRecord) | → Employee | **404** (AC-IAM-52a; was 403 on Details/Edit/Delete) | filtered; `Create` employee, `Edit` before and after |
| Dashboard | actual | OrgScoped | counts of employees, qualifications, documents | — | the qualification and document counts (unfiltered today) |
| DataImport | actual | CentralOnly | imports org units (as roots) and employees anywhere | 403 | — |
| Delegations | actual | NotApplicable(self-service: the user's own delegations; the delegate is any employee, PLAN-50) | — | — | — |
| DiwanCorrespondences | actual | OrgScoped(DiwanCorrespondence) | new `OrgUnitId` (HCM052, D-52-2) | 404 | Index, the employee pick-list |
| Documents | actual | OrgScoped(EmployeeDocument) | → Employee | 403 kept | Create/Edit/Delete unchecked today |
| DsarInternal | latent | NotApplicable(service-to-service) | — | — | — |
| EmployeeCorrections | actual | OrgScoped(EmployeeCorrection) | → Employee | 403 kept | `RequestCorrection` unchecked today |
| EmployeeLifecycle | latent | CentralOnly | Employee lifecycle operations | 403 | — |
| EmployeeLifecycleInternal | latent | NotApplicable(service-to-service) | — | — | — |
| EmployeeProfiles | actual | OrgScoped(Employee) | Employee; `vm.Id` = EmployeeProfile → Employee | 403 kept | — |
| Employees | actual | OrgScoped(Employee) | Employee.PrimaryOrgUnitId | 403 kept | filtered; the posted unit stays the action's validation (200 with an error, kept) |
| EmployeesApi | actual | OrgScoped(Employee) | Employee | 403 kept | filtered |
| EmploymentRecords | actual | OrgScoped(EmploymentRecord) | → Employee | 403 kept | the org-unit pick-list |
| EssPortal, EssTalent | actual (`bm-employee`) | NotApplicable(self-service by ownership) | — | — | — |
| EstablishmentChanges | actual | CentralOnly (see the acceptance section) | EstablishmentChangeRequest (no unit) | 403 | — |
| EstablishmentFinance | actual | OrgScoped (the unit itself); `Import` CentralOnly | posted/queried unit | 403 kept | reconciliation without a unit (agency-wide) |
| EstablishmentPlanningApi | actual | OrgScoped(WorkforcePlan); imports and `SubmitChange` CentralOnly | WorkforcePlan.OrgUnitId | 403 kept | reconciliation without a unit; PlanGap of a plan with no unit |
| EstablishmentTemporalApi | actual | OrgScoped(FundedPosition); `Catalog*` CentralOnly | FundedPosition.OrgUnitId | 403 kept | — |
| Evaluations | actual | OrgScoped(PerformanceEvaluation) | → Employee | 403 kept | Index without an employee (unfiltered today) |
| ExamMarking | latent | NotApplicable(self-service: anonymous marking) | — | — | — |
| ExportJobs | actual | NotApplicable(self-service: the requester's job, whose scope the job records) | — | — | `Status` has no owner check today (fixed) |
| FieldInspectionVisits | actual | OrgScoped(FieldInspectionVisit) | new `OrgUnitId` (HCM052, D-52-2) | 404 | Index |
| FundedPositions | actual | OrgScoped(FundedPosition) | FundedPosition.OrgUnitId | 403 kept | filtered |
| Home | actual | NotApplicable(a page with no org records) | — | — | — |
| HrStatisticReports | actual | OrgScoped(HrStatisticReport) | HrStatisticReport.OrgUnitId | 404 | Index, the org-unit pick-list |
| JobCatalog | actual | CentralOnly (today's 403 kept, D-IAM-3: no widening) | no unit | 403 | — |
| KeyPositions | actual | OrgScoped(KeyPosition) | KeyPosition.OrgUnitId | 404 | Index; org-unit and employee pick-lists |
| LookupApi | actual | OrgScoped(Employee) | Employee; units | 403 kept (AC-06) | `employees/by-ids` served from the cache unchecked (fixed) |
| MasterDataDuplicates, MessagingRepair, Onboarding, RecruitmentAdmin, TenantQuarantine | latent | CentralOnly | agency administration | 403 | — |
| OfferLetters | actual | RecruitmentOrgScope(Offer) (unchanged) | → FundedPosition | 404 | filtered |
| OrgStructureApi, OrgUnits | actual | OrgScoped(OrgUnit) | the unit itself; `ParentId`, `HeadEmployeeId` | 403 kept | a null parent (a move to the root) is refused for a restricted user |
| Policies, PolicyTemplates | actual | NotApplicable(reference data) — inventory C | no unit | — | — |
| PolicyEnforceInternal, ServiceRegimeInternal, WorkflowExternalInstances | latent | NotApplicable(service-to-service) | — | — | — |
| PublicCareers | actual | NotApplicable(the public portal, anonymous) | — | — | — |
| PublicJobPostings | actual | RecruitmentOrgScope(PublicPosting) + `citizenApplicationId` | → requisition's position | 404 | filtered; `ChangeApplicationStatus`/`DeclareCitizenCoi` take a citizen application as `id` |
| Qualifications | actual | OrgScoped(Qualification) | → Employee | 403 kept | Create/Edit/Delete unchecked today |
| RecruitmentAppeals | actual | OrgScoped(RecruitmentAppeal) | → application → requisition → position | 404 (as `Details` today) | Index filtered in memory after `Take(200)` today |
| RecruitmentApplications | actual | RecruitmentOrgScope(Application) + the new kinds | → requisition → position | 404 | the published-requisition pick-list of `Create`; `Hire`'s unit and employee pick-lists |
| RecruitmentAssessmentCriteria | actual | RecruitmentOrgScope(Requisition) (new) | → requisition | 404 | — |
| RecruitmentReports | actual | OrgScoped(RecruitmentRequisition) | → position | report filters are not keys (an empty report, 200, kept) | filtered; its export job records the scope |
| RecruitmentRequisitions | actual | RecruitmentOrgScope(Requisition) (unchanged) | → FundedPosition | 404 | `VacantPositions(includeId)` |
| ReferenceCatalog | actual | CentralOnly (today's 403 kept) | no unit | 403 | — |
| ReferenceData | actual | NotApplicable(reference data) — inventory C | no unit | — | — |
| Reports | actual | OrgScoped(Employee) | Employee | 403 kept | filtered; its export job records the scope |
| Rosters | actual | OrgScoped(ShiftRoster) | ShiftRoster.OrgUnitId; `RemoveAssignment(id)` = ShiftAssignment → roster | 404 | Index; org-unit and employee pick-lists |
| SelectionCommittees | actual | RecruitmentOrgScope(CommitteeSession) (unchanged) | → requisition | 404 | — |
| ServiceRegimeExceptions | actual | OrgScoped(ServiceRegimeException) | → Employee | 404 | Index |
| Shifts | actual | NotApplicable(reference data); writes CentralOnly (D-52-3) | no unit | 403 on writes | — |
| Signatures, SignaturesApi | actual | CentralOnly (see the acceptance section) | SignatureRecord (no unit) | 403 | — |
| StatsApi | actual | NotApplicable(a page with no org records) | — | — | — |
| StatusHistory | actual | OrgScoped(EmploymentStatusHistory) | → Employee | 403 kept | `FullLog`; the employee pick-list |
| SuccessionPlans | actual | OrgScoped(SuccessionPlan) | → KeyPosition.OrgUnitId; `RemoveCandidate(id)` = candidate | 404 | Index; key-position and employee pick-lists |
| TalentMarketplace | actual | RecruitmentOrgScope(InternalVacancy) (new) | InternalVacancy.OrgUnitId; InternalApplication → vacancy | 404 | Index, the org-unit pick-list |
| WorkInbox | actual | NotApplicable(self-service: the inbox service filters both scopes) | — | — | — |
| WorkflowApprovals | actual | OrgScoped(WorkflowInstance) | WorkflowInstance.ScopeOrgUnitId | 403 kept | — |
| WorkflowDefinitions | actual | NotApplicable(reference data) | — | — | — |
| WorkflowInstances | actual | OrgScoped(WorkflowInstance) on `List`, `Get`, `History`; step decisions keep the workflow gates | `ScopeOrgUnitId` (stamped at start, HCM052 backfill) | **404** (AC-IAM-52d) | `List` |
| WorkforcePlans | actual | OrgScoped(WorkforcePlan) | WorkforcePlan.OrgUnitId | 403 kept | `GapReport(planId)` |

### HR.Contracts (22 controllers; only `HireWizard` actual)

Every employee record is keyed through `EmployeeId` to the local `EmployeeOrgUnitSnapshot` (no snapshot = central, §3 rule 2).

| Controller | Stance | Entity → unit | Answer | Lists, exports, pick-lists |
|---|---|---|---|---|
| AdministrativeDecisions | OrgScoped(AdministrativeDecision) | `RelatedEmployeeId` → snapshot (none = central) | 404 | Index (unfiltered today) |
| AdvanceRequests | OrgScoped(AdvanceRequest) | → snapshot | 403 kept (QA-034 list) | filtered; `ApproveAjax`/`RejectAjax` body `Id` |
| CommencementRecords | OrgScoped(CommencementRecord) | → snapshot | 403 kept (PLAN-34) | filtered |
| ContractRenewals, ServiceExtensions | NotApplicable(a redirect, no records) | — | — | — |
| ContractSignatures | OrgScoped(ContractSignature) | → Contract → snapshot | 404 | — |
| ContractTypeRegimeMaps, ContractTypes, LeavePolicies | NotApplicable(reference data) | — | — | — |
| Contracts | OrgScoped(Contract) | → snapshot | 403 kept (PLAN-34) | filtered; `Details` (plan item) |
| Dashboard | CentralOnly (agency counts) | — | 403 | — |
| Export | OrgScoped | LeaveRequest, AdvanceRequest → snapshot | — | all four exports, direct and as a job (the job records the scope) |
| ExportJobs | NotApplicable(self-service: the requester's job) | — | — | — |
| HireWizard | OrgScoped(ServiceTransaction) | → snapshot | 404 by `transactionId` (AC-IAM-52b); `Index`, `Continue`, `Step1` 403 kept (QA-034) | filtered |
| Home | NotApplicable(a page with no org records) | — | — | — |
| LeaveBalances | OrgScoped(LeaveBalance) | → snapshot | 403 kept | filtered; `Create(vm.EmployeeId)` (plan item), `InitializeYear(employeeIds)` |
| LeaveRequests | OrgScoped(LeaveRequest) | → snapshot | 403 kept | filtered |
| ProbationRecords | OrgScoped(ProbationRecord) | → snapshot | 403 kept (PLAN-34) | filtered |
| ProcessInterventions | CentralOnly (saga repair) | — | 403 | — |
| SeparationRecords, SeparationWizard | OrgScoped(SeparationRecord) | → snapshot | 403 kept (PLAN-34) | filtered |
| ServiceTransactions | OrgScoped(ServiceTransaction) | → snapshot | 403 kept (PLAN-34) | filtered |

### Admin.Logistics (8 controllers, all actual)

| Controller | Stance | Entity → unit | Answer | Lists |
|---|---|---|---|---|
| Assets | OrgScoped(Asset) | the active custody's employee → snapshot (`AssetOrgScopeGuard`); none = central | 403 kept (AC-32) | filtered today |
| AssetAssignments | OrgScoped(AssetAssignment) | → employee snapshot | 403 kept (AC-32) | filtered today |
| SupplyOrders | OrgScoped(SupplyOrder) | SupplyOrder.OrgUnitId; a posted unit is a key | 404 (AC-IAM-52c) | Index (unfiltered today) |
| ReceiptRecords | OrgScoped(ReceiptRecord) | → SupplyOrder.OrgUnitId | 404 | Index (unfiltered today) |
| MaintenanceRecords | OrgScoped(MaintenanceRecord) | → Asset (as above) | 404 | Index; the asset pick-list |
| InventoryItems, Vendors | NotApplicable(reference data); writes CentralOnly (D-52-3) | — | 403 on writes | — |
| Home | NotApplicable(a page with no org records) | — | — | — |

### HR.Movements (the delegation path, P-9) and the other actual controllers

| Service | Controller | Stance | Note |
|---|---|---|---|
| hr-movements | TransferRequests, LoanAssignmentRequests | OrgScoped, both units in the scope (403 kept) | PLAN-50's principal-scope check stays in the actions |
| hr-movements | SecondmentRequests, PromotionRequests, DelegationRequests | CentralOnly | only `EmployeeId`, no employee snapshot in HR.Movements: central for a restricted user (G-MOV-10, wave 2) |
| payroll | Ess | NotApplicable(self-service by ownership) | |
| file-storage | FilesApi | NotApplicable(files are reached through their owning record; a restricted user reaches only the health probe) | |
| gateway | Account, Home | NotApplicable(a page with no org records) | |
| shared | Culture | NotApplicable(a page with no org records) | |

The other 8 controllers of HR.Movements and every controller of the eleven latent services are rows of their service's list
(`IAM-H: latent, no restricted role reaches it (D-IAM-3)`).

## Inventory B — the entity types that start a workflow

| Entity type | Start | Key (the resolver) |
|---|---|---|
| RecruitmentRequisition | internal (`ApprovalStartCoordinator`) | `FundedPosition.OrgUnitId` |
| OfferLetter | internal | `FundedPosition.OrgUnitId` |
| SelectionCommitteeSession | internal | its requisition's `FundedPosition.OrgUnitId` |
| RecruitmentAppeal | internal (`RecruitmentAppealService`) | its application's requisition's `FundedPosition.OrgUnitId` |
| EstablishmentChangeRequest | internal | none (polymorphic `TargetEntityId`): central |
| RecruitmentCompetition | internal (`CompetitionLifecycleService`) | none (a competition spans requisitions): central |
| CompetitionResultVersion | internal (`CompetitionResultsService`) | none: central |
| AssetAssignment, AssetLossReport | external (Admin.Logistics, `ExternalApprovalService`) | the request's `ScopeOrgUnitId` (PLAN-43) |
| CardRequest | external (IDCard) | the request's `ScopeOrgUnitId` |
| DisciplinaryAction | external (Compliance) | the request's `ScopeOrgUnitId` |
| SeparationRecord | external (HR.Contracts) | the request's `ScopeOrgUnitId` |

Policy-triggered starts (`PolicyOrchestrator`) go through the same starter with the domain's subject types: the four recruitment types
and the establishment change above.

## Inventory C — agency-wide catalogues a restricted user writes today

`Shifts` (HR.Core), `InventoryItems` and `Vendors` (Admin.Logistics): D-52-3, writes become central (403). The others get
`[OrgScopeNotApplicable("reference data")]` and keep their write policies (which roles write them is RBAC, IAM-H):

| Controller | Who writes today | Note |
|---|---|---|
| CareerPaths | hr-core (so `bm-core`) | create/delete career paths and steps |
| Policies, PolicyTemplates | `Policy.Author` admits hr-core | the policy lifecycle (PLAN-47) |
| ReferenceData | hr-core | writes the table `ReferenceCatalog` keeps central (an RBAC gap for IAM-H) |

Not catalogues but agency-level writes a restricted user reaches today, closed here: `DataImport` (org units as roots, employees
anywhere) → CentralOnly.

## Defects the inventory found (closed in P3–P4 unless stated)

- Writes before any scope check: Create/Edit/Delete of `ApprovedCVs`, `Contacts`, `Documents`, `EmploymentRecords`, `Evaluations`,
  `Qualifications`; `DailyAttendanceRecords.Create` and `Edit` (moves a record to any employee); `EmployeeCorrections.RequestCorrection`;
  `Approvals` Approve/Reject/Cancel; `RecruitmentAppeals` writes; `Shifts` writes.
- A failed Create with `ReturnTo=details` (Contacts, Documents, Evaluations, Qualifications) renders any employee's details page.
- Unfiltered lists: `Evaluations` without an employee, `Approvals`, `DiwanCorrespondences`, `HrStatisticReports`,
  `FieldInspectionVisits`, `StatusHistory.FullLog`, `ServiceRegimeExceptions`, the dashboard's counts, `SupplyOrders`, `ReceiptRecords`,
  `MaintenanceRecords`; pick-lists of every branch in KeyPositions, Rosters, SuccessionPlans, TalentMarketplace, HrStatisticReports,
  EmploymentRecords, StatusHistory, Diwan, the recruitment `Create` and `Hire` forms.
- `LookupApi.GetEmployeesByIds` serves cached entries without a scope check.
- `OrgUnits`/`OrgStructureApi`: a null parent moves a unit to the root; `HeadEmployeeId` unchecked.
- `RecruitmentRequisitions.VacantPositions(includeId)` returns any funded position; `WorkforcePlans.GapReport(planId)` unchecked.
- The recruitment filter lets an id of the wrong kind through (an id it cannot find passes): `PublicJobPostings.ChangeApplicationStatus`,
  `DeclareCitizenCoi` (a citizen application), `TalentMarketplace.ChangeApplicationStatus` (an internal application).
- Interview slots are loaded by id alone (`RecruitmentInterviewService`): an in-scope application with a foreign `slotId`.
- `ExportJobs.Status` (HR.Core) has no owner check.
- `SuccessionPlans.AddCandidate` copies a foreign employee's latest evaluation score.

## The demo server (read-only, 20:55Z..21:10Z)

Files: `artifacts/plan52/vps/`. Tag `d0e446fe3280` (the product code of `main`). One tenant `00000000-0000-4000-8000-000000000001`:
30 units (ROOT, 9 departments, 20 sections; the demo has departments, not geographic branches), 500 employees.

| Unit (subtree) | Employees | Attendance | Rosters | Key positions | Supply orders |
|---|---:|---:|---:|---:|---:|
| HR — Human Resources (`…111102`) | 85 | 3024 | 1 | 1 | 10 |
| FIN — Finance (`…111103`) | 68 | 2478 | 0 | 1 | 8 |
| ROOT itself (central for a branch user) | 10 | 420 | 0 | 1 | 0 |
| the 7 other departments | 337 | 11928 | 0 | 7 | 32 |

**Records with no key, before HCM052:** Diwan 0 rows (0 without an employee), inspections 0, statistics reports 1 (on ROOT), rosters
1 (all with a unit), key positions 10 (0 without a unit), succession plans 10 (0), attendance 17850 (0 without a unit), employees 500
(0), internal vacancies 5 (0). **Workflow instances: 70, every one without `ScopeOrgUnitId`** (RecruitmentRequisition 22,
CardRequest 14, AssetAssignment 16, AssetLossReport 8, DisciplinaryAction 7, EstablishmentChangeRequest 3).
**Satellite employee snapshots: 0** in HR.Contracts, Admin.Logistics, HR.Health and HR.Training: every contracts record (8 appointment
transactions among 60), every asset (224) and every maintenance record (40) is central for a restricted user on the demo; supply orders
(50) all carry a unit.

**Chosen for `e2e-branch-manager`** (P-16): **HR (`11111111-1111-1111-1111-111111111102`)** — the subtree with the most records
(85 employees, 3024 attendance rows, the only roster, a key position, 10 supply orders); **FIN** holds records of every kind the
negatives need (a key position, attendance, supply orders).

`Keycloak:DirectorySync:Enabled` = false in every container (Production reads `appsettings.json`; no environment override; the
Development file sets true). `ITransferExecuted` is bound to 7 queues (admin-logistics, auth-directory, hr-contracts, hr-core,
hr-movements, hr-training, payroll) — none for HR.Health. Live realm: `hr-gateway-client` maps `scope_org_unit_id` into the token, as
the manifest says; the user profile does not declare it (as `tenant_id`, which `add-demo-user.sh` already writes through its
temporary `unmanagedAttributePolicy`); no user holds `branch-manager`.

## The acceptance classes that use a restricted persona

Generated read-only from the 31 classes that name a restricted persona (`artifacts/plan52/inventory/acceptance-restricted-requests.md`).
Only `br1-mgr`, `br2-mgr` and `b-branch-manager` make application requests; the other six restricted personas only take a Keycloak token
(`AcceptanceIdentityPopulationTests`). No test reads another branch's record successfully: every such request already expects a denial.

| Class (required?) | What the restricted persona does | What this plan must keep |
|---|---|---|
| AcHcm21, AcHcm22, AcHcm25, AcHcm26..30, AcIam50d, AcPol49f, AcWf44e (required), both recruitment journeys | `br1-mgr` opens and approves or rejects RIF requisitions, offers, committee sessions (GET token page, POST `ApproveWorkflow`/`RejectWorkflow`) | the recruitment filter's unit through `FundedPosition.OrgUnitId`, with descendants (`EmpBr1`/`EmpD1` sit in RIF leaf units); the in-app refusals of AcHcm25c/d stay non-403 |
| AcHcm29 (required) | `br1-mgr` approves requisition and offer step 1 only; the hire wizard steps are `a-contracts-maker`/`-checker`'s | — |
| AcWf23 (required) | inbox (≥ 3 items, equal to the stats count) and `Approvals/Review/{…0926}`: a legacy request whose `ApprovedCV` has **no row and no unit** | **P-12 (a test that relied on an unscoped screen):** the legacy queue is scoped here, so the seed's legacy request points at a real `ApprovedCV` of `EmpBr1` (RIF); the assertions stay as they are |
| AcWf43e (required) | `br1-mgr` approves an external instance with **no unit** of which he is the requester: **exactly 400**, the requester rule | step decisions on `api/workflow-instances` keep the workflow gates' answer (`WorkflowGateOrgScope` already refuses an instance outside the actor's branch or with no unit, in-app); only `List`/`Get`/`History` take the 404 filter (P-5) |
| AcEst24f, AcWf23e (required) | `GET /ui/hr-core/api/stats/pending-approvals` exactly 200 | `StatsApi` is not central-only; it counts what the inbox lists |
| AcPol49e (required) | submit of a central establishment change (no unit): **exactly 403** | `SubmitChange` central-only, refused before any lookup |
| AcEst18c (required) | `ReferenceCatalog/Create?kind=Grade`: **exactly 403** (no redirect) | `ReferenceCatalog` central-only |
| AcEst11 (required) | `gap-analysis?orgUnitId=ALEP`: **exactly 403** | the establishment APIs keep 403 |
| AcHcm32b (required) | `RecruitmentReports?OrgUnitId=ALEP` 200 and empty; `Requisition/{ALEP}` 404 | report filters are not keys; the requisition page 404 |
| AcS2s42c (required) | a service call on behalf of `b-branch-manager` (200); his own call to the internal route **exactly 403** | `PolicyEnforceInternal` not applicable (service-to-service); the policy refuses the user first |
| Ac02, Ac03, Ac06, Ac32 (lab, not required, treated as red here, P-13) | 403 or 404 on the other branch's employee, 403 on the lookup (AC-06) and on `Assets/Details` (AC-32) | the kept 403s; the denial carries no record id |

AcEst11, AcEst18, AcEst24 and `AcceptanceTenantConsistencyTests` are `required: true` in `ci-scope.json` but carry no
`[RequiredAcceptance]` attribute (recorded, not changed). The UI POST helpers let a 404 through (only the database assertion that follows
catches it), so a scope 404 on a `br1-mgr` POST would surface as a state mismatch: P6 reads those failures with that in mind.

**The decisions this section changes in the tables above:** `Approvals` → OrgScoped(ApprovalRequest) 404, and the inbox's legacy items
and the stats count use the same key; `WorkflowInstances` → `List`/`Get`/`History` OrgScoped 404, the step decisions keep the gates
(their reason is written on the actions); `EstablishmentChanges`, `Signatures`, `SignaturesApi` → CentralOnly (no restricted persona uses
them; their records carry no unit); `StatsApi` → NotApplicable (it counts the inbox).
