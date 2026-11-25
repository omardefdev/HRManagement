# PLAN-55 — final report

The employee's bank account, and pay profile versions that keep the unit and the regime (PAY-B).
Plan: `docs/acceptance-plans/PLAN-55-pay-bank-account-pay-profile.md`. Prompt: `docs/acceptance-plans/execution/PLAN-55-AGENT-PROMPT.md`.
Rules for the plans that follow: `docs/PAYROLL_RULES.md` (the account, its pinning) and `docs/PAYROLL_SALARY_CHANGE.md` (the versions).
Checkpoint with every stamp and decision (D1–D30): `reports/plan55-progress.md`.

## 1. Summary per phase

| Phase | What was done | Result |
|-------|---------------|--------|
| P0 | Baseline; every reader and writer of the profile's bank fields; every writer of a pay profile; what «disbursed» means in the code; the demo server's picture (read-only) | `reports/plan55-baseline.md` |
| P1 | `EmployeeBankAccount`, the number validator, the four commands with the separation of duties in the handlers, the API and the pages, migration `PAY055_EmployeeBankAccounts` | PostgreSQL tests as a non-superuser under forced row security in two agencies |
| P2 | A run pins the account at its snapshot and on its line; payment orders, the treasury file, FR-04 and the DSAR answer read the pinned account or the entity; the readiness report; the retirement hold | determinism and fraud tests unmoved |
| P3 | `CreateNextVersion` is the one place a profile is made; the contract activation goes through PLAN-53's applier; PAY-029 kept; the late `IEmployeeCreated` fills | `Pay029PayProfileTests.cs` untouched; writers list 7 → 6 |
| P4 | The repair: an administrators' action, dry run first, and its script | unit and HTTP tests |
| P5 | PostgreSQL proofs: concurrent verification and registration, both orders of the two events, a real run to its orders and its treasury file | Testcontainers step 282 passed |
| P6 | AC-PAY-55a..e on the prod-like stack; the independent review (13 findings) and its fixes; the regression stages (prod-like, lab, leastpriv); the seeded counts | see §10 |
| P7 | `origin/main` did not move during the session (`12b83970`) | nothing to merge |
| P8 | The demo server: backup, one build, deploy, the repair, five server checks, the older plans' checks | see §11 |
| P9 | Docs, this report, the local gates, the release branch, the PR, CI, the merge | see §10 and §14 |

## 2. Commits

On `feature/plan-55-pay-bank-account-pay-profile`, from `origin/main` `12b83970`:

| Commit | What |
|--------|------|
| `07fe4506` | PLAN-54's merge note (cherry-picked from `docs/plan-54-merge-note`) |
| `ad3b4e69` | this plan's prompt, as the owner pasted it into the session |
| `607380c6` | P0: baseline, inventories, the demo's picture |
| `c0c39ca2` | P1: the account, its two people, the migration |
| `21e2ca85` | P2: the pinning and its readers |
| `14690e18` | P3: the versions; the contract activation through the applier |
| `c8ba051c` | P4: the repair |
| `ccb72bfa` | P5: PostgreSQL proofs |
| `31349e50` | AC-PAY-55's class with its scope, ownership and personas; `pay055-validate.ps1`; AC-UX-67's key pages |
| `31f3cc00` | the review's fixes |
| `7702cc88` | 55d reads the orders' status as stored; the repair script's gateway path |
| `ea0d97a6` | P6's record (the acceptance jobs) — **the commit the demo server runs** (`ea0d97a6ca26`) |
| P8, P9 | the server check class and the script's session sign-in; the documents and this report (the two commits after `ea0d97a6`; no product code) |

87 files, about 12,900 lines added and 180 removed up to `ea0d97a6`; no file of another service's product code but the shared
policy name (`HrManagementPolicies.PayrollRepair`).

## 3. Decisions applied and deviations

**The owner's decisions, applied as written:** D-PAY-11 (an IBAN always, after ISO 13616's check; a local number only for a bank
defined in `Payroll:Bank:Banks`, empty in Production, Staging and the prod-like mirror), D-PAY-14 (the processing policy
registers; the approval policy, never the registrar, verifies with the bank's reference), D-PAY-15 (the run pins the account at
its snapshot).

**Deviations from the plan's text. None defers a part of the plan.**

