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

## P5 — notes for a resume (acceptance facts, from a read-only map of the helpers, 2026-11-28)

- Session helper: `RecruitmentAcceptance.StartAsync(ac, key, scenario, personas...)` → `s["persona"].PostAsync(step, path, tokenPage,
  fields)` / `PostWithStatusAsync` / `GetAsync`; `s.Steps`, `s.Checks`, `s.Recorder.WriteResult`. UI client:
  `AcS2s42TrustBoundaryTests.UiClientAsync(persona)`. psql reads: `AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync/ScalarAsync`.
- Inbox: `CustodyJourney.ApproveFromInboxAsync(persona, instance, recorder)`; reject = `CustodyJourney.PostFormAsync(http,
  ApprovalPage(instance), RejectAction, {Comments})`. Forged outcome: `CrossServiceJourney.PublishOutcomeAsync` (AC-WF-46; hard-codes its
  code/role) and `AcceptanceMessagingHelper.ConnectBusAsync()`. RabbitMQ management: `RABBITMQ_MANAGEMENT_URL` (default
  http://localhost:15683), purge = `DELETE api/queues/%2F/{queue}/contents` (none in the repo yet). `AcS2s42.StartAsync` waits for HR.Core's
  ready only: AC-SEP-60i waits for hr-contracts' own `/health/ready` (direct port 6220).
- Screens: `ui/hr-core/Employees/Create` (NationalId, FirstName, MiddleName, LastName, BirthDate, Gender, MaritalStatus, PrimaryOrgUnitId,
  HireDate, Cadre; 302 → `/Employees/Details/{id}`); `ui/hr-contracts/Contracts/Create` (EmployeeId, ContractTypeId, StartDate, EndDate,
  ProbationMonths, Terms; type `AGENCY-A-ACC-LAW50-PERM`).
- **Deviation (P-12's fixture):** `Employees/Create` opens no regime exception, so `ServiceRegimeExceptions/Resolve` has nothing to resolve,
  and no screen sets a contract's grade or salary (an activation by the screens always leaves a PAY029-NOSAL review hold, which AC-PAY-58's
  isolation rule forbids a class to leave in agency A). The copy is therefore: an employee by `Employees/Create` (central-a), in the
  template's unit; its regime classified by the payroll classes' sanctioned precondition `AcceptancePayrollRegime.
  ClassifyRecordsAddedSinceTheSeedAsync` (the regime gate is not what AC-SEP-60 proves); a **Pending** contract by `Contracts/Create`
  (a-contracts-maker), never activated — no pay profile, no hold, no payroll footprint. The execution ends a pending contract as it ends an
  active one. PLAN-64 (the settlement) extends the fixture when it needs a paid profile.
- a-leaver: HQ ordinal 30 (Active), `AGENCY-A` 49 → 50, `DATA-AUTH` 70 → 71.

## P5 — acceptance, the older classes, CI, the scripts

- **Written:** `AcceptanceLeaverFixture` (the copy as recorded above: `Employees/Create` by central-a in the template's unit, the regime by
  the payroll classes' precondition, a Pending contract by `Contracts/Create`, then a read until HR.Contracts holds the copy's unit for
  the org scope; a fingerprint of the template's rows that every scenario asserts unchanged); `AcSep60Journey.cs` (a cookie-keeping
  screen session — the flash message reaches the page — that posts the form a page renders by its `data-sep-action`, or an action with a
  page's token; the per-scenario evidence; the separation's steps; the cleanup that cancels every record not executed, with a reason, and
  checks its instance ended); `AcSep60SeparationGovernanceTests` a..h and `AcSep60OutcomeSweepTests` i, both `[RequiredAcceptance]`;
  `CustodyJourney.RejectFromInboxAsync` beside `ApproveFromInboxAsync`; AC-INT-37's RabbitMQ management client made internal for i.
- **AC-SEP-60c:** the maker opens step 1's approval page and posts its approval with no token if the page refuses him; the layer that
  answered (the module policy, HR.Core's action policy, the antiforgery check, or the engine's role) is recorded, and the instance's
  state (in progress, step 1) is what the test asserts (P-12).
- **AC-SEP-60d:** on a record ready to execute (approved, decision issued, notification completed, effective today), so only the policy
  stands before the execution: `Step3Execute` and `Execute` through the gateway → 403 each.
- **AC-S2S-42h on the new path:** creation through the wizard as before; the record submitted by the maker, approved from the inbox
  (central-a, a-management-approver), its decision issued and its notification completed by the checker; the execution meets the hold
  and the HR.Core outage. psql writes only the employee's regime and hold (not the record's status, items, decision or approval); the
  never-executed record is cancelled at the end. The test is renamed `..._at_creation_and_execution_...` (no list names it).
- **Seeded counts:** the persona a-leaver has an employee, so the acceptance seed adds one directory entry: `DATA-DIRECTORY` 61 → 62
  (as PLAN-46/51/58 raised it per employee-linked persona); `DATA-AUTH` 70 → 71.
