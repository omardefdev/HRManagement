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
