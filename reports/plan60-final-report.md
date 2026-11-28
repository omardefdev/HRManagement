# PLAN-60 — final report: the separation journey unblocked, and its decision governed (SEP-A)

Branch `feature/plan-60-sep-unblock-decision-governance`, released as `release/plan-60`. Prompt:
[PLAN-60-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-60-AGENT-PROMPT.md); plan:
[PLAN-60-sep-unblock-decision-governance.md](../docs/acceptance-plans/PLAN-60-sep-unblock-decision-governance.md); progress record:
[plan60-progress.md](plan60-progress.md); baseline: [plan60-baseline.md](plan60-baseline.md).

> Draft written in P7; the server (§12), the PR run (§11), the merge and the timing (§15) are completed in P9 and P10.

## 1. Summary per phase

| Phase | What it delivered |
|---|---|
| P0 | Timing, keep-awake (PID 1900124), preconditions all met, PLAN-46's contract present, baseline tests green, the inventories, the demo's read-only picture (tag `8086ac8629af`, 26 records, 0 saga rows, 0 `Guid.Empty`, 0 duplicates). |
| P1 | The state machine (`SeparationStatus`, one transition table, `SeparationStatusSync` for the linked transaction), one creation path (`SeparationRecordFactory`), the partial unique index, `xmin`, migration `SEP060` with its backfill under forced RLS, the start-up `SeparationStatusReconciler`. |
| P2 | The approval through HR.Core's engine: `SEPARATION-APPROVAL` and `SEPARATION-APPROVAL-SENSITIVE` seeded with distinct approvers; submission, withdrawal, reopening; the outcome consumer; the sweep (`WorkflowApprovalSweepJob<SeparationApprovalOwnerStore>`). |
| P3 | The numbered decision `DEC-SEP-YYYY-#####`; the checklist by completion mode (Manual, System, PartyAck) with the four new items; exceptions by two people; the parties' acknowledgements. |
| P4 | The execution (its order of checks, Damascus's date, one outbox unit), the explicit POST policies, the cancellation by status, the interventions, the saga's statuses, the screens (one panel partial). |
| P5 | `AcceptanceLeaverFixture`, the persona `a-leaver`, AC-SEP-60a..h and i, the older classes on the new path, CI placement, `sep060-validate.ps1` and `sep060-report-unlinked.ps1`. |
| P6 | The images (16 services and the migrator), two prod-like stacks, AC-SEP-60 three times on one stack and again on the second, the rest of `main`, the isolated classes, s2s42, leastpriv, the full Testcontainers step, two read-only reviews and their fixes, one PLAN-46 defect found and fixed. |
| P7 | The documents (P-16) and this draft. |
| P8–P10 | (completed below) |

## 2. Commits

| Commit | Phase | Subject |
|---|---|---|
| `916a9e26` | — | the agent prompt |
| `cada300b` | P0 | baseline |
| `cf348a0f` | P1 | the state machine, the creation path, SEP060 |
| `95d74ec4` | P2 | the approval through HR.Core's engine |
| `cbfe70c2` | P3 | the decision and the checklist |
| `d983f4cd` | P4 | the execution, the policies, the cancellation, the screens |
| `92f0c997` | P5 | AC-SEP-60a..i, the fixture, CI placement, the scripts |
| `46314a05` | P6 | the reviews' fixes |
| `49ed7614` | P6 | HR.Core's status read (a PLAN-46 defect) |
| `d4947522` | P6 | the local acceptance record |
| (P7) | P7 | the documents |

P1 to P4 were written together (the shared files mixed) and committed by file group, so an intermediate commit may not build alone. AC-QA-34e's list changed in `cf348a0f` and its renamed test landed in `d983f4cd`: they agree at HEAD, but not in one commit as P-13 asks.

## 3. Decisions applied and deviations

- **Two acceptance classes** (P-12): a..h in `prodLike` `main`; i in `isolated` (it stops `hr-contracts-web` and purges its outcome queue).
- **PLAN-51's split and `Step3Execute`:**
  - the maker's acts take `HrContracts.Write`;
  - the second person's take `HrContracts.Approve`, in the service itself: the execution, the wizard's `Step3Execute`, the decision, the exception's approval, the cancellation;
  - `HrContractsSeparationPostPoliciesTests` pins the map.
- **AC-SEP-60c's honest refusal:** the maker's approval of his own step is refused by the module policy (403 on the page and on the post), before any separation-of-duties rule, because no persona holds `hr-contracts` and `hr-core`. The exclusion itself is `Sep060ExclusionTests`'.
- **The definitions' seeding and distinct approvers:** both chains in `WorkflowPackSeeder` (`f0050018`, `f0050019`), both codes in `DistinctApproverCodes`; `AcWf43VpsChecks` expects 14 published definitions.
- **Records no person created:** the submitter stands in for the creator in the engine's exclusions, and (after the review) in the decision's issuer rule and the exception's approver rule.
- **The backfill's added rules:**
  - a record with no employee is cancelled with PLAN-60's reason;
  - open duplicates per employee are cancelled with PLAN-60's duplicate reason, keeping the started or the newest one. A record whose saga started is never cancelled: two started ones stop the migration;
  - after the review, rules 2 and 3 key on "started" as rule 5 does (published, or a saga row): a compensated saga cancels its record instead of returning it to draft.
- **`Delete` and the interventions' cancel:**
  - a separation record is never deleted: the list's delete became the cancellation with a reason;
  - the interventions' Cancel refuses a separation until PLAN-63;
  - the cancellation of an executing record is PLAN-63's.
- **The leaver fixture (P-12):**
  - the copy is an employee made by `Employees/Create`;
  - its regime is set by the payroll classes' stated precondition (`AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync`), the one named SQL allowance `sep060-validate.ps1` checks;
  - its contract is a **Pending** one made by `Contracts/Create`.

  Why: the only screen path that gives a contract a grade and a salary is a recruitment offer, and each such hire takes one of the 40 `AC33-POOL` positions every recruitment class shares; P-14's runs alone need about 26 copies. So the copy's pay profile is Payroll's inactive placeholder. AC-SEP-60g proves that profile untouched (its row's checksum) and that Payroll wrote nothing for agency A.