- **`sep060-validate.ps1`:** its UPDATE pattern missed C#'s escaped quote (`UPDATE \"Table\"`), the seeder check could not cross the
  descriptions' `;`, and `$Matches` was overwritten inside the codes loop — all three fixed; the no-SQL rule now also refuses calls of
  SQL-writing helpers but one named allowance (the fixture's single `ClassifyRecordsAddedSinceTheSeedAsync`). `-SelfTest`: 21 planted
  defects refused, the repository passes. `sep060-report-unlinked.ps1` parses (it runs against a stack in P6 and the server in P9).
- **Results:** `dotnet build HRManagement.slnx -warnaserror` 0/0; the acceptance project offline 79 passed, 369 skipped (the AC-SEP-60
  scenarios skip without a stack), `AcceptanceCiScopeTests` and `AcceptanceV2CatalogTests` among the passed; unit (realm render,
  separation POST policies, persona catalog) 42/42; Testcontainers `Sep060ServicePolicyHttpTests` 1/1; whitespace verified on every
  changed C# file, no BOM.
- `docs/CI_AND_TESTING.md` corrected to the fixture as built (it described the screens P-12 planned).
- Finished P5 (UTC): 2026-11-28T01:38:41Z
- Next step: P6 — disk and memory, the images one at a time, `acceptance-reset.ps1`, the prod-like stack, AC-SEP-60 twice, the main group,
  the isolated classes, s2s42, leastpriv, the seeded counts, `down -v`, the full Testcontainers step, the independent review.

## P6 — local acceptance and the independent review (in progress)

- Started (UTC): 2026-11-28T01:40:25Z. Images: the 16 services one at a time from `92f0c997` (PLAN-58's helper, kept outside the tree),
  1,291 s (01:40:34Z → 02:02:34Z), every one rebuilt; disk 21 → 19 GB free; the 15 superseded images of this repository removed by id.
- Two read-only subagents beside the build: a reference check of the new acceptance code against the source (24 min) and the
  independent review of `916a9e26..HEAD` (17 min).
- **Reference check:** one blocker — the rendered-form lookup's `action="` regex matched inside the panel's own `data-sep-action="…"`
  (the tag helper writes the real `action` after it), so every rendered post would have gone to `/submit`, `/withdraw`… at the root:
  anchored on whitespace. Two timing risks fixed: the saga row appears when HR.Contracts consumes its own outbox message (polled, 30 s);
  the decision's year is HR.Core's counter's (UTC) — either year accepted. Everything else (tables, columns, stored enum forms, fields,
  routes, rendered forms per persona and state, the Arabic fragments, the inbox links, the redirect shapes, the seed facts, the queue
  `hr-contracts-web-separation-approval-outcome`) matched the source.
- **Independent review:** no high; 2 medium, 1 low-medium, 5 low. Handled:
  1. *(medium) after a rollback, the previous tag and the four new items:* verified — EF 10 does not read an unknown enum name as the
     default, it throws («Cannot convert string value … to any value in the mapped enum»). So PLAN-59's tag, after a rollback, cannot
     load the whole checklist of a record holding one of the four items: its separation wizard (steps 2–4) and its publication fail on
     such a record (which also keeps it from executing that record outside the approval); its list, its record page, its step-filtered
     reads and the records it creates keep working. **Deviation from P-5/P-18's «that tag runs on this schema»:** true for the columns
     and every other path, not for those pages; a park-and-restore of the four items around a rollback was weighed and not built (an ops
     table, its grants and a script for a rollback the plan already calls not advised). `Sep060RollbackMappingTests` pins the behaviour;
     `docs/OPS_VPS_SINGLE_HOST.md` (P7) states it as what a rollback leaves behind.
  2. *(medium) the fixture's deviation:* kept, with its reason made precise: the only screen path that gives a contract a grade and a
     salary is a recruitment offer (the hire wizard provisions the pending contract from the accepted offer's terms), and each such hire
     takes one of the 40 `AC33-POOL` positions every recruitment class shares; P-14's runs of AC-SEP-60 alone need about 26 copies (a..h
     twice, i twice), so that path would empty the pool and break «repeatable on one stack without a reset». Consequence stated: AC-SEP-60g
     cannot show «the copy's profile still active» (the copy has none); it now also proves that Payroll wrote nothing for agency A
     (settlements, adjustments, holds) since the submission. The regime's SQL write stays the named allowance of `sep060-validate.ps1`.
  3. *(low-medium) after a rollback, an old-tag cancellation sets the flag only:* the open-record index still counts that record until
     this tag's reconciler runs (a new record of that employee is refused meanwhile, a 500 on the old Create), and an old-tag cancel of a
     pending record leaves its engine instance in the inbox (an approval of it is ignored: the record is no longer pending). Documented
     with finding 1 (fix forward).
  4. *(low) acceptance isolation on a failure path:* every scenario now keeps the ids it creates from the moment of creation
     (`CreateAsync(track:)`, `ApprovedAsync(track)`) and the cleanup takes them all, executed ones skipped by their status.
  5. *(low, latent) the backfill and a compensated saga:* the old compensation reset the flag and kept the saga row, so rule 3 made the
     record a legacy draft. Rules 2 and 3 now key on «started» as rule 5 already did (published, or a saga row): a compensated saga cancels
     its record, one still compensating keeps it Executing, a newer live draft of the employee stays open; `LegacyStatus` and the
     reconciler follow (it also applies the saga's end to a record the previous tag left Executing). Planted cases added to the backfill
     test and the unit rules. No such data exists on the demo or in the seed.
  6. *(low) the issuer and the exception's approver when the record has no creator:* the submitter is now excluded as well, as in the
     engine's exclusions (P-7) and the review brief; unit tests with and without a creator.
  7. *(low) two concurrent issuances:* the loser now answers with the winner's decision (reloaded after the `xmin` conflict) instead of
     a 500, and logs the number it generated and did not use; a PostgreSQL test reproduces the race.
  8. *(low, process) AC-QA-34e's list and the renamed test:* the list changed in `cf348a0f` (P1's file group) and the test was renamed in
     `d983f4cd` (P4's), not in one commit as P-13 asks and as this record said; at HEAD they agree. The record is corrected here; the
     history is not rewritten.
- Tests after the fixes: unit (separation filters) 280/280; SQLite integration 160 passed, 11 skipped (Testcontainers-only); Testcontainers
  `Sep060*` 48/48 (the race test needed its own setup fixed once: its side contexts must be the host's tenant-less ones).

