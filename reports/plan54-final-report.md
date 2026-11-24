# PLAN-54 — final report

The payroll legal framework and its reference data in production (PAY-A).
Plan: `docs/acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md`. Prompt: `docs/acceptance-plans/execution/PLAN-54-AGENT-PROMPT.md`.
Contract for the plans that follow: `docs/PAYROLL_CONFIG_VERSIONING.md`. Checkpoint with every stamp: `reports/plan54-progress.md`.

## 1. Summary per phase

| Phase | What was done | Result |
|-------|---------------|--------|
| P0 | Baseline; the readers and writers of the framework's tables and of manual inputs; the demo server's payroll picture (read-only) | `reports/plan54-baseline.md` |
| P1 | The data: statutory deduction rules, field sources, editors, a reference, status and exception on every value; fingerprint v2 beside the frozen v1; migration `PAY054_LegalFrameworkPacks` (existing rows by column defaults) | PostgreSQL tests as a non-superuser under forced row security |
| P2 | A framework is created from a template, cloned, or imported from a JSON pack, as a draft with its own date; which version governs a period; publishing never cuts back in time or into a posted period | unit 6713, architecture 371 |
| P3 | A v2 run reads its version and nothing general; the v1 path untouched; the insurance export and the partial disbursement read the version | identity proved on eight engine cases and on real runs |
| P4 | Three people approve, attest and publish; the interim gate on the cycle and on a calculation; the indicators | unit and PostgreSQL tests on seven instants around the expiry |
| P5 | A manual allowance, deduction or adjustment waits for a second person; every reader filtered | unit 6787, architecture 388 |
| P6 | AC-PAY-54a..f on the prod-like stack; the independent review and its fixes; the regression stages; the Testcontainers step | see §11 |
| P7 | `origin/main` did not move during the session (`53690e0e`) | nothing to merge |
| P8 | The demo server: backup, one build, deploy, the server checks | see §12 |
| P9 | Docs, this report, the local gates, the release branch, the PR, CI, the merge | see §11 and §16 |

## 2. Commits

Branch `feature/plan-54-pay-legal-framework-reference-data`, from `origin/main` = `53690e0e`.

| Commit | What |
|--------|------|
| `907c1cee` | PLAN-53's merge note, brought in as every plan brings the note of the one before |
| `6ae73571` | This plan's prompt |
| `1cc3e310` | P0: the baseline and the inventories |
| `72a48a4a` | P1: the data and fingerprint v2; migration PAY054 |
| `5abcecbb` | P2: create, clone, import; the governing version; publishing |
| `a5590be0` | P3: the engine, the export and the disbursement read the version |
| `0d1a8777` | P4: the cycle's governance and the interim gate |
| `18a25040` | P5: manual inputs with a second person |
| `1e74d54e` | P6, first part: the persona, AC-PAY-54a..f, the empty-database test, `pay054-validate` |
| `3bc94d27` | The review's fixes (see §3 and §11) |
| `27f5a010` | P8: the server checks, this report, the server's section, the plan's status |
| the commit after it | The resume rule reads the job's partitions (see §11); this report's last numbers |
| `14070764`, `83b99786` | Progress notes; `83b99786` carries P6's message |
| `0a926937` | The directory count with the new persona; the import script's default user |
| `a87a4522` | The documents, the server checks, the register is not rewritten by a pack that repeats it |

`27f5a010` changes documents and test code only. The commit after it changes one rule of the resume command and the run page
(§11): the server ran `83b99786a420` for every check of §12 and is built and deployed once more on the final commit after the
push (recorded in the merge note of `reports/plan54-progress.md`).

## 3. Decisions applied and deviations

No deviation defers a part of the plan.

**The governing version (P-6).** The version of a period is chosen by date among the published **and the superseded** versions: a
code that has an active version first, then the latest effective date, then the highest number, then the code and the id, so the
answer never depends on row order. A superseded version ends the day before its successor starts and still governs its own months.
The «active configuration» that end-of-service and the event consumers read is the version that governs **today** in the payroll
calendar (Damascus). `IsActive` is no longer read to choose.

**The cycle's order and its publisher (P-8).** Draft → approved (by a person who is not an editor) → attested (by a third person)
→ published (by the approver or the attestor, never an editor). The editors are recorded per version (created, imported, edited).
Approval is from a draft only and once (the old method re-approved an approved version and rewrote its approver).

**The name clash.** The plan's entity is `StatutoryDeductionRule`; the engine already had a rule of that name. The engine's rule
is `StatutoryDeductionEngineRule` now.

**The rest, each with its reason in the progress file (D1 to D24):**

