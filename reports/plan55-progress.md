Started (UTC): 2026-11-24T05:14:03Z

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
| P4 the repair | next | |
| P5 PostgreSQL proofs | | |
| P6 acceptance, review | | |
| P7 main | | |
| P8 demo server | | |
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

Stops: none so far.

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

## Server changes and their undo

None yet (P0 only read).

## Open issues

None.

## The exact next step

P4: the repair. `POST api/payroll/admin/backfill-profile-orgunit?dryRun=true` (a new `PayrollAdminController`, `[OrgScopeCentralOnly]`,
policy `Payroll.Repair` = hr-admin, antiforgery on the POST, a `GET …/antiforgery`), a service in Application that lists and fills
(one agency per call, never overwrites, one audit entry, idempotent), `scripts/pay055-backfill-profile-orgunit.ps1`; rows in
`PayrollWriteActionsTests`; tests. Then the full unit suite (not run since P1).
