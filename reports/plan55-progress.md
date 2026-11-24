Started (UTC): 2026-11-24T05:14:03Z
Resumed (UTC): 2026-11-24T20:06:20Z — the turn ended on its own at 10:28:11Z while it waited for the fourth start of the prod-like stack (the wait reached the tool's limit and no further call was made); the owner wrote «Try again» at 20:06Z. The session stood still 9 h 38 min. No restart of the machine (up 1 day 9 h); the keep-awake process was gone and was started again (new PID in `artifacts/plan55/keepawake.pid`).

# PLAN-55 — progress (the session's checkpoint)

Main folder: `/home/omar/HRManagement` (the owner's Linux notebook). One session, no second clone, no locks.
Branch: `feature/plan-55-pay-bank-account-pay-profile` from `origin/main` `12b83970`.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 baseline | done | 2026-11-24T05:21:55Z |
| P1 the account | done | 2026-11-24T06:11:40Z |
| P2 the pinning | done | 2026-11-24T06:46:59Z |
| P3 the versions | done | 2026-11-24T07:17:16Z |
| P4 the repair | done | 2026-11-24T07:49:45Z |
| P5 PostgreSQL proofs | done | 2026-11-24T08:21:43Z |
| P6 acceptance, review | done | 2026-11-24T21:52:09Z |
| P7 main | done: `origin/main` is still `12b83970` (fetched 21:44:43Z), nothing to merge | 2026-11-24T21:52:09Z |
| P8 demo server | next | |
| P9 docs, gates, report, merge | | |

## Timing log

| What | Start (UTC) | End (UTC) | Note |
|------|-------------|-----------|------|
| Session start | 05:14:03 | | |
| Baseline build (`-warnaserror`) | 05:15:14 | 05:19:27 | 4 min 12 s, 0 errors |
| Demo picture (read-only, ssh) | 05:16:31 | 05:16:40 | |
| Inventory by search | 05:16:54 | 05:16:56 | `artifacts/plan55/inventory.txt` |
| Baseline unit filters | 05:20:06 | 05:20:50 | 436 passed |
| P1 migration proofs on PostgreSQL (Testcontainers) | 05:47:28 | 05:47:52 | 4 passed |
| P1 architecture tests (build + run) | 05:48:29 | 05:50:03 | 400 passed |
| P1 HTTP tests on PostgreSQL (build + run) | 05:51:09 | 05:53:02 | 10 passed (3 new, 7 of PLAN-53's scope class) |
| P1 full build `-warnaserror` + `has-pending-model-changes` | 05:53:30 | 05:55:14 | 0 warnings, 0 errors; no pending model change |
| P1 full unit suite | 05:55:36 | 06:06:01 | 6868 passed, 10 min 12 s |
| P2 unit suites it touches (Pay055, Iam053, Frd001, Pay029, writers, determinism, Pay054) | 06:29 | 06:31 | 537 passed |
| P2 PostgreSQL tests (build + run) | 06:32:37 | 06:36:12 | 30 passed (the real-run pinning proof, the DSAR answer, and the changed classes) |
| P2 full build `-warnaserror` + architecture tests | 06:43:01 | 06:45:10 | 0 warnings, 0 errors; 400 passed |
| P3 unit suites it touches (Pay055, Iam053, Pay029, writers, consumers) | 07:05 | 07:06 | 360 passed |
| P3 PostgreSQL and HTTP tests (build + run) | 07:08:28 | 07:12:34 | 50 passed |
| P3 full build `-warnaserror` + architecture tests | 07:12:45 | 07:15:37 | 0 warnings, 0 errors; 400 passed |
| P5 `Pay055BankAccountPostgreSqlTests` (8) | 07:57 | 07:58 | 8 passed, 34 s |
| P5 CI's full Testcontainers filter on the host (`Category=Testcontainers&Category!=ObjectStorage`) | 07:59:30 | 08:20:06 | 282 passed, 1 skipped, 20 min 28 s |
| P6 validate script: `pay055-validate.ps1` and `-SelfTest` | 08:38 | 08:39 | pass; 27 planted defects refused |
| P6 guard tests of scope, ownership, personas and scripts (unit filter) | 08:40 | 08:46 | 529 passed |
| P6 acceptance reset (`acceptance-reset.ps1`) | 08:47:34 | 08:47:37 | volumes of `hrm-acceptance` reset; credentials kept |
| P6 image build, 16 services one at a time (`artifacts/plan55/build-images-sequential.ps1`) | 08:48 | 09:17 | 1,746 s, revision `31349e50`, disk 42 GB free after |
| P6 independent review (one read-only subagent, beside the image build) | 08:49 | 09:00 | 13 findings: 2 high, 4 medium, 7 low |
| P6 review fixes: unit filter 381, PostgreSQL filter 36 | 09:30 | 09:37 | all passed |
| P6 chain after the fixes: full build, architecture (401), full unit suite | 09:38:26 | 09:51:20 | build 0 errors; 401 passed; **the unit run was aborted by a test host crash after 6316 passes** (swap 3.6 of 4 GB used; the machine's known crash) |
| P6 full unit suite again, with `--blame` | 09:51:33 | 10:02:45 | 6950 passed, 10 min 58 s, no crash |
| P6 payroll image rebuilt from the review-fix commit | 10:04:55 | 10:07:51 | 155 s, revision `31f3cc00` |
| P6 prod-like stack start, four runs of `acceptance-up.ps1 -Profile ProdLike -UseLocalBaseImages -NoBuild` | 10:08:00 | 10:50:08 | 1: RabbitMQ's 5 s health check timed out under a load of 54 (every service starting at once, swap full); 2: the same; 3: a Keycloak admin call answered 401 during the identity sync; 4: ready at 10:50:08Z (32 min) |
| P6 on the first prod-like stack, after the resume: the repair script's dry run; AC-PAY-55; the other prod-like classes; AC-UX-67 | 20:06:20 | 20:16:08 | repair: the path was wrong (`/api/payroll/api/payroll/admin` is 404; the gateway serves `/api/payroll/admin`), fixed; dry run of agency A: 0 fillable, 1102 not fillable, nothing changed. AC-PAY-55: a, b, c, e passed, **55d failed on its own SQL** (`PaymentOrders.Status` is text), fixed, passed alone. `AcIam53` + `AcPay54` + `AcIam51` + `AcIam52` + `AcHcm25`: 31 of 31. `AcUx67`: 7 of 7, the three new key pages opened (200, one policy, no violation) |
| P6 stack down (`acceptance-reset.ps1`) | 20:16:30 | 20:17:05 | volumes removed |
| P6 acceptance job, prod-like, fresh stack (`acceptance-job.ps1 -Profile ProdLike -NoBuild -Stage prodLike:journey,prodLike:main,prodLike:isolated -Classes <14>`) | 20:17:29 | 20:50:53 | **PASS**, 2,004 s: stack 882 s; journey 1 of 1 (37 s); main 36 of 36 (208 s: AC-PAY-55a..e with AC-IAM-51/52/53, AC-PAY-54, AC-HCM-25 in the group's own order); isolated: AcInt37 8, AcMsg40, AcWf44 5, AcWf45 8, AcWf46 9, AcPol47 1, AcOps39 8 (805 s); `acceptance-guard: PASS (14 classes in 9 trx files)`; the job removed its stack |
| P6 acceptance job, lab (`-Profile Lab -NoBuild -Stage lab:main,lab:journey -Classes Ac21,Ac23,Ac24,Ac25,AcHcm29,RecruitmentToHireJourneyE2ETests`) | 20:51:18 | 21:11:37 | **PASS**, 1,219 s: stack 921 s; main 7 of 7 (247 s); lab journey 1 of 1 (22 s); `acceptance-guard: PASS (6 classes in 2 trx files)` |
| P6 seeded counts by psql on the fresh lab stack, before its tests (`artifacts/plan55/p6-seeded-counts.txt`) | 21:07:01 | 21:07:05 | the 32 tables of `acceptance-verify.ps1` have their expected counts (pay profiles 1489, runs 57, orders 72, holds 12); `EmployeeBankAccounts` 0: the seed adds no account |
| P6 migrator image (`hr-migrate:local`) through `docker compose build` from a one-service file (no buildx here) | 21:13:27 | 21:34:02 | 1,235 s, fifteen bundles, 5.13 GB; disk 40 GB before, 31 GB at the end, 36 GB after pruning the build stages (4.73 GB) |
| P6 acceptance job, leastpriv (`-Stage prodLike:leastpriv -NoBuild`) | 21:34:21 | 21:44:30 | **PASS**, 609 s: stack 191 s; `AcOps38FirstBootTests` + `AcTen48LeastPrivilegeTests` 9 of 9 (367 s): PAY055 is applied by the migrator's role from empty volumes and Payroll starts on it as its own role; `acceptance-guard: PASS (2 classes in 1 trx files)`; the job removed its stack |
| P6 full build `-warnaserror` (with `AcPay55VpsChecks.cs`, written during the waits), format check of the new file | 21:44:47 | 21:52:09 | 0 warnings, 0 errors (6 min 53 s); format clean |
| P4 chain: full build, architecture, PLAN-55 integration, full unit suite | 07:30:27 | 07:45:51 | 0 errors; 401 passed; 19 of 20 (one test of mine assumed a branch-restricted hr-admin; rewritten, 6 of 6 after); 6920 passed, 10 min 2 s |

Stops:
- **10:28:11Z to 20:06:20Z, 9 h 38 min: the turn ended on its own** while waiting for the prod-like stack's start script (the fourth
  start; it finished «ready» at 10:50:08Z with nobody to read it). Resumed on the owner's «Try again».

## P0 — what was done

- Keep-awake: `systemd-inhibit --what=sleep:idle --who=plan55` detached, PID in `artifacts/plan55/keepawake.pid` (2868026). Stop it by
  that PID at the end.
- Preconditions: all pass. `origin/main` = `12b83970` (PLAN-54's merge is there). Tree clean. `ssh hrm-vps` → `hrm`, host key
  `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`. `gh`: `omardefdev` active, scopes `repo`, `workflow`. Port 22 open.
- PLAN-54's merge note cherry-picked from `docs/plan-54-merge-note` (`8ca10418` → `07fe4506`); that local branch deleted after checking
  that its one commit is on this branch.
- The prompt was pasted into the session: it was written from the session's own transcript to
  `docs/acceptance-plans/execution/PLAN-55-AGENT-PROMPT.md` and committed (`ad3b4e69`).
- The local disk: 38 GB free of 233 GB; 15 GB RAM. The server: tag `014db7e3a01a`, 24 GB free.
- Baseline and inventory: `reports/plan55-baseline.md`.

## Decisions (with reasons)

- **D1 — «Disbursed» for a salary line** = it has a payment order in `Submitted`, `Acknowledged`, `Settled` or `PartiallySettled`. No
  order, a `Draft` order or a `Rejected` order = not yet disbursed. Reason: `Draft` → `Submitted` is the moment the instruction leaves for
  the channel (`PaymentOrderService.CanTransition`). The retirement hold (P1) uses it.
- **D2 — The demo's repair is expected to change nothing:** 425 active profiles lack a regime, and no employee has an earlier profile
  that carries one. The repair copies from the employee's own history only (P-8), so it reports them as not fillable.

- **D3 — One filtered unique index, not two.** `IX_EmployeeBankAccounts_OneActiveOnePending` is unique on
  `(TenantId, EmployeeId, Status)` where `Status IN (0, 1)`: exactly the plan's rule (one `Active` and one `PendingVerification` per
  employee and agency). Reason: a verification updates two rows in one save (the account in force → `Retired`, its replacement →
  `Active`); with the status inside the index, EF orders the two updates by the index's values, so the old row leaves before the
  new one arrives. Two indexes on the same two columns would leave that order to chance. P5 proves it on PostgreSQL.
- **D4 — An account the migration moved has no registrar, so it is never verified.** It can only be rejected and registered again
  by a person (`NoRegistrar`, 409). Reason: «no Active account without a second person»; nobody can be shown to be the second.
- **D5 — The migration moves one account per employee and agency,** not one per profile: PLAN-53's copy-forward put the same number
  on every later version, and the one-pending rule allows one. It takes the active profile's number, else the latest profile's.
- **D6 — The answers.** A broken input rule 400; the registrar verifying his own registration 400 (the plan's AC-PAY-55a; PLAN-53
  answers 403 for its own self-decision); a second pending account, a wrong state, a concurrent decision 409; outside the scope or
  the agency 404 (PLAN-52's rule; the plan wrote 403).
- **D7 — Retire and reject take `Payroll.Approve`;** the registrar may reject his own pending registration (a withdrawal activates
  nothing). Registering the number that is already in force is 409.
- **D8 — The retirement hold has no fraud alert.** PAY-029's hold comes with an alert for the fraud review queue; a retired account
  is not a suspicion. The hold is `Manual`, run-specific, `ACCOUNT-RETIRED:` at the start of its reason, and is released by the
  existing release. One per employee and run; a reversed or cancelled run gets none.
- **D9 — The API takes the kind by name** (`"Iban"`, `"Local"`): the service has no string-enum JSON converter, and a number in a
  body says nothing.
- **D10 — A refused registration form is shown again without the number it posted:** the number is typed again, never echoed.
- **D11 — No per-country IBAN length.** Form (2 letters, 2 digits, letters and digits), 15 to 34 characters (the standard's
  maximum, the registry's shortest), check digits 02..98, mod 97 = 1. Syria is outside the registry.
- **D12 — For an IBAN the bank code is optional;** one that is given must be defined. With no code the bank's name is the
  registrar's text (mandatory). A local number needs a defined bank, whose name is taken from its definition.

- **D13 — A correction run has no snapshot rows:** its lines are computed when the correction is created, so each pins the account
  in force at that moment (the same rule, D-PAY-15, at the correction's own calculation).
- **D14 — The order and the file carry the pinned account's number whatever its status now.** A pinned account that was retired
  since is behind the retirement hold (the disbursement gate blocks the batch); the readiness report lists those employees
  (`employeesWithRetiredAccount`). A local number goes into `BeneficiaryIban` as it is.
- **D15 — The treasury refusal** is `MissingPinnedBankAccountsException` (an `InvalidOperationException`, so the screens' existing
  catch shows it): `EmployeeIds` for an API, and a message with up to 25 names and a count of the rest; an employee the directory
  gives no name for is counted, never shown by an id. The export exists on screens only; the ids are served by the readiness API.
- **D16 — DSAR:** present = an account in force or waiting for verification; verified = an account in force.
- **D17 — The obsolete columns are named as text** in `DeliberatelyNotCopied` (an obsolete member is not referenced under
  `-warnaserror`); `Iam053ApplierCopyForwardTests` now requires that only `[Obsolete]` members join the identity set there.

- **D18 — `CreateNextVersion` carries the salary and the grade only from a version in force.** From a placeholder or a closed
  version it carries who the employee is (unit, regime); the salary and grade are the source's, else 0 and none. Reason: this is
  exactly what PAY-029's first activation did (`BaseSalary = salary ?? 0`, `PayGradeId = grade?.Id`), and a closed version's salary
  ended with it (a re-hire).
- **D19 — A placeholder** is a profile that is inactive and has no end (`IEmployeeCreated`'s, or the unpriced profile an activation
  leaves inactive). It never held a day: a version replaces it whatever its date, and closing it may leave its end before its start
  (PLAN-53's «version with no day»). A version that was already closed (inactive with an end) keeps its end and gets no second
  snapshot.
- **D20 — The applier's signature.** `ApplySystemChangeAsync` keeps its seven arguments; an overload adds
  `PayProfileContractActivation(FirstVersionIsActive, HistoryReason)`, honoured for `ContractActivated` only (any other source with
  it is refused). With it: no profile in force is needed, the proposal may be empty, and the only date rule is `StartsInside` (no day
  of a version in force before its start, no day of a closed version).
- **D21 — The refusal kinds of a contract activation, each recorded:**
  1. the start date is before the start of the version in force (`BeforeCurrentVersion`) → review hold `PAY055-ACTIVATION`, consumed;
  2. the start date is on or before the last day of a closed version (`InsidePreviousVersion`) → the same hold, consumed;
  3. a concurrent save (`Conflict`) → thrown; MassTransit's retry delivers it again and it meets the settled state (not a loop: the
     retry policy is bounded, and a contract already applied changes nothing);
  4. `SalaryNotPositive`, `GradeNotFound`, `SourceReferenceTooLong` cannot occur: the handler proposes a salary only when it is
     positive, a grade it read from the database, and a contract id of 36 characters.
  The hold is PAY-029's pattern (a `Manual` hold with no run, the rule code at the start of its reason, its alert); one per employee.
- **D22 — A message delivered twice:** the handler looks first for an `Applied` request of this employee with source
  `ContractActivated` and this contract's id; when there is one it does nothing (no second adjustment, hold, version or event).
- **D23 — `Pay029PayProfileTests.cs` is untouched.** It builds the handler from a repository and the options; the handler then uses
  `PayProfileChangeApplier.Detached` (default rules, system calendar, no bus: no event). The host always injects the registered
  applier: `ContractActivatedConsumerTests` (the service's real registrations) and the PostgreSQL tests assert the staged event.
- **D24 — A negative agreed salary** was written as it came on a first activation; it is now 0 (unpriced) with PAY-029's range hold
  when a grade is known. Not a PAY-029 behaviour a test or the plan names; recorded.
- **D25 — A late `IEmployeeCreated`** fills the unit and the regime of the employee's current profile (in force, else the latest)
  where they are empty. It never overwrites.

- **D26 — The repair adds no writer of pay profiles.** It lists (reads only) and, for a real run, sends the named writer's own
  command per listed profile: `HandleEmployeeCreatedHandler` fills the unit and the regime of the employee's current profile where
  they are empty and never overwrites. So `PayProfileWritersTests.AllowedWriters` stays at six, one fill rule exists, and a second
  run finds nothing. A real run that changed something writes one audit entry (`PayProfileOrgUnitBackfill`: the operator, the
  profiles) and one security audit event.
- **D27 — The repair's policy is a new `Payroll.Repair`** (roles from configuration, `hr-admin` by default). No existing
  administrators' policy fits Payroll (`HrCore.Delete` is HR.Core's, `Messaging.Repair` admits the platform administrator).
- **D28 — `[OrgScopeCentralOnly]` on the repair** is proved with the policy's roles configured to include a restricted role: an
  `hr-admin` is never branch-restricted (`HrOrgScopeVisibility`), so «a restricted administrator» does not exist.
- **D29 — The repair copies from the employee's own history only.** A profile whose empty unit or regime no profile of the same
  employee carries is reported in `notFillableProfileIds` and left alone: the repair invents no value.

- **D30 — The plan's concurrency test, as it can exist.** The plan words it «two concurrent verifications of two accounts of the
  same employee». One employee never has two accounts waiting (the index allows one), so two verifications at once are of the same
  account: `xmin` decides (proved deterministically with a stale read, and with six sessions at once: one wins, five are 409). The
  index decides what the handlers' own reads cannot see: six registrations at once leave one waiting account, and a second row in
  force or waiting written directly is refused by `IX_EmployeeBankAccounts_OneActiveOnePending`. D3 is proved: sixteen replacements
  in a row, each retiring the account in force and activating the new one in one save.

- **The independent review (P6).** One fresh read-only subagent read the prompt, the plan, the decision log and the diff, beside the
  image build (it built and ran nothing). No refuter was used: both high findings were verified against the code they name. What it
  found and what was done (commit `31f3cc00`; every fix has a test):
  1. **High, fixed.** The treasury file was produced and submitted while a retired pinned account's hold was active (the hold only
     stopped the batch). Now the file and the disbursement gate both refuse a line whose pinned account was retired and that no
     person released (`BankAccountPinning.RetiredAndNotReleasedAsync`): an active `ACCOUNT-RETIRED` hold, or no released one. A
     released hold is a person's decision to pay the pinned account. The PostgreSQL proof now shows the refusal, the four-eyes
     release, and then the file.
  2. **High, fixed.** A retirement between the run's snapshot and the writing of its line placed no hold (the hold looked at lines
     only). The hold now also covers a run whose snapshot pinned the account while the employee has no line in it yet; and the
     gate/file rule of (1) refuses a retired pinned account with no hold at all (the safety net).
  3. **Medium, fixed in part.** «Disbursed» (D1) is now: a payment order that left for the channel **in a batch the channel did not
     reject, return, fail or reverse** (such a batch may be submitted again to the same account). The other half stays and is
     recorded for PLAN-58: a run paid by the treasury file alone has no payment order, so it counts as not yet disbursed and a later
     retirement holds it (nothing records that a treasury file was paid until the disbursement path is unified).
  4. **Medium, fixed.** The contract activation threw a bare `InvalidOperationException` on a concurrent save, which the bus sends
     to the error queue without a retry. It now throws `DbUpdateConcurrencyException`, which `MessagingFaults.IsTransient` retries
     (D21.3 corrected).
  5. **Medium, texts fixed; the rest is PLAN-58's.** Releasing an `ACCOUNT-RETIRED` hold pays the retired account the line pinned,
     and one held employee blocks the whole batch. The hold's reason, the run's card, the notice and the file's refusal now say so,
     and say that recalculating a run before it is frozen pins the account in force. An approver's action that re-pins a frozen
     line, or leaves the employee out of the batch, belongs to PLAN-58's disbursement path.
  6. **Medium-low, fixed.** A second refused activation of the same employee was consumed with nothing recorded (one hold per rule).
     The refusal hold is now one per refused contract (the contract's id in its reason).
  7. **Low-medium, narrowed.** `IEmployeeCreated` and the first `IContractActivated` handled at the same moment both read no
     profile. The event now reads again after it writes its placeholder and fills the activation's version. Two transactions that
     are both still open do not see each other: what is left then is exactly what the repair fills, from that placeholder.
  8. **Low, fixed.** The repair: one transaction for the fills and the audit entry; a real run given the dry run's list changes
     nothing outside it (the script always does a dry run first and sends its list); it is refused (409) while a calculation of the
     agency is pending, running or paused (a filled profile changes the version a snapshot compares).
  9. **Low, fixed.** The verification page showed the whole number to the registrar and for accounts with no registrar: now only
     to a person who can verify.
  10. **Low, fixed.** A bank's pattern with alternatives was matched at its first alternative: now anchored whole; and the service
      refuses to start on a bank list it cannot use (no code or name, a code twice, a pattern that does not compile).
  11. **Low, fixed.** `EmployeePayProfiles/Index.cshtml` had been rewritten whole by an edit that normalised its line endings:
      restored from `main`, one line added.
  12. **Low, fixed.** The readiness report said «held» from the account's status alone: it now reads the holds
      (`employeesHeldForRetiredAccount`), and the card shows the held and the released separately.
  13. **Low.** The hold's reason no longer carries the account's id (fixed). The framework's English answer to a malformed JSON
      body is the same on every API of the system: not changed here, recorded.

## Notes for later phases (so documents are not read twice)

- Patterns to follow: `PayProfileChangesApiController` (antiforgery endpoint, `[OrgScoped]`, policies per action, `PayProfileChangeHttp`),
  `ManualInputCommands` (the actor in `CreatedBy`, the decider is not the creator, `UnknownActor`), `PayProfileChangeResult` (failure
  kind → HTTP status), `WF044` (lifting `FORCE` inside a migration), `PAY054` (`TenantRlsMigrationHelper.EnableRls`).
- `Pay029PayProfileTests` builds `HandleContractActivatedHandler(repository, options)` with two arguments: P3 keeps that constructor
  working so the file stays untouched.
- Tests that name the profile's bank fields (they change in P2, when the fields become obsolete): see the baseline's first table.
- `AcIam53SalaryChangeTests` sets the bank fields by SQL and asserts they are carried: P2 changes it to the fields a version still owns.
- CI's Testcontainers filter: `Category=Testcontainers&Category!=ObjectStorage` (new PostgreSQL classes need that category).

## Files changed

- P1 (38 files): `Payroll.Domain` (`EmployeeBankAccount`, `BankAccountEnums`, `BankAccountNumberValidator`, `BankAccountId` on
  `SalaryLine` and `PayrollRunInputSnapshot`, the index name), `Payroll.Application/Features/BankAccounts/` (commands, rules,
  result, messages, options), the repository and DI, `PayrollDbContext`, `PayrollOrgScopeKeys`, migration
  `20261124052855_PAY055_EmployeeBankAccounts`, `EmployeeBankAccountsApiController`, `EmployeeBankAccountsController` with four views,
  the navigation and the pay profile list's link, `Payroll:Bank:Banks` empty in the three settings files and mirrored in
  `docker-compose.acceptance-prodlike.yml`; tests `Pay055BankAccountNumberValidatorTests`, `Pay055BankAccountTests` (+ harness),
  `Pay055MigrationPostgreSqlTests`, `Pay055BankAccountHttpTests`, rows in `PayrollWriteActionsTests`; changed
  `Iam053PayrollScopeHttpTests` (seeds an account per unit: the registry names the type now) and `OutboxAtomicityIntegrationTests`
  (its repository wrapper gets the new member).

- P0: `reports/plan54-progress.md` (the cherry-picked merge note), `docs/acceptance-plans/execution/PLAN-55-AGENT-PROMPT.md`,
  `reports/plan55-baseline.md`, `reports/plan55-progress.md`.

- P2 (26 files): `BankAccountPinning.cs` (the active accounts at a snapshot, the pinned numbers of lines, the readiness query, the
  treasury refusal), `PayrollRunJobHandlers` (the snapshot pins), `PayrollEmployeeCalculationUnit` and `PayrollRunParallelCalculator`
  (the line copies), `CorrectionRunHandlers`, `PaymentOrderService`, `PayrollExternalExportService`, `FraudScanInputBuilder` and
  `RunPayrollFraudScanCommand`, `DsarInternalController`, `PayrollApiController` (`GET runs/{id}/bank-readiness`),
  `PayrollRunsController` and `Views/PayrollRuns/Details.cshtml` (the card), `EmployeePayProfile` (three `[Obsolete]`),
  `EmployeePayProfileVersioning` (no longer copies them); tests `Pay055PinningTests`, `Pay055BankAccountPostgreSqlTests` (pinning,
  DSAR), `Pay055TestAccounts`. **Existing tests changed, each keeping its meaning:** `Iam053SalaryChangeHarness`,
  `Iam053SalaryChangeStateMachineTests`, `Iam053SystemChangeAndCalendarTests`, `Iam053PayProfileChangePostgreSqlTests.Transaction`
  (the next version carries every field a version still owns: unit, regime, grade — the bank account is no field of a version);
  `Iam053ApplierCopyForwardTests` (every property is copied or named; the three obsolete columns are named and left empty);
  `Frd001PaymentBlockIntegrationTests` and `Pay054V2RunPostgreSqlTests` (the verified account is planted as the account entity and
  pinned by the run, instead of two profile fields); `AcIam53SalaryChangeTests` (carried fields: unit, regime, grade).

- P3 (9 files): `EmployeePayProfileVersioning` (`CreateNextVersion`, `PreviousOf`, `IsPlaceholder`, `StartsInside`,
  `PayProfileVersionChange`), `PayProfileChangeApplier` (versions through `CreateNextVersion`; the activation overload; `Detached`),
  `PayProfileChangeMessages` (two messages), `InboundEventHandlers` (`HandleContractActivatedHandler` through the applier,
  `HandleEmployeeCreatedHandler`); tests `Pay055ProfileVersioningTests` (36), two event-order tests in
  `Pay055BankAccountPostgreSqlTests`, the branch-list test in `Pay055BankAccountHttpTests`. **Existing tests changed:**
  `PayProfileWritersTests` (the contract handler leaves the list, six writers at most, creation only in `CreateNextVersion` and the
  seed, the versioning's callers are the applier and the new employee's first profile), `ContractActivatedConsumerTests` (its
  hand-built host gets a bus double and an empty configuration, as a service has; it now also asserts the applied request and its
  event). `Pay029PayProfileTests` unchanged.

- P4 (10 files): `Features/PayProfileRepair/BackfillProfileOrgUnit.cs`, `PayrollAdminController`
  (`POST api/payroll/admin/backfill-profile-orgunit?dryRun=true`, `GET …/antiforgery`), the policy `Payroll.Repair`
  (`HrManagementRoles.cs`, `HrManagementAuthorizationExtensions.cs`), `scripts/pay055-backfill-profile-orgunit.ps1`; tests
  `Pay055ProfileRepairTests`, two tests in `Pay055BankAccountHttpTests`, rows in `PayrollWriteActionsTests`.

- P5 (1 file): four tests added to `Pay055BankAccountPostgreSqlTests` (two verifications at once; two registrations at once and the
  index; sixteen replacements in one save each; a retirement racing a replacement's verification).

## Server changes and their undo

None yet (P0 only read).

## Open issues

None.

## The exact next step

P8, the demo server. `origin/main` = `12b83970` (unchanged); the server ran `014db7e3a01a` with 24 GB free at P0. In order: check ssh
(`whoami` = `hrm`) and that the running tag is on `origin/main`; `pg_dumpall` to `/opt/hrm/backups/pre-plan55-<utc>/` and verify it;
free the disk to 30 GB (build cache, local images of older releases; each removal recorded with its undo); commit
`AcPay55VpsChecks.cs`, then build once, detached, on that commit; `scripts/vps/deploy.ps1 -Tag <sha12>`; the repair's dry run, then
its real run of that list (expected: 425 not fillable, 0 changed); the five PLAN-55 server checks
(`artifacts/plan55/run-vps.ps1`, with `PAY055_VPS_PROFILES_MD5=f409f25a00f0f1ecafb418e9bf6c93e4` and
`PAY055_VPS_PROFILES_AMOUNTS_MD5=7c1359efefdbdd1a637f62393f9411c5`); the PLAN-36..54 server checks and the smoke. Local stacks: all
down; `hr-migrate:local` (5.13 GB) is still here for a rerun and is removed at the end.
