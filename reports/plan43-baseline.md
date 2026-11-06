# PLAN-43 baseline

Taken on `origin/main` 7480843e (PLAN-42 merged), normal mode, 2026-11-06.

## 0.2 PLAN-42 is in

`PolicyEnforceInternalController` derives mode, actor and tenant on the server and ignores `Mode` and `ActorUserId` from the body. `S2s042PolicyEnforceInternalTests` are green, inside the filter below.

## 0.3 Build

`dotnet build HRManagement.slnx -warnaserror`: 0 warnings, 0 errors, 38 s.

## 0.4 Tests

| Suite | Result |
|---|---|
| Unit, filter `Workflow`, `Wf0`, `Hcm025`, `Est024`, `Architecture`, `S2s042` | 742 passed, 0 failed, 2 min 16 s |
| Integration, filter `Hcm025`, `Workflow`, `Ac33` | not rerun here: PR 37's local CI (integration 352, Testcontainers 139, 0 failed) and post-merge `ci` run 42179150674 on this commit are the baseline |

## 0.5 Definitions

From `WorkflowPackSeeder`, confirmed on the demo server (17 rows, all version 1, identical roles). The stack SQL check follows in P5.

| Code | Published | DomainEntityType | Step roles | Binding (pinned v1) |
|---|---|---|---|---|
| ASSET-ASSIGN-APPROVAL | yes | AssetAssignment | hr-core, hr-management-approver | Assets.Custody |
| ASSET-LOSS-APPROVAL | yes | AssetLossReport | admin-logistics | Assets.Custody |
| HIRE-WIZARD | yes | ServiceTransaction | hr-core, hr-core | Contracts.Hire |
| SEPARATION-WIZARD | yes | SeparationRecord | hr-core, compliance | Contracts.Separation |
| ID-CARD-ISSUANCE | yes | CardRequest | **security** (not in the catalog or realm) | IDCard.Lifecycle |
| TRANSFER-APPROVAL | yes | TransferRequest | hr-core, hr-core | Movements.Transfer |
| RECRUITMENT-HIRE | no (retired) | RecruitmentApplication | hr-core | none |
| DISCIPLINARY-APPROVAL | yes | DisciplinaryAction | compliance | Compliance.Disciplinary |
| IT-TICKET | yes | Ticket | **it-support** x2 (not in the catalog or realm) | IT.Tickets |
| RECRUITMENT-REQUISITION-APPROVAL | yes | RecruitmentRequisition | hr-core, hr-management-approver | Core.Recruitment |
| RECRUITMENT-OFFER-APPROVAL | yes | OfferLetter | hr-core, hr-management-approver | Core.Recruitment |
| RECRUITMENT-OFFER-OVERRIDE-APPROVAL | yes | OfferLetter | hr-core, hr-management-approver x2 | Core.Recruitment |
| RECRUITMENT-SELECTION-COMMITTEE | yes | SelectionCommitteeSession | hr-core x2, hr-management-approver | none |
| RECRUITMENT-APPEAL-REVIEW | yes | RecruitmentAppeal | compliance | none |
| ESTABLISHMENT-CHANGE-APPROVAL | yes | EstablishmentChangeRequest | hr-core, hr-management-approver | none |
| RECRUITMENT-COMPETITION-APPROVAL | yes | RecruitmentCompetition | hr-core, hr-management-approver | none |
| RECRUITMENT-RESULTS-APPROVAL | yes | CompetitionResultVersion | hr-core, hr-management-approver | none |

Other writers: `WorkflowPlan03Seeder` (a second ASSET-ASSIGN-APPROVAL version only if the published one has fewer than two steps, so a no-op on a fresh database); `AssetAssignmentWorkflowSeeder` and `AssetLossWorkflowSeeder` have no production caller.

A binding has no version column: the pin is `WorkflowDefinitionId`.

## 0.6 Callers