- **A PLAN-46 defect fixed in HR.Core:**
  - `WorkflowExternalInstancesController`'s class-level `[ValidateAntiForgeryToken]` also applied to its `GET {id}/external-status`, so every owner's status read answered 400;
  - this broke the panel's approval state, a withdrawal that finds the instance ended, the cancellation's withdraw-first step, and the sweep;
  - the GET now opts out like its POSTs, and a unit test pins it.
- **The rollback (P-5, P-18):** PLAN-59's tag runs on this schema, with one exception. It cannot load the whole checklist of a record that holds one of the four new items (EF refuses an unknown enum name: `Sep060RollbackMappingTests`), so its separation wizard and its publication stop on such a record. `OPS_VPS_SINGLE_HOST.md` documents this, with what else it leaves behind. Fix forward.
- **The demo's limit:** the server checks take one Resignation to `Approved` and cancel it; no decision, no execution.
- **The merge by the agent**, on the owner's standing instruction, after every check is green.
- **P0's seed prediction corrected:** 56 `Executing` and 30 legacy `Draft` (the seeder loops per agency), not 57/29.

## 4. The state machine and the migration

| From | To |
|---|---|
| Draft | PendingApproval (with an engine instance), Cancelled |
| PendingApproval | Approved, Rejected, Draft (withdrawn, or the engine's Cancelled), Cancelled (withdrawn first) |
| Rejected | Draft (reopened), Cancelled |
| Approved | Executing (the execution), Cancelled |
| Executing | AwaitingIntervention, Completed |
| AwaitingIntervention | Executing (resumed) |

- **Seeded statuses** (prod-like stack): 86 records, 56 `Executing` and 30 legacy `Draft`, ten items each with its mode.
- **The backfill in its tests:**
  - `Sep060MigrationPostgreSqlTests`: a `NOSUPERUSER NOBYPASSRLS` owner, forced RLS, two agencies; every rule's planted rows, the indexes, `FORCE` back on;
  - the planted-data case of `Sep060SeparationPostgresIntegrationTests`: the four combinations, the duplicate, the compensated saga, a saga still compensating, a newer live draft;
  - on the leastpriv stack, the migrator applied it (AC-OPS-38a/b);
  - **on the demo:** §12.
- **The unlinked report** on stack 1: 116 records, 86 without a transaction (the seeded ones); 54 Separation transactions, 24 without a record.

## 5. The approval

- **Chains:**
  - `SEPARATION-APPROVAL`: hr-core, then hr-management-approver;
  - `SEPARATION-APPROVAL-SENSITIVE`: hr-core, compliance, hr-management-approver, for Disciplinary and Judicial (`Separation:Approval:SensitiveTypes`).
- **Submission** goes through PLAN-46's `WorkflowApprovalSubmission`, with the creator and the submitter excluded.
- **The outcome** goes through `SeparationApprovalOutcomeConsumer` and `WorkflowOutcomeGuard`. On the stack it reached the record 129–204 ms after the last step.
- **The sweep's measured delay:** A approved 317 s and 318 s after its submission. The allowance is `StaleAfterMinutes` 5 + `IntervalMinutes` 1 + the start-up: 396 s and 394 s.
- **The withdrawal's race:** B was withdrawn 17–18 s after its submission, its instance already ended. The page said the approval had ended before the withdrawal and that its outcome (Approved) was applied, and B ended `Approved`, never `Draft`.

## 6. The decision and the checklist

- **Decisions issued on the stacks:**
  - `DEC-SEP-2026-00001` onward, one per approved record that needed it, never by its creator or submitter;
  - a second call returns the same decision and spends no number;
  - a concurrent second issuance answers with the winner's decision (`xmin`, tested on PostgreSQL).
- **Modes:**
  - System: DecisionIssued and ContractTerminated;
  - Manual: EmployeeNotified, with evidence of five characters or more;
  - PartyAck: the seven parties, completed by their acknowledgement or by the saga's finalization.
- **Exceptions:**
  - requested by the maker, with a reason of twenty characters or more;
  - approved by a second person, never the requester, the creator or the submitter;
  - a party's item and the decision are never excepted.

## 7. The execution and the policies

- **The order of checks:**
  1. the status (`Approved`);
  2. the decision;
  3. the pre-execution items;
  4. the effective date in Damascus;
  5. the legal hold (fail-closed);
  6. the regime.
- **Then one save:**
  - the contract `Terminated`, with `EndDate` = the effective date, and its item;
  - the record `Executing`, with its actor;
  - the transaction moved;
  - `IStartSeparationSaga` in the outbox.
- **Policy map:** §3.
- **The saga's end of each executed copy:** `Completed` with the seven parties (Payroll, HR.Core, IDCard, Auth.Directory, Admin.Logistics, IT.Infra, Records.Archive), on every run.

## 8. AC-SEP-60a..i

| Scenario | People | Proved |
|---|---|---|
| a | a-contracts-maker; central-a; a-management-approver | Draft → PendingApproval with an instance of SEPARATION-APPROVAL; two inbox approvals → Approved; decided by the authority |
| b | maker; central-a; a-management-approver; a-contracts-checker | the decision, EmployeeNotified with evidence, the execution on today in Damascus → Executing, the contract Terminated at the effective date, the saga Completed; refused before the decision and before the date (naming it) |
| c | maker; central-a; a-management-approver | the maker's approval of his step refused (403, the module policy), the instance at step 1; his decision refused by the issuer rule |
| d | a-management-approver | Step3Execute and Execute → 403 through the gateway on a record ready to execute; the service half: `Sep060ServicePolicyHttpTests` |
| e | maker | a second open record refused; another copy's contract refused; the subject unchanged after the submission |
| f | maker; checker | the maker's own approval of his exception refused; the checker's approval excepts the item; executed → Completed |
| g | maker; central-a; a-management-approver | rejected with its reason → Rejected, no saga, the copy's Payroll rows untouched and nothing written for the agency |
| h | maker; central-a; a-compliance; a-management-approver | Disciplinary → three steps; central-a refused the compliance step by its role; Approved |
| i | maker; central-a; a-management-approver; checker | two outcomes lost while hr-contracts-web was down; B's withdrawal applies its ended instance's outcome; A approved by the sweep; a forged outcome for C ignored and logged |

Runs:
- **stack 1:** a..h 8/8, then 7/8 (a test-data defect, fixed), then 8/8; the rest of `main` 86/86 after them;
- **stack 2:** a..h 8/8; i 1/1, twice.

## 9. The existing tests and checks that changed

- **The integration classes** (ContractLifecycle, Fun002, Int003, G3, ContractSeparationChain, EndToEndReleaseWorkflowSmoke, Plan034, SoDForgedApprover): each reaches its record through the factory, the engine's outcome, the decision and the execution. Int003's DOM001 is now `DOM001_Execute_EndsTheContract_AtTheEffectiveDate_AndCompletesItsItem`, because its premise flipped.
- **AC-QA-34e:** its eleven names kept, except DOM001's (renamed with its meaning).
- **AC-INT-37a and 37e:**
  - 37a uses an employee whose service has not ended, and passes the contract when he has several;
  - 37e cancels its record, so it repeats.
- **AC-S2S-42h:** the record is submitted and approved from the inbox; the checker issues the decision and completes the notification; the execution meets the hold and HR.Core's outage. psql writes only the regime and the hold.
- **The server checks and registries:**
  - `AcWf43VpsChecks`: Published 12 → 14, and two more distinct-approver codes;
  - `WorkflowDefinitionsHaveHandlersTests` and `Wf046RegistryEnforcedTests` as P-7.

## 10. The guards and their negative tests

- `sep060-validate.ps1`: 10 checks; `-SelfTest` refuses 21 planted defects.
- `HrContractsSeparationPostPoliciesTests`:
  - the policy map, antiforgery and the stance;
  - no path starts the saga but the execution;
  - no path puts a record in PendingApproval without an instance.
- `Wf043ExternalStartTests.Every_service_action_opts_out_of_the_class_antiforgery_check_and_the_class_keeps_it`.
- `Sep060RollbackMappingTests` and `Sep060RollbackReadingTests`.

## 11. Results

- **Host tests:**
  - unit: the separation filters 280/280, and the controller tests 219/219 after the last fixes;
  - SQLite integration: 160 passed, 11 skipped;
  - Testcontainers (CI's filter): 344/345, 1 gated skip.
- **Acceptance:**
  - AC-SEP-60: §8;
  - AC-WF-46 9/9, AC-WF-45 8/8, AC-INT-37 8/8, the prod-like journey 1/1;
  - s2s42: 8/9 (42i: no `kubectl` here) and 1/1;
  - leastpriv 9/9.
- **The PR run's artifact** (the evidence of record): (P8).

## 12. The server

(P9)

## 13. Changes for operators and API clients

- **The record page** shows:
  - its status, and its approval's state from HR.Core;
  - its decision, and its checklist by mode;
  - the acts its status allows: submit, withdraw, reopen, issue the decision, complete an item, request or approve an exception, execute, cancel with a reason.
- **The approval** happens in HR.Core's work inbox; HR.Contracts has no approval button.
- **The decision's number** is `DEC-SEP-YYYY-#####`. The execution's refusals name what is missing: the decision, an item, or the date.
- **Older records** are marked as older records that need an approval, until their first submission.
- **No delete, publish or override.** The cancellation needs a reason.

## 14. Items other plans own, and defects for later plans

- **Other plans:**
  - PLAN-61: the legal types at creation, and the single entry;
  - PLAN-62: the parties' truth, and the clearance;
  - PLAN-63: cancelling an execution, the compensation, and the interventions' cancel of a separation;
  - PLAN-64: the settlement, with a paid copy for the fixture;
  - PLAN-65: the rerun of wave 1.
- **For later:** a park-and-restore of the four new items around a rollback, if a rollback ever has to keep the separation wizard working.

## 15. Timing

(P10)

## 16. Readiness note

- **The fixture:** `AcceptanceLeaverFixture.CreateAsync(template, recorder)` gives a new copy per scenario: an employee, his classified regime, a pending contract. PLAN-64 extends it once a screen gives a contract its grade and salary.
- **The statuses** and their table: §4.
- **The hooks each plan meets:**
  - PLAN-61: the execution's order of checks, met by its types at creation;
  - PLAN-62: the PartyAck items and the saga's finalization;
  - PLAN-63: the refused cancellations;
  - PLAN-64: the Payroll party.