- The effective date is inside fingerprint v2: a version approved for one date is not the same version for another.
- `expiresOn` is the exception's last day (active while today ≤ that day, in Damascus), as the prompt's dates say; the plan wrote
  «greater than».
- A draft of fingerprint v1 is not approved (it would publish a new legacy version); a new version is cloned from it.
- A refused calculation leaves one `Failed` job row with the reason, and the run stays a draft pinned to nothing.
- The pack marks `DED-SOC` and `DED-HEALTH` as insurance (`GOSI`) with the employer's 14% on `DED-SOC`, so a v2 run of the pack
  exports exactly what a v1 run exports today. Which body each deduction really belongs to is D-PAY-1 / PAY-E.
- The API has list, get and the four cycle acts beside the plan's four endpoints (the acceptance class and the server check run
  the cycle as three users).
- A manual input needs an employee who has a pay profile.
- **From the review (D22 to D24):** the interim gate stops a run, its resume and a correction run; it does not stop an
  end-of-service settlement or an amount derived from an event (refusing a message would park it in a retry queue for a legal
  reason no retry cures, and those amounts reach a payslip only through a run). An agency whose first version starts in the
  future has no governing version until that day. A version's priority rules are stored inactive and are in force through their
  version.

**Deviations of this machine (nothing committed for them):**

