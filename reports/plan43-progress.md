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
- P-6: the allow-list is empty, 42c proves the effect and no instance; closes M3 for the registered types. What stays for PLAN-51: any other hr-core.workflow caller holds no start now, so only the general contract of PLAN-46 and per-type registration remain.

- P3: done. Finished (UTC): 2026-11-06T11:33:14Z. Full unit suite before the last fix: 4987 passed, 1 failed (Hcm025ApprovalGatesTests.Non_enforced_entity_types_keep_their_previous_behaviour asserted the behaviour this phase changes on purpose; rewritten, not an AC-HCM-25 or parity test). After it: Hcm025, Wf043, guard, Est024, Hcm021, Hcm022, Wf023, SoD filters 314 passed, 0 failed. Hcm025PanelServerParityTests unchanged. hcm025-validate PASS, dated obligations PASS.

## Decisions in P3
- ExemptEntityTypes lives in docs/qa/workflow-step-role-exemptions.json too, watched by a new dated obligation (WF043-STEP-ROLE-EXEMPTIONS, reviewBy 2027-02-01); a test keeps it equal to the three settings files.
- The panel's role check now always equals the server's: an exempt type has no role check on either side.

- P4: done. Finished (UTC): 2026-11-06T11:44:24Z. Wf043 integration: 18 of 18 (Testcontainers PostgreSQL and RabbitMQ, and the real HR.Core pipeline), 2 min 23 s. Regression filter Hcm025ProductionApproval, Ac33AssetLoss, S2s042, Msg040, Workflow, Ops039: 73 tests, 72 passed; the one failure was Wf043's own read test, fixed since. Docker Desktop was found stopped at 2026-11-06T11:3xZ and started; no data lost seen yet.

## Found in P4
- HrModuleAuthorizationFilter refused workflow-admin before the controller's policy; WorkflowDefinitions joined its list of controllers with their own policies. Unit tests could not see this: only the real pipeline did.

## Next step
P5.

## Lock and stack log
- acceptance-stack acquired 2026-11-06T11:46:52Z. acceptance-up -Profile ProdLike started right after (all images rebuilt: the persona catalog changed).
- While it builds (2026-11-06T11:51:14Z): docs (engine, SoD matrix, gap study status, plan status and notes), wf043-validate.ps1 PASS, CodeQL baseline entries 061, 062, 063 marked Fixed (those actions now carry a token or moved), ci-scope and ac-ownership updated.
- Stack: first up failed at 12:01Z (old volumes, persona digest); reset (project volumes only) and up again, 261 s, services ready; the script's last step needs pwsh, skipped by design here. 0.5 confirmed by SQL on the stack: 17 definitions, Published and Retired only, distinct approvers on ASSET-ASSIGN-APPROVAL alone.
- AC-WF-43a..f alone: 6 of 6, 16 s (2026-11-06T12:09:35Z). 43e starts on the service identity with X-Acting-User, because a branch manager's scope guard refuses an asset in storage (no open holder), so the UI cannot reach it for br1-mgr: a deviation from the plan's wording.
