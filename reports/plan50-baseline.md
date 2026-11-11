# PLAN-50 — baseline and inventories (P0)

Read on `148ce2c5` = `origin/main` `1ee60c47` + PLAN-49's merge note + the PLAN-50 prompt. Two read-only inventory agents and the
session itself. Line numbers are those of this commit. Long outputs: `artifacts/plan50/` (not committed).

## 0.2 Build

`dotnet build HRManagement.slnx -warnaserror`: 0 errors, 0 warnings (52.7 s, incremental, 10:03Z).

## 0.3 The plan's baseline filter

`dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Gateway|~SoD|~Workflow|~Hcm025|~Hcm028"`:
**957 passed, 0 failed** (43 s). No failure list.

## 0.4 The reach table (today)

Principal: `hr-management-approver` only, plus `offline_access` and a valid `tenant_id` (`a-management-approver`).
Policies: `HrManagement.Approve` = Admin, `hr-management-approver`; `HrCore.Approve` and `SoD.ManageDelegations` = Admin, `hr-core`,
`hr-management-approver`, `platform-admin`. `Module.HrMovements` = Admin, `hr-movements` (gateway and service filter).
`HrCore.Write` = Admin, `hr-core`, a service account with `hr-core.workflow` (not the approver).

| # | Assembly | Action | Verb | Gateway path | Gateway policy today | Gateway | Service (module + action) |
|---|----------|--------|------|--------------|----------------------|---------|---------------------------|
| 1 | HR.Movements | TransferRequests.Approve | POST | `/ui/hr-movements/TransferRequests/Approve/{id}` | Module.HrMovements | NO | NO (module) |
| 2 | HR.Movements | TransferRequests.Reject | POST | `…/TransferRequests/Reject/{id}` | Module.HrMovements | NO | NO (module) |
| 3 | HR.Movements | SecondmentRequests.Approve | POST | `…/SecondmentRequests/Approve/{id}` | Module.HrMovements | NO | NO |
| 4 | HR.Movements | LoanAssignmentRequests.Approve | POST | `…/LoanAssignmentRequests/Approve/{id}` | Module.HrMovements | NO | NO |
| 5 | HR.Movements | PromotionRequests.Approve | POST | `…/PromotionRequests/Approve/{id}` | Module.HrMovements | NO | NO |
| 6 | HR.Movements | DelegationRequests.Approve | POST | `…/DelegationRequests/Approve/{id}` | Module.HrMovements | NO | NO |
| 7 | HR.Contracts | HireWizard.Step3Approve | POST | `/ui/hr-contracts/HireWizard/Step3Approve` | HrManagement.Approve | yes | yes (module filter exempt; class HireView) |
| 8 | HR.Core | Onboarding.Apply (`api/onboarding/agencies/apply`) | POST | `/api/hr-core/api/onboarding/agencies/apply` | HrCore.Approve | yes | yes |
| 9 | HR.Core | Onboarding.ApplyFromFile | POST | `…/api/onboarding/agencies/apply-file` | HrCore.Approve | yes | yes |
| 11 | HR.Core | Approvals.Approve | POST | `/ui/hr-core/Approvals/Approve[/{id}]` | HrCore.Approve (PLAN-44) | yes | yes |
| 12 | HR.Core | Approvals.Reject | POST | `/ui/hr-core/Approvals/Reject[/{id}]` | HrCore.Approve | yes | yes |
| 13 | HR.Core | EmploymentRecords.Approve | POST | `/ui/hr-core/EmploymentRecords/Approve/{id}` | **HrCore.Write** | **NO** | yes |
| 14 | HR.Core | EmployeeCorrections.Approve | POST | `/ui/hr-core/EmployeeCorrections/Approve/{id}` | **HrCore.Write** | **NO** | **NO** (class `HrCore.Write`) |
| 15 | HR.Core | EmployeeCorrections.ApproveApi | POST | `/api/hr-core/api/employee-corrections/{id}/approve` | **HrCore.Write** | **NO** | **NO** |
| 16 | HR.Core | Competitions.Finalize | POST | `/ui/hr-core/Competitions/Finalize/{id}` | **HrCore.Write** | **NO** | yes (central only) |
| 17 | HR.Core | Competitions.Cancel | POST | `/ui/hr-core/Competitions/Cancel/{id}` | **HrCore.Write** | **NO** | yes |
| 18 | HR.Core | Delegations.Index / Create (GET) | GET | `/ui/hr-core/Delegations[/Create]` | HrCore.ReadOnly | yes | yes |
| 19 | HR.Core | Delegations.Create | POST | `/ui/hr-core/Delegations/Create` | **HrCore.Write** | **NO** | yes |
| 20 | HR.Core | Delegations.Revoke | POST | `/ui/hr-core/Delegations/Revoke/{id}` | **HrCore.Write** | **NO** | yes |