- The prompt file was pasted into the session; it was committed from the session's own text.
- Acceptance images are built one service at a time by a local helper (the disk), and the migrator image through
  `docker compose build` (no buildx here: the job's own `docker build` cannot read `COPY --exclude`).
- `scripts/ci-local.ps1` is not usable here (as in PLAN-53): the same step list is run natively in a fresh clone.
- AC-UX-67 (the browser class) is run with the job's environment by hand: the job's Chromium step needs `sudo`.

## 4. The framework

**The pack.** `docs/syr/payroll-framework-packs/schema.v1.json` is the contract (`payroll-framework-pack.v1`);
`law50-interim.json` is the interim pack of Law 50: nineteen fields, five brackets, four statutory deductions (`DED-TAX` 5,
`DED-SOC` 7 with the employer's 14, `DED-HEALTH` 2, `DED-PENS` 3), four holidays, three priority rules, seventeen decisions. Every
value is `Interim`: the tax values under `EX-STR2-TAX-INTERIM`, the rest under `EX-STR2-LAW50-UNCERTIFIED`.

**Its proof of identity (D-PAY-1: no number moves).**

- `Pay054FrameworkPackImporterTests` compares the pack with the seeds value by value (`PayrollRegimeSeed`, the Demo seed's
  deduction definitions, the acceptance seed's priority rules, the decision register).
- `Pay054EngineV2Tests`: on eight cases the imported pack pays, line for line, what the seeded version pays with the Demo seed's
  deduction types; the same holidays and working days for every month of 2026.
- `Pay054V2RunPostgreSqlTests`: the same employees and month on real runs in PostgreSQL, v1 then v2: identical lines and
  identical insurance export.
- `Pay054HashV2Tests` recomputes the three seeded versions' v1 fingerprints and gets the demo server's stored values.
- `scripts/pay054-validate.ps1` reads the seeds from their source and compares the pack again, statically.

**The importer's rules.** One JSON object, at most 512 KB, depth 16, 500 rows a table; an unknown property is refused; every value
needs a reference and a status, and an interim value an exception number; brackets ascend and the last is open-ended; codes,
dates and amounts are checked for form and scale; a code belongs to one regime. One transaction: a refused pack leaves no row (fifty
planted defects, each refused with the field's JSON path and an Arabic message that never repeats the file). Decisions are upserted
by their id; a row the pack repeats as it is is not written. A draft's content can be replaced by an import (`targetDraftId`).

**The API** (`api/payroll/frameworks`, every write with the antiforgery token): `GET` list and one; `POST` create (a regime's
template or a clone); `POST import`; `GET {id}/export`; `PUT {id}/effective-from`; `POST {id}/approve`, `legal-attest`, `publish`,
`activate`. A broken rule is 400, a concurrent change or a state conflict 409, no such version 404.

**The screens.** Payroll → Legal frameworks: the list, «إطار جديد», «استيراد حزمة قانونية», the version's page with its four
tables and nineteen fields (reference, status and exception for each; who edited, approved and attested, by name), the draft's edit
form (a view model with the invariant decimal binder: the old form could not bind `0.5` under the Arabic culture), and the four
acts. The dashboard says when no framework governs today and where to import one. The calculation's refusal names the import page.

## 5. Versions and periods

- **Publishing** (`PayrollConfigurationVersioning.TryPublish`): an approved version only, from its own effective date. Refused,
  with nothing changed, when the date is on or before the date of the code's published version (a publication never cuts back in
  time), or inside a period that has a posted run of the same regime (**D-PAY-13**; a posted run pinned to no version counts for
  every regime). On success the published predecessor becomes `Superseded` and ends the day before.
- **The resolver** (`ResolveGoverning`): see §3. Twenty-one cases in `Pay054VersioningTests`, among them: December is still
  version 1's after version 2 is published from January; two codes of a regime; a gap month answers «no framework»; ties.
- **The active configuration** is the resolver for today (Damascus).
- **A clone** takes the date the user gives (the old clone inherited its source's date, which made every new version retroactive).
- **Concurrency:** a version's row has a row version (`xmin`). An edit that lands between an approver's read and his save, an
  approval that lands under an editor's save, and two publications that supersede the same version: the late request answers 409
  and writes nothing (proved on PostgreSQL by landing the other request at the handler's first read of the clock).

## 6. The engine

| | A v2 version | A v1 version (every version that existed before this plan) |
|---|---|---|
| Statutory deductions | the version's `StatutoryDeductionRules`: `min(base, ceiling) × employee % + fixed`, on gross pay or the base salary | `DeductionTypes` marked statutory, on gross pay, as before |
| Tax brackets | the version's; a version without brackets is not published | the version's, else the built-in defaults |
| Holidays | the version's | the version's and the general ones |
| Priority rules of a partial disbursement | the version's; a decision that names another rule set is refused | the decision's rule set, else the active general rules |
| Insurance export | the employee's share is what the run deducted for the version's insurance rules (by the rule's id); the employer's share is each rule's employer percentage of the same capped base | 7% and 14%, and the deductions named «تأمين» |
| Fallback | none | unchanged |

`PayrollFrameworkReferenceData` is the one place that decides what a run or a correction reads beside its version: nothing for
v2. The «statutory» flag of a general deduction type is read-only on its screens.

## 7. Governance

| Act | Policy | Refused for |
|-----|--------|-------------|
| create, import, edit, set the date | `Payroll.MasterData` | a version that is not a draft |
| approve | `Payroll.Approve` | an editor of the version; a version that is not a draft; a legacy draft; a value without a reference; an interim value whose exception is not active today |
| legal attestation | `Payroll.LegalAttest` = the policy `Payroll:Legal:AttestorPolicy` names (`Payroll.Approve` by default: a second approver) | an editor; the approver; a cited decision that is not in the register; the interim gate; content that no longer matches the approved fingerprint |
| publish | `Payroll.Approve` | an editor; anyone but the approver or the attestor; no active attestation of the same fingerprint; the interim gate; the date rules of §5 |
| activate | `Payroll.Approve` | a version that is not published |

- **The interim gate.** `IPayrollLegalControlGuard.IsExceptionActive(id, asOf)` reads the exception's last day from the STR-002
  catalog; «today» is `IPayrollCalendar.Today` (Damascus). It applies at approve, attest and publish, and to a calculation: a run's
  start, its resume and a correction run are refused with «انتهى الاستثناء القانوني X» when an exception the version cites has
  ended. With no catalog to ask, nothing interim passes. The shared start-up check, which reads the status only, is unchanged.
- **The dates.** Tests on seven instants around the expiry: 31 January 2027 21:00 UTC is already 1 February in Damascus and is
  refused.
- **The indicators.** A run's page and its provenance say «إطار مؤقت — استثناء …» or «إطار أقدم من حزم الإطار القانوني»; the job's
  log too. The dashboard warns from sixty days before an exception ends, for every version that governs today (a superseded one
  included), and says «انتهى» after.
- **The service refuses to start** on an attestor policy name it does not know.

## 8. Manual inputs

- **Entering:** `CreateManualInputCommand` writes an allowance, a deduction or an adjustment as `PendingApproval`, origin
  `Manual`, with who entered it. The forms bind view models: a run id, a state or an origin sent on the form is ignored.
- **Deciding:** `DecideManualInputCommand`, `Payroll.Approve`. The person who entered it gets 403; a rejection needs a note; a
  decision is not taken twice (409); a system row is never decided.
- **The readers that feed pay, all on `Approved`:** the run's loader (the three kinds), the correction run's adjustments, the
  separation settlement's outstanding instalments. The medical consumer's «already handled» read takes `System` rows only.
- **The system writers** (consumers and seeds) write `Approved` / `System`, by the entity's and the column's defaults.
- `Payroll:ManualInputs:RequireApproval=false` is honoured in Development only.
- **The screens:** the three lists show the state, the origin, who entered and who decided, and the two decision forms for a
  second person.

## 9. The existing tests that changed, and the numbers that did not move

| Test | Change | The meaning it keeps |
|------|--------|----------------------|
| `Pay002ImmutableConfigTests` | calls the new signatures (a clone takes a date; publishing takes the version's own date) | an approved or published version is not edited; a run is pinned to a fingerprint |
| `Gov002LegalGovernanceTests` (two tests) | call `PayrollFrameworkGovernance.TryPublish`; the second uses a classified v2 version with a separate attestor | no publication without an attestation; one with a matching attestation writes the notice |
| `PayrollRegimeAndRulesTests` | the engine rule's new name | the rule's results |
| `Plan034ScopedIndexTests` | the three controllers' two new constructor arguments | the lists' branch scope |
| `OutboxAtomicityIntegrationTests` | its repository wrapper gained the new members | one save with the outbox |
| `Iam052ServiceScopeHttpTests` | its host takes optional settings and test services | the scope of every scoped action |

No assertion of an existing payroll result was changed. Unchanged and green: the golden pack tests, `PayrollEnginePipelineTests`,
`Pay021` determinism, `Pay023`, and on the lab stack AC-21, AC-23, AC-24 and AC-25 (v1 runs of the seed).

The two architecture lists that this plan touched only shrank: `org-scope-allowlist/payroll.txt` lost `PayrollConfigurations`;
`entity-binding-allowlist/payroll.txt` lost `PayrollConfigurations.Edit` and the three manual-input `Create` rows.

## 10. The guards and their own negative tests

- `PayrollWriteActionsTests`: the policy of every framework action and manual-input action, and who reaches it; the guarded
  reads; the attestor setting.
- `Pay054FrameworkWritersTests`: every writer of a version and its tables is named; a new one fails the test.
- `Pay054FrameworkPackImporterTests`: fifty planted defects in a pack, eight unreadable files; each refused, no row, no echo.
- `scripts/pay054-validate.ps1 -SelfTest`: twenty-nine planted defects (a pack value that differs from the seed, a missing
  reference, an exception outside the catalog, a migration with a byte order mark or a data statement, a default that would leave
  inputs pending, a setting or its mirror wrong, a framework write without the antiforgery attribute, the class not required, an
  attestor persona who can also edit), then the repository itself.
- AC-PAY-54a's first check is P-10's dated limit (§15).

## 11. Results

**Unit** 6791/6791 after the review's fixes (6787 before them). **Architecture** 388/388. Build `-warnaserror` 0 errors. Format
clean on every changed file.

**Integration** (MassTransit in memory and SQLite, CI's filter `Category!=Testcontainers`): **455 passed, 3 skipped, 0 failed**.
**Integration on PostgreSQL** (Testcontainers, CI's filter `Category=Testcontainers&Category!=ObjectStorage`, on the final code):
**264 passed, 1 skipped, 0 failed** (11 min 24 s). This plan's: `Pay054MigrationPostgreSqlTests` (existing rows keep their values
under a non-superuser owner and forced row security; another agency sees nothing), `Pay054FrameworkHttpTests` and its three
sibling classes (Payroll.Web itself, three users), `Pay054V2RunPostgreSqlTests` (identity on real runs; the gate; the race; manual
inputs; the disbursement), `Pay054EmptyDatabaseFirstRunPostgreSqlTests` (an empty database with `Seed:Profile=None`: employees
through Payroll's own consumers, the calculation refused until a framework is imported, approved, attested and published, then
calculated and pinned).

**Acceptance, local** (the full suite runs once, in CI):

| Stage | Result |
|-------|--------|
| prod-like, `AcPay54LegalFrameworkTests` | **AC-PAY-54a..f 6/6 Passed** at the first run on the stack |
| prod-like, the plan's list | `AcIam53SalaryChangeTests` 5/5, `AcIam51ExplicitPolicyTests` 7/7, `AcIam52BranchScopeTests` 5/5, `AcHcm25ProductionApprovalTests` 8/8, `AcUx67ReadableInterfaceTests` 7/7 in Chromium with three new key pages |
| prod-like journey and `isolated`, fresh stack | journey 1/1 and seven classes 47/47; guard PASS |
| lab, fresh stack | AC-21, AC-23, AC-24, AC-25, `AcHcm29HireContractTests` 7/7, the lab journey 1/1; guard PASS |
| leastpriv | `AcOps38FirstBootTests`, `AcTen48LeastPrivilegeTests` 9/9; guard PASS: PAY054 applied by the migrator's role |

**The independent review** (one fresh read-only subagent): two high findings, both verified in the code and fixed with tests
before the server build — «resume» did not pass the interim gate and could resume the row of a refused calculation (an empty run
marked calculated and frozen); and two scenarios of the new acceptance class recalculated a frozen run. Two medium ones fixed: a
version had no row version (the race of §5), and a version's priority rules were stored active (a rollback would mix them into a
legacy disbursement). The low ones: fixed, or recorded with their reason in the progress file.

**A defect of the review's own fix, found by the local CI steps and fixed before the push.** The first fix told «a job that
never started» by its run being pinned to no version. `Pay003ResumablePayrollRunTests.Snapshot_VersionConflict_Detected`, which
resumes a paused job that has its snapshot on a run not yet pinned, then got «this job never started» instead of the snapshot
conflict. The rule now reads the job itself: a job without a partition never took a snapshot and is not resumed.

**The evidence of record** is the artifact of the pull request's Acceptance run (D-QA-20: never a local run); its link is in the
merge note of `reports/plan54-progress.md`.

## 12. The server

See `docs/OPS_VPS_SINGLE_HOST.md`, «الإطار القانوني للرواتب وبياناته المرجعية (PLAN-54)», for what the demo users see, the change
table and the rollback note.

**Before** (P0, and again before the deploy, identical): tag `5aa61b78e3a6`; three v1 versions (checksum
`3efb4119f16c75a8e3daab570ef03f0b`); the decision register 17 rows (`4ee88def8114915fcd22ee453ec904fb`); no attestation; six runs,
none pinned; inputs 800 / 500 / 80.

**Changes, in order (each with its undo in `reports/plan54-progress.md`, «Server changes»):**

| # | When (UTC) | Change | Undo |
|---|------------|--------|------|
| 1 | 16:01 | The local images of the two older releases `27cbf35db9cc` and `1ee455bf1a6f` removed (29 GB free → 41 GB; the line is 30 GB before a build). They stay in the loopback registry | `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` |
| 2 | 17:04:49 | **Backup** `/opt/hrm/backups/pre-plan54-20261123T170449Z` (700; `pg_dumpall.sql` 35,538,324 bytes, `env.backup`, `SHA256SUMS`, each 600). Verified: sums OK, 17 `CREATE DATABASE`, the dump is complete | delete the folder when it is no longer needed |
| 3 | 17:19 to 17:56 | **One build**, detached, of `83b99786a420`: upload 100 s (32.7 MB), build 2,165 s, push 50 s, 17 of 17 images; disk 40 GB before, 17 GB at the lowest seen, 33 GB after | none needed |
| 4 | 17:56:44 to 18:02:07 | **Deploy** of `83b99786a420` (previous `5aa61b78e3a6`): pull 5 s, up 150 s, bootstrap 64 s with PAY054, reconcile 15 s, healthy 22 s; the services were away for about 3 minutes at most. Smoke: pass 50, fail 0, warn 1 | `scripts/vps/deploy.ps1 -Tag 5aa61b78e3a6` |
| 5 | 18:04 to 18:12 | What this plan's checks leave: one version `LAW_50_2004_CHK54`, approved, attested, unpublished; one rejected manual allowance dated inside a posted month | none needed (records) |

**After — this plan's four server checks (`AcPay54VpsChecks`), all Passed:**

1. **The migration.** PAY054 recorded; the three new tables have forced row security; the demo's three versions are v1 with
   their fingerprints and **the same checksum as before** (`3efb4119…`); the decision register `17|4ee88def…`; inputs 800 / 500 /
   80, every one `Approved` and `System`; the six runs as before, none pinned.
2. **The cycle (the inert version).** `e2e-payroll-processor` imported the real pack as `LAW_50_2004_CHK54` from 2026-04-01, the
   first day of the last posted month: a draft. Publishing it: 400. The processor's approval: 403. `e2e-payroll-approver`
   approved; his own attestation: 400. **`hradmin` attested** (he meets the attest policy in demo mode, so no user was added to
   the realm). Publishing: 400, «…تشغيل رواتب مرحّل تنتهي فترته في 2026-04-30» (D-PAY-13). The version stays approved, attested and
   unpublished: it can never govern a run. A pack with a 120% bracket: 400, the field named, no row. The other versions, the
   decision register (its checksum and each row's write time) and the runs are as before.
3. **A manual input.** An allowance for a demo employee, dated inside the posted month: pending and `Manual`; its author's
   approval 403; the approver rejected it with a note; nothing pending is left.
4. **The screens**, in a real Chromium as `e2e-payroll-processor`, at 1366×768 and 390×844: the frameworks list, the version's
   page, the import and create forms, the three manual input lists and the allowance form, a run's page and the dashboard —
   twenty loads, each HTTP 200 with one CSP policy and no violation. The version's page is Arabic and right-to-left, names its
   editor through the registry's span and prints no user id, says «مؤقت», names the exception and the statutory deductions.

**The inert version** is the only framework row this plan added to the demo. Its effective date is inside a posted month, so
D-PAY-13 refuses its publication under this release; its priority rules are stored inactive, so the release before this one would
not read them either.

**The rollback note** (in `docs/OPS_VPS_SINGLE_HOST.md`): PLAN-53's tag never reads the new tables and fields; it counts a pending
or rejected manual input, since it knows no approval (the check's allowance is dated inside a posted month and enters no run);
it computes a v2 version with the general tables and without the interim gate (the demo has no published v2 version); and it
picks the active configuration by the `IsActive` flag, not by date.

**A machine note.** The screens check passed at its fifth run. The owner's dev container `hrm-keycloak` restarts about every
minute on this machine and each restart changes a network interface under Chromium: `ERR_NETWORK_CHANGED`, and over HTTP/3 a
navigation that stalls until its timeout (the server's edge logged those responses as cut by the client; `curl` over TCP was
steady throughout). The check now probes one page at one size per step, repeats a step this machine's network cut (eighteen
times in the passing run) and launches Chromium without QUIC. What each page must show is unchanged.

**Everyone else is unchanged — the server checks of PLAN-36..53** (`AcOps36VpsBootTests` without 36j, `AcInt37VpsServerTests`
and the checks of PLAN-38..53; 19:53:35Z to 21:50:48Z, 7,033 s on a link of about 480 ms that day): **53 of 59 in the run, 58 of
59 after the reruns.** The six, each with its cause:

1. `AC_WF_46_vps_the_sweeps_run…` — no sweep cycle yet after the deploy, as after every deploy. Run again after a cycle:
   **Passed**.
2. `AC_UX_67_vps_the_policy_page…`, `…a_destructive_form…`, `…the_portal_is_styled…` — page loads cut by this machine's network
   under Chromium (30 s timeouts, `ERR_NETWORK_CHANGED`). Run again without QUIC: **Passed** (the portal at its third run).
3. `AC_UX_67_vps_every_key_page…` — the long tour of thirty key pages at two sizes, which PLAN-53 could not pass from this
   machine. With the page-by-page repeat it **Passed** (11 min 54 s, three repeats): every key page of the demo, this plan's three
   among them, carries one policy and raises no violation.
4. `AC_POL_47_vps_the_migration_the_trigger…` — **not passed, the same local cause as in PLAN-53:** it reads
   `artifacts/plan47/vps/fingerprint-before.json`, an uncommitted file of the formatted Windows machine. This plan touches no
   policy data.

`scripts/vps/smoke.ps1 -RequireIntegrationWiring` (inside the deploy): pass 50, fail 0, warn 1 (the port check from the client's
side, as before). The demo is up on `83b99786a420`, 22 containers, every health check passing.

**Two changes to the older server checks, for the machine that runs them** (they are not in CI's Acceptance job; what they
assert is unchanged): the browser checks of PLAN-53 and PLAN-67 launch Chromium without QUIC, and PLAN-67's tour uses the shared
`Ux067PageProbe.ProbeEachAsync`, which probes a page again when the client's own network cut its load.

## 13. Changes for API clients and operators

- **New API:** `api/payroll/frameworks` (§4). Every `POST` and `PUT` needs the antiforgery token of `GET …/antiforgery`.
- **Changed behaviour:** `POST api/payroll/runs/{id}/calculate` and the resume of a job answer 400 with «انتهى الاستثناء القانوني
  X» when the governing version's exception has ended, and with a message that names the import page when no version governs the
  period. A run's provenance gained `frameworkCode`, `frameworkVersionNumber`, `frameworkHashSchemaVersion`,
  `frameworkPredatesLegalPacks`, `interimIndicator`, `interimExceptionIds` and `jobLog`.
- **Manual inputs:** the three create forms write a pending input; `Approve` and `Reject` are new actions for `Payroll.Approve`.
- **Operators:** `scripts/pay054-import-legal-pack.ps1` imports a pack as a user through the gateway (credentials never stored or
  printed); `scripts/deploy-organization.ps1` prints the command. **The deploy step:** an agency with `Seed:Profile=None` has no
  framework until one is imported, approved, attested and published (`docs/PILOT_DEPLOYMENT.md`, step 3). **Two payroll approvers
  per agency** beside the payroll clerk (D-PAY-12).
- **Settings:** `Payroll:ManualInputs:RequireApproval` (true) and `Payroll:Legal:AttestorPolicy` (`Payroll.Approve`).
- **`scripts/pay054-validate.ps1`** for a reviewer who edits the pack, the migration or the settings.

## 14. Items other plans own, and defects for later plans

- **Final legal values (D-PAY-1, PAY-E).** Every value of the pack is interim. **The interim values tax income twice:** `DED-TAX`
  is 5% of gross pay as a statutory deduction, and the brackets tax the same income. The seeds do this today and the pack
  reproduces it exactly (a test pins it); the correction belongs with the signed values.
- **PLAN-56 (the run's state machine and reconciliation):** a run that holds any non-zero `PayrollAdjustment`, manual or written
  by a consumer, fails its own reconciliation («عدم تطابق صافي السطر»: it expects net = gross − deductions while the engine adds
  the adjustment). Older than this plan; found by this plan's real-run test. A completed run cannot be cancelled or calculated
  again by any product path (G-PAY-04).
- **PLAN-58:** the rest of G-PAY-09 (release, alerts, the threshold, role conflicts in the token).
- **PLAN-59:** AC-12's rewrite.
- **A new agency's first month:** no version governs before the first framework's date (D23); the operator's document says so.
- **The demo's service-regime gate** still refuses a new Regular run (PLAN-53 §12): not this plan's.
- **Recorded from the review, not changed:** two first publications of one code at the same instant both publish (the resolver
  still answers by date and number); an approval and a rejection of one manual input at the same instant are last-writer-wins;
  the migration's `Down` restores the register's global unique index (a rollback is by image tag, never by `Down`); an import
  rewrites register rows other versions cite (the register is outside every fingerprint, by P-5).
- **A dated item that is not this plan's:** the temporary coverage floor of `Payroll.Application` is reviewed by 2026-12-29
  (`QA-COVERAGE-TEMPORARY-FLOORS`).

## 15. The dated limit: 2027-01-31

The interim pack cites `EX-STR2-TAX-INTERIM` and `EX-STR2-LAW50-UNCERTIFIED` of `docs/str-legal/control-catalog.json`. Both end on
**2027-01-31**. From 2027-02-01, Damascus time:

- an agency whose governing version holds interim values **calculates no payroll** (the run, its resume and its correction are
  refused), and no interim version is approved, attested or published;
- `AcPay54LegalFrameworkTests` fails at 54a's first check, naming the two exceptions and the date, by design; the dated
  obligations check (`STR002-CONTROL-EXCEPTIONS`) warns thirty days before and fails CI seven days before;
- versions of fingerprint v1 (the demo's, the seeds') are not affected.

**The decision this asks of the owner, before that date:** renew or replace the two exceptions in the STR-002 catalog (a signed
decision of the roles the catalog names), or have the final values signed and publish a version of signed values (D-PAY-1,
PAY-E). The payroll dashboard starts warning on 2026-12-02.

## 16. Timing

All times UTC, 2026-11-23. The stamps are in `reports/plan54-progress.md`.

| | |
|-|-|
| Started | 07:26:43Z |
| Stop 1 | about 07:43Z to 08:39:12Z — **about 56 min.** Cause: the owner stopped the turn during a P1 test run, then wrote «اكمل» |
| Stop 2 | about 09:52Z to 10:46:11Z — **about 54 min.** Cause: the machine restarted (boot 10:43:07Z) during P3's PostgreSQL tests; the owner wrote «اكمل» |
| Stop 3 | about 13:15Z to 14:29:25Z — **about 75 min.** Cause: the owner stopped the turn in P6, then wrote «اكمل» |
| Stop 4 | 23:15Z to 23:31:06Z — **about 16 min.** Cause: the turn ended on its own while it waited for the local CI steps; the owner wrote «Try again» |
| This report written | 22:15Z; its last numbers 2026-11-24T00:15Z |
| Wall-clock to this report | 16 h 48 min, of which **about 3 h 21 min stopped** and about **13 h 27 min working** |

Phases (finished): P0 07:36:53Z · P1 08:50:07Z · P2 09:39:43Z · P3 10:56:44Z · P4 11:32:43Z · P5 12:04:28Z · P6 17:18:00Z ·
P7 nothing to merge · P8 22:12Z.

Inside the working time, what was waiting rather than work (the phases overlapped most of it with code, tests and documents):

| Waiting for | Time |
|-------------|------|
| Local image builds: 16 images 23 min 10 s; the payroll image again 1 min 50 s and 1 min 58 s; the migrator 25 min 14 s | 52 min |
| Acceptance stacks: prod-like 14 min 51 s and 16 min 39 s, lab 15 min 3 s, leastpriv 3 min 29 s (one prod-like start was repeated for a count this plan had to raise) | 50 min |
| Acceptance runs: AC-PAY-54 68 s; the plan's prod-like classes 5 min 37 s; the journey and the isolated group 15 min; lab 5 min; leastpriv 6 min 19 s | 33 min |
| Full unit runs (9 min 19 s, 7 min 41 s, 7 min 25 s, 7 min 8 s, and one aborted by a test host crash after 6 min); the Testcontainers step 13 min 4 s | 51 min |
| The independent review (ran beside the image build) | 11 min |
| The server: upload 1 min 40 s; one build 36 min 5 s and its push 50 s; one deploy 5 min 23 s with the smoke | 44 min |
| This plan's server checks: the migration, the cycle and the manual input 8 min 24 s; the screens check 27 min 28 s in its passing run, and 62 min in four runs this machine's network cut | 98 min |
| The older plans' server checks: 117 min in one run, and 24 min of reruns | 141 min |
| The local CI steps: a first run stopped by a stale compiler server (14 min), a second run of 41 min that found the defect of §11, then the integration step and the Testcontainers step again (30 min), and the run on the final commit | about 2 h |

**The largest avoidable cost was this machine's network under Chromium** (about 90 minutes): the owner's dev container
`hrm-keycloak` restarts every minute. The checks are now built for it (§12).

**CI:** the wait for the pull request's checks, the Actions minutes, the merge and the post-merge run are recorded in the merge
note of `reports/plan54-progress.md` (this report is committed before the push, as the local gates need).

**`pwsh -NoProfile -File scripts/plan-timing.ps1 -From 25 -To 67`** (run at 22:13Z; the script prints local time, UTC+3):

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
PLAN-54      13 2026-11-23 10:26 2026-11-23 10:27 2026-11-23 20:17  False 9:49 
PLAN-66      12 2026-11-06 20:46 2026-11-06 21:03 2026-11-07 00:36  False 3:33 
PLAN-67      15 2026-11-13 11:06 2026-11-13 18:39 2026-11-22 15:34  False 212:…

Span = first commit -> merge (or last commit if not merged). WallClock = "Started (UTC)" in the progress file -> merge.
Note: without a merge commit, a later commit with the same plan scope (for example a live-run fix) extends that plan's End.
```

## 17. Readiness note for PLAN-55..59 and PAY-E

- **Names to use.** A framework version is a `PayrollConfiguration`; `IsLegacyFramework` tells a v1 from a v2 version. The version
  of a day is `PayrollConfigurationVersioning.ResolveGoverning`; through the repository, `GetPublishedConfigurationForPeriodAsync`
  (a period) and `GetActiveConfigurationAsync` (today). `GetConfigurationByIdAsync` loads every table of a version.
  `PayrollFrameworkReferenceData.LoadAsync` is what a calculation reads beside its version: use it, never the general tables.
  `PayrollInterimGate.RefusalForRun` is the gate of any new calculation path.
- **The v2 container PAY-E fills.** A new value that governs pay goes inside the version: a column on `PayrollConfiguration` with a
  row in `PayrollConfigurationFieldSource.GovernedFields`, or a child table that implements `ILegallySourcedValue`; and into
  `PayrollConfigurationHashService.BuildV2Canonical`, the pack's contract (`PackContract`, `schema.v1.json`, the mapper both ways)
  and `CloneAsDraft`. `Pay054HashV2Tests` fails when a property of a version's tables is in neither the fingerprint nor the
  bookkeeping list, and `Pay054CloneCarriesEverythingTests` when a clone drops one.
- **The cycle a new framework goes through.** Import the pack with the final values marked `Signed` (no exception needed), under
  the same code, with an effective date after the last posted period; a second person approves, a third attests, the approver or
  the attestor publishes; the interim version is superseded the day before and keeps governing its own months.
- **Manual inputs.** A new input kind implements `IManualPayInput`, is created through `CreateManualInputCommand`, and every reader
  filters on `Approved` (`Pay054ManualInputApprovalTests` lists the readers).
- **PLAN-56** takes the run's state machine: the reconciliation defect of §14, cancelling a completed run, and the cut-off of
  inputs. The refusal row of a calculation (`Failed`, no snapshot, the run pinned to nothing) is a shape it must keep or replace.
- **PLAN-55 and PLAN-57..59** are untouched by this plan except through the names above; `Payroll.LegalAttest` is a policy they
  can reuse for a legal second look.