**`POST api/workflow-instances`, production:** `WorkflowTriggerService` in Admin.Logistics only (both custody types), token of `svc-admin-logistics`. No user-facing caller, so no Stop under P-5.

**Tests:** no test starts through HTTP. `EstablishmentChangeFlow`, `AcMsg40AtomicPublishingTests` and `Ac11PolicyWorkflowTests` call approve and reject only; AcMsg40 inserts its instance by SQL, AC-11 gets its instance from internal enforce and moves to `Start` (P-6). `Hcm025ReviewFixTests` calls `controller.Start` in process and moves to a 403 proof. `HrCoreEndpointTests` drives the trigger service against a recording handler.

**`EnforceStepRoleEntityTypes`:** `WorkflowStageGuard`, `Program.cs`, three settings files, `WorkflowPanelBuilder`, `WorkflowInstancesController`, `scripts/hcm025-validate.ps1`, `WorkflowStageGuardRoleTests`. `docker-compose.acceptance-prodlike.yml` has no such key: the plan's row for it adds the new one only if needed.

**Instance creation in production code:** the controller's `Start`, `WorkflowStarter` (coordinator, policy orchestrator, `RecruitmentAppealService`, `CompetitionLifecycleService`, `CompetitionResultsService`), and two acceptance seed patches. None of the direct starters uses a custody type.

## P-4 Entity types and who can reach approval

`POST .../approve` passes for hr-admin, hr-core, branch-manager, hr-management-approver (and today a service token). So a step role of `admin-logistics`, `compliance`, `security` or `it-support` cannot reach it.

| Type | Step roles | Started by | Enforced today | Unreachable role | Decision |
|---|---|---|---|---|---|
| AssetAssignment | hr-core, approver | Admin.Logistics | no | none | enforced |
| AssetLossReport | admin-logistics | Admin.Logistics | no | admin-logistics | **exempt until PLAN-45** (plan default) |
| ServiceTransaction | hr-core x2 | nothing | no | none | enforced |
| TransferRequest | hr-core x2 | nothing | no | none | enforced |
| RecruitmentApplication | hr-core | nothing, retired | no | none | enforced |
| SeparationRecord | hr-core, compliance | nothing | no | compliance | **exempt until PLAN-46** (P-4) |
| DisciplinaryAction | compliance | nothing | no | compliance | **exempt until PLAN-46** (P-4) |
| CardRequest | security | nothing | no | security | **exempt until PLAN-46** (P-4) |
| Ticket | it-support x2 | nothing | no | it-support | **exempt until PLAN-46** (P-4) |
| RecruitmentRequisition, OfferLetter, SelectionCommitteeSession, EstablishmentChangeRequest, RecruitmentCompetition, CompetitionResultVersion | hr-core, approver | coordinator or starter | yes | none | enforced |
| RecruitmentAppeal | compliance | `RecruitmentAppealService` | yes | none in practice: compliance acts through `/ui/hr-core/RecruitmentAppeals` | enforced |

The four P-4 exemptions are inert today: nothing starts those types, the new `Start` refuses them, and the server holds no instance of them. They keep today's behaviour and carry a dated obligation. Recorded as a deviation from the plan's «the only exemption».

## Server before-picture (read-only)

- Running tag `a7ad95e0a4fa`, previous `3d0b2df84f82`.
- `WorkflowDefinitions`: the 17 rows above.
- `WorkflowInstances`: **0 rows**, so no in-flight instance can change who acts on it.
- `PolicyWorkflowBindings`: 11 rows, as in the table.
- Realm roles (22 plus the 3 defaults): admin-logistics, auditor, auth-directory, branch-manager, compliance, dev-analytics, file-storage, hr-admin, hr-contracts, hr-core, hr-employee, hr-health, hr-management-approver, hr-movements, hr-training, idcard, it-infra, payroll, payroll-approver, payroll-processor, platform-admin, records-archive. No `workflow-admin`.