Also under `PolicyApprover` since PLAN-47 (the approver passes): `PolicyTemplates.PublishFromUi`, `Policies.Publish`
(`/api/policies/templates/{id}/publish`). Matches G-IAM-01: five movement types (six actions with the transfer reject), the nine
`HrCore.Approve` actions (Onboarding ×2, Approvals ×2, EmploymentRecords, EmployeeCorrections ×2, Competitions ×2), and the two
delegation writes. **Deviation from the prompt:** the prompt says "the three `Onboarding` API actions"; the controller has two
(`apply`, `apply-file`), both under `HrCore.Approve` on the class and the action. `platform-admin` passes `SoD.ManageDelegations` but not `Module.HrCore`, so it reaches no delegation page today.

**Note:** the gateway strips the module prefix of both `/ui/{m}` and `/api/{m}`, so every MVC action of a service is also reachable as
`/api/{m}/{Controller}/{Action}`; the rules of this plan open the `/ui/` form only, and the `/api/` form keeps the stricter policy.

## 0.5 Delegation

| What | Where | Readers / writers |
|------|-------|-------------------|
| `ApprovalDelegation` (`SharedKernel/SeparationOfDuties/ApprovalDelegation.cs`): `PrincipalId`, required `DelegateId`, optional `EntityTypeScope`, optional `RoleScope`, `ActiveFrom`, `ActiveUntil`, `Reason`, `IsRevoked`, `RevokedBy`, `RevokedAt` | DbSet `HRCoreDbContext.cs:54`, config 300-304, table from `IAM003_SeparationOfDuties` | **one**: `DelegationsController` (Index 34, Create 85-105, Revoke 114-141); tests `SeparationOfDutiesTests`, `SoDPolicyIntegrationTests` |
| `WorkflowDelegation` (`HR.Core.Domain/Entities/Workflow/WorkflowDelegation.cs`) | DbSet `HRCoreDbContext.cs:94`, config 921-925, table from the hand-written `WF001_WorkflowEngine` | **none** (and no test); `scripts/wf001-workflow-validate.ps1:49,65` expects its DbSet and table |
| Row security | both tables have `TenantId` and get ENABLE + FORCE + `tenant_isolation` from the dynamic hardening (`TenantRlsMigrationHelper.HardenAllTenantIdTablesSql`, run on every start and by `scripts/sql/ten002c-harden-tenant-rls.sql`) | — |
| `ResolveDelegate` / `IsActingAsDelegate` | `SeparationOfDutiesService.cs:158-178`, ignore `RoleScope` | tests only |
| `new SeparationOfDutiesService(` | `AuthUsersController.cs:94`, `LeaveRequestHandlers.cs:273`, `AdvanceRequestHandlers.cs:128`, `TransferRequestsController.cs:212`, `MovementsExtendedControllers.cs:359, 705`, `ApprovalWorkflowService.cs:24, 108`, `RoleAssignmentGuard.cs:39, 70`; one DI singleton in Payroll | **none passes delegations** |

`DelegationsController`: class `SoD.ManageDelegations`; `Create` takes a free GUID `DelegateId`, free-text scopes, and checks only
`ValidateDelegation` (ids, self, period, 90 days, reason); `Index` lists every delegation of the tenant to every holder of the policy;
no audit row. No navigation link to the page.

