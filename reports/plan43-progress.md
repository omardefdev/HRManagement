Started (UTC): 2026-11-06T07:12:23.9627032Z

# PLAN-43 progress (checkpoint)

- Mode: normal (PLAN-42 merged, PR 37, 7480843e at 2026-11-06T07:10:37Z)
- Clone: D:\my projects\HRManagement (primary; freed by PLAN-42's session)
- Base: origin/main 7480843e3ffc74388246fdad8d28323c23deaf31, plus PLAN-42's merge note 73a46d1a (rides with this plan's first commit)
- Branch: feature/plan-43-wf-definition-governance-engine-api

## Timing notes
- First attempt 2026-11-05T19:39:47Z: BLOCKED in P0 (primary clone, PR 37 open, no second clone).
- WAITING-PEER: 2026-11-05T19:45Z to 2026-11-06T07:11Z for PR 37 (owner chose to wait; first CI run red at 21:21Z, PLAN-42 session idle until poked at ~23:00Z; second run green, merged 07:10:37Z).

## Phase status
- P0: done. Finished (UTC): 2026-11-06T07:21:11Z

## Inventories
- Duration: definitions and callers subagent about 7 min, in parallel with the baseline build (38 s) and unit filter (2 min 16 s).

## Decisions
- P-4: SeparationRecord, DisciplinaryAction, CardRequest and Ticket go into ExemptEntityTypes (step role cannot reach approval before PLAN-46), owner PLAN-46, dated obligation; AssetLossReport until PLAN-45. Nothing starts those four today.
- Start and cancel move to a separate service-only controller (same routes), class demands antiforgery and each bearer-only action opts out, the PLAN-42 pattern, so the CodeQL gate holds without baseline growth.
- HrCoreBranchOperation loses its service clause: every user of it is an approve or reject action.

- P1: done. Finished (UTC): 2026-11-06T07:38:35Z. Unit: Wf043, gateway, policies, Ops039, personas, Hcm025, Workflow filters 515 passed, 0 failed. EF pending-model gate passed (15 contexts).

## Decisions in P1
- WorkflowDefinitionRead policy (deviation): the plan reads with HrCoreReadOnly, which a persona holding only workflow-admin does not pass, and adding the role there would open all HR.Core reads. Class policy is the read policy; every other action carries WorkflowDefinitionAdmin, and a test requires it.
- LastEditedBy column (addition): the last editor of a draft counts as an author, so one person cannot rewrite a colleague's draft and publish it.
- A new version keeps the DomainEntityType of its code (addition): starts go by code, so a weaker type would escape the guardrail.
- Every definition write needs an antiforgery token from GET api/workflow-definitions/antiforgery (CodeQL gate, PLAN-40 pattern).
- keycloak-reconcile did not create realm roles, against P-8's assumption. It now creates the manifest's missing roles (role rows in reconcile-plan.tsv); none deleted, mappings untouched. On the server this also creates the older roles the realm lacks.
- Known entity types = HandledEntityTypes and the registry, as the plan says: the health report therefore also lists HIRE-WIZARD, SEPARATION-WIZARD, TRANSFER-APPROVAL and DISCIPLINARY-APPROVAL (unknown type) beside the security and it-support ones.
- P-3: the P1 migration holds only the definition columns; WorkflowInstance fields wait for WF043_ExternalStart.

Resumed (UTC): 2026-11-06T11:04:33Z. Reason: the owner interrupted the P2 writes, then said to continue. P0 and P1 committed, tree clean, main unmoved at 7480843e, locks free, server on a7ad95e0a4fa.

- P2: done. Finished (UTC): 2026-11-06T11:23:39Z. Unit filters 1089 passed, 0 failed. Integration and acceptance follow in P4 and P5.