| # | The plan | What was built, and why |
|---|----------|-------------------------|
| 1 | 403 outside the caller's branch | **404** for an employee or account outside the scope or the agency (PLAN-52's rule, P-9): the existence of an account is not revealed |
| 2 | «Retired» as a state | **Two ways to it:** the account in force is retired in the same save in which its replacement is verified, and an approver may retire it alone with a reason |
| 3 | no policy named for reject and retire | **`Payroll.Approve`** for both (P-4): retiring the account in force can leave an employee with no destination. The registrar may reject his own waiting registration: a withdrawal activates nothing |
| 4 | two filtered unique indexes | **One**, on (agency, employee, status) for the two live states: a verification updates two rows in one save, and with the status inside the index EF orders the two updates. Proved on PostgreSQL with sixteen replacements in a row (D3) |
| 5 | the registrar's own verification | **400**, as AC-PAY-55a words it (PLAN-53 answers 403 for its own self-decision). On the demo the processor does not hold the approval policy, so the policy answers 403 first; the handler's 400 is shown there by the administrator, who holds both |
| 6 | «two concurrent verifications of two accounts of the same employee» | One employee never has two accounts waiting (the index). The test is what can exist: two verifications of the same account (`xmin`: one wins, the rest 409), six registrations at once (one stays), a second live row written directly (the index refuses) (D30) |
| 7 | the migration moves «each profile with a number» | **One account per employee and agency** (PLAN-53's copy-forward put the same number on every later version), `PendingVerification`, with no registrar — so it can only be rejected and registered again (D4, D5) |
| 8 | the kind of identifier as an enum in the body | **By name** (`"Iban"`, `"Local"`): the service has no string-enum converter and a number in a body says nothing |
| 9 | the retirement hold | A `Manual` hold with **no fraud alert** (a retired account is not a suspicion), one per employee and run. **Stronger than the plan after the review:** while it is active neither the batch nor the treasury file goes out, and it also covers a run whose snapshot pinned the account before the line was written |
| 10 | the repair «copies from the employee's latest profile» | The same, **through the named writer** (`HandleEmployeeCreatedCommand` per listed profile): no new writer of pay profiles. New policy `Payroll.Repair` (no existing administrators' policy fits Payroll) |
| 11 | the repair script signs in by password | Also by **a bearer token or a signed-in session's cookie**: Production and the demo keep the password grant closed (found at P8, fixed in the script; §7) |
| 12 | a negative agreed salary on a first activation | Was written as it came; it is now 0 (unpriced) with PAY-029's hold. Not a behaviour a test or the plan names (D24) |

## 4. The account

**States.** `PendingVerification` (registered by a processor, or moved by the migration) → `Active` (verified by another
person) or `Rejected` (with a reason); `Active` → `Retired` (by an approver with a reason, or when its replacement is verified).
Nothing leaves `Rejected` or `Retired`; an account is never deleted.

**Rules.**
1. No `Active` account without a second person and the bank's reference. The handler compares the actor with the registrar the
   command recorded (`CreatedBy` is set from the actor, never left to the audit interceptor); an act with no known actor, and an
   account with no known registrar, are refused.
2. A change of account is a new registration. The account in force stays in force until its replacement is verified; then both
   rows change in one save.
3. A broken input rule is 400 with an Arabic reason and no row: a missing number, a bad IBAN form (2 letters, 2 digits, letters
   and digits, 15 to 34 characters), check digits outside 02..98 or mod 97 ≠ 1, a local number with no bank or an undefined bank,
   a local number that does not match its bank's whole pattern, a missing bank name or reference or reason, texts over their
   lengths. No length per country is assumed: Syria is outside the IBAN registry.
4. One `Active` and one `PendingVerification` per employee and agency, by a unique filtered index and by the handlers' reads; a
   concurrent decision loses with 409 (`xmin`).
5. The answers are the same in the API and the pages: 400, 409, 404 (§3).

**Policies** (`PayrollWriteActionsTests`): register `Payroll.Process`; verify, reject, retire `Payroll.Approve`; the lists and the
readiness report `Module.Payroll`; the repair `Payroll.Repair`. Every API `POST` validates an antiforgery token
(`GET api/payroll/bank-accounts/antiforgery`). Branch scope: `[OrgScoped(typeof(EmployeeBankAccount))]` through the employee's
active profile; a central user sees every unit.

**Masking.** `MaskedNumber` (bullets and the last four characters) is all the API, the lists and every page show. The whole
number is shown in one place: the approver's verification page, to a person who can verify the account, with `no-store`. A
refused registration form comes back without the number it posted.

