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
- Fixes committed `46314a05`; hr-contracts-web rebuilt from it (67 s), the migrator `hr-migrate:local` through `docker compose build`
  (02:23:43Z → 02:42:12Z, 18.5 min); the superseded migrator image and this build's regular cache removed (12 → 21 GB free). The other 15
  service images are from `92f0c997`, whose service code is the same as `46314a05`'s outside HR.Contracts.
- **Prod-like stack 1** (`acceptance-reset.ps1`, then `acceptance-up.ps1 -Profile ProdLike -NoBuild -RequireCleanBaseline`): ready in
  694 s (02:43:22Z → 02:54:56Z); its verification Pass (DATA-AUTH 71, DATA-DIRECTORY 62, DATA-SEPARATION 86, DATA-EMP and DATA-CONTRACT
  1,617, SEED-03); the sweep's keys on hr-contracts-web (`IntervalMinutes` 1, `StaleAfterMinutes` 5).
- **The seeded statuses:** 56 `Executing` and 30 legacy `Draft` (every item with its mode: 10 per record). P0's 57/29 was wrong — the
  seeder loops per agency (72, 10 and 4 records: 24 + 4 + 2 multiples of 3 unpublished); every record's status is the backfill's rule on
  its own flag.
- **AC-SEP-60a..h run 1** (02:56:11Z → 02:57:43Z): 8/8. b: `DEC-SEP-2026-00003`, executed `Executing`, the contract `Terminated` at
  2026-11-28, the saga `Completed` with the seven parties; the second copy refused before the decision and before the date (2026-11-29).
  c: the maker's approval page and post both 403 (the module policy), the instance in progress at step 1; his decision refused by the
  issuer rule. d: `Step3Execute` and `Execute` 403 through the gateway. e, f (the exception by the checker, then executed and `Completed`),
  g (`Rejected` with its reason, no saga), h (`SEPARATION-APPROVAL-SENSITIVE`; central-a on the compliance step: no form, the post refused
  «هذه الخطوة تتطلب دور «compliance»», still step 2). Approvals reached the record 129–204 ms after the last step.
- g found more than expected: the copy has a pay profile — Payroll's inactive placeholder made at the employee's creation (no grade,
  salary 0, `IsActive` false). g now compares the profile row's checksum before and after the rejection, and records it.
- **Run 2** (02:59:54Z → 03:01:22Z): 7/8 — d failed at its submission: HR.Core refused the approval title «…ولا يتضمن رقماً طويلاً»,
  because the copy's random hex suffix held a long run of digits (a test-data defect; real names hold none). Its draft was tracked and
  cancelled by the cleanup. The fixture's suffix is now letters only.
- **Run 3** (03:02:09Z → 03:03:40Z), the second whole run on the same stack: **8/8**.
- **The rest of `main` on the same stack** (17 classes, 03:03:52Z → 03:17:59Z): **86/86**, none skipped — AC-SEP-60's copies,
  contracts, pay profiles, accounts and executed separations met by every other class of the group.