## Decisions in P2
- Start and cancel live in WorkflowExternalInstancesController (same routes): class demands antiforgery, bearer-only actions opt out, as PLAN-42 did; a change of file from the plan's row, for the CodeQL gate. The wiring matrix points at it.
- HrCoreBranchOperation lost its service clause, and the evaluator refuses a service-account actor, so no route can let a service act on a step.
- The custody facts go through Facts into ContextJson, so conditions and the event's asset ids keep working; the body has no ContextJson.
- A key reused for another entity is a conflict (addition).
- AC-11: requester b-logistics, step 1 b-branch-manager, step 2 b-approver, because D-WF-8 bars a requester from every step. Test renamed.
- P-6: the allow-list is empty, 42c proves the effect and no instance; closes M3 for the registered types. What stays for PLAN-51: this holds for this API only. HrCoreWrite keeps its service clause, so an hr-core.workflow token can still submit records that start through the coordinator.

- P3: done. Finished (UTC): 2026-11-06T11:33:14Z. Full unit suite before the last fix: 4987 passed, 1 failed (Hcm025ApprovalGatesTests.Non_enforced_entity_types_keep_their_previous_behaviour asserted the behaviour this phase changes on purpose; rewritten, not an AC-HCM-25 or parity test). After it: Hcm025, Wf043, guard, Est024, Hcm021, Hcm022, Wf023, SoD filters 314 passed, 0 failed. Hcm025PanelServerParityTests unchanged. hcm025-validate PASS, dated obligations PASS.

## Decisions in P3
- ExemptEntityTypes lives in docs/qa/workflow-step-role-exemptions.json too, watched by a new dated obligation (WF043-STEP-ROLE-EXEMPTIONS, reviewBy 2027-02-01); a test keeps it equal to the three settings files.
- The panel's role check now always equals the server's: an exempt type has no role check on either side.

- P4: done. Finished (UTC): 2026-11-06T11:44:24Z. Wf043 integration: 18 of 18 (Testcontainers PostgreSQL and RabbitMQ, and the real HR.Core pipeline), 2 min 23 s. Regression filter Hcm025ProductionApproval, Ac33AssetLoss, S2s042, Msg040, Workflow, Ops039: 73 tests, 72 passed; the one failure was Wf043's own read test, fixed since. Docker Desktop was found stopped at 2026-11-06T11:3xZ and started; no data lost seen yet.

## Found in P4
- HrModuleAuthorizationFilter refused workflow-admin before the controller's policy; WorkflowDefinitions joined its list of controllers with their own policies. Unit tests could not see this: only the real pipeline did.


- Whole job on a64f7a67, -Profile Both: PASS, guard 69 classes in 12 trx files, 3473 s (lab stack 778 s, lab-main 857 s, prod-like stack 247 s, leastpriv 397 s). prodLike-main 14 (AcHcm25 8, AcWf43 6), AcInt37 8, AcMsg40 8, AcOps39 8, final 4, AcS2s42 9, AcOps38 8, both journeys, all green.
- Lab-main 7 failures, none required: Ac21, Ac23, Ac24, Ac25 as in the PLAN-41 baseline (regime seed defect); Ac08, Ac19, Ac30 only here: their scripts need PowerShell 7 (Ac08 uses ??; Ac30 lost SQL quoting under 5.1) and this machine has none; the same three passed in PR 37's CI.
- Independent review (one fresh subagent, read only): no high, 6 medium, 9 low. No refuter needed. Fixed with tests in 9b8f71ce: M1 retire needs a newer version; M2 every editor counts (EditorUserIdsJson replaces LastEditedBy); M3 conditional steps do not count, last step has no condition, an external start never completes with no approver; M4 custody keeps distinct approvers; M5 a loss report names the holder; L1 IsPublished also blocks edit and delete; L3 an unnamed actor is refused when a requester exists; L4 the role step does not use kc_body. Left: M6 a start that times out after HR.Core committed leaves an unlinked record (PLAN-45 retry); L2 no registered-type guard in WorkflowStarter (no caller; wf043-validate covers it); L4 kc_body's status for older steps; L5 publish not tied to a content hash (a pending definition cannot be edited); L6 WorkflowCode is required on the wire; L7 no-tenant fallback (PLAN-45); L8 catalog roles that WorkflowStepAction refuses; L9 rollback note goes in the ops document.
- After the fixes, images rebuilt, -Profile ProdLike -Classes AcWf43, AcHcm25, AcOps39, AcMsg40, AcS2s42: PASS, 1206 s (14, 8, 8, 9).
- Stack down with volumes; hrm-leastpriv taken down first (it stays up after a local job, as PLAN-42 said). acceptance-stack released 2026-11-06T13:32:16Z. wf043-validate PASS. No evidence committed.
- P5: done. Finished (UTC): 2026-11-06T13:32:16Z.