**Audit.** The number and the holder's name are `[AuditRedact]` in the entity audit rows. Each act is also a `PayrollOverride`
security audit event that names the account's id and the act. The number is in no log line, error message, hold reason, event or
URL (AC-PAY-55a and the server check search the audit rows for it and find none).

**The bank list.** `Payroll:Bank:Banks` (`Code`, `NameAr`, `AccountRegex`), bound with `ValidateOnStart`: the service refuses to
start on a list it cannot use. Empty in Production, Staging and the prod-like stack; the local path is proved by unit tests.

## 5. The pinning

| Reader | What it took before | What it takes now |
|--------|---------------------|-------------------|
| The run's snapshot (`PayrollRunJobHandlers`) | — | the employee's `Active` account at the snapshot → `PayrollRunInputSnapshot.BankAccountId` |
| The salary line (`PayrollEmployeeCalculationUnit`) | — | the snapshot's account → `SalaryLine.BankAccountId`; a correction run pins the account in force when it is computed |
| Payment orders (`PaymentOrderService`) | the active profile's verified IBAN | the line's pinned account (`BeneficiaryIban`; a local number goes there as it is) |
| The treasury file (`PayrollExternalExportService`) | the profile's IBAN, refusing with a count | the line's pinned account; refuses naming the employees (up to 25 names and a count), first for a retired pinned account nobody released, then for a missing one |
| FR-04 (`FraudScanInputBuilder`) | the profile's IBAN | the pinned accounts' numbers: one account shared by several employees of the run |
| The disbursement gate (`PaymentDisbursementGate`) | holds | holds, and a retired pinned account that no person released |
| The DSAR answer | the profile's fields | the entity: present (in force or waiting), verified (in force) |

The account is not an input of the amount: it is in neither `InputFingerprint` nor `ProfileVersion`, so the determinism tests,
AC-21 and every golden number did not move.

**The readiness report** (`GET api/payroll/runs/{id}/bank-readiness`, and the card on the run's page): lines, lines with and
without a pinned account, the employees without one (ids in the API, names on the page), the employees whose pinned account was
retired since, and which of those are still held (read from the holds). A line without an account blocks neither the calculation
nor the approval.

**The holds.** A retirement places one `Manual` hold per run on the employee (`ACCOUNT-RETIRED:` at the start of its reason, no
account id or number in it) for every run that pinned the account and has not disbursed his line. «Disbursed» is a payment order
that left for the channel in a batch the channel did not reject, return, fail or reverse. A reversed or cancelled run gets none.
While the hold is active nothing is paid to the retired account and the file is not produced; the existing four-eyes release is
a person's decision to pay the account the line pinned (the texts say so, and say that recalculating a run before it is frozen
pins the account then in force).

## 6. The versions

- **`EmployeePayProfileVersioning.CreateNextVersion`** is the one place a pay profile is made (the demo seed apart;
  `PayProfileWritersTests` and `pay055-validate.ps1` refuse a `new EmployeePayProfile` anywhere else). It starts from the profile
  in force, else the employee's latest, else makes the first. It carries the unit and the regime always; the salary and the grade
  only from a version in force (D18). It closes an open previous version the day before the new one starts and writes the history
  snapshot. The three old bank fields are `[Obsolete]`, not copied, and named in `DeliberatelyNotCopied`.
- **The contract activation** (`HandleContractActivatedHandler`) no longer writes a profile: it calls
  `ApplySystemChangeAsync(…, PayProfileContractActivation, ct)` with source `ContractActivated` and the contract's id, so every
  activation is an `Applied` request row with its event, in the same save as the version, the zero adjustment and any hold. The
  boundary day is no longer counted twice.
- **PAY-029 kept**, with `Pay029PayProfileTests.cs` untouched: no known salary → an unpriced profile, inactive under
  `Payroll:RequireBaseSalaryOnActivation`, with the `PAY029-NOSAL` review hold; a salary outside the grade's bounds → accepted with
  the `PAY029-RANGE` hold.
- **The refusals and their holds.** An activation dated before the start of the version in force, or inside a closed version, is
  not applied: one review hold `PAY055-ACTIVATION` per refused contract (the refusal in its alert), and the message is consumed. A
  concurrent save is thrown as a concurrency exception, which the bus delivers again. The same contract delivered twice changes
  nothing the second time.
- **The late `IEmployeeCreated`** fills the empty unit and regime of the employee's current profile and overwrites nothing; it
  reads again after it writes its placeholder and fills a version an activation wrote at the same moment. Both orders of the two
  events are proved on PostgreSQL.
- `PayProfileWritersTests.AllowedWriters` went from seven to six (the contract handler left); the list only shrinks.