- **AC-SEP-60i run 1** (03:18:14Z → 03:18:49Z): failed at the withdrawal of B. **A defect of PLAN-46, found here:** HR.Core's
  `WorkflowExternalInstancesController` carries `[ValidateAntiForgeryToken]` on the class (so that a later POST is protected), which applies
  to GETs too; PLAN-43's two POSTs opt out, PLAN-46's `GET {id}/external-status` did not. Every owner's status read answered 400
  («the required antiforgery cookie … is not present»): the separation panel's approval state, a withdrawal that finds the instance ended
  (it gave PLAN-46's «وستُطبّق نتيجته قريباً» instead of applying the outcome), the cancellation's withdraw-first step on an ended
  instance, and the sweep — for every owner (AC-WF-46 never reads the status, so it went unseen). Fix: `[IgnoreAntiforgeryToken]` on the
  GET, as on the POSTs (the policy needs a client_credentials token no browser holds); a unit test pins every action's opt-out and the
  class's check. The run's cleanup could not cancel A and B (their withdrawal met the same 400): left `PendingApproval` with ended
  instances on stack 1, recorded, gone with its `down -v`.
- `sep060-report-unlinked.ps1` on stack 1: 116 records, 86 without a transaction (the 86 seeded ones), 54 Separation transactions, 24
  without a record (seeded). Counts after the runs: employees 1,651 (+34: the 33 copies and AC-IAM-50b's), contracts 1,650 (+33),
  accounts 104 and directory entries 95 (+33: Auth.Directory makes one per new employee), pay profiles 1,524 (+35 placeholders).
- Stack 1 brought down with its volumes (`acceptance-reset.ps1`, 5 s) to rebuild hr-core-web: with the stack up the machine had 3 GB of
  memory left and two unit builds were killed by their time limits.
- Fix committed `49ed7614`; hr-core-web rebuilt from it (168 s, no stack up); the old image removed by id.
- **Prod-like stack 2** (04:20:32Z → 04:31:35Z, 663 s; its verification Pass). **Deviation (P-2):** started with a one-minute load of
  13.8 on 8 CPUs (the hr-core-web build had just ended); it came up at the first attempt. Its end went unseen for 19 minutes: a monitor's
  second `grep` lacked `--line-buffered` (every stage is line-buffered since).
- **The isolated classes on stack 2, each alone** (04:51:01Z → 05:07:24Z): AC-SEP-60a..h on the fixed HR.Core 8/8 (63 s); **AC-SEP-60i
  run 1 1/1** (5 min 28 s: two outcomes queued while hr-contracts-web was down 18 s and purged; its start-up 6.5 s; B withdrawn 18 s after
  its submission → «انتهى مسار الاعتماد قبل السحب: طُبّقت نتيجته «معتمد»», `Approved`; the forged outcome for C ignored and logged with its
  instance id, C withdrawn → `Draft`, its instance `Cancelled`; **A approved by the sweep 317 s after its submission**, 396 s allowed);
  **AC-SEP-60i run 2 1/1** (5 min 30 s; B at 17 s; A swept at 318 s of 394); AC-WF-46 9/9; AC-WF-45 8/8 (CustodyJourney changed); the
  prod-like journey (`RecruitmentToHireJourneyProdLikeE2ETests`) 1/1; AC-INT-37 8/8.
- **s2s42** (overlays `docker-compose.acceptance-s2s42.yml` and `-wf46.yml`, hr-core-web, admin-logistics-web and idcard-web recreated, as
  the job's `Add-StageOverlay`): `AcS2s42TrustBoundaryTests` 8/9 — **AC-S2S-42h passed on the new path** (40 s); 42i fails here only for
  want of `kubectl` (P-14's known local-only failure, recorded, not chased); `AcWf46PolicyOrphanTests` 1/1. Stack 2 brought down with
  its volumes.
- **leastpriv** (`scripts/ci/acceptance-job.ps1 -Profile ProdLike -Stage prodLike:leastpriv -NoBuild`, 05:32:54Z → 05:42:05Z): its stack
  178 s, `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests` **9/9** (4 min 56 s). AC-OPS-38a: hr_contracts_db owned by
  `hr_contracts_migrator`, its service connected as `hr_contracts_app`, healthy on SEP060's schema; a second migrator run applies nothing
  (38b) — SEP060 applied as the migrator under forced row security. The job tore its stack down (no `hrm-leastpriv` container or volume
  left); no committed evidence rewritten.
- **The full Testcontainers step on the host** (CI's filter `Category=Testcontainers&Category!=ObjectStorage`, 06:20:01Z → 06:33:15Z):
  **344 passed, 1 skipped** (`Ops038BootstrapIdempotencyTests`, gated on its own image run; AC-OPS-38b proved the same on the leastpriv
  stack), 13 min 10 s. No Testcontainers container left.
- `scripts/sep060-validate.ps1` Pass; `-SelfTest` 21 planted defects refused. The committed evidence untouched (every local run wrote
  under `artifacts/plan60/runs/`).
- Measured for QA-066's rule (P8): AC-SEP-60a..h ≈ 1.5 min in `main`; AC-SEP-60i ≈ 5.5 min in `isolated`.
- Finished P6 (UTC): 2026-11-28T06:33:42Z
- Next step: P7 — the documents (the gap study's three status lines, `OPS_VPS_SINGLE_HOST.md`'s PLAN-60 section with the rollback note,
  the plan's Status and notes), the final report's draft, `main` merged if it moved.

## P7 — the documents

- `docs/FUN_002_SEPARATION_GAP_STUDY.md`: the status lines of G-SEP-01, G-SEP-04 and G-SEP-11 (they name the PR run's `AC-SEP-60`
  evidence folder; the run's link goes in the final report and the merge note).
- `docs/OPS_VPS_SINGLE_HOST.md`: PLAN-60's section — what changes for the demo's users, the migration and its backfill, the rollback and
  what the previous tag leaves behind, and HR.Core's status read fixed (the backup, the numbers and the change table follow in P9).
- `docs/EVENT_PUBLISHER_INVENTORY.md` (in P6): the separation's publishers after PLAN-60.
- The plan: `Status: Implemented — 2026-11-28`, and «ملاحظات التنفيذ والانحرافات».
- `reports/plan60-final-report.md`: the draft up to the server's section.
- `origin/main` has not moved since `96c9594d` (fetched): no merge.
- The app's file-write hook timed out twice; the report was written through the shell.
- Finished P7 (UTC): 2026-11-28T07:09:05Z
- Next step: P8 — the Release build, the local CI steps in a fresh clone, the pre-push check, `release/plan-60`, the push, the PR.