## 0.6 What PLAN-37..49 changed in the four files

| File | Changes |
|------|---------|
| `HrModuleAuthorizationFilter` | PLAN-43 (`WorkflowDefinitions` in the name list), PLAN-45 (`WorkflowApprovals`), PLAN-47 (`PolicyTemplates`). The `Service.*` bypass predates them. |
| `GatewayPathAuthorizationMiddleware` | PLAN-42 (internal enforce), PLAN-43 (`/api/workflow-definitions`, `/api/workflow-instances/{id}/approve|reject` → `WorkflowStepAction`, the rest of that API → `WorkflowExternalStart`), PLAN-44 (`Approvals/Approve|Reject` → `HrCore.Approve`, exact segment), PLAN-45 (`/ui/hr-core/WorkflowApprovals` → `WorkflowStepAction` on POST, `HrCoreWorkInbox` on GET), PLAN-47 (`TryGetPolicyLifecyclePolicy`), PLAN-48 (comments). The four MVC workflow actions still match by `Contains` → `HrCore.BranchOperation`. |
| `WorkflowApprovalEvaluator` | PLAN-43 (service account refused first; step role for every type; `WorkflowSodRules` after the role), PLAN-44 (the gates and the inbox bar the same makers), PLAN-45 (custody chain). Order today: service account → structure → step role → `WorkflowSodRules` → entity gate → initiator (approve only). |
| `WorkflowInstanceActionService` | PLAN-40 (outbox in the same save), PLAN-44 (`xmin`, the decision's unit of work, `Undone`), PLAN-45/46 (external outcome publisher, `HandledEntityTypes`), PLAN-48 (national definition read). The history entry comes from `engine.ApproveStep/Reject`; the signature is recorded inside the unit of work with `actor.SignatureRole(stepRole)`. PLAN-50's change is additive (history fields, signature fields), not the order of save and publish. |

## Inventories

### The five movement controllers (P-3)

No class-level `[Authorize]`; conventional route `{controller=TransferRequests}/{action=Index}/{id?}`; every POST has antiforgery; no
action returns JSON or a partial. Target policy per action:

| Controller | Index, Details (GET) | Approve (POST) | Reject (POST) | `HrMovements.Write` |
|------------|----------------------|----------------|---------------|---------------------|
| TransferRequests | override `ApprovalAccess` | override + `HrManagement.Approve` | override + `HrManagement.Approve` | Create ×2, Edit ×2, Execute, Cancel |
| SecondmentRequests | override | override + Approve | — | Create ×2, Edit ×2, Activate, Close, Cancel |
| LoanAssignmentRequests | override | override + Approve | — | Create ×2, Execute, Close, Cancel |
| PromotionRequests | override | override + Approve | — | Create ×2, Execute |
| DelegationRequests (movement «ندب», not approval delegation) | override | override + Approve | — | Create ×2, Execute, Close |

Today: creator check (`RequestedBy == by`) in all five Approve actions; `SeparationOfDutiesService.ValidateAction` in transfer,
secondment, loan; org scope (`Ensure…InScopeAsync`) in transfers (all actions) and loans (all but Index filter); none in the other
three. **`RequestedBy ??=` in the Create of secondments, loans, promotions and assignments keeps a posted value** (transfers overwrite
it). Views gate no button on a policy.

### Gateway rules for HR.Core's mutating paths, and the table (P-4)

Today (in order): `/api/tenant-quarantine` → TenantQuarantine.Reconcile; `/api/workflow-definitions` → DefinitionAdmin/Read; policy
lifecycle (PLAN-47: publish → PolicyApprover, UI/draft writes → PolicyAuthor, GET → PolicyReader); `/api/lookup` → HrCore.Lookup;
`/ui/hr-core/RecruitmentAppeals` → AppealReview; `/ui/hr-core/ExamMarking` → ExamMarking; `/ui/hr-core/WorkflowApprovals` →
StepAction (POST) / WorkInbox (GET); GET → WorkInbox / Lookup (static) / ReadOnly; POST: `/api/onboarding/` → HrCore.Approve,
`/api/workflow-instances/{id}/approve|reject` → StepAction, rest of `/api/workflow-instances` → ExternalStart, `Contains`
`ApproveWorkflow|RejectWorkflow|ApproveResultsWorkflow|RejectResultsWorkflow` → BranchOperation, `Approvals/Approve|Reject` →
HrCore.Approve, else HrCore.Write. Static files of other modules: no carve-out (the module policy).

Rows of `GatewayHrCoreActionPolicies`: `EmploymentRecords/Approve`, `EmployeeCorrections/Approve`, `api/employee-corrections/{id}/approve`,
`Competitions/Finalize`, `Competitions/Cancel`, `Approvals/Approve`, `Approvals/Reject` (PLAN-44, moved), `api/onboarding/agencies/apply`,
`…/apply-file` (moved), `Delegations/Create`, `Delegations/Revoke` → `SoD.ManageDelegations`,
`PolicyTemplates/PublishFromUi` and `api/policies/templates/{id}/publish` → `PolicyApprover` (PLAN-47, moved).

### Users of the three route policies

- `WorkflowStepAction`: `WorkflowInstances.Approve/Reject` (API), `WorkflowApprovals.ApproveWorkflow/RejectWorkflow`; gateway lines 243,
  264. Tests: `Wf043ExternalStartTests`, `Wf045ExternalApprovalGateTests`, `Wf045WorkflowApprovalsControllerTests`, `Wf046HrCoreSideTests`.
- `HrCore.BranchOperation`: `ApproveWorkflow/RejectWorkflow` of `RecruitmentRequisitions`, `OfferLetters`, `SelectionCommittees`,
  `EstablishmentChanges`, `Competitions` (and `ApproveResultsWorkflow/RejectResultsWorkflow`); gateway line 269. Tests:
  `GatewayPathAuthorizationMiddlewareTests` (191-205, 242-251), `FineGrainedPolicyTests` (143, 152), `Wf043ExternalStartTests` (439-441),
  `Hcm031MarkerAccessTests` (92-94), `S2s042InternalRoutesAreServiceOnlyTests` (sample, 270-290).
- `HrManagement.Approve`: the six movement actions and `HireWizard.Step3Approve` (HR.Contracts; gateway 279-284). No unit test names it.

### The actor, the history and the signature

- `WorkflowActionActor.FromPrincipal`: `WorkflowInstancesController:90`, `WorkflowApprovalsController:73,93,110`,
  `RecruitmentRequisitionsController:733`, `OfferLettersController:495`, `SelectionCommitteesController:264`,
  `EstablishmentChangesController:276`, `CompetitionsController:808`, `RecruitmentAppealsController:234`, `ApprovalsController:34,218`,
  `WorkflowPanelBuilder:76,220`.
- History writers: `WorkflowInstanceActionService.AppendHistory`, `PolicyWorkflowCodeGuard`, `ExternalApprovalService` (withdraw),
  `ApprovalRepairService`, two acceptance seed patches, `WF044` SQL. Readers: mapper (`ToState`, `ToSodFacts`), the history API, the
  approval page, the panels, the offer override rule, the outcome publisher, messaging repair, committee and appeal handlers, minutes,
  start coordinator, metrics and SLA. All read with `System.Text.Json` into the record, so optional fields are compatible.
- Signature: `WorkflowSignatureService` signs a twelve-field anonymous payload (instance, definition id, version, code, entity type,
  entity id, step level, step name, intent, actor id, comments, action time), `SignerRole = actorRole`. **No product code rebuilds a
  workflow payload to verify it** (`SignatureVerifier.VerifyWithPayload` has no caller); `WorkflowSignatureServiceTests` asserts the record.

### Entity types a delegation can name (`Delegation:EntityTypes`)

Workflow types with a step for `hr-management-approver`: `RecruitmentRequisition`, `OfferLetter` (normal and override),
`SelectionCommitteeSession`, `EstablishmentChangeRequest`, `RecruitmentCompetition`, `CompetitionResultVersion`, `AssetAssignment`,
`AssetLossReport`, `DisciplinaryAction` (after the PLAN-46 chain). Movement types: `TransferRequest`, `SecondmentRequest`,
`LoanAssignmentRequest`, `PromotionRequest`, `DelegationRequest`. Not listed: `RecruitmentAppeal` (compliance only), `CardRequest`
(idcard), `SeparationRecord` (not seeded).

### Tests that post an approval through the gateway, or build a delegation

Workflow API: Ac11, AcMsg40 (40b), AcWf43 (43c, 43e), AcWf44 (44b), AcWf45 (45d), AcWf46 (46e), `EstablishmentChangeFlow`.
`WorkflowApprovals`: `CustodyJourney` (AcWf45, AcWf43, AcWf46, AcPol49, Ac11) and the VPS checks of PLAN-45..49.
`RecruitmentRequisitions/…Workflow`: AcHcm21, AcHcm25, AcWf44 (44e), AcHcm26..30 (the approver posts step 2), `RecruitmentAcceptance`.
`OfferLetters`, `SelectionCommittees`, `EstablishmentChanges` (AcHcm26 h), `Competitions` (AcHcm31). `Competitions/Finalize` as hr-core
(AcHcm31). `Approvals/Approve` (AcWf44 d). Onboarding (Ac07). E2E journeys. **No test posts** `EmploymentRecords/Approve`,
`EmployeeCorrections/Approve`, a movement `Approve`, or a delegation. `ApprovalDelegation` is built in `SeparationOfDutiesTests` and
`SoDPolicyIntegrationTests`; nothing builds `WorkflowDelegation`.

### The acceptance classes of P-11

| Class | List / group / required |
|-------|--------------------------|
| AcHcm25ProductionApprovalTests, AcWf43DefinitionGovernanceTests, AcTen48NationalScopeTests, AcPol49EngineScopeTests | prodLike / main / true |
| AcWf44, AcWf45, AcWf46, AcPol47, AcInt37, AcMsg40 | prodLike / isolated / true |
| AcTen48LeastPrivilegeTests | prodLike / leastpriv / true |
| RecruitmentToHireJourneyProdLikeE2ETests | prodLike / journey / true (E2E) |
| Ac06TransferAccessTests | lab / main / **false** (it tests the branch scope of `api/lookup/employees`, not movements) |
| AcEst24, AcHcm21, AcHcm22, AcHcm28, AcHcm31, AcWf23 | lab / main / true |
| AcHcm26, AcHcm27 | lab / isolated / true |
| RecruitmentToHireJourneyE2ETests | lab / journey / true (E2E) |

`ci-scope.json`: `order[]` of eleven stages; `stageJobs[]` lab-main, lab-rest, prodlike (journey, main, isolated, s2s42),
prodlike-final, prodlike-policy (pol49), leastpriv, meta; every class exactly once (`AcceptanceCiScopeTests`).

## The server, read-only (2026-11-11T10:0xZ)

- Running tag `046316bde9e7`, on `origin/main` (`git branch -r --contains`). Host key: the recorded ED25519 key.
- `hr_core_db`: `ApprovalDelegations` **0 rows** (so D-50-4 revokes nothing on the server); `WorkflowDelegations` **0 rows**; one tenant.
- Live realm `hrmanagement`, user profile: declares `username`, `email`, `firstName`, `lastName` only, **no `employee_id`** (and no
  `tenant_id`), no `unmanagedAttributePolicy`. So an admin write of `employee_id` is dropped there, and the Auth.Directory fix has no
  effect on the live realm until the profile declares it. The manifest's profile declares `employee_id`, `tenant_id`,
  `scope_org_unit_id`, `acceptance_fixture`; `reconcile.sh` does not manage the user profile.
- `svc-hr-movements`: `hr-core.lookup` as an optional client scope; its service account holds `default-roles-hrmanagement` only.
- `e2e-checker-mgmt`: `hr-core`, `hr-management-approver`, `offline_access`. `e2e-movements-ops`: `hr-movements`, `offline_access`. The
  admin API shows no attribute on either (unmanaged attributes are hidden); the manifest gives them `tenant_id` only, no `employee_id`.
