Started (UTC): 2026-11-07T20:33:47.4331981Z

# PLAN-46 progress

## Status
- P0: in progress (baseline tests running). P1 code written, tests written, not yet built.

## Decisions
- `AlreadyActive` keeps HR.Core's meaning: the instance is running. False on a success = the key found a finished instance.
- `WorkflowApprovalCancelResult`: an already finished instance answers Success=false, AlreadyTerminal=true, so a `if (Success) -> Draft` can never return it to draft.
- `WorkflowApprovalStatusDto` gets two optional last fields, `Reason` and `CorrelationId`: the sweep needs them to apply the same outcome as the event.
- `WorkflowOutcomeDecision` (implements `IWorkflowOutcomeDecided`) lets the consumer and the sweep share `ApplyOutcomeAsync`.
- Client tenant: `TenantIdFlow` first, request item as fallback, sent explicitly; owner sets the flow to the record's tenant. No tenant = refused locally (`Invalid`).
- Missing base address does not stop start-up (plan): optional read, not `HrCoreEndpoint.RequireBaseUri`.
- Metric: reuses PLAN-43's `WorkflowMetrics.OutcomeIgnored`; new `workflow_approval_sweep_applied_total`. The sweep adds 0 per reason so the series exists.
- No `ConsumerDefinition` is needed: the consumer outbox applies to every endpoint through `AddConfigureEndpointsCallback` (plan named an endpoint definition).
- AC-WF-46j key: `PolicyEngine:IDCard.Lifecycle` (the plan's `PolicyEngine:IdCard` is not a domain).

## Notes for later phases
- `scripts/hcm025-validate.ps1:95` needs `"RECRUITMENT-HIRE"` last in `RetiredCodes`, no trailing comma.
- `SeedAsync` skips existing codes: new versions only through `EnsureVersionAsync`, called from `NationalSeedOrchestrator`.
- Tests that move with the change: `Wf043DefinitionValidatorTests:174`, `Wf043DefinitionLifecycleTests:416`, `Hcm025ApprovalGatesTests:270`, `Wf043SodRulesTests:88,97,103,124,138,279`, `WorkflowStageGuardRoleTests:63`, `Wf045WorkflowApprovalsControllerTests:246`, `Hcm028AuthorizationTests:55`, `Phase5Tests:20`, `Ops038DemoDefectTests:274-358`, `AcceptanceV2CatalogTests:84`, `AcWf43VpsChecks:32-39`.
- `MassAssignmentArchitectureTests` does not cover IDCard.Web or Compliance.Web: bind form models, or assign explicitly, so the new fields are not overpostable.
- `EmployeeCreatedConsumer` guards on `Pending` only: extend to `PendingApproval` and `Approved`.
- Add `IX_AuditLogs_EntityType_TenantId` to IDCard and Compliance.
- `Data001ConcurrencyArchitectureTests`: add the two entities.

## Next step
Wait for baseline tests, commit P0, then build and run the P1 tests.

## Phase log
- P0 Finished (UTC): 2026-11-07T20:47:54Z. Inventories (two read-only subagents, in parallel): 6.5 min. Build 80 s. Baseline unit 320 passed; integration 11 passed, 16 skipped (RUN_TESTCONTAINERS unset), 0 failed. Server read-only counts in artifacts/plan46/vps/p0-counts.txt.
- P1 Finished (UTC): 2026-11-07T20:47:54Z. Wf046HttpWorkflowApprovalClientTests, Wf046OutcomeGuardTests, Wf046ApprovalSweepJobTests: 59 passed. Solution build 0 errors.
- Note: no Python on this machine; edit with the Edit tool or sed.

## Next step (after P1)
P2: HR.Core registry, ExternalOutcomePublisher, external-status, guardrails, policies, exemptions lifted, wiring, definitions, RetiredCodes, guards.
- P2 Finished (UTC): 2026-11-07T21:01:25Z. Unit filter (Wf04*, Hcm025, Hcm028, guards, wiring, outbox, S2s042, seeds, Workflow*): 1492 passed, 2 failed by design (WorkflowDefinitionsHaveHandlersTests.Each_consumer_exists for CardRequest and DisciplinaryAction: the consumers come in P3 and P4; they must be green at the end of P4). wf043-validate PASS, seed-001-validate PASS, hcm025-validate FAIL only on those two, wiring doc up to date.
  - Decisions: the guard keeps SeparationRecord as a pending entry (PendingPlan PLAN-60): its type may publish no definition, and the test fails the day the consumer exists until the entry is completed. HR.Contracts registers AddWorkflowApprovalClient now, so its matrix calls are true. Compliance becomes audience-only in the inbox; the layout keeps its appeal menu. Tests changed because the behaviour changed on purpose: Hcm025ApprovalGatesTests (exempt types -> every type enforced; it is not AC-HCM-25 nor the parity tests), Wf043SodRulesTests, WorkflowStageGuardRoleTests, Wf043DefinitionValidatorTests, Wf043DefinitionLifecycleTests (2), Wf045WorkflowApprovalsControllerTests.
  - Rollback note: the previous tag knows none of the three registry types: an instance of them stays InProgress there, and nothing starts one.

## Next step (after P2)
P3: the card.
- P3 Finished (UTC): 2026-11-07T21:14:14Z. Unit (Wf046*, Phase5, Data001, IdCard, Fun005, outbox, wiring, mass assignment): 285 passed, 1 failing by design (Data001 row for DisciplinaryAction, P4). Guard: CardRequest consumer found by reflection. IDCard has no pending model changes; migration without BOM.
  - Decisions: WorkflowApprovalSubmission (ServiceDefaults) holds rules 2-5 for every owner (PLAN-60 reuses it). A key whose instance was withdrawn before the record pointed at it spends its number. A save that fails after a start cancels it unless the record already points at it (a concurrent double submit). CardRequest gets ReplacesCardId: Renew opens a Replacement draft; the old card is revoked when the new one is issued (ProcessRequest or IssuedCards/Create). IssuedCards/Edit can no longer move a card to another employee. Create of a request copies only the form's three fields (no overposting of the approval fields). AutoSubmitOnHire=true is refused at start-up (no requester to vouch for a bus-made request). IDCard has no lookup client, so the title is the purpose only; the subject goes as SubjectEmployeeId and HR.Core shows the name.
  - Deferred (wave 2, as the plan says): ProcessRequest still issues a local serial and publishes no ICardIssued (G-IDA-03).
  - Persona a-idcard-2 (AGENCY-A, ordinal 45); AGENCY-A count 44.
  - Rollback: the previous tag reads statuses 3 and 4 as unknown enum values; it lets nothing process them (its CanProcess is Pending only) and shows the number.

## Next step (after P3)
P4: discipline.
- P4 Finished (UTC): 2026-11-07T21:20:50Z. Unit (Wf046*, Phase4/5, Data001, Compliance, Disciplinary, Fun004, outbox, guard, wiring, mass assignment, IdCard): 397 passed, 0 failed. Compliance has no pending model changes; migration without BOM.
  - Decisions: existing rows become Approved (2) and ApprovedBeforeEngine by column defaults (works whatever row security says); OutcomePublishedAt = CreatedAt by an UPDATE inside a DO block that lifts FORCE only if it was set and restores it (PLAN-44 pattern). Deviation from P-3's first option: the defaults are NOT reset to Draft/false, because this code always writes both columns, while the previous tag (after a rollback) omits them and publishes at once: its rows must be approved-before-engine too, never published again.
  - The publish of IDisciplinaryActionTaken lives in DisciplinaryApprovalOutcomeConsumer.StageActionTakenAsync ([StagesOutboxMessages]); the consumer and the sweep reach it through DisciplinaryApprovalService.ApplyOutcomeAsync. MessageId = DeterministicMessageId(action id): payroll's inbox drops any second copy. Excluded deciders: creator and submitter.
  - Rollback: the previous tag ignores the approval columns; a Draft or PendingApproval action has no payroll effect there (it was never published), until the roll-forward.

## Next step (after P4)
P5: PostgreSQL tests (Wf046ExternalOutcomePostgresTests, Wf046DisciplinaryConsumerPostgresTests, migration under forced RLS, sweeps under forced RLS), then acceptance (AcWf46CrossServiceApprovalTests + P-11 list), then the independent review.
- P5 (in progress): PostgreSQL Wf046ExternalOutcomePostgresTests (4) and Wf046DisciplinaryConsumerPostgresTests (6: consumer outbox one event and no second on redelivery, xmin, migration as non-superuser owner under FORCE in two tenants, both sweeps as the app role in two tenants) + Ops038 card tests: 15/15 pass (Testcontainers, 3 min).
  - Acceptance: AcWf46CrossServiceApprovalTests (46a..i, isolated) and AcWf46PolicyOrphanTests (46j) in the s2s42 overlay stage, with docker-compose.acceptance-wf46.yml (PolicyEngine__IDCard.Lifecycle=Enforce on hr-core-web and idcard-web; idcard-web added to recreate). Reason: a second overlay stage would force a full stack restart between them.
  - AC-INT-37c and AcOps38VpsDemoDefectChecks step 4 issue the card from a request seeded approved (they prove the serial and the form, not the approval); Renew issues nothing since PLAN-46.
  - Independent review started (one subagent) in parallel with the acceptance runs.
  - Independent review (one subagent, ~10 min): 0 high, 4 medium, 4 low. No refuter (none high). Fixed, with tests:
    1. ProcessRequest saved the card before the request: one save now (a second Process, an issue from the same request or a cancel loses on xmin and leaves no card). IssuedCards/Create answers a lost race with the form, not a 500.
    2. The sweep read only the oldest page: it now pages on (SubmittedAt, Id) through every waiting record (MaxPages 100); the PostgreSQL test pages one by one.
    3. A start of a Generic type refuses a version that breaks its guardrail (DefinitionMissing), whatever tenant it belongs to.
    6. An approval refused for naming the requester/creator closes the record as Rejected with a reason, instead of leaving it pending forever.
    7. IssuedCards/Edit may only bring the expiry forward; a later expiry is a renewal.
    8. The Submit-finished and Withdraw-AlreadyTerminal paths answer a lost xmin race with a message, not a 500.
  - Not changed, recorded: (4, medium) after a failed link save AND a failed compensating cancel, a later edit plus resubmission finds the same key's instance, approved on the old facts; it needs two failures in a row, and the generic page links to the owner's current record. For a later plan: a content hash in the facts. (5, low) old and new binaries must not overlap: on the single host, compose recreates each service (old stops before new starts); a rollback runs the previous tag's rules (its Renew issues at once, its Edit ignores approval) until the roll-forward - recorded as rollback behaviour.
  - Acceptance prod-like, run 1 (after the review fixes; 27 min including the image build): AcWf46CrossServiceApprovalTests 9/9 (46a..i), AcWf45 8/8, AcWf43 6/6, AcWf44 5/5, AcHcm25ProductionApprovalTests 8/8, AcS2s42 9/9, AcMsg40 8/8, AcOps39 8/8 (persona count with a-idcard-2). Failed: AcWf46PolicyOrphanTests 46j (the definitions list read by central-a shows only agency A's definitions: now read by the national p-workflow-admin-2, as AC-WF-43 does); AcInt37 37c (the seeded request used a card design of another tenant: now agency A's), 37b and 37e blocked by data that only the journey stages create (not run with -Classes; they run in CI after prodLike:journey).
  - Acceptance prod-like, reruns (-NoBuild): 46j needed the national reader and a readiness wait after the overlay recreated hr-core-web (502 otherwise); 37c an agency-A design. Rerun 2: RecruitmentToHireJourneyProdLikeE2ETests 1/1, AcInt37 8/8 (37b and 37e have their data after the journey), AcWf46PolicyOrphanTests 1/1 (46j: orphans unpublished; under Enforce, ProcessRequest of an approved request issued the card and HR.Core kept 8 instances before and after). Guard PASS. Evidence copied to artifacts/plan46/acceptance/.
  - Acceptance lab (-NoBuild, 7 min): Ac11PolicyWorkflowTests 1/1, AcWf23WorkInboxUiTests 6/6, AcHcm28CommitteeAppealTests 4/4, AcEst24 3/3, Ac33 1/1, RecruitmentToHireJourneyE2ETests 1/1, AcceptanceV2CatalogTests 9/9, AcceptanceIdentityPopulationTests 1/1. Guard PASS. Stack down -v; committed evidence untouched.
  - Times: prod-like run 1 27 min (with the image build), reruns 9 and 8 min, lab 7 min.
- P5 Finished (UTC): 2026-11-07T22:42:36Z.

## Next step (after P5)
P6: origin/main has not moved (c8252dc6) as of 22:00Z; check again, then P7 server (backup, single detached build, deploy, demo users, checks).
P6: origin/main unchanged at c8252dc6 (2026-11-07T22:43:00Z); nothing to merge.
P6 Finished (UTC): 2026-11-07T22:43:00Z

## P7 server log
- Condition: running tag 9560b03e51a3 is on origin/main. Release tag: dbf53280121d (the final code of P1-P5 plus the server checks and demo users).
- Backup: /opt/hrm/backups/pre-plan46-20261107T224439Z (700, files 600): pg_dumpall.sql (17 CREATE DATABASE), env.backup, SHA256SUMS verified. Undo: none (delete when no longer needed).
- Before: 20 disciplinary actions, 0 payroll disciplinary records; cards 45 Pending, 450 Processed; 457 issued cards; definitions 16 Published, 3 Retired.
- Build 1 failed: "no space left on device" (disk 95%). Freed with `docker builder prune -af` and `docker image prune -af --filter until=12h` (87 GB; disk 40%). Undo: none needed, every release stays in the loopback registry. Build 2 started detached.
- Build 2: 21 min 41 s (23:08:24 to 23:30:05Z), 17/17 images tagged dbf53280121d; source and build cache removed by the script; disk 43%.
- Deploy 1 (23:30Z) stopped at keycloak-reconcile ("could not read the realm roles", right after PostgreSQL restarted: the PLAN-40 incident); hrm-bootstrap had exited 0. Deploy 2, the same command: exit 0 in 116 s; current-tag dbf53280121d, previous-tag 9560b03e51a3; smoke 50 pass, 0 fail, 1 warn. Downtime about 4 min (23:30 to 23:34Z). Undo: `scripts/vps/deploy.ps1 -Rollback` (to 9560b03e51a3; safe by P-3).
- Demo users added to the live realm with scripts/vps/add-demo-user.sh (password from the server .env, not printed): e2e-idcard-maker (idcard), e2e-idcard-checker (idcard), e2e-compliance-checker (compliance); national tenant. Undo: delete the three users in Keycloak.
- Server checks need VPS_SSH_HOST, VPS_APP_DOMAIN, VPS_SSO_DOMAIN and VPS_TARGET_IP (without VPS_TARGET_IP the client connects to 127.0.0.1): artifacts/plan46/vps/env.sh.
- AC_WF_46 vps: schema/old actions/definitions Pass; card journey Pass; warning journey Pass (payroll exactly once).
- Server suites (16 min, nothing heavy beside): AcOps36VpsBootTests without 36j + AcInt37VpsServerTests + PLAN-38..46 server checks: 30/31; the sweep check ran before the first cycle of idcard-web, which restarted at 23:49Z during the suites. After the first cycles (23:59Z: "Workflow approval sweep of CardRequest: 1 tenant(s), 0 waiting, 0 applied" and the same for DisciplinaryAction) the sweep check passed: 31/31.
- After: disciplinary 22 (20 Approved+ApprovedBeforeEngine, 2 Approved by the engine from the checks); payroll disciplinary records 2, at most 1 per action (the 20 old ones have none, as before); cards 47 draft, 453 processed; issued 460; definitions 12 Published, 9 Retired; disk 42%.
- P7 Finished (UTC): 2026-11-08T00:00:15Z.

## Next step (after P7)
P8: docs (status lines done; PLAN-46 plan status and notes), ci-local + prepush, final report, release branch, PR, wait, merge.
