# PLAN-51 baseline (P0)

Taken on `feature/plan-51-iam-explicit-policy-every-action` at `c1f60b3e` (= `origin/main` `0db7292e` + PLAN-50's merge note + the
prompt). Raw data under `artifacts/plan51/` (not committed): `inv-0.4-actions.tsv` (every action of the fifteen web assemblies),
`inv-0.4-offenders.tsv`, `inv-entity-bindings.tsv`, `inv-0.5-s2s.md`, `inv-0.6-ess-pages.md`.

## 0.2 and 0.3 — build and the listed filter

| Step | Result |
|------|--------|
| `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors (1 min 02 s, incremental) |
| `dotnet test src/Tests/HRManagement.UnitTests --filter "…Architecture\|…GatewayPath\|…Hcm028Authorization\|…Hcm031Marker\|…Iam050"` | 486 passed, 0 failed (26 s) |

## 0.4 — the writes without an explicit policy

Scanned by reflection over the fifteen web assemblies (the scanner that becomes `HRManagement.ArchitectureTests`, P1): every action
whose HTTP method attribute includes POST, PUT, PATCH or DELETE. "Explicit" = an `[Authorize(Policy=…)]` on the action or its class
(base classes included), or `[AllowAnonymous]`. `[Authorize(Roles=…)]` alone is counted separately: the study counted it as explicit,
this plan does not (a write names its policy).

| Service | Writes | Policy on the action | Policy on the class | Anonymous | Roles only | **No policy** | Study §2.2 |
|---------|-------:|------:|------:|------:|------:|------:|------:|
| HR.Core | 275 | 175 | 45 | 13 | 0 | **42** | 42 |
| HR.Contracts | 64 | 19 | 4 | 0 | 0 | **41** | 41 |
| Payroll | 61 | 45 | 0 | 0 | 0 | **16** | 16 |
| HR.Movements | 36 | 24 | 4 | 0 | 0 | **8** | 26 (PLAN-50 gave 18 their policy) |
| HR.Training | 32 | 0 | 2 | 0 | 0 | **30** | 30 |
| HR.Health | 30 | 6 | 0 | 0 | 0 | **24** | 24 |
| Compliance | 36 | 0 | 0 | 0 | 4 | **32** | 30 (+2 actions since) |
| IDCard | 27 | 0 | 0 | 0 | 0 | **27** | 25 (+2 since) |
| Admin.Logistics | 30 | 0 | 0 | 0 | 0 | **30** | 28 (+2 since) |
| IT.Infra | 25 | 0 | 0 | 0 | 0 | **25** | 25 |
| Records.Archive | 24 | 0 | 0 | 0 | 0 | **24** | 24 |
| Dev.Analytics | 19 | 1 | 1 | 0 | 0 | **17** | 17 |
| Auth.Directory | 8 | 0 | 1 | 0 | 0 | **7** | 7 |
| File.Storage | 14 | 9 | 5 | 0 | 0 | **0** | 0 |
| Gateway | 7 | 2 | 0 | 0 | 5 | **0** | 1 (`Culture.SetCulture`, below) |
| **Total** | **688** | 281 | 62 | 13 | 9 | **323** (+9 roles only) | 336 |

The nine "roles only" writes: Compliance `DsarRequests.Create/ExecuteDeletion/Fulfill/UpdateStatus` (`Roles = compliance,platform-admin`);
gateway `AuditSecurity.CreateHold/Ingest/TriggerWormExport`, `PlatformTenant.EnterTenant/ExitTenant` (`Roles = platform-admin`).

**Shared controller.** `CultureController.SetCulture` (`POST api/culture`) lives in ServiceDefaults and is discovered by every service and
the gateway; it has no policy (the study listed it under the gateway). It is scanned as a sixteenth catalog entry, `shared`.

**HR.Core:** the 42 are exactly the 42 rows of `tests/architecture/hrcore-post-policy-allowlist.txt`.

**One PATCH:** HR.Core `EmployeesApi.Patch` (explicit `HrCore.Write`). The gateway's `IsMutating` covers POST, PUT and DELETE only, so a
PATCH falls through to `Module.HrCore` at the gateway (the service still applies `HrCore.Write`).

### Who reaches a write without a policy today

The service applies the module filter only; the gateway applies its path rule. "Direct" = a token that reaches the service without the
gateway (the gateway proof of PLAN-42 refuses a user's direct call, so this is the defence-in-depth layer the plan adds).

| Service | Service (module policy) | Gateway rule for POST/PUT/DELETE | Reaches through the gateway | Reaches the service alone |
|---------|-------------------------|----------------------------------|-----------------------------|---------------------------|
| HR.Core | `Module.HrCore`: hr-admin, hr-core, hr-core-supervisory, hr-core-executive, branch-manager, hr-management-approver; services with `hr-core.lookup` or `hr-core.workflow` | `HrCore.Write`: hr-admin, hr-core, services with `hr-core.workflow` | hr-admin, hr-core | **+ the two read roles, branch-manager, the approver, any service with lookup or workflow** |
| HR.Contracts | `Module.HrContracts`: hr-admin, hr-contracts, hr-management-approver | `HrContracts.Approve`: hr-admin, hr-contracts (HireWizard has its own rules) | hr-admin, hr-contracts | **+ hr-management-approver** |
| Payroll | `Module.Payroll`: hr-admin, payroll | `Module.Payroll` | hr-admin, payroll | same |
| HR.Movements | `Module.HrMovements`: hr-admin, hr-movements | `Module.HrMovements` | same | same |
| HR.Training, IDCard, Records.Archive, Admin.Logistics, IT.Infra, Compliance, Auth.Directory | Admin or the module role | the module policy | same | same |
| HR.Health | hr-admin, hr-health, hr-health-clinical | `Module.HrHealth` | same | same |
| Dev.Analytics | hr-admin, dev-analytics, platform-admin | `Module.DevAnalytics` | same | same |

So the roles that write today without being the module's writer are: in HR.Core the read roles, branch-manager and the approver (and
service accounts), on all 42; in HR.Contracts the approver, on all 41. In the single-role modules the writer is the module role.

### The writes without a policy, per service (the seed of the ceiling lists)

See `artifacts/plan51/inv-0.4-lists.md` for the same lists; P1 commits them as `tests/architecture/write-policy-allowlist/{service}.txt`.

- **hr-core** (42): `ApprovedCVs.Create`, `ApprovedCVs.Delete`, `ApprovedCVs.Edit`, `ApprovedCVs.FinalizeApproval`, `CareerPaths.AddStep`, `CareerPaths.Create`, `CareerPaths.Delete`, `CareerPaths.RemoveStep`, `Contacts.Create`, `Contacts.Delete`, `Contacts.Edit`, `DailyAttendanceRecords.BulkEntry`, `DailyAttendanceRecords.ClosePeriod`, `DailyAttendanceRecords.Create`, `DailyAttendanceRecords.Delete`, `DailyAttendanceRecords.Edit`, `DiwanCorrespondences.Create`, `Documents.Create`, `Documents.Delete`, `Documents.Edit`, `EmployeeProfiles.Save`, `ExportJobs.Cancel`, `FieldInspectionVisits.Create`, `HrStatisticReports.Create`, `KeyPositions.Create`, `KeyPositions.Delete`, `KeyPositions.Edit`, `Qualifications.Create`, `Qualifications.Delete`, `Qualifications.Edit`, `Rosters.AddAssignment`, `Rosters.Create`, `Rosters.Delete`, `Rosters.RemoveAssignment`, `Shifts.Create`, `Shifts.Delete`, `Shifts.Edit`, `Signatures.Verify`, `SuccessionPlans.AddCandidate`, `SuccessionPlans.Create`, `SuccessionPlans.Delete`, `SuccessionPlans.RemoveCandidate`
- **hr-contracts** (41): `AdministrativeDecisions.Create`, `AdministrativeDecisions.Delete`, `AdministrativeDecisions.Edit`, `AdvanceRequests.Create`, `AdvanceRequests.Delete`, `AdvanceRequests.Edit`, `AdvanceRequests.Reject`, `AdvanceRequests.RejectAjax`, `CommencementRecords.Create`, `CommencementRecords.Delete`, `CommencementRecords.Edit`, `ContractSignatures.Create`, `ContractSignatures.Delete`, `ContractTypes.Create`, `ContractTypes.Delete`, `ContractTypes.Edit`, `Contracts.Activate`, `Contracts.Create`, `Contracts.Delete`, `Contracts.Edit`, `ExportJobs.Cancel`, `LeaveBalances.Create`, `LeaveBalances.InitializeYear`, `LeavePolicies.Create`, `LeavePolicies.Delete`, `LeavePolicies.Edit`, `LeaveRequests.Create`, `LeaveRequests.Delete`, `LeaveRequests.Edit`, `LeaveRequests.Reject`, `LeaveRequests.RejectAjax`, `ProbationRecords.Create`, `ProbationRecords.Delete`, `ProbationRecords.Edit`, `SeparationRecords.Create`, `SeparationRecords.Delete`, `SeparationRecords.Edit`, `SeparationWizard.Step1`, `SeparationWizard.Step2CompleteStep`, `SeparationWizard.Step2Confirm`, `SeparationWizard.Step3Execute`
- **payroll** (16): `CompensationTypes.Create`, `CompensationTypes.Delete`, `CompensationTypes.Edit`, `DeductionTypes.Create`, `DeductionTypes.Delete`, `DeductionTypes.Edit`, `EmployeePayProfiles.Create`, `ExportJobs.Cancel`, `PayGrades.Create`, `PayGrades.Delete`, `PayGrades.Edit`, `PayrollReports.BulkPayslips`, `PayrollRuns.Create`, `SalaryScales.Create`, `SalaryScales.Delete`, `SalaryScales.Edit`
- **hr-movements** (8): `DistributionPlans.Approve`, `DistributionPlans.Create`, `DistributionPlans.Edit`, `DistributionPlans.GapAnalysis`, `MovementNotices.Create`, `MovementNotices.Edit`, `MovementNotices.Generate`, `TransferFinancialImpacts.Create`
- **hr-training** (30): `Certificates.Verify`, `ELearningCourses.AddLesson`, `ELearningCourses.AddModule`, `ELearningCourses.Create`, `ELearningCourses.Delete`, `ELearningCourses.Edit`, `ELearningCourses.TogglePublish`, `MissionFiles.Approve`, `MissionFiles.Complete`, `MissionFiles.Create`, `MissionFiles.Edit`, `MissionFiles.MarkOngoing`, `QualificationCodes.Create`, `QualificationCodes.Delete`, `QualificationCodes.Edit`, `TrainingCourses.Create`, `TrainingCourses.Delete`, `TrainingCourses.Edit`, `TrainingEnrollments.Cancel`, `TrainingEnrollments.Complete`, `TrainingEnrollments.Create`, `TrainingNominations.Approve`, `TrainingNominations.Create`, `TrainingNominations.Reject`, `TrainingPlans.Approve`, `TrainingPlans.Create`, `TrainingPlans.Edit`, `TrainingProviders.Create`, `TrainingProviders.Delete`, `TrainingProviders.Edit`
- **hr-health** (24): `HealthRetirementRecords.Create`, `HealthRetirementRecords.Edit`, `HealthStatusEntries.Create`, `HealthStatusEntries.Edit`, `InsuranceCards.Activate`, `InsuranceCards.Cancel`, `InsuranceCards.Create`, `InsuranceCards.Edit`, `InsuranceCards.MarkExpired`, `InsuranceCards.Revoke`, `InsuranceCards.Suspend`, `InsuranceContracts.Create`, `InsuranceContracts.Delete`, `InsuranceContracts.Edit`, `MedicalCommitteeReports.Create`, `MedicalCommitteeReports.Edit`, `PeriodicMedicalChecks.Create`, `PeriodicMedicalChecks.Delete`, `PeriodicMedicalChecks.Edit`, `WorkplaceSafety.Create`, `WorkplaceSafety.CreateFirstAid`, `WorkplaceSafety.CreateInspection`, `WorkplaceSafety.Delete`, `WorkplaceSafety.Edit`
- **compliance** (32): `AuditFindings.CloseItem`, `AuditFindings.Create`, `AuditFindings.CreateReferral`, `AuditFindings.Edit`, `AuditFindings.Mitigate`, `Complaints.AddFollowUp`, `Complaints.AddRecommendation`, `Complaints.Appeal`, `Complaints.Assign`, `Complaints.AttachEvidence`, `Complaints.Cancel`, `Complaints.Close`, `Complaints.Create`, `Complaints.Refer`, `Complaints.Resolve`, `Complaints.StartInvestigation`, `ComplianceRecommendations.Create`, `ComplianceRecommendations.Edit`, `DisciplinaryActions.Appeal`, `DisciplinaryActions.Create`, `DisciplinaryActions.DecideAppeal`, `DisciplinaryActions.Edit`, `DisciplinaryActions.Submit`, `DisciplinaryActions.Withdraw`, `ExportJobs.Cancel`, `LegalCases.Assign`, `LegalCases.Close`, `LegalCases.Create`, `LegalCases.Edit`, `LegalCases.UpdateStatus`, `LegalReferrals.Create`, `LegalReferrals.Edit`
- **idcard** (27): `AccessPermissions.Create`, `AccessPermissions.Delete`, `AccessPermissions.Edit`, `CardDesigns.Create`, `CardDesigns.Delete`, `CardDesigns.Edit`, `CardPermissionSets.Create`, `CardPermissionSets.Delete`, `CardPermissionSets.Edit`, `CardRequests.Cancel`, `CardRequests.Create`, `CardRequests.Edit`, `CardRequests.ProcessRequest`, `CardRequests.Submit`, `CardRequests.Withdraw`, `IssuedCards.Create`, `IssuedCards.Edit`, `IssuedCards.MarkDelivered`, `IssuedCards.MarkExpired`, `IssuedCards.MarkLost`, `IssuedCards.MarkPrinted`, `IssuedCards.Renew`, `IssuedCards.Revoke`, `IssuedCards.UrgentRevoke`, `OfficialSealRecords.Create`, `OfficialSealRecords.Delete`, `OfficialSealRecords.Edit`
- **admin-logistics** (30): `AssetAssignments.CreatePost`, `AssetAssignments.EditPost`, `Assets.AssignToEmployee`, `Assets.CancelAsset`, `Assets.CreateFromReceipt`, `Assets.CreateLegacy`, `Assets.Edit`, `Assets.MarkLost`, `Assets.RecoverAsset`, `Assets.RetryApprovalStart`, `Assets.ReturnAssignment`, `Assets.WithdrawApproval`, `InventoryItems.Create`, `InventoryItems.Delete`, `InventoryItems.Edit`, `MaintenanceRecords.Create`, `MaintenanceRecords.Edit`, `ReceiptRecords.CreateFromOrder`, `ReceiptRecords.CreatePost`, `ReceiptRecords.Edit`, `SupplyOrders.AddLine`, `SupplyOrders.Approve`, `SupplyOrders.Cancel`, `SupplyOrders.Close`, `SupplyOrders.Create`, `SupplyOrders.Edit`, `SupplyOrders.Fulfill`, `Vendors.Create`, `Vendors.Delete`, `Vendors.Edit`
- **it-infra** (25): `BackupJobs.Create`, `BackupJobs.Delete`, `BackupJobs.Edit`, `BackupJobs.Run`, `EnvironmentConfigurations.Create`, `EnvironmentConfigurations.Edit`, `SecurityIncidents.Create`, `SecurityIncidents.Delete`, `SecurityIncidents.Edit`, `SecurityIncidents.Handle`, `ServerDevices.Create`, `ServerDevices.Delete`, `ServerDevices.Edit`, `SupportTickets.Assign`, `SupportTickets.Close`, `SupportTickets.Create`, `SupportTickets.Edit`, `SupportTickets.Reopen`, `SupportTickets.Resolve`, `SupportTickets.RetryProvision`, `SystemPermissionRecords.Create`, `SystemPermissionRecords.Edit`, `SystemPermissionRecords.Revoke`, `UserDevices.Create`, `UserDevices.Edit`
- **records-archive** (24): `ArchiveIndexEntries.Create`, `ArchiveIndexEntries.Edit`, `ArchiveItems.ApproveDisposal`, `ArchiveItems.AttestCompleteness`, `ArchiveItems.CancelDisposalRequest`, `ArchiveItems.Create`, `ArchiveItems.Edit`, `ArchiveItems.PlaceLegalHold`, `ArchiveItems.RecordDisposal`, `ArchiveItems.ReleaseLegalHold`, `ArchiveItems.RequestDisposal`, `ArchiveItems.ReturnLoan`, `ArchiveItems.StartLoan`, `ArchiveItems.UpdateRetention`, `DigitalFileRecords.Create`, `DigitalFileRecords.Delete`, `DigitalFileRecords.Edit`, `DocumentClassifications.Create`, `DocumentClassifications.Edit`, `LoanRecords.Create`, `LoanRecords.Edit`, `LoanRecords.OverdueLoanCheck`, `PhysicalFileRecords.Create`, `PhysicalFileRecords.Edit`
- **dev-analytics** (17): `Dashboards.Create`, `Dashboards.Edit`, `DataRefreshSchedules.Create`, `DataRefreshSchedules.Edit`, `DataRefreshSchedules.Trigger`, `EtlJobs.Create`, `EtlJobs.Edit`, `EtlJobs.Run`, `ExportJobs.Cancel`, `ExportJobs.EnqueueWorkforce`, `Kpis.Create`, `Kpis.Edit`, `ReadModels.Create`, `ReadModels.Edit`, `ReadModels.Rebuild`, `Reports.Create`, `Reports.Edit`
- **auth-directory** (7): `AuthUsers.Edit`, `AuthUsers.SyncFromKeycloak`, `DirectoryEntries.Create`, `PermissionPolicies.Create`, `Roles.Create`, `Roles.Delete`, `Roles.Edit`

### Entity bindings in the fifteen assemblies

`MassAssignmentArchitectureTests` scanned four web assemblies (the ones the unit project references); the other ten were skipped
silently. Over all fifteen, 125 actions bind an entity (key = controller.action):

| Service | Actions | Owner |
|---------|--------:|-------|
| HR.Movements | 12 | this plan (P-8: view models) |
| HR.Health | 20 | wave 2, IAM-F (G-IAM-10) |
| Payroll | 16 | wave 2, IAM-F |
| HR.Training | 14 | wave 2, IAM-F |
| IT.Infra | 14 | wave 2, IAM-F |
| Dev.Analytics | 12 | wave 2, IAM-F |
| Records.Archive | 12 | wave 2, IAM-F |
| Compliance | 11 | wave 2, IAM-F |
| IDCard | 10 | wave 2, IAM-F |
| Auth.Directory | 4 | wave 2, IAM-F |
| HR.Core, HR.Contracts, Admin.Logistics, File.Storage, Gateway | 0 | — |

## 0.5 — service calls

From `artifacts/plan51/inv-0.5-s2s.md` (23 service-token calls in the code, 3 gateway calls with the user's token, and the 45
`calls[]` entries of `deploy/wiring/service-wiring.json`, read from the code; nothing run).

| Endpoint in HR.Core | Policy | Callers (client id; scope) | Through a non-`Service.*` policy of HR.Core today |
|---------------------|--------|-----------------------------|---------------------------------------------------|
| `POST api/internal/codes/generate` (and `preview`, no caller) | `Service.CodeGenerate` | svc-hr-contracts, svc-idcard, svc-records-archive, svc-admin-logistics, svc-it-infra, svc-compliance; `hr-core.workflow` | no |
| `POST api/internal/policies/enforce` | `Service.PolicyEnforce` | svc-hr-contracts, svc-idcard, svc-admin-logistics; `hr-core.workflow` | no |
| `POST api/workflow-instances`, `POST …/{id}/cancel`, `GET …/{id}/external-status` (`WorkflowExternalInstancesController`) | `Workflow.ExternalStart` | svc-idcard, svc-compliance, svc-admin-logistics; `hr-core.workflow` | **yes: the module filter adds `Module.HrCore`**, which they pass through its `hr-core.workflow` branch (svc-idcard holds no `hr-core.lookup`) |
| `POST api/lookup/employees/by-ids` (and `org-units/by-ids`, no caller) | `HrCore.Lookup` | svc-hr-contracts, svc-admin-logistics, svc-payroll, svc-compliance, svc-auth-directory; `hr-core.lookup` | `HrCore.Lookup` (the filter skips `LookupApi` by its name) |
| `api/internal/service-regime/*`, `api/internal/employees/{id}/lifecycle-flags`, `api/internal/approval-delegations/active`, `api/internal/dsar-subjects/*` | `Service.HrCoreInternal`, `Service.HrCoreLifecycle`, `Service.ApprovalDelegationRead`, `Service.DsarSubjectRead`/`Erase` | hr-contracts, hr-movements, payroll, compliance; `hr-core.lookup`, `dsar.*` | no |

Other services' endpoints (Payroll pay grades and DSAR, File.Storage service upload, DSAR and references, Auth.Directory sync) carry
`Service.*` policies (the file references add `Module.FileStorage`, which admits the `file-storage.references` scope).

**Answers.**
- **`hr-core.workflow` is needed on the three endpoints only** (five routes: generate and its unused preview, enforce, and the
  workflow-instances start, cancel and external status).
- **No service account reaches a write through `HrCore.Write` or `HrCore.BranchOperation` today.** Every service calls HR.Core
  directly (`Integration:HrCoreBaseUrl` = `http://hr-core-web:8080`), never through the gateway's `HrCore.Write` rule, and no
  `calls[]` entry names `HrCore.Write`, `HrCore.BranchOperation`, `Module.HrCore` or `HrCore.ReadOnly`. Removing the service branch
  breaks no call; the unit test `FineGrainedPolicyTests.HrCoreWrite_ServiceWorkflowScope_Succeeds` asserts the old branch and is
  inverted (P-9).
- **The workflow-instances routes depend on the module filter's `hr-core.workflow` branch of `Module.HrCore`.** Decision: the
  controller gets `[ModulePolicyOverride(Workflow.ExternalStart)]`, so it no longer leans on `Module.HrCore` at all; §5 rule 1 keeps the
  read branches of `Module.HrCore` and `HrCore.ReadOnly` unchanged.
- **`LookupApi` keeps working for its five service callers** once the name list gives way to `[ModulePolicyOverride(HrCore.Lookup)]`;
  all five hold `hr-core.lookup` (D-51-3).
- **No caller of `api/codes/generate`** (non-internal): `CodeGenerationController` has no generate action any more; only a negative
  acceptance check (`AcS2s42TrustBoundaryTests`) names it.
- **No caller of the gateway's evidence ingest** (`api/platform/evidence/artifacts`), and no `svc-*` client can obtain
  `evidence.ingest`: an empty `Evidence:IngestClients` breaks nothing (D-51-5).
- **Matrix and code** disagree in four small places that do not touch this plan (an unused workflow client registered in HR.Contracts,
  two candidate DSAR reads and a file-reference DELETE not in the matrix, the candidate anonymize's class-level `DsarSubjectRead`):
  recorded as defects for later, not changed here.

## 0.6 — the pages of employees and markers

From `artifacts/plan51/inv-0.6-ess-pages.md` (read from the code):

- **No page that `hr-employee` or `hr-exam-marker` reaches calls `/api/lookup/*`, and no browser code anywhere calls `/api/internal`.**
- All these pages use `_HrSharedLayout.cshtml` (`HRManagement.Web.Shared`), which loads 16 assets (css, js, tom-select,
  `entity-pickers.js`, `display-names.js`) from the service's own path base; `data-lookup-base="/ui/hr-core"` is hard-coded.
- `display-names.js` fetches only when the page has `data-hr-employee-id` / `data-hr-org-unit-id` elements; `entity-pickers.js` only
  for `data-hr-picker` elements or the org-unit tree button. None of EssPortal (6 views), EssTalent (2), ExamMarking (3), Payroll
  `Ess/Payslips`, HR.Training `EssLearning` (3) has one, nor the `_EmployeeDisplayName` partial: names are rendered on the server.
- **What they need is HR.Core's static files:** the 11 HR.Core pages load their assets from `/ui/hr-core/{lib,css,js}/`, which pass
  `HrCore.Lookup` today. Moving them to `Gateway.Authenticated` (P-4) is required, or those pages lose their styles when the lookup
  narrows to staff.
- Payroll and HR.Training self-service pages load their assets from `/ui/payroll/…` and `/ui/hr-training/…`, which pass the module policy
  only: a user with `hr-employee` alone most likely gets 403 on them today. Pre-existing, and the self-service of those two modules is
  wave 2 (IAM-H); recorded as a defect for later, not changed here.
- Decision: no ownership endpoint is needed in the self-service controllers; nothing is removed from those pages (the scripts make no
  call there).

## 0.7 — the module filter's name list

Every controller, in every service, that `HrModuleAuthorizationFilter`'s name list matches today, with the policy it will carry
(the class-level policy it already has, so nothing widens):

| Service | Controller | Class policy today | After PLAN-51 |
|---------|------------|--------------------|---------------|
| HR.Core | `EssPortal` | `Module.Ess` | `[ModulePolicyOverride(Module.Ess)]` |
| HR.Core | `EssTalent` | `Module.Ess` | `[ModulePolicyOverride(Module.Ess)]` |
| Payroll | `Ess` | `Module.Ess` | `[ModulePolicyOverride(Module.Ess)]` |
| HR.Training | `EssLearning` | `Module.Ess` | `[ModulePolicyOverride(Module.Ess)]` (stays outside the gateway's list: wave 2) |
| HR.Contracts | `HireWizard` | `HrContracts.HireView` | `[ModulePolicyOverride(HrContracts.HireView)]` |
| HR.Core | `LookupApi` | `HrCore.Lookup` | `[ModulePolicyOverride(HrCore.Lookup)]` |
| HR.Core | `CodeGeneration` | `HrCore.ReadOnly` (action policies on top) | `[ModulePolicyOverride(HrCore.ReadOnly)]` |
| HR.Core | `Policies` | `Policy.Reader` | `[ModulePolicyOverride(Policy.Reader)]` |
| HR.Core | `PolicyTemplates` | `Policy.Reader` | `[ModulePolicyOverride(Policy.Reader)]` |
| HR.Core | `Onboarding` | `HrCore.Approve` | `Platform.AgencyOnboarding` (P5) |
| HR.Core | `TenantQuarantine` | `TenantQuarantine.Reconcile` | `[ModulePolicyOverride(TenantQuarantine.Reconcile)]` |
| HR.Core | `RecruitmentAppeals` | `HrCore.AppealReview` | `[ModulePolicyOverride(HrCore.AppealReview)]` |
| HR.Core | `WorkInbox` | `HrCore.WorkInbox` | `[ModulePolicyOverride(HrCore.WorkInbox)]` |
| HR.Core | `WorkflowApprovals` | `HrCore.WorkInbox` | `[ModulePolicyOverride(HrCore.WorkInbox)]` |
| HR.Core | `ExamMarking` | `HrCore.ExamMarking` | `[ModulePolicyOverride(HrCore.ExamMarking)]` |
| HR.Core | `WorkflowDefinitions` | `Workflow.DefinitionRead` | `[ModulePolicyOverride(Workflow.DefinitionRead)]` |
| HR.Core | `EmployeeLifecycleInternal` | `Service.HrCoreLifecycle` | nothing: the filter already skips `Service.*` |
| Auth.Directory | `DirectoryInternalSync` | `Service.DirectorySync` | nothing: the filter already skips `Service.*` |

No other controller in any service starts with `Ess` or carries one of the names.

## Server (read-only)

Not taken yet: from this machine outbound TCP 22 times out (to the server and to github.com:22) since the start of the session, while
the server answers ping and TCP 443 (progress, decision 1). The picture (running tag, gateway audit rows per agency and without one,
the services' `Authorization` and `Gateway` configuration as the containers see them) is taken read-only as soon as SSH answers, and in
any case before P8; it is recorded in `reports/plan51-progress.md` and in the final report.
