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