## 7. The repair

`POST api/payroll/admin/backfill-profile-orgunit?dryRun=true` (policy `Payroll.Repair`, central only, the caller's agency, an
antiforgery token) and `scripts/pay055-backfill-profile-orgunit.ps1`. For each active profile with an empty unit or regime it
takes the value from the employee's latest profile that carries it; it never overwrites, never invents, touches nothing else. A
real run is one transaction with one audit entry, is given the dry run's own list by the script and changes nothing outside it,
is refused while a calculation of the agency is in progress, and a second run finds nothing.

| Where | Found | Changed |
|-------|-------|---------|
| Unit and HTTP tests | planted profiles: filled from the employee's own history; a profile with no source reported and left | as listed, once |
| The prod-like stack, agency A (the seed) | 0 fillable, 1,102 not fillable | 0 |
| **The demo** (2026-11-24T22:38Z) | 425 active profiles with no regime, none with no unit; **0 fillable, 425 not fillable** | **0** |

On the demo no employee has an earlier profile that carries a regime (the demo's seed never wrote one), so there is nothing the
repair may copy. Giving those 425 profiles a regime is a data decision, not a repair.

**Found at P8 and fixed:** the script asked Keycloak for a token by password; Production and the demo keep that grant closed. It
now also takes `-AccessToken` / `HRM_REPAIR_ACCESS_TOKEN` or `-CookieHeader` / `HRM_REPAIR_COOKIE` (a signed-in session), as
PLAN-40's script does. The demo's runs used `hradmin`'s session.

## 8. The existing tests that changed, and the numbers that did not move

| Test | What changed | The meaning it keeps |
|------|--------------|----------------------|
| `PayProfileWritersTests` | the allowed writers are six (the contract handler left); creation is allowed in `CreateNextVersion` and the seed only | no undeclared writer of a pay profile; the list only shrinks |
| `Iam053ApplierCopyForwardTests` | the three bank fields left the copied set; only `[Obsolete]` members may join the not-copied identity set | a new field of the profile is copied or deliberately named |
| `Iam053SalaryChangeHarness`, `…StateMachineTests`, `…SystemChangeAndCalendarTests`, `Iam053PayProfileChangePostgreSqlTests.Transaction` | the seed profile has no bank fields; the assertions on them are gone | the new version carries the salary, the unit and the regime of the old one |
| `Iam053PayrollScopeHttpTests` | its sweep of scoped routes now includes the two bank account controllers, with a waiting account another user registered | every scoped payroll route answers 404 outside the caller's branch |
| `PayrollWriteActionsTests` | rows for every new write action (the accounts' API and pages, the repair), with who reaches each role by role | every write action names its policy |
| `ContractActivatedConsumerTests` | the host gets a bus double and an empty configuration; asserts the applied request and its event | the consumer's real registrations handle an activation |
| `Frd001PaymentBlockIntegrationTests`, `Pay054V2RunPostgreSqlTests` | the verified account is an entity the run pins, not profile fields | a held employee is not paid; a v2 run exports what it computed |
| `OutboxAtomicityIntegrationTests` | its counting repository implements the new member | unchanged |
| `AcIam53SalaryChangeTests` | the carried fields are the unit, the regime and the grade (the bank fields left) | a salary change keeps who the employee is |
| `Ux067PageProbe` | three key pages added | every key page under one policy with no violation |

**Not touched, and green:** `Pay029PayProfileTests`, `Gov002LegalGoldenPackTests`, `PayrollEnginePipelineTests`, the determinism
tests, `Pay023FrozenRunIsolationTests`, the `Frd001*` unit tests, AC-21, AC-23, AC-24, AC-25, AC-HCM-25a..h and
`Hcm025PanelServerParityTests`. No payroll amount moved.

## 9. The guards and their own negative tests

- `PayProfileWritersTests`: a writer outside the list, and a `new EmployeePayProfile` outside `CreateNextVersion`, fail it (its
  own planted-source tests).
- `PayrollWriteActionsTests`, `OrgScopeCoverageTests`, `InterfaceTextGuardTests`: the new controllers and pages are inside them,
  with no row added to any ceiling list.
- `scripts/pay055-validate.ps1` — six checks: no reader of the three obsolete fields; no profile made outside its place; the
  migration has no byte order mark, lifts and restores `FORCE` around its data move, and makes moved accounts pending; the bank
  list empty in Production, Staging and the prod-like mirror; an antiforgery token on every `POST` of the three new controllers;
  `AcPay55BankAccountTests` required in `prodLike[]`. **`-SelfTest`: 27 planted defects, each refused.**
- `Pay055MigrationPostgreSqlTests`: the migration as the migrator's role, a non-superuser under forced row security: a planted
  number in two agencies becomes one pending account each, in its own agency; without the lifted `FORCE` it would move nothing.

## 10. Results

| Suite | Result |
|-------|--------|
| Build `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors |
| Unit (final code, 2026-11-25T00:19Z) | 6,950 passed, 7 min 36 s |
| Architecture | 401 passed |
| Integration, CI's Testcontainers filter on the host (final code, 2026-11-24T22:04Z) | 282 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`, which needs the migrator image CI builds), 12 min 51 s |
| `has-pending-model-changes` (Payroll) | none |
| `pay055-validate.ps1` and `-SelfTest` | pass |
| Acceptance, prod-like, fresh stack (the journey, `main`, `isolated`: 14 classes) | **PASS** — main 36 of 36 with **AC-PAY-55a..e**, AC-IAM-51/52/53, AC-PAY-54, AC-HCM-25; the journey 1; the isolated group 39; guard PASS |
| Acceptance, AC-UX-67 (by hand with the job's environment) | 7 of 7; the three new key pages 200, one policy, no violation |
| Acceptance, lab (AC-21, AC-23, AC-24, AC-25, AC-HCM-29, the lab journey) | **PASS** — 7 and 1; guard PASS |
| Acceptance, leastpriv (`AcOps38FirstBootTests`, `AcTen48LeastPrivilegeTests`) | **PASS** — 9 of 9: PAY055 applied by the migrator's role from empty volumes |
| Seeded counts by psql on a fresh stack | the 32 tables of `acceptance-verify.ps1` as expected; no account seeded |
| The demo server | this plan's five checks passed; the older plans' checks (PLAN-36..54): 62 of 63 — 61 in one run of 73 minutes, the sweeps check on a rerun after a sweep cycle; the one not passed is `AC_POL_47_vps_the_migration…`, which reads a file of the formatted Windows machine (PLAN-53 recorded it); smoke with `-RequireIntegrationWiring` 50 pass, 0 fail |
| Local gates before the push | the local CI steps in a fresh clone of this report's commit, then `plan035-prepush.ps1`: their result, with every step not run named, is in the merge note of `reports/plan55-progress.md` |

**The evidence of record** is the artifact of the pull request's Acceptance run; its link, each check's result, the merge commit
and the post-merge run are in the merge note of `reports/plan55-progress.md` (this report is committed before the push, as the
local gates need). No local evidence is committed.

**AC-PAY-55, what each scenario proves:** *a* — a valid IBAN by the processor is pending; the approver verifies it with a
reference; the registrar's own verification 400; a wrong check digit 400 and no row; a second waiting account 409; the number
masked and in no audit row. *b* — a salary change through PLAN-53's request and approval (in agency B) gives a version with the unit
and the regime of the one before. *c* — two months: the first run pins the first account; the account is replaced; the second run pins
the new one and the first run's orders still carry the old one. *d* — a pinned account retired before the disbursement holds the
employee; the batch and the file are refused until a second person releases the hold. *e* — another agency's processor and
approver get 404 on the employee, the account and every act.

## 11. The server

**Before** (P0's picture, 2026-11-24T05:16Z): tag `014db7e3a01a`, 22 containers, 24 GB free; 425 pay profiles, all active, none
with a bank field, none with an empty unit, 425 with an empty regime and no earlier profile to copy from; no payment order, no
hold; runs 1 / 4 / 1 by status.

**After** (2026-11-24T22:52Z): tag `ea0d97a6ca26`, 22 containers, every health check passing, smoke 50 pass and 0 fail; migration
`PAY055_EmployeeBankAccounts` applied; the same 425 profiles with the same two checksums; no line or snapshot pins an account;
four test accounts of the checks, none in force or waiting.

| # | UTC | Change | Undo |
|---|-----|--------|------|
| 1 | 21:52:54 | Backup `/opt/hrm/backups/pre-plan55-20261124T215254Z` (700; files 600): `pg_dumpall.sql` 38,262,947 bytes, `env.backup`, `SHA256SUMS`; verified (sums, 17 `CREATE DATABASE`, complete); later the repair's reports and undo lists | delete the folder when not needed |
| 2 | 21:53 | Local images of `5aa61b78e3a6` and `83b99786a420` removed (23 → 35 GB free; the line is 30); both stay in the loopback registry | `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` |
| 3 | 21:54–22:31 | One detached build of `ea0d97a6ca26`: upload 120 s, build 2,037 s, push 40 s, 17 of 17 images; disk 35 GB before, 8.8 GB at the lowest, 27 GB after | none needed |
| 4 | 22:32:02–22:35:47 | Deploy (previous `014db7e3a01a`): pull 3 s, up 131 s, bootstrap 60 s with PAY055, reconcile 13 s, healthy 14 s; away about 2.5 minutes at most | `scripts/vps/deploy.ps1 -Tag 014db7e3a01a` |
| 5 | 22:38 | The repair: dry run, then the real run of its (empty) list; nothing changed | none needed |
| 6 | 22:39–22:52 | The checks left four test accounts of three employees: one `Retired`, three `Rejected` | none needed (records) |

**This plan's five server checks** (`AcPay55VpsChecks`, evidence under `artifacts/plan55/vps/`, not committed): the migration;
an account registered by one person, verified by another and retired; a second account its registrar cannot verify, rejected;
the readiness report of an existing run (425 lines, all without an account, read-only); seven screens in a real Chromium at two
sizes — one CSP policy, no violation, Arabic, names by the registry, the number masked everywhere but on the approver's
verification page. No account the checks make stays usable.

**The rollback note** (`docs/OPS_VPS_SINGLE_HOST.md`): PLAN-54's tag, read with `git show`, never reads the accounts table or the
new columns; it reads the profile's bank fields again (empty on the demo, as before); its contract activation writes versions
without unit and regime again (the repair fills them after the return); a retirement hold is an ordinary manual hold to it.
No SQL and no restore are needed. On a server that has accounts in force, no payment order or treasury file should be made
while the older release runs.

## 12. Changes for API clients and operators

- **New API** (through the gateway, `api/payroll/…`): `GET|POST employees/{employeeId}/bank-accounts`;
  `POST employees/{employeeId}/bank-accounts/{id}/verify|reject|retire`; `GET bank-accounts/antiforgery`;
  `GET runs/{id}/bank-readiness`; `GET admin/antiforgery`; `POST admin/backfill-profile-orgunit?dryRun=true|false` (optional body
  `{ "profileIds": [...] }`). Every `POST` needs the antiforgery header. The account's kind is sent by name. Answers: 201/200, 400,
  404, 409 with `{ error, accountId, status }`.
- **Changed for a client:** a payment order's `BeneficiaryIban` and the treasury file now come from the account the run pinned;
  an employee with no verified account at the run's snapshot has none, whatever was on his profile. The profile's
  `BankAccountIban`, `BankName` and `BankAccountVerifiedAt` are no longer read or written (the columns stay).
- **A contract activation** now appears as an `Applied` salary change request (source «تفعيل عقد») and publishes
  `IPayProfileChangeApplied`.
- **Operators:** `scripts/pay055-backfill-profile-orgunit.ps1` (dry run by default; `-Apply`; a session cookie or a token on
  Production); `scripts/pay055-validate.ps1`; the setting `Payroll:Bank:Banks`; the policy `Payroll.Repair`
  (`Authorization:Policies:Payroll.Repair:Roles`). Accounts an older build left on profiles arrive as «بانتظار التحقق» with no
  registrar: reject each and register it again.

## 13. Items other plans own, and defects for later plans

- **PLAN-58:** leaving an employee without an account out of the batch and the file; the unified disbursement path. **Recorded
  for it by this plan:** a run paid by the treasury file alone has no payment order, so it counts as not yet disbursed and a later
  retirement holds it; an approver's action that re-pins a frozen line or leaves a held employee out of the batch (today one held
  employee blocks the whole batch, and releasing the hold pays the retired account).
- **Wave 2:** self-service registration, automatic verification with the bank (PAY-I, PAY-J), encryption of the number at rest
  (SOV), the double financial effect of a transfer or secondment (PAY-G).
- **`IAM-H`:** a branch-restricted payroll role. None was added; the branch parts are proved by HTTP tests with a restricted
  principal.
- **PLAN-56:** the reconciliation of a run with a non-zero adjustment.
- **Not this plan's, recorded:** the framework's English answer to a malformed JSON body is the same on every API of the system.
  The server check of the workflow sweeps reads the last 5,000 log lines of two services that write about 6,800 lines in ten
  minutes (every outbox poll's SQL at `Information`), so it fails for about three minutes of every ten; it passed when run just
  after a sweep cycle.
  The demo's 425 profiles have no legal regime (a data decision). The server's disk fell to 8.8 GB during the build.
- **Left narrow, recorded:** `IEmployeeCreated` and the first `IContractActivated` handled in two transactions that are both
  still open do not see each other; what is left then is exactly what the repair fills.

## 14. Timing

All times UTC. The stamps are in `reports/plan55-progress.md`.

| | |
|-|-|
| Started | 2026-11-24T05:14:03Z |
| Stop 1 | 2026-11-24T10:28:11Z to 20:06:20Z — **9 h 38 min.** Cause: the turn ended on its own while it waited for the fourth start of the prod-like stack (a wait reached the tool's limit and no further call followed); the owner wrote «Try again» |
| This report written | 2026-11-25T00:15Z |
| Wall-clock to this report | 19 h 1 min, of which **9 h 38 min stopped** and about **9 h 23 min working** |

There was no other stop: no machine restart, no `BLOCKED:`, no owner action.

Phases (finished): P0 05:21:55Z · P1 06:11:40Z · P2 06:46:59Z · P3 07:17:16Z · P4 07:49:45Z · P5 08:21:43Z · P6 21:52:09Z (the
stop is inside it) · P7 nothing to merge · P8 2026-11-25T00:10:06Z.

Inside the working time, what was waiting rather than work (the documents, the server check class and this report were written
during most of it):

| Waiting for | Time |
|-------------|------|
| Local image builds: 16 images 29 min 6 s; the payroll image again 2 min 56 s; the migrator 20 min 35 s | 53 min |
| Acceptance stacks: the first prod-like stack needed four starts (42 min; three failed under this machine's load, and the last 22 minutes of it are inside the stop); then prod-like 14 min 42 s, lab 15 min 21 s, leastpriv 3 min 11 s | 75 min |
| Acceptance runs: AC-PAY-55, the plan's prod-like classes and AC-UX-67 on the first stack 10 min; the job's prod-like tests 17 min 30 s; lab 4 min 29 s; leastpriv 6 min 7 s | 38 min |
| Full unit runs (10 min 2 s; one aborted by a test host crash after 6,316 passes; 10 min 58 s; the last one before this report) and the Testcontainers step twice (20 min 28 s, 12 min 51 s) | about 75 min |
| The independent review (ran beside the image build) | 11 min |
| The server: upload 2 min; one build 33 min 57 s and its push 40 s; one deploy 3 min 45 s with the smoke | 40 min |
| This plan's server checks: four in 5 min 2 s; the screens 3 min 45 s and 2 min 1 s | 11 min |
| The older plans' server checks: 73 min in one run (117 min in PLAN-54: the link was faster), and two short reruns | 75 min |

**The largest avoidable cost was the stop** (9 h 38 min of 19 h). After it, the costliest waits were this machine's stack starts
(15 GB of memory, the swap full: three failed starts) and the server suites.

**CI:** the local CI steps and the pre-push check run on the commit of this report; their result, the wait for the pull
request's checks, the Actions minutes, the merge and the post-merge run are recorded in the merge note of
`reports/plan55-progress.md` (this report is committed before the push, as the local gates need).

**`pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`** (run at 2026-11-25T00:11Z; the script prints local time, UTC+3):

```text

Plan    Commits Started          FirstCommit      End              Merged Span
----    ------- -------          -----------      ---              ------ ---- 
PLAN-25      11                  2026-10-29 20:31 2026-10-29 23:21   True 2:50 
PLAN-26      12                  2026-10-21 13:38 2026-10-21 22:49  False 9:10 
PLAN-27      11                  2026-10-21 22:57 2026-10-22 09:43  False 10:45
PLAN-28      13                  2026-10-22 10:03 2026-10-22 15:03  False 5:00 
PLAN-29      13                  2026-10-22 15:21 2026-10-22 20:36  False 5:15 
PLAN-30      21                  2026-10-23 00:45 2026-10-29 07:45  False 150:…
PLAN-31      14                  2026-10-29 10:24 2026-10-29 15:03  False 4:38 
PLAN-32       9                  2026-10-29 14:12 2026-10-29 16:31   True 2:19 
PLAN-33      11                  2026-10-29 17:49 2026-10-29 20:18   True 2:28 
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47
PLAN-35      49 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13
PLAN-36      26 2026-10-31 16:32 2026-10-31 16:41 2026-11-01 00:13   True 7:31 
PLAN-37      23 2026-11-01 01:24 2026-11-01 01:30 2026-11-01 12:27   True 10:57
PLAN-38      18 2026-11-01 13:24 2026-11-01 13:25 2026-11-02 02:13  False 12:47
PLAN-39      13 2026-11-02 02:11 2026-11-02 02:13 2026-11-02 11:05  False 8:52 
PLAN-40      20 2026-11-02 11:01 2026-11-02 11:05 2026-11-04 11:56  False 48:51
PLAN-41      17 2026-11-04 11:48 2026-11-04 11:56 2026-11-05 15:04  False 27:08
PLAN-43      14 2026-11-06 10:12 2026-11-06 10:21 2026-11-06 19:03  False 8:42 
PLAN-44      14 2026-11-06 21:11 2026-11-06 21:20 2026-11-07 16:25  False 19:05
PLAN-45      13 2026-11-07 16:55 2026-11-07 16:56 2026-11-07 23:11  False 6:15 
PLAN-46      14 2026-11-07 23:33 2026-11-07 23:34 2026-11-08 15:06  False 15:31
PLAN-47      15 2026-11-08 15:06 2026-11-08 15:06 2026-11-09 15:30  False 24:23
PLAN-48      12 2026-11-09 18:27 2026-11-09 18:28 2026-11-10 02:18   True 7:50 
PLAN-49      11 2026-11-10 11:35 2026-11-10 11:37 2026-11-11 04:35   True 16:58
PLAN-50      14 2026-11-11 13:00 2026-11-11 13:02 2026-11-11 23:10   True 10:08
PLAN-51      15 2026-11-12 02:26 2026-11-12 02:30 2026-11-12 20:04   True 17:34
PLAN-52      11 2026-11-12 23:25 2026-11-12 23:26 2026-11-13 12:40   True 13:13
PLAN-53      17 2026-11-21 03:15 2026-11-22 15:34 2026-11-23 07:39  False 16:05
PLAN-54      17 2026-11-23 10:26 2026-11-23 10:27 2026-11-24 06:20  False 19:53
PLAN-55      12 2026-11-24 08:14 2026-11-24 08:14 2026-11-25 03:11  False 18:56
PLAN-66      12 2026-11-06 20:46 2026-11-06 21:03 2026-11-07 00:36  False 3:33 
PLAN-67      15 2026-11-13 11:06 2026-11-13 18:39 2026-11-22 15:34  False 212:…

Span = first commit -> merge (or last commit if not merged). WallClock = "Started (UTC)" in the progress file -> merge.
Note: without a merge commit, a later commit with the same plan scope (for example a live-run fix) extends that plan's End.
```

## 15. Readiness note for PLAN-56..59

- **The pinned account on the line (PLAN-58's disbursement).** `SalaryLine.BankAccountId` is the destination of a line; never
  read «the employee's current account» for a run. `BankAccountPinning.PinnedAccountsAsync` gives the pinned accounts of a run's
  lines with their present status; `DisbursedLineIdsAsync` is the one definition of «disbursed»; `RetiredAndNotReleasedAsync` is
  the rule the gate and the file share — a new disbursement path must call it, or a retired account is paid. A line with no
  account is legal: the readiness report (`GetRunBankReadinessQuery`) lists it, and PLAN-58 decides what the batch does with it.
  `PaymentOrder.BeneficiaryIban` holds a local number as it is when the bank list is in use.
- **The automatic sources of a version.** A new pay profile comes from `EmployeePayProfileVersioning.CreateNextVersion`, and an
  automatic change from `IPayProfileChangeApplier.ApplySystemChangeAsync` with its source: `ContractActivated` is wired (with
  `PayProfileContractActivation`); `Transfer`, `Promotion` and `Correction` exist as sources and wait for their consumers (PAY-G,
  PLAN-64). When a consumer is moved, remove its name from `PayProfileWritersTests.AllowedWriters` (six today). Dates come from
  `IPayrollCalendar`, stored at 00:00 UTC.
- **The holds a retirement makes.** `PaymentHold` of type `Manual` with `ACCOUNT-RETIRED:` at the start of its reason
  (`EmployeeBankAccount.RetiredHoldRule`), bound to a run, one per employee and run, released by the existing four-eyes release. A
  review hold `PAY055-ACTIVATION` (no run) marks a contract activation that was not applied. A reconciliation (PLAN-56) that
  counts held employees should tell these from fraud holds by that prefix.
- **A run's months in acceptance.** AC-PAY-55 calculates May and June 2029 for agency A and the current month for agency B; a
  class that needs clean months takes others.
