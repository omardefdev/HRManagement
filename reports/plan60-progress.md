Started (UTC): 2026-11-27T23:18:37Z

# PLAN-60 progress

Main folder: `/home/omar/HRManagement` (the owner's Linux notebook). Branch `feature/plan-60-sep-unblock-decision-governance` from
`origin/main` `96c9594d`. Keep-awake: `systemd-inhibit` PID 1900124 (`artifacts/plan60/keepawake.pid`).

## P0 — Timing, keep-awake, preconditions, baseline

- Preconditions: no other plan session; tree clean; `origin/main` holds PR #60's merge; `ssh hrm-vps whoami` = `hrm`, ED25519 key
  `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; `gh` active account `omardefdev` (`repo`, `workflow`).
- The prompt was pasted into the session: written from the session's text to `docs/acceptance-plans/execution/PLAN-60-AGENT-PROMPT.md`.
- PLAN-59's merge note cherry-picked from `docs/plan-59-merge-note` (then that branch deleted: its content is on this branch).
- Baseline: `reports/plan60-baseline.md` — PLAN-46's contract all present by name (no adapter); build green 4 min 47 s; unit 640,
  integration 46 + 49 (Testcontainers), architecture 119, all green; `pol049-validate` FAIL (28) and `acceptance-baseline-validate` exit 1,
  as on `main`.
- Demo picture (one ssh call, 12 s): 26 records (16 published, 10 not), no saga row, no `Guid.Empty`, no open duplicate, no employee with
  two started records → no Stop. Workflow definitions Published 12 / Retired 9.
- Durations: inventories — code inventory < 1 s (grep), test inventory 21 min 43 s (one read-only subagent, beside my reading); the demo
  picture 12 s.
- Finished (UTC): 2026-11-27T23:49:09Z
- Next step: P1 (the state machine, the factory, the migration).

## P1–P4 — written together, committed by phase (Resumed: no stop since P0)

**Why together (a recorded deviation of process, not of content):** P1's state machine changed the setters of `SeparationRecord`, so
every handler, consumer, controller and test that wrote the old flags had to move in the same build; P2..P4 then touched the same files
(`SeparationRecordHandlers.cs`, `Program.cs`, the test host `ContractsTestServices.cs`, `IContractsRepository`). The phase commits are cut
by file group after the four phases were built and tested together on P4's state: an intermediate commit may not build on its own. The
build, the unit, integration, Testcontainers and architecture filters ran at P1's, P2's, P3's and P4's states (results below).

- **P1** (state machine, integrity, migration): `SeparationStatus`, `SeparationStepCompletionMode`, the four new `SeparationStep` values,
  the record's fields (internal setters: only `SeparationWorkflowRules` writes the status; `ApprovedBy` private), `SeparationWorkflowRules`
  (table, `Transition`, `IsOpen`, the legacy backfill and the reconciler's rules), `SeparationStatusSync` (the transaction follows along its
  own table, a history row per transition; no row added to its table), `SeparationRecordFactory` (one creation path for the form, the
  wizard and the inbound requests), the gate reads the employee's status (terminal names in SharedKernel, pinned against HR.Core's rule),
  `GetEmployeeContractsLookupQuery` and the forms' script, the edit lock, `Advance` refuses a separation, migration
  `20261127235200_SEP060_SeparationStatusGovernance` (no BOM; rules 1–7 under lifted FORCE; `RAISE` on two started records),
  `SeparationStatusReconciler` at start-up, the seed by the backfill's rules. Pulled forward from P4 to keep DOM001 honest: the
  execution ends the contract at the effective date (`ContractWorkflowRules.Terminate(contract, endDate)`).
- **P2** (approval): HR.Core seeds `SEPARATION-APPROVAL` (f0050018) and `-SENSITIVE` (f0050019) as version 1 in `Workflows`, both in
  `DistinctApproverCodes`, both chains in `EnsureCrossServiceChainsAsync`; `SeparationApprovalRouting`; `SeparationApprovalService`
  (submission and withdrawal through `WorkflowApprovalSubmission`, `ApplyOutcomeAsync` shared by the consumer, the sweep and the
  withdrawal; creator and submitter excluded; a record no person created excludes the submitter and names the subject);
  `SeparationApprovalOutcomeConsumer`; `SeparationApprovalOwnerStore` + `AddWorkflowApprovalSweep` (lock `4_001_014`); the guard entry
  without `PendingPlan`; `Wf046RegistryEnforcedTests` without its skip; `R-SEP-START` no longer an orphan by type; the overlay's sweep keys
  for `hr-contracts-web` (`StaleAfterMinutes` **5**, not 2 — AC-SEP-60i withdraws ~3 min after the submission and must meet it pending).
- **P3** (decision, checklist): `IssueSeparationDecisionCommand` (`DEC-SEP`, once, the issuer not the creator, Damascus's issue date);
  linked decisions refused on Edit and Delete; `CompleteManualStepCommand`, `RequestStepException`/`ApproveStepException` (two people,
  never the requester nor the creator, Manual items only); `SeparationPartyAckChecklistConsumer` (seven parties; none while compensating);
  the journey matrix v2 and its both-ways test; IT access's SLA aligned to the matrix (48 h).
- **P4** (execution, policies, screens): `ExecuteSeparationCommand` (status → decision → pre-execution items → Damascus's date → hold →
  regime; one save with the outbox), `SeparationClock`, the cancellation by status with a reason (PendingApproval withdrawn in the engine
  first, judged again on the result), `Delete`/`PublishSeparation`/`OverrideChecklistStep`/`Step2CompleteStep` removed,
  `Step3Execute` → `HrContractsApprove`, `SeparationInterventionStatusConsumer`, the interventions' Resume/Cancel (antiforgery + `GET
  antiforgery`; Cancel refuses a separation), the saga's end → `Completed` by `SeparationRecordId`; the screens on one panel partial.

**Test results:** P1: unit 785/785; integration (SQLite) 3 failures fixed → green; Testcontainers: the reconciler's duplicate item (its
added items had no tenant in a test context) fixed → all 26 `Sep060*` green. P2: unit, integration, Testcontainers all green. P3: all
green. P4: see below.

**Decisions:** AC-QA-34e's eleven names kept except `DOM001_Publish_DoesNotAutoComplete_ContractTerminated` →
`DOM001_Execute_EndsTheContract_AtTheEffectiveDate_AndCompletesItsItem` (its premise flipped; the list changed in the same commit).
`Cancel_AfterPublish_PreEffective_PublishesCancelSaga` (not counted) → `Cancel_after_the_execution_is_refused_until_PLAN63` (it
asserted the old rule). The ContractLifecycle override tests → `StepException_*`.
- P4 results: unit 876/876, SQLite integration 168 (9 skipped as Testcontainers-only), Testcontainers 180/180, architecture 170/170
  (after two interface findings fixed: the destructive forms ask first with `data-hr-confirm`, the checklist's empty row reads «لا توجد»).
  Formatting verified on every changed file. `Pol049RulesInventoryTests` green.
- Commits: P1 `cf348a0f`, P2 `95d74ec4`, P3 `cbfe70c2`, P4 (this commit).
- Finished P1–P4 (UTC): 2026-11-28T00:50:31Z
- Next step: P5 — the persona `a-leaver`, `AcceptanceLeaverFixture`, `AcSep60SeparationGovernanceTests` (a..h) and
  `AcSep60OutcomeSweepTests` (i), `Sep060ServicePolicyHttpTests`, the older acceptance classes (AC-INT-37a/e, AC-S2S-42h), CI scope,
  `sep060-validate.ps1` and `sep060-report-unlinked.ps1`.