## P7 log
- P6: origin/main unmoved at 7480843e, no rebase needed.
- Local CI on c6faf0a9: 91 of 91, 1030 s, unit 4994, integration 366, Testcontainers 148, 0 failed. The first run stopped: Wf043ApiBoundaryIntegrationTests built a host per test and passed EF's limit of 20 service providers, failing 171 tests; one host per class fixed it.
- demo-server acquired 14:01Z. Backup /opt/hrm/backups/pre-plan43-20261106T140127Z (700/600): pg_dumpall.sql 25.9 MB, env.backup, SHA256SUMS OK, 17 CREATE DATABASE lines.
- Build 1 c6faf0a92e6e: 1349 s. Deploy: 156 s, reconcile 7 changes (workflow-admin and six older roles the realm lacked), smoke 50 pass, 0 fail, 1 warn.
- Server check found: the migration's backfill matched no row. WorkflowDefinitions has forced row security and hr_core_migrator carries no tenant; the Testcontainers run was a superuser and the acceptance stack seeds after migrating, so neither could see it. Fixed by WorkflowPackSeeder.BackfillGovernanceAsync at start-up (2026-11-06T14:34:08Z). The demo stayed usable: starts read IsPublished. A second build was needed for this; a deviation from "build once".

- Build 2 3bd9ee92bcf5: 1299 s. Deploy attempt 1 stopped at keycloak-reconcile right after PostgreSQL restarted (could not read the realm roles, unknown_error): the tag stayed, 7 containers up, applications down about 4 minutes (from about 14:56Z to 15:00Z). Attempt 2, the same command as the prompt says: exit 0, 120 s, reconcile 0 changes, smoke 50 pass, 0 fail, 1 warn. The transient error sits in the retry gap already open since PLAN-40.
- Server checks 1 to 8: definitions 16 Published and 1 Retired, distinct approvers on ASSET-ASSIGN-APPROVAL alone; health report 6 of 16 invalid, service healthy; workflow-admin in the realm; hradmin refused 403 on the start through the edge; e2e-checker-mgmt approved a requisition through both steps, instance Completed with no requester; leave tour in AC_OPS_38; no in-flight instance existed before; smoke green; AcOps36 (36j excepted), AcInt37 server, PLAN-38, 39, 40 server checks 21 of 21, 1226 s, and the 2 PLAN-43 checks.
- Requisition numbers: the demo seed wrote REQ-2026-00001..3 without moving the counter, so a real create answered 500 three times; the check moved the counter past them. A defect for later, now gone on this server.
- GATEWAY_CALLER_PROOF_SECRET removed from .env, as PLAN-42 deferred to the next deploy; nothing in the repository reads it; a copy is in env.backup.
- 22 containers, 0 unhealthy. demo-server released 2026-11-06T15:25:44Z.
- P7: done. Finished (UTC): 2026-11-06T15:25:44Z.

- P8 docs and report written 2026-11-06T15:27:15Z. Review done in P5.

## Next step
Local CI and pre-push on the final head, release/plan-43, PR, wait, merge.
