Started (UTC): 2026-11-23T07:26:43Z

# PLAN-54 progress (the agent's checkpoint)

Session: one, in the main folder `/home/omar/HRManagement` (Linux notebook), branch `feature/plan-54-pay-legal-framework-reference-data`.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 baseline | done | 2026-11-23T07:36:53Z |
| P1 data and fingerprint | done | 2026-11-23T08:50:07Z |
| P2 create, import, governing version | done | 2026-11-23T09:39:43Z |
| P3 engine reads the version | done | 2026-11-23T10:56:44Z |
| P4 governance | done | 2026-11-23T11:32:43Z |
| P5 manual inputs | done | 2026-11-23T12:04:28Z |
| P6 acceptance and review | done | 2026-11-23T17:18:00Z |
| P7 main | done: `origin/main` did not move (`53690e0e`) | 2026-11-23T17:18:00Z |
| P8 demo server | done | 2026-11-23T22:15:00Z |
| P9 docs, gates, report, merge | done | 2026-11-24T03:20:17Z |

## P0 — timing, keep-awake, preconditions, baseline

- Started (UTC): 2026-11-23T07:26:43Z (the file's first line).
- **Keep-awake:** `systemd-inhibit --what=sleep:idle --who=plan54`, PID 3171465 (`artifacts/plan54/keepawake.pid`). Stop it by that PID at the end.
- **Preconditions:** 1 pass (main folder, on `docs/plan-53-merge-note`, no other session); 2 pass (`origin/main` = `53690e0e`, the merge of
  `release/plan-53` is there); 3 pass (tree clean); 4 pass (`ssh hrm-vps whoami` = `hrm`, ED25519 host key
  `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`, port 22 open); 5 pass (`gh`: `omardefdev` active, scopes `repo`, `workflow`).
- **P-1:** branch from `origin/main` (upstream unset, so nothing can push to `main` by accident); PLAN-53's merge note cherry-picked
  (`907c1cee`, same diff as `38933f23`); this prompt committed (`6ae73571`). **Deviation:** the prompt file was not on disk (the owner
  pasted it into the session); it was written to `docs/acceptance-plans/execution/PLAN-54-AGENT-PROMPT.md` from the session's own
  transcript, byte for byte. `docs/plan-53-merge-note` deleted after checking its content is on this branch.
- **The machine today:** `/` 50 GB free of 233 GB; Docker holds 8 images of other projects and the owner's dev containers (untouched).
  **PLAN-53's local base images and its helper (`artifacts/plan53/sdk-local/`) are gone**, and so are every acceptance image and the
  third-party images of the stack. **The link is fast today:** `mcr.microsoft.com/dotnet/sdk:10.0.400` pulled in 2 min 44 s
  (07:28:49Z to 07:31:33Z), `aspnet:10.0` and `runtime-deps:10.0` in 69 s more (`artifacts/plan54/pull-base.log`). So the images can be
  built from the registry's base images, as CI does; one service at a time stays (the disk).
- **Durations:** baseline build 4 min 54 s; unit filter 15 s (167/167); payroll integration filter 23 s (28/28); the demo's picture 6 s.
- **Baseline:** `reports/plan54-baseline.md`; inventories `artifacts/plan54/inventory.*.txt`; the demo's picture
  `artifacts/plan54/vps/p0-picture.txt`.
- **Server at P0:** tag `5aa61b78e3a6` (PLAN-53's last, the rollback target), 22 containers (20 healthy of 20 with a check), 30 GB free.

## P1 — the data and the fingerprint

- Finished (UTC): 2026-11-23T08:50:07Z.
- **Entities:** `StatutoryDeductionRule`, `PayrollConfigurationFieldSource`, `PayrollConfigurationEditor` (the editors' record of P-8);
  `ILegallySourcedValue` on brackets, holidays, statutory deductions, field sources and priority rules; `HashSchemaVersion` and the
  five child collections on `PayrollConfiguration`; `IManualPayInput` and its five fields on the three inputs (and `Reason` on an
  allowance); `LegalAttestation.InterimExceptionIdsJson`; `PayrollRunJob.Log`.
- **Fingerprint v2** (`PayrollConfigurationHashService`): v1's method is unchanged text; v2 adds the effective date, the four tables,
  the field sources and every reference, status and exception; rows sorted by their keys, money with two decimals.
- **`CloneAsDraft`** carries every table and reference, takes the date it is given, and for a v1 source starts from what a v1 run
  reads (`PayrollLegacyReferenceData`), unclassified.
- **Migration `20261123074128_PAY054_LegalFrameworkPacks`:** no byte order mark; every existing row by column default
  (`HashSchemaVersion` 1, `Approved`/`System`, `Interim`, `[]`), no data statement; row security on the three new tables;
  `has-pending-model-changes`: none.
- **Tests:** unit filter `Pay054|Pay002|Gov002|Payroll` 194/194 (11 s); `Pay054MigrationPostgreSqlTests` 3/3 on PostgreSQL as a
  NOSUPERUSER NOBYPASSRLS owner under forced row security (14 s); build `-warnaserror` 0/0 (35 s incremental); format clean.
- **Proof that v1 did not move:** `Pay054HashV2Tests` recomputes the three seeded versions' fingerprints and gets the demo server's
  stored values (`9d028823…`, `6a3fa3da…`, `a5de27a1…`).
- **Files:** `Payroll.Domain/Entities/{StatutoryDeductionRule,PayrollConfigurationFieldSource,PayrollConfigurationEditor,ILegallySourcedValue,IManualPayInput}.cs`,
  `Enums/{PayrollLegalValueStatus,ManualInputApproval}.cs`, edits to nine entities, the hash service, the versioning service,
  `PayrollDbContext`, the migration, and the tests `Pay054HashV2Tests`, `Pay054CloneCarriesEverythingTests`, `Pay054MigrationPostgreSqlTests`.

## P2 — create, import, and the version that governs a period

- Finished (UTC): 2026-11-23T09:39:43Z.
- **The pack:** `docs/syr/payroll-framework-packs/schema.v1.json` (the documented contract) and `law50-interim.json` (generated from
  the seeds' values, then checked against them by a test). The importer validates in code
  (`PayrollFrameworkPackValidator`); a test keeps the schema's required fields, enumerations and row properties in line with it.
- **Application** (`Payroll.Application/Features/PayrollConfigurations/`): `PayrollFrameworkPack` (model and reader: 512 KB, depth 16,
  unknown properties refused), `PayrollFrameworkPackValidator`, `PayrollFrameworkPackMapper`, and the commands import, create,
  set-effective-from and export (`PayrollFrameworkCommands.cs`). The import is one transaction; the actor is recorded as an editor.
- **Domain:** `ResolveGoverning` (Published and Superseded, an active code first, the latest date, the highest number, then the code
  and the id), `TryPublish` (Approved only, the version's own date, never on or before the published one's date, D-PAY-13),
  `TryActivate` (Published only), `PayrollFrameworkTemplate`, `PayrollFrameworkMessages`.
- **Repository:** `GetActiveConfigurationAsync` and `GetConfigurationByRegimeCodeAsync` are the resolver for today (Damascus,
  `IPayrollCalendar`); `GetConfigurationByIdAsync` loads every table; `GetFrameworkForUpdateAsync`.
- **Web:** `PayrollFrameworksApiController` (`api/payroll/frameworks`: antiforgery, list, get, create, import, export,
  effective-from; every POST and PUT with `[ValidateAntiForgeryToken]`), `PayrollConfigurationsController` (create, import, export,
  edit on a view model, activate; `[OrgScopeNotApplicable]`), the views `Create`, `Import`, `Details` (the four tables and the field
  sources with reference, status and exception), `Edit` (with the date), the dashboard's «لا يوجد إطار منشور», and the calculation's
  refusal naming the import page.
- **Operations:** `scripts/pay054-import-legal-pack.ps1` (ASCII, parses under pwsh 7); `scripts/deploy-organization.ps1` prints it.
- **Lists that shrank:** `tests/architecture/org-scope-allowlist/payroll.txt` lost `PayrollConfigurations` (it declares its stance);
  `tests/architecture/entity-binding-allowlist/payroll.txt` lost `PayrollConfigurations.Edit` (it binds a view model now).
- **Tests:** full unit suite 6713/6713 (9 min 19 s); architecture 371/371; payroll integration (not Testcontainers) 29/29;
  `Pay054FrameworkHttpTests` 2/2 on PostgreSQL through Payroll.Web; build `-warnaserror` 0/0; format clean.
  New: `Pay054FrameworkPackImporterTests` (50 planted defects, each refused with the field named, no echo, no row; 8 unreadable
  files; decisions; replace; export round trip; create; the date; schema in line; pack equals seed), `Pay054VersioningTests` (21),
  `Pay054FrameworkWritersTests`, `Pay054FrameworkEditFormTests`, and the framework rows of `PayrollWriteActionsTests`.
- **Found while testing:**
  - a re-import by the same person in the same instant marked his existing editor row as new (a duplicate key); the importer now
    marks only the rows it added;
  - **the edit form could not bind a fraction** under the service's Arabic culture (`0.5` for the end-of-service rate: PLAN-53's
    known decimal defect) and bound the entity; it is a view model now with the invariant decimal binder, so the form works and its
    `IAM-F` row left the list;
  - the shared in-process test host starts in Development and so seeds the Demo frameworks; it takes optional settings now, and these
    tests run with `Seed:Profile=None`.

## P3 — the engine reads the version

- Finished (UTC): 2026-11-23T10:56:44Z.
- **The engine:** `StatutoryDeductionEngineRule` has two paths. v2: the version's `StatutoryDeductions` in their order,
  `min(base, ceiling) × EmployeePercent / 100 + FixedAmount`, the base being gross pay or the base salary paid this period. Legacy
  (v1): the old code, untouched. `TaxRule` falls back to `DefaultBrackets` on the legacy path only.
- **What a run reads beside its version** (`PayrollFrameworkReferenceData`, used by the calculator and by correction runs): nothing
  for v2 (its own holidays, no deduction type is even loaded); the general holidays, the version's own and the deduction types for
  v1, with the line «مسار قديم…» in the job's log.
- **Partial disbursement:** a run pinned to a v2 version reads that version's rules (a decision naming another rule set is refused);
  a legacy run reads the decision's rule set or the general active rules, and «general» now excludes rows that belong to a version.
- **The export:** for a v2 run, the employee's share is what the run deducted for the version's insurance rules (by the component's
  rule id, not by name) and the employer's share is each rule's employer percentage of the same capped base; 7% and 14% live on in
  the legacy path only.
- **`DeductionTypes`:** «إلزامي» is read-only on its screens with «انتقل إلى الإطار القانوني»; a posted value is ignored.
- **Does any v1 version have no brackets?** No seeded version (each has five) and none on the demo (five each, baseline). Only bare
  test fixtures reach the fallback; it stays on the legacy path.
- **Tests:** `Pay054EngineV2Tests` 20 (the v2 rule; the general types ignored by v2; v1 as before; no built-in bracket on v2; **the
  identity proof on eight cases**: the imported pack pays line for line what the seeded version pays with the Demo seed's deduction
  types; the same holidays and working days for every month of 2026); `Pay054V2RunPostgreSqlTests` 3 on PostgreSQL (**the same
  employees and month give identical lines on real runs**, v1 then v2; editing the general tables after pinning moves neither the v2
  result, its fingerprint nor its export, while September — still the superseded v1's month — follows them; the disbursement rules);
  `Pay054DeductionTypesHttpTests` 1. Golden pack, pipeline and Pay002 tests unchanged and green.
  Runs: PLAN-54 PostgreSQL filter 10/10 (43 s); payroll integration 36/36; architecture 371/371; payroll unit filter 972/972.
- **A finding for D-PAY-1 / PAY-E, recorded, not changed:** the interim values tax income twice (`DED-TAX` 5% of gross as a statutory
  deduction, and the brackets). A test pins that the pack reproduces it, because the seeds do it today.

## P4 — the cycle's governance

- Finished (UTC): 2026-11-23T11:32:43Z.
- **`PayrollFrameworkGovernance`** (domain): approve (a draft of fingerprint v2, not by an editor, with brackets, through the interim
  gate), attest (an approved version, by neither an editor nor the approver; every cited decision in the register; the attestation
  records the cited decisions and exceptions), publish (approved, an active attestation of the same fingerprint, by the approver or
  the attestor, never an editor; then `Versioning.TryPublish`). `PayrollLegalGovernance` keeps the attestation record and the change
  notice; its old publish rule is gone.
- **The interim gate:** `IPayrollLegalControlGuard.IsExceptionActive(id, asOf)` and `ExceptionExpiresOn(id)`; at approve, attest and
  publish, every value has a reference and every interim value cites an exception active that day, the refusal naming the value and
  the exception; at a run's start and at a resume (`PayrollInterimGate.RefusalForRun`): «انتهى الاستثناء القانوني X», the run neither
  pinned nor started, a `Failed` job row with the reason in `Error` and `Log`. «Today» is `IPayrollCalendar.Today` (Damascus).
  With no catalog to ask, no exception is active (fails closed).
- **The policy:** `Payroll.LegalAttest`, met through the policy `Payroll:Legal:AttestorPolicy` names (`Payroll.Approve` by default);
  the service refuses to start on an unknown name or on the policy itself. The setting and `Payroll:ManualInputs:RequireApproval`
  are in the three settings files and mirrored in `docker-compose.acceptance-prodlike.yml`.
- **Web:** the four acts on the screens go through the commands; a refusal renders the version's page with the message and status
  400. API: `POST {id}/approve`, `legal-attest`, `publish`, `activate`, each with its policy and `[ValidateAntiForgeryToken]`.
- **Indicators:** a run's page and its provenance (`interimIndicator`, `interimExceptionIds`, `frameworkPredatesLegalPacks`,
  `jobLog`); the job's log («إطار مؤقت — استثناء …»); the dashboard, from 60 days before an exception a published v2 version cites
  ends, names it, its date and the frameworks.
- **Tests:** `Pay054FrameworkGovernanceTests` (every actor rule and act with the state unchanged on each refusal; the gate on seven
  instants around 31 January 2027, 21:00 UTC being 1 February in Damascus; the run gate; signed values; the real guard against a
  catalog file, and the shared status-only check unchanged); `Pay054V2RunPostgreSqlTests` start-gate test (refused, not pinned, the
  failed job's log; a paused job not resumed; the legacy month goes on; renewed, it calculates);
  `Pay054FrameworkCycleHttpTests` (three users on the screens and the API, every wrong order 400 or 403 with nothing changed,
  D-PAY-13, the indicators, the dashboard at 61, 60 and after 0 days); policy rows and setting tests in `PayrollWriteActionsTests`.
  Runs: payroll unit filter 994/994; PLAN-54 PostgreSQL 12/12; architecture 378/378; integration (not Testcontainers) 47/47.
- **Existing tests that changed:** `Gov002LegalGovernanceTests.Publish_WithoutAttestation_Fails` and
  `Publish_WithMatchingAttestation_CreatesNotice` now call `PayrollFrameworkGovernance.TryPublish` (the rule moved); the second uses
  a classified v2 version with an approver and a separate attestor, since an unclassified clone is no longer approved. Their meaning
  is kept: no publication without an attestation; a publication with a matching one writes the notice.
- **Found while testing:** the integration hosts validate their container and register no legal catalog, so the cycle's handlers
  could not be built; the guard is optional in them and the gate fails closed without it.

## P5 — manual inputs with a second person

- Finished (UTC): 2026-11-23T12:04:28Z.
- **Commands** (`Payroll.Application/Features/ManualInputs/ManualInputCommands.cs`): `CreateManualInputCommand` (an allowance, a
  deduction or an adjustment: the employee, the type, the amount, the dates, the reason; written `PendingApproval`, `Manual`, with
  who entered it; it has no field for a run, a state or an approver) and `DecideManualInputCommand` (the decider is not the person
  who entered it: 403; a rejection needs a reason; a decision is not taken twice: 409; a system row is never decided).
  `Payroll:ManualInputs:RequireApproval=false` is honoured in Development only, with a warning at start elsewhere.
- **Readers that feed pay, all filtered on `Approved`:** the loader (allowances, deductions, adjustments), the correction run's
  adjustments, the separation consumer's outstanding advance instalments. The medical-recommendation consumer's «already handled»
  read takes rows of origin `System` only, so a manual input never suppresses an event.
- **Web:** `ManualInputViewModels.cs` (three allowlist forms with the invariant decimal binder), `Create` through the command,
  `Approve` and `Reject` (`Payroll.Approve`) on the three controllers, the lists with the state, the origin, who entered and who
  decided, and the decision forms for a second person (`data-hr-confirm` on reject). The forms name no run any more.
- **Lists that shrank:** `tests/architecture/entity-binding-allowlist/payroll.txt` lost `AllowanceRecords.Create`,
  `DeductionRecords.Create` and `PayrollAdjustments.Create`.
- **Tests:** `Pay054ManualInputApprovalTests` 19; the manual input rows of `PayrollWriteActionsTests`;
  `Pay054V2RunPostgreSqlTests.A_manual_input_enters_no_run…` (a real run); `Pay054ManualInputHttpTests` (the three screens as three
  users; crafted `AppliedInRunId`, `PayrollRunId`, `ApprovalStatus`, `Origin` ignored; a fraction binds).
  Runs: full unit suite 6787/6787 (7 min 41 s); architecture 388/388; PLAN-54 PostgreSQL 14/14; integration (not Testcontainers,
  payroll and the hosts) 122/122; build `-warnaserror` 0/0; format clean.
- **Existing test changed:** `Plan034ScopedIndexTests` constructs the three controllers with their two new constructor arguments
  (the mediator, and an authorization service that says «no»); what it tests, the lists' branch scope, is as it was.
- **Found while testing:**
  - the create handler added the entity as `object`, which EF has no set for; each kind is added by its own type;
  - **a defect older than this plan, for PLAN-56:** a run that holds any non-zero `PayrollAdjustment` (manual, or written by a
    consumer: mission, secondment, training…) is refused by the run's own reconciliation («عدم تطابق صافي السطر»):
    `PayrollRunReconciliation.ValidateLineIntegrity` expects `net = gross − deductions` while `RoundingRule` adds the adjustments to
    the net. Not changed here (the run's state machine and reconciliation are PLAN-56's). The real-run test uses an allowance and a
    deduction; an adjustment is proved at the loader.
- **Machine incident:** the first full unit run was aborted at 4239 tests («Test host process crashed», no failing test, exit 1).
  The same binaries passed 6787/6787 on the next run with `--blame`; no kernel message was readable. Taken as memory pressure on
  this 15 GB machine (the owner's containers, a browser and the build servers were up). About 6 min lost.

## P6 — acceptance, then the independent review

Started 2026-11-23T12:04Z. Checkpoint 12:41Z (the images are building; nothing below is on a stack yet unless it says so).

- **Committed so far:** `1e74d54e` (an intermediate commit, so the images carry a clean revision): the persona, the acceptance
  class, the empty-database test, the validate script, the scope and ownership rows, three key pages of AC-UX-67.
- **The persona `a-payroll-approver-2`** (agency A, HQ, ordinal 48; `payroll` and `payroll-approver`; scenario AC-PAY-54):
  `testdata/acceptance/personas.json`, `AcceptancePersonaCatalog.cs` (AGENCY-A 46 to 47), `DATA-AUTH` 67 to 68 in
  `scripts/acceptance-verify.ps1`. No other count was found by search. No local stack or volume existed, so nothing to reset.
- **`AcPay54LegalFrameworkTests`** (AC-PAY-54a..f; `[RequiredAcceptance]`, ordered, evidence `AC-PAY-54`), `prodLike[]` required,
  group `main`; `AC-PAY-54` in `ac-ownership.json` (PLAN-54, `provesClaim`). It uses its own codes (`LAW_50_2004_AC54*`) and its own
  months (2031-01, 2031-02, 2031-06): a version of 2031 governs nothing before 2031 for the other classes of the stack.
  54a starts with P-10's check (both exceptions active in the catalog today, else it fails naming them and the date). 54c's
  negative puts a statutory row in the general deduction types table by psql (test-only; the screens no longer create one) and
  removes it in `finally`. 54e's month is 2031-06. 54f uses the seed's posted runs (pinned to no version: they count for every regime).
- **`scripts/pay054-validate.ps1`** with `-SelfTest`: passes, 28 planted defects refused. It reads the seeds from their source
  (`PayrollRegimeSeed`, `DemoPayrollSeeder.DeductionDefs`, `AcceptancePayrollSeeder`) and compares the pack with them, checks every
  value's reference, status and exception against the catalog, the migration's defaults (and no `UPDATE`), the two settings and their
  mirror, the antiforgery attribute on every write of the frameworks API, and the class, its ownership and the persona. Like
  `iam053-validate.ps1` it is run by hand and by the report, not by a CI step.
- **`Pay054EmptyDatabaseFirstRunPostgreSqlTests`** (P-12; Testcontainers, picked up by CI's filter): Payroll.Web on an empty
  database with `Seed:Profile=None`; three employees through `EmployeeCreatedConsumer` and `ContractActivatedConsumer` (one joins
  mid-month); the calculation is refused with the message that names the import page, also while the version is a draft and while
  it is approved; a pack with a 120% bracket leaves no row in thirteen tables; import, approve, attest, publish by three people
  over the API; the same run is then calculated, pinned to the version, each line with the version's four statutory deductions to
  the unit, and no general row exists. **Passes** (15 s). HR.Core's answer to «is anyone blocked from pay?» is the declared double
  `FakeHrCoreServiceRegimeClient` (another service; every payroll calculation test uses it). `Pay054FrameworkHttpTests.StartAsync`
  takes optional extra test services for that.
- **AC-UX-67's key pages** gained `payroll-framework-details`, `payroll-framework-import` and `payroll-manual-allowances`.
- **Tests at this checkpoint:** build `-warnaserror` 0 errors (42 s); unit **6787/6787** (7 min 25 s); architecture **388/388**;
  format clean on the changed files.
- **Images:** no acceptance image existed here. Built one service at a time by the local helper
  `artifacts/plan54/build-images-sequential.ps1` (PLAN-53's, not committed), revision `1e74d54e`, started 12:37:17Z; the first
  service took 141 s (the base images were pulled this morning, the link is fast today). Disk 47 GB free.
- **Images, first build:** 16 services in 1,390 s (12:37:17Z to 13:00:37Z), revision `1e74d54e`. Disk 46 GB free after it.
- **Independent review:** one fresh read-only subagent, 12:40Z to about 12:52Z, beside the image build (it ran no build and no
  test). No refuter was used: both high findings were verified by reading the code they name. What it found and what was done
  (commit `3bc94d27`; every fix has a test):
  1. **High, fixed.** «استئناف» (`ResumePayrollRunJobCommand`) did not pass the interim gate, and the row that carries a refused
     calculation (a `Failed` job of a run pinned to nothing) could itself be resumed: an empty, unpinned run would be marked
     calculated and frozen. Now a job that never started is not resumed (the button is not offered either), a started one passes
     the gate again, and a repeated refusal reuses its one row. Test: the interim gate run test in `Pay054V2RunPostgreSqlTests`.
  2. **High, fixed (the acceptance class, before its first run).** AC-PAY-54c and 54e calculated a calculated run again; a
     calculated run is frozen (AC-23 asserts the 400). 54c's negative now calculates 2031-03, a new snapshot of version 2, beside
     the planted general row (no component from it, the result equal to February's); 54e's allowance appears in the next month's
     run, 2031-07. No product path cancels a completed run, so nothing is cancelled by psql.
  3. **Medium, fixed.** A version had no row version: an edit landing between an approver's read and his save left a version
     approved with a fingerprint of other content, and two publications could both supersede one version. `PayrollConfiguration`
     now has the `xmin` token (in PAY054 itself, its designer and the snapshot; `has-pending-model-changes` reports none); every
     content write also writes the version's row; a late save answers 409 and writes nothing. Behind it, an attestation and a
     publication of a v2 version recompute the fingerprint. Tests: `A_request_that_lands_between_another_persons_read_and_save…`
     (PostgreSQL; the other request is landed at the handler's first read of the clock) and `Pay054ReviewFixesTests`.
  4. **Medium, fixed by design.** A version's priority rules were stored active; PLAN-53's code reads every active row for a
     decision that names no rule set, so a rollback after any import would mix them into a legacy disbursement. They are stored
     inactive and are in force through their version (the pinned run's allocation takes them all).
  5. **Medium/low, part fixed, part a decision (D22).** A correction run of a run pinned to an interim version now passes the
     gate. End-of-service settlements and the amounts Payroll derives from events (mission, medical, training) are not gated.
  6. **Low, recorded (D23).** An agency whose first version starts in the future has no governing version today.
  7. **Low, fixed.** AC-PAY-54a's count of unsourced values skipped a NULL exception and two tables.
  8. **Low, fixed.** A copy of an unpublished version dropped its authors: they stay editors of the copy.
  9. **Low.** Fixed: the expiry warning names a superseded version that still governs today; the validate script refuses any
     data statement in the migration's `Up`, raw strings included (29 planted defects). Recorded, not changed: the migration's
     `Down` restores the global unique index of the decision register (it fails once two agencies hold one decision; a rollback
     of this plan is by image tag, never by `Down`); two first publications of one code at the same moment, with no published
     predecessor to conflict on, both publish (the resolver still answers by date, then version); an approval and a rejection of
     one manual input at the same moment are last-writer-wins (both are valid decisions of authorised people);
     `PayrollConfigurationVersioning.TryApprove` stays public for the tests that use it; an import by one master-data holder
     rewrites decision register rows other versions cite (P-5's design; the register is outside every fingerprint).
  What it checked and found correct: the v1 fingerprint and the legacy path unchanged; a v2 run, correction, disbursement and
  export read the version only; fingerprint v2 covers every value the rules read; no gap or back-cut in publishing; refusals
  change nothing; Damascus dates; the import's limits and transaction; antiforgery and policies on every write; row security;
  pending inputs filtered in every reader; the migration's defaults; the changed tests keep their meaning; the lists only shrank.
- **After the fixes:** build `-warnaserror` 0 errors; unit **6791/6791** (7 min 8 s); architecture **388/388**; the plan's
  PostgreSQL tests 16/16 (47 s); format clean; `pay054-validate` and its self-test pass.
- **The prod-like stack, first start (15:00Z to 15:15Z, 891 s)** stopped at its own verification: `DATA-DIRECTORY` 59, expected
  58. The new persona adds a directory entry as well as a user; the count is raised to 59 beside `DATA-AUTH` 68 (commit
  `0a926937`). Verification then **Pass** on the same stack.
- **`AcPay54LegalFrameworkTests` on that stack: 6/6 Passed at its first run** (68 s; evidence
  `artifacts/plan54/runs/ac54-run1/`). Read from the evidence: 54a a draft of fingerprint 2 with 5 brackets, 4 statutory
  deductions, 4 holidays, 3 priority rules and 19 field sources, 0 values unsourced; approved, attested and published by three
  people; the run of 2031-01 has 1102 lines pinned to the version, each with the version's four statutory deductions. 54c
  version 2 has another fingerprint, version 1 is superseded to 2031-01-31, January's result is the same md5 before and after,
  March equals February beside the planted 50% general row. 54e 635,000.00 in June, 640,432.10 in July. 54f refused for the
  posted period ending 2026-07-31.
- **The other prod-like classes of P-14, on the same stack** (the helper, the job's environment): `AcIam53SalaryChangeTests`
  5/5, `AcIam51ExplicitPolicyTests` 7/7, `AcIam52BranchScopeTests` 5/5, `AcHcm25ProductionApprovalTests` 8/8 (160 s);
  `AcUx67ReadableInterfaceTests` **7/7** in Chromium with the three new key pages opened (HTTP 200, one policy, no
  violation each).
- **The import script on the stack:** `pay054-import-legal-pack.ps1 -Code LAW_50_2004_OPS -EffectiveFrom 2032-01-01` imported
  a draft and printed the three next steps; a pack with a 120% bracket was refused with the field named and nothing written.
  The script's default acceptance user was wrong (`acc-a-payroll-processor`; the persona is `e2e-payroll-processor`): fixed.
- **The prod-like journey and the `isolated` group, fresh stack** (`acceptance-job.ps1 -Stage prodLike:journey,prodLike:isolated
  -NoBuild`, 15:23:49Z to 15:56:13Z, 1,944 s; stack 999 s): journey 1/1, `AcInt37ProductionWiringTests` 8/8,
  `AcMsg40AtomicPublishingTests` 8/8, `AcWf44InstanceIntegrityTests` 5/5, `AcWf45AssetApprovalLoopTests` 8/8,
  `AcWf46CrossServiceApprovalTests` 9/9, `AcPol47RuleLifecycleTests` 1/1, `AcOps39KeycloakDataProtectionTests` 8/8.
  `acceptance-guard: PASS (8 classes in 8 trx files)`.
- **Written while the stacks started:** `AcPay54VpsChecks` (P8's four server checks), the documents of P9 (commit
  `a87a4522`), and one more change the server check asked for: an import whose pack repeats the decision register writes no
  register row (it used to rewrite every row's `UpdatedAt`), so the check can assert that no decision row of the demo is
  written. Unit test in `Pay054ReviewFixesTests`.
- **A note on the images:** the classes above ran on the payroll image of `14070764` (the review's fixes). The register change
  (`a87a4522`) came after them; the payroll image was rebuilt from it (118 s) for the lab stage. CI runs every class on the final
  code.
- **The lab stage, fresh stack, the payroll image of `a87a4522`** (`acceptance-job.ps1 -Profile Lab -NoBuild -Classes
  Ac21…, Ac23…, Ac24…, Ac25…, AcHcm29…, RecruitmentToHireJourneyE2ETests`; 15:57:51Z to 16:21:04Z, 1,393 s with the reset
  and the image; stack 903 s, verification Pass): `Ac21PayrollDeterminismTests`, `Ac23PayrollFreezeTests`,
  `Ac24PartialDisbursementTests`, `Ac25FraudHoldTests` and `AcHcm29HireContractTests` **7/7**, the lab journey 1/1;
  `acceptance-guard: PASS (6 classes in 2 trx files)`. These are v1 runs (the seed's versions): they compute as before.
- **The leastpriv stage** (the migration under the least-privilege roles, from empty volumes). The job's own
  `docker build` of the migrator cannot run here (no buildx: the legacy builder does not know `COPY --exclude`), so the image
  was built through `docker compose build` with a one-service file in the scratchpad (16:22:30Z to 16:47:44Z, 1,514 s,
  fifteen bundles, 5.13 GB; 36 GB free at the lowest, 40 GB after pruning the build stages). Then
  `acceptance-job.ps1 -Stage prodLike:leastpriv -NoBuild` (16:48Z to 16:58Z, 614 s; stack 209 s, tests 379 s):
  `AcOps38FirstBootTests` and `AcTen48LeastPrivilegeTests` **9/9**, `acceptance-guard: PASS (2 classes in 1 trx files)`.
  PAY054 is applied by the migrator's role and Payroll starts on it as its own role.
- **Stacks down** after each stage (`acceptance-reset.ps1`; the leastpriv job removes its own). No local evidence was copied
  into the committed `acceptance-evidence/`.
- **The Testcontainers step on the host, CI's filter** (`Category=Testcontainers&Category!=ObjectStorage`; 17:03:47Z to
  17:16:58Z, 791 s): **264 passed, 1 skipped, 0 failed** (13 min 4 s), on the final code.
- **P7:** `origin/main` is still `53690e0e` (fetched 16:00Z): nothing to merge.
- Finished (UTC): 2026-11-23T17:18:00Z.

## Stops and resumes

The first two lines were not in this file until 14:30Z (they were only in the session's own record); they are written here as
they were stamped then.

- Resumed (UTC): 2026-11-23T08:39:12Z. Reason: the owner stopped the turn during a P1 `dotnet test` and then wrote «اكمل».
  Standstill about 56 min. From then on long commands run detached with a log under `artifacts/plan54/`.
- Resumed (UTC): 2026-11-23T10:46:11Z. Reason: the machine restarted (boot 10:43:07Z) during P3's PostgreSQL tests; the owner
  wrote «اكمل». Standstill about 54 min. The keep-awake was started again (PID in `artifacts/plan54/keepawake.pid`).
- Resumed (UTC): 2026-11-23T14:29:25Z. Reason: the owner stopped the turn in P6, while the review's findings were being verified
  in the code (the images had finished at 13:00:37Z), and then wrote «اكمل». Standstill about 75 min (from about 13:15Z). No
  process was running; the machine did not restart; the tree held only this file's edit.
- Resumed (UTC): 2026-11-23T23:31:06Z. Reason: the turn ended on its own while it waited for the local CI steps (no command
  was interrupted; the detached run went on and finished at 23:15Z), and the owner wrote «Try again». Standstill about 16 min
  (from 23:15Z). The machine did not restart.

## P8 — the demo server

- Started (UTC): 2026-11-23T16:00Z (the read-only checks and the disk, beside the local lab stage; the build waits for the
  local regression).
- **SSH and P-17's condition:** `ssh hrm-vps` → `hrm`; the known host key is `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`.
  Running tag `5aa61b78e3a6`, contained in `origin/main`. 22 containers, up 1 day 4 h.
- **The picture before the deploy (17:05Z, read-only, the same statements as P0):** identical to P0's, line for line
  (`artifacts/plan54/vps/p8-picture-before.txt`): three v1 versions (checksum `3efb4119f16c75a8e3daab570ef03f0b`), the decision
  register 17 rows (`4ee88def8114915fcd22ee453ec904fb`), no attestation, six runs (none pinned), inputs 800 / 500 / 80,
  last payroll migration `PAY053_SalaryChangeRequests`.

- **The build, once, on `83b99786a420`** (P6's commit; everything after it is documents and the server check class). The
  archive (32.7 MB) and the committed `deploy/vps/build-on-host.sh` were uploaded as `build-on-host.ps1` does (100 s) and the
  script started **detached** on the server (`setsid nohup`, 17:19:18Z; log `/opt/hrm/build-83b99786a420.log`, exit code in
  `/opt/hrm/build-83b99786a420.done`). Build 2,165 s, push 50 s, exit 0 at about 17:56Z; 17 of 17 images. Disk: 40 GB before,
  17 GB at the lowest seen, 33 GB after the script's own cleanup.
- **The deploy** (`scripts/vps/deploy.ps1 -Tag 83b99786a420`, 17:56:44Z to 18:02:07Z, 323 s with the smoke): pull 5 s, up
  150 s, bootstrap 64 s (PAY054 applied by the migrator), keycloak reconcile 15 s, healthy 22 s. `deployments.log`:
  `2026-11-23T18:01:04Z|tag=83b99786a420|action=deploy|result=healthy|previous=5aa61b78e3a6`. The services were away for about
  3 minutes at most (the «up» step). **Smoke: pass 50, fail 0, warn 1** (the port check from the client's side, as before).
- **This plan's server checks** (`AcPay54VpsChecks`, the helper `artifacts/plan54/run-vps.ps1`; evidence
  `artifacts/plan54/vps/`):
  1. *The migration* — **Passed.** PAY054 recorded; the three new tables have forced row security; the demo's three versions
     are v1 with their fingerprints and the checksum of P0's picture (`3efb4119…`); the decision register `17|4ee88def…`;
     inputs 800 / 500 / 80, every one `Approved` and `System`; runs as in the picture, none pinned.
  2. *The cycle* — **Passed.** `e2e-payroll-processor` imported the real pack as `LAW_50_2004_CHK54` from 2026-04-01 (the
     first day of the last posted month); publishing the draft 400; the processor's approval 403; `e2e-payroll-approver`
     approved; his attestation 400; **`hradmin` attested (he meets the attest policy in demo mode: no user was added)**;
     publishing 400 «…تشغيل رواتب مرحّل تنتهي فترته في 2026-04-30». The version stays approved, attested and unpublished. A pack
     with a 120% bracket: 400, the field named, no row. The other versions, the decision register (checksum and every row's
     write time) and the runs are as before.
  3. *A manual input* — **Passed.** An allowance for a demo employee, dated inside the posted month 2026-04: pending and
     `Manual`; the author's approval 403; the approver rejected it with a note; nothing pending is left; the system inputs
     are as before.
  4. *The screens* — **Passed at the fifth run** (27 min 28 s): ten screens at two sizes in a real Chromium as
     `e2e-payroll-processor`, each HTTP 200, one CSP policy, no violation; the version's page Arabic, right-to-left, its editor
     named by the registry's span, no user id printed, «مؤقت», the exception and the statutory deductions named.
     **The four runs before it failed for this machine's network, not for the server** (62 min): the owner's dev container
     `hrm-keycloak` (restart count 1268, every minute) changes a network interface under Chromium — `ERR_NETWORK_CHANGED`,
     `ERR_NAME_NOT_RESOLVED`, a sign-in that does not come back, and over HTTP/3 a navigation that stalls to its 60 s timeout
     (the edge logged «aborting with incomplete response» for the client; Payroll answered each page in under 100 ms; `curl`
     over TCP was steady, 2 to 7 s a request). A temporary diagnostic (not committed) showed the same on the sign-in itself.
     The check now probes one page at one size per step, repeats a step the machine's network cut (18 repeats in the passing
     run, 39 attempts for 21 steps) with pauses of different lengths, and launches Chromium with `--disable-quic`.
- **The server checks of PLAN-36..53** (nothing heavy beside them; 19:53:35Z to 21:50:48Z, 7,033 s; the link was about
  480 ms that day and a check took 5 to 7 minutes): **53 of 59 in the run, 58 of 59 after the reruns.**
  - `AC_WF_46_vps_the_sweeps_run…`: no sweep cycle yet after the deploy; run again after one: Passed.
  - Four browser checks of PLAN-67 (the policy page, the destructive form, the portal, the tour of key pages): this
    machine's network under Chromium. The browser checks of PLAN-53 and PLAN-67 now launch Chromium without QUIC, and the
    tour uses the new shared `Ux067PageProbe.ProbeEachAsync` (a page the client's network cut is probed again). Run again:
    the policy page and the form Passed, the portal Passed at its third run, **the tour Passed** (11 min 54 s, three
    repeats; PLAN-53 could not pass it from this machine): thirty key pages, this plan's three among them.
  - `AC_POL_47_vps_the_migration_the_trigger…`: **not passed, the local cause PLAN-53 recorded** (it reads
    `artifacts/plan47/vps/fingerprint-before.json`, a file of the formatted Windows machine).
  - The suites rewrote files under the committed `acceptance-evidence/*/vps/` and wrote `reports/msg040-replay-server-…json`:
    restored and removed.
- **The demo at the end of P8:** `83b99786a420`, 22 containers, every health check passing, smoke 50 pass and 0 fail.
- Finished (UTC): 2026-11-23T22:15:00Z.

## P9 — docs, local gates, report, the pull request

- Started (UTC): 2026-11-23T22:15Z. The documents and the report were written while the stacks and the server build ran
  (commits `a87a4522`, `27f5a010`).
- **Local gates, first run** (`scripts/ci/run-steps.sh --all` in a fresh clone of `27f5a010`, 22:17Z): stopped at «Check
  pending EF model changes» with «Build failed» for thirteen of fifteen contexts — Razor parse errors on files that had not
  changed, the stale compiler server this machine is known for (`dotnet build-server shutdown` and the same project built
  clean). Run again with `UseSharedCompilation=false`, `UseRazorBuildServer=false`, `MSBUILDDISABLENODEREUSE=1` and
  `DOTNET_CLI_USE_MSBUILD_SERVER=0` (22:34Z to 23:15Z, 2,470 s): every step passed up to the integration step.
- **That run found a defect of the review's fix, and it is fixed:** «Integration tests (MassTransit InMemory)» 453 passed,
  **1 failed**, `Pay003ResumablePayrollRunTests.Snapshot_VersionConflict_Detected`. The fix of the review's first finding
  told «a job that never started» by its run being pinned to no version; that test resumes a paused job that has its
  snapshot and partition on a run not yet pinned, and got «هذه المهمة لم تبدأ» instead of the snapshot conflict. The rule now
  reads the job itself: **a job without a partition never took a snapshot and is not resumed** (the refusal row has none).
  The run page offers «استئناف» by the same rule. `Pay054V2RunPostgreSqlTests`' paused job got the partition a started job has.
  After it: integration (not Testcontainers) **455 passed, 3 skipped, 0 failed** (18 min 55 s); the Testcontainers step, CI's
  filter, **264 passed, 1 skipped** again (11 min 24 s; 23:36Z to 00:07Z).
- **The server and this change:** the demo runs `83b99786a420`, built before it. The difference is that one rule (for every
  state the product itself creates the two rules agree: a job that started has partitions and its run is pinned) and
  documents and tests. The final commit is built and deployed once more after the push, beside the wait for CI, and the
  four checks are run again (recorded in the merge note below).

- **Local gates on `014db7e3` (the final commit of the feature branch):** `scripts/ci/run-steps.sh --all` in a fresh clone,
  without shared compiler servers (00:09Z to 00:46Z, 2,244 s): **87 steps passed, 0 failed**, 5 not run and named
  (`Integration tests (Testcontainers)`: run natively on the same code, 264 passed and 1 skipped; `Coverage gate`,
  `Install ReportGenerator`, `Generate coverage report`: they follow that step and would install a global tool;
  `Verify Docker builds`: the images were built here one at a time and seventeen on the server). In it: build 428 s, unit
  474 s, architecture 5 s, integration 403 s. `scripts/plan035-prepush.ps1`: gitleaks, actionlint, cilocal, forbidden,
  nonprod and signatures PASS (16 commits, unsigned, not required).
- **The server on the final commit.** Build of `014db7e3a01a`, detached (00:09:34Z to about 00:43Z): build 1,971 s, push
  35 s, 17 of 17; disk 32 GB before, 15 GB at the lowest seen, 25 GB after. **The first deploy (00:49:36Z) stopped at
  `keycloak-reconcile` after PostgreSQL was recreated — the known PLAN-40 incident — and left the application containers
  created and not started (`deployments.log`: `up-failed`).** The same command was run again at once (00:52:09Z to 00:55:35Z):
  pull 4 s, up 119 s, bootstrap 55 s, reconcile 14 s, healthy 15 s; `2026-11-24T00:54:47Z|tag=014db7e3a01a|action=deploy|
  result=healthy|previous=83b99786a420`; smoke pass 50, fail 0, warn 1. **The demo was down for about 5 minutes** (00:50Z to
  00:55Z). This plan's four checks again on `014db7e3a01a`: all **Passed** (the screens check in 2 min 55 s). The migration
  check's comparison with P0's picture needed the adjustments' count raised from 80 to 85: five zero-amount adjustments were
  written between 20:55Z and 21:41Z by Payroll's own consumers during the older plans' server checks (two leave approvals with
  their cancellations, one disciplinary warning), each `System` and `Approved` — the system writers behave as designed.
  The checks left a second version of `LAW_50_2004_CHK54` (approved, attested, unpublished) and a second rejected allowance.
- **The pull request:** <https://github.com/omardefdev/HRManagement/pull/54>, `release/plan-54` (`origin/main` + one `--no-ff`
  merge of the feature branch) pushed 2026-11-24T00:49:12Z.
- **CI, first run (45300243393, 00:49Z to 01:51Z):** `CI` pass (42 min 24 s), `CodeQL` pass, `Gitleaks` pass, `E2E` pass
  (13 min 20 s), `Acceptance images` pass, the stages lab-main, lab-rest, leastpriv, prodlike-policy and prodlike-final pass;
  **`Acceptance stage (prodlike)` failed.** Five tests: AC-IAM-53a (and 53d, which needs 53a) and AC-PAY-54a, 54c, 54e, all
  with the same answer to «calculate»: «لا يمكن إتمام تشغيل الراتب — 1 موظفاً لديهم استثناء نظام خدمة مفتوح أو بدون نظام مصنّف».
  **Cause, read from the stage's trx:** the classes of a group run in an order that changes from run to run; in this run
  `AcIam50ApproverReachDelegationTests` ran before the payroll classes, and its 50b adds an employment record through the
  form — open, with no service regime — which makes that employee a payroll blocker for every later run of the agency
  (the gate is the product's, PLAN-53 §12). PLAN-53's own pull request passed because its run drew another order. Not a
  defect of the product or of this plan's code; a test of PLAN-53 and the new class shared one hidden precondition.
  **Fix (release branch only):** `AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync`, a stated test precondition
  that applies the acceptance seed's classification to records added after the seed, called before each calculation in
  `AcPay54LegalFrameworkTests` and before 53a's run. Exceptions of other types are left to block. **Reproduced and proved
  locally** on a fresh prod-like stack in the failing order: `AcIam50…` 5/5, then `AcIam53…` 5/5 and `AcPay54…` 6/6; the
  record 50b added reads `law50`. Unit 6792/6792, architecture 388/388, format clean.

- **CI, second run (45311799517, on `7d293bb4`, 02:21:40Z to 03:18:38Z): every check green.** `CI` pass (27 min 9 s),
  `CodeQL` pass (13 min 27 s), `Gitleaks` pass (1 min 2 s), `E2E` pass (44 min 42 s), `Acceptance images` pass (23 min 49 s),
  `Acceptance stage` lab-main (26 min 14 s), lab-rest (13 min 53 s), leastpriv (6 min 38 s), meta (8 min 38 s), prodlike
  (30 min 2 s, with AC-PAY-54a..f and AC-IAM-53a..e), prodlike-final (31 min 21 s), prodlike-policy (11 min 8 s) pass,
  `Acceptance` pass, `Reuse check` and `Dedupe post-merge run` pass. **The evidence of record** is that run's acceptance
  artifact: <https://github.com/omardefdev/HRManagement/actions/runs/45311799517>.
- **Merged by agent on owner instruction (UTC): 2026-11-24T03:19:08Z.** `gh pr merge 54 --merge`; merge commit
  `12b83970b4c8`. `git switch main; git pull --ff-only`: `main` = `origin/main` = `12b83970b4c8`. The server's tag
  `014db7e3a01a` is contained in `origin/main`.
- **The post-merge run on `main`** (45318660987): success — `Dedupe post-merge run` and `Gitleaks` ran, the other jobs were
  skipped because the pull request's run tested the same tree.
- **Waiting for CI:** run 1 62 min (00:49Z to 01:51Z), run 2 57 min (02:21Z to 03:18Z); between them 30 min for the cause,
  the fix and its reproduction on a local stack. **Actions minutes:** 260 job-minutes in run 1 and 239 in run 2, under one
  minute in the post-merge run.
- **Total:** started 2026-11-23T07:26:43Z, merged 2026-11-24T03:19:08Z: **19 h 52 min wall-clock**, of which about 3 h 21 min
  stopped (four stops, see «Stops and resumes») and about 16 h 31 min working, about 2 h of that waiting for CI.
- **End of the session:** the keep-awake process stopped by its PID; the fresh clone used for the local CI steps and this
  plan's local acceptance images removed (other projects' images, containers and volumes never touched); the demo is up on
  `014db7e3a01a`.
- Finished (UTC): 2026-11-24T03:25:00Z.

## Decisions (with reasons)

Recorded as they are taken; see each phase.

- **D1 (P-7, the name clash):** `PayrollRegimeAndRulesTests.cs` needed a qualified name, so the engine's rule class
  `Engine.Rules.StatutoryDeductionRule` is renamed `StatutoryDeductionEngineRule`; the entity keeps the plan's name.
- **D2 (P-7, `LegalSource`):** `DisbursementPriorityRule.LegalSource` («المصدر القانوني») means the same as a legal reference: it is
  reused (the interface's `LegalReference` reads it); only `LegalStatus` and `InterimExceptionId` are added to that entity.
- **D3 (`HashSchemaVersion` in code):** the C# default is 1 and only the creation paths (create, clone, import) set 2, so the Demo
  and Acceptance seeds and the several hundred test fixtures stay v1 and compute as before. A test pins which product files may
  construct a `PayrollConfiguration` (P2).
- **D4 (column defaults live in the migration only):** the model declares no default for the new enum columns. With a model default,
  EF would treat the CLR default (`Signed`, `Approved`) as «not set» and write the database default instead (`Interim`), a silent
  wrong value. EF writes every value explicitly; the defaults fill the existing rows and plain-SQL inserts.
- **D5 (a legal reference that starts with `LD-` cites a decision):** the plan gives each value three fields (`legalReference`,
  `legalStatus`, `interimExceptionId`) and asks that every cited decision exist; so a reference is either a decision id or a free
  citation.
- **D6 (the effective date is inside fingerprint v2):** an approved version cannot change its date without changing its fingerprint.
- **D7 (`LegalDecisionRecords` unique index):** now `(TenantId, DecisionId)`; it was unique across agencies (baseline fact 1).
- **D8 (`PayrollRunJob.Log`):** «سجل المهمة» had no field; the job gains a short text log (the legacy path, the interim indicator, an
  expired exception). A refusal before a job exists is recorded on a job row of status `Failed`, which is not an active job.

- **D9 (which version governs a period, P-6):** as the prompt says, with two additions: the code's name and the id settle a tie
  (so the answer never depends on row order), and `GetConfigurationByRegimeCodeAsync` — a third resolver the plan does not list —
  is the same resolver for today, filtered by regime or code.
- **D10 (the last bracket has no ceiling):** «ascending with no gap» includes the gap above the last limit: the last bracket's
  upper limit must be the open-ended amount `9999999999999999.99` (the seeds' own value), or income above it would be untaxed.
- **D11 (a code keeps one regime):** an import or a create under a code that another regime already uses is refused.
- **D12 (the interim pack's insurance rules):** today's export finds the employee's insurance share by name («تأمين»): `DED-SOC`
  and `DED-HEALTH`. So the pack marks both `insuranceBody: GOSI` and gives `DED-SOC` the employer's 14%; a v2 run of the pack
  exports what a v1 run exports. Which body each deduction really belongs to (pension to GOIP) is D-PAY-1 / PAY-E.
- **D13 (errors never echo the file):** a pack error carries a JSON path cut at the first name the contract does not know, and an
  Arabic message that says what is required. The API's import reads its body itself, not through the model binder, for the same
  reason.
- **D14 (the exception number on the page):** the interface guard reads a printed `…Id` as a technical id. A value's interim
  exception number is a legal citation the reader looks up (like a decree number), so the details page prints it through the view
  model's `ExceptionCitation`; no row was added to the guard's lists.
- **D15 (the API's surface):** the plan lists four endpoints; `GET` list and `GET {id}` are added as reads, and the cycle's acts get
  API endpoints in P4 (the acceptance class and the server check run the cycle as three users).

- **D16 (an exception's last day):** the plan writes `expiresOn > asOf`; this prompt's P-10 gives the dates (the framework is
  refused «from 2027-02-01», the dashboard warns «from 2026-12-02», 60 days before). So `expiresOn` is the exception's last day:
  it is active while `asOf <= expiresOn`, in the payroll calendar's date. An exception without a readable date is not active.
- **D17 (a legacy draft is not approved):** a draft of fingerprint v1 (none exists on the demo) would publish a new legacy
  version; the approval refuses it and says to create a new version from it.
- **D18 (approval is from Draft only):** the old `TryApprove` also re-approved an approved version, rewriting its approver.
- **D19 (a refused run leaves a job row):** «the job's log says…» for a run that never got a job: a `Failed` job row (not an
  active job) carries the refusal; the run is neither pinned nor started.

- **D20 (a manual input needs a pay profile):** entering an input for an employee who has no pay profile is refused (the old
  forms accepted any id).
- **D21 (the operator script's path and cookie):** through the gateway the frameworks API is `/api/payroll/frameworks` (the gateway
  forwards `/api/payroll/**` unchanged, as the acceptance classes call PLAN-53's API); the service scopes its antiforgery cookie to
  `/ui/payroll`, so the script sends the cookie back in a `Cookie` header, as the acceptance harness adds it by hand.

- **D22 (what the interim gate stops):** a payroll calculation: a regular run, its resume, and a correction run. It does not
  stop an end-of-service settlement or the amounts Payroll derives when it consumes an event (a mission, a medical recommendation,
  a training premium): refusing a message would park it in a retry queue for a legal reason no retry can cure, and every one of
  those amounts reaches a payslip only through a run, which the gate stops. PAY-E may widen it with the final values.
- **D23 (no version governs today):** the active configuration is now the version that governs the payroll calendar's date. An
  agency whose first published version starts in the future has none until that day: an event-derived amount is then 0, as it
  was for an agency with no framework. `PILOT_DEPLOYMENT.md` tells the operator to give the first framework an effective date
  on or before the first month the agency works.
- **D24 (a version's priority rules are stored inactive):** `IsActive` is the general table's switch. A version's rule is in
  force through the version a run is pinned to; stored active it would be read by the release before PLAN-54 after a rollback.

## Server changes and their undo commands

1. **2026-11-23T16:01Z, the disk before the build.** The server had 29 GB free (the line is 30 GB), no build cache, and the local
   images of three releases. The local images of the two older ones, `27cbf35db9cc` and `1ee455bf1a6f` (17 each), were removed
   with `docker rmi`; the running release `5aa61b78e3a6` was not touched. 41 GB free after it. Both tags stay in the loopback
   registry. **Undo:** `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` for the image wanted back.
2. **2026-11-23T17:04:49Z, the backup.** `/opt/hrm/backups/pre-plan54-20261123T170449Z` (700): `pg_dumpall.sql`
   (35,538,324 bytes), `env.backup`, `SHA256SUMS` (each 600). Verified: both sums OK; 17 `CREATE DATABASE` lines (the sixteen
   databases and `hrm_admin`); the dump ends with «PostgreSQL database cluster dump complete». **Undo:** delete the folder when it
   is no longer needed (`rm -rf /opt/hrm/backups/pre-plan54-20261123T170449Z`).
3. **2026-11-23T17:19Z to 17:56Z, the build's files.** `/opt/hrm/build-83b99786a420.log` and `.done` (kept, as for every
   earlier build), `/opt/hrm/build/images-83b99786a420.json`; the script removed its source, the build cache and the SDK image;
   the uploaded `/opt/hrm/build-on-host-83b99786a420.sh` was removed after the build. Seventeen images tagged `83b99786a420`
   in the loopback registry. **Undo:** none needed; the files can be deleted, the tag stays in the registry.
4. **2026-11-23T17:56:44Z, the deploy of `83b99786a420`** (previous `5aa61b78e3a6`). Migration `PAY054_LegalFrameworkPacks`
   applied to `payroll_db`. **Undo:** `pwsh -NoProfile -File scripts/vps/deploy.ps1 -Tag 5aa61b78e3a6` (or `-Rollback`); the
   database keeps PAY054, which that release does not read (the rollback note in `docs/OPS_VPS_SINGLE_HOST.md`).
5. **2026-11-23T18:04Z to 18:12Z, what the checks left.** One framework version `LAW_50_2004_CHK54` v1, approved, attested
   (by `hradmin`), unpublished, effective 2026-04-01 inside a posted month (D-PAY-13 refuses its publication; it governs
   nothing), with its child rows and one attestation; one manual allowance, **rejected**, dated inside 2026-04. No decision
   row was written. **Undo:** none needed; they are records (a version is never deleted).

## Open issues

- For PLAN-56 (older than this plan): a run with a non-zero `PayrollAdjustment` fails its own reconciliation (see P5).
- For D-PAY-1 / PAY-E: the interim values tax income twice (see P3).

## Exact next step

None: the plan is merged. The next plan brings this note in from the local branch `docs/plan-54-merge-note` (no pull request
is opened for it), as this plan brought PLAN-53's.
