# PLAN-53 — final report

Payroll write policies, and a salary change that another person approves (IAM-B).
Plan: `docs/acceptance-plans/PLAN-53-iam-payroll-salary-change.md`. Prompt: `docs/acceptance-plans/execution/PLAN-53-AGENT-PROMPT.md`.
Contract for the plans that follow: `docs/PAYROLL_SALARY_CHANGE.md`. Checkpoint with every stamp: `reports/plan53-progress.md`.

## 1. Summary per phase

| Phase | What was done | Result |
|-------|---------------|--------|
| P0 | Baseline, the inventory of the sixteen writes and of the pay profile writers, the payroll gate in acceptance read from the code, the demo server's payroll picture (read-only) | `reports/plan53-baseline.md` |
| P1 | Every payroll write names its policy; `Payroll.MasterData`, `Payroll.ChangeCancel`; the ceiling list deleted; `PLAN-53` out of the guard's owners | unit 6353, architecture 344 |
| P2 | `PayProfileChangeRequest`, `PayrollReferenceChangeRequest`, the versioning rules, migration `PAY053_SalaryChangeRequests` (row security, filtered unique indexes, `Restrict` relations) | PostgreSQL tests as a non-superuser |
| P3 | The salary change: submit, decide, cancel, one applier, the month rule, the activation job, three outbox events, the branch scope, the API, the screens, the PUT alias; `UpsertEmployeePayProfileCommand` deleted | unit 6488, architecture 355, Testcontainers 22/22 |
| P4 | Salary scales and pay grades change by a request another person approves; a grade in use or a scale with grades is not deleted | unit 6540, architecture 358 |
| P5 | Payroll runs compute in acceptance (`AcceptancePayrollRegimePatch`); AC-23 and AC-25 rewritten on the request; AC-23, AC-24, AC-25 required | lab job: guard PASS |
| P6 | AC-IAM-53a..e on the prod-like stack; the independent review and its fixes; the Testcontainers step | see §11 |
| P7 | `origin/main` did not move during the session (`4ce01f59`) | nothing to merge |
| P8 | The demo server: backup, build, deploy, the server checks | see §12 |
| P9 | Docs, this report, the release branch, the PR, CI, the merge | see §11 and §15 |

## 2. Commits

On `feature/plan-53-iam-payroll-salary-change`, from `origin/main` = `4ce01f59`:

| Commit | Phase | Message (short) |
|--------|-------|-----------------|
| `5089fc8c` | P0 | PLAN-67's merge record, rebuilt from GitHub (the agent's note was lost with the Windows machine) |
| `37717393` | P0 | the PLAN-53 agent prompt |
| `92fab7c6` | P0 | baseline, the payroll writes and pay profile writers, the payroll gate in acceptance, the demo's payroll picture |
| `351fecd1` | P1 | every payroll write names its policy; `Payroll.MasterData`; Payroll's ceiling list is empty |
| `7a422a78` | P2 | the two request entities, versions that carry every field, migration PAY053 |
| `6ad35c05` | P3 | a salary change is requested by a processor and approved by another person |
| `f9c83a71` | P4 | salary scales and grades change by a request another person approves |
| `32717c1d` | P5 | acceptance payroll runs pass the service-regime gate; AC-23, AC-24 and AC-25 required |
| `e71a9ed9` | P6 | the independent review's findings; AC-IAM-53a..e written and placed in the CI scope |
| `9baa4d09` | P6 | P6's start stamp |
| `c1798c3f` | P6 | AC-IAM-53a..e pass on the prod-like stack; review fixes; CI scope and ownership updated |
| `79d523f8`, `0640c2ac` | P9, P8 | the documents; the first server steps (the deployed tag is `0640c2ac6db0`) |
| the P8 and P9 commits | P8, P9 | the server checks and the deploy scripts' `tar`; this report |

The release branch `release/plan-53` is `main` + one `--no-ff` merge of the feature branch.

## 3. Decisions applied and deviations

Decisions D-IAM-2, D-PAY-6, D-53-1..5 and the prompt's P-1..P-19 were applied as written. The deviations:

**Deviations of the design (none defers a part of the plan):**
- **`ActivationCron` is an interval** (`Payroll:SalaryChange:Activation:IntervalMinutes`, 60): the repository has no cron library.
- **P-6's reading of «a month whose run is frozen, approved or posted»:** a run whose period holds the effective date, neither
  reversed nor cancelled, that covers the employee (a Regular run covers everyone; a run of another kind covers the employees on
  its lines), and is frozen, approved or posted, or a Regular run being calculated or validated. The acceptance seed holds frozen
  FinalSettlement and Correction runs with no line in 2026-11, 2026-12 and 2027-02 (measured, §8).
- **The scale-to-grades relation is `Restrict` too,** with the grade-to-profiles relation.
- **Seven writers of a pay profile are named,** two more than the plan lists: the separation and the disciplinary consumers
  (PLAN-64). `ApplySystemChangeAsync` is ready and no consumer calls it yet (PLAN-55, PAY-G).
- **`ExportJobs.Cancel` is declared a read** under `Module.Payroll` (it cancels its owner's export job, no payroll data), so the
  declared reads go from 22 to 23. `Payroll.ChangeCancel` also admits the `Payroll.MasterData` roles; the handler keeps the
  cancellation to the requester or an approver.
- **The plan's «existing exceptions report» does not exist;** the profiles outside a grade's bounds are shown by a new filter on
  the pay profiles screen and counted on the grade's page and on its change request.
- A version cannot start before the one it replaces; a grade's bound of zero is «not set».

**A deviation from the definition of done: AC-12 is not green and not required.** AC-12 submits a run for review without
calculating or validating it; the product answers 400 («يجب إكمال الحساب والتحقق من الإجماليات قبل الإرسال للمراجعة»). It was
Blocked for the same cause before this plan (PLAN-41's report), PLAN-59 owns its rewrite (§2 there, G-PAY-05), and the prompt
says AC-12 «does not change». The regime gate was never its cause. AC-23, AC-24 and AC-25 are green and required; AC-12's
ownership row says what was measured.

**Deviations forced by the machine (this session only, nothing committed):**
- The Windows machine was formatted; the plan ran on the owner's Ubuntu notebook (PowerShell 7, 15 GB RAM, a disk with
  9 to 18 GB free, a link of 14 KB/s to 750 KB/s). New scripts stay ASCII and PowerShell 5.1 compatible.
- **P-2's disk line (15 GB) could not be met:** the disk had 18 GB free at the resume and 14 GB when the images were due. The
  images were built one service at a time with the build-stage cache pruned after each, instead of all at once.
- **The registry's SDK image could not be pulled** (14 to 17 KB/s). The two local base images `acceptance-up.ps1` supports were
  assembled from the local `sdk:10.0` and `aspnet:10.0` images and the SDK 10.0.400 and runtime 10.0.11 installed on the host
  (the same Microsoft files). CI builds from the registry's images; the evidence of record is CI's.
- **`scripts/ci-local.ps1` and the leastpriv stage:** see §11.
- **Two incidents of the agent's own,** both recorded in the progress file: `personas.json` was written again while the first
  image was building, so one image carried another seed fingerprint (one check failed at the first stack start; one image
  rebuilt, about 18 minutes lost); and **the disk filled completely for about a minute** while the migrator image was built with
  8.4 GB free (freed by pruning the build cache; the repository, the owner's dev containers and the other projects' containers
  were checked after it and were unharmed).

## 4. The policies, before and after

Before: none of the sixteen actions named a policy; the module filter alone guarded them (`Module.Payroll` and `Payroll.Write`
= Admin or `payroll`), so the `payroll` role alone — the approver, the poster, the reverser, a reader — could write each.

| Action | Before | After |
|--------|--------|-------|
| `CompensationTypes.Create / Edit / Delete` | module filter | `Payroll.MasterData` |
| `DeductionTypes.Create / Edit / Delete` | module filter | `Payroll.MasterData` |
| `PayGrades.Create / Edit / Delete` | module filter, direct write | `Payroll.MasterData`; submits a reference change request |
| `SalaryScales.Create / Edit / Delete` | module filter, direct write | `Payroll.MasterData`; submits a reference change request |
| `EmployeePayProfiles.Create` | module filter, direct write | `Payroll.Process`; submits a salary change request |
| `PayrollRuns.Create` | module filter | `Payroll.Process` |
| `PayrollReports.BulkPayslips` | module filter | `Payroll.Process` (D-53-5) |
| `ExportJobs.Cancel` | bare `[Authorize]` | `Module.Payroll`, named; the owner check stays |

`Payroll.MasterData` = `hr-admin`, `payroll-processor` (from `Authorization:Policies:Payroll.MasterData:Roles`, in the three
settings files and mirrored in the prod-like stack). `Payroll.Process` = `payroll-processor`. `Payroll.Approve` =
`payroll-approver`. `Payroll.ChangeCancel` = whoever passes one of the three; the handler then requires the requester or an
approver.

New actions: `PayProfileChangesApi.Submit` and the PUT alias `PayrollApi.UpsertPayProfile` → `Payroll.Process`;
`PayProfileChangesApi.Approve / Reject / ActivateDue`, `PayProfileChanges.Approve / Reject`,
`PayrollReferenceChanges.Approve / Reject` → `Payroll.Approve`; `PayProfileChangesApi.Cancel`, `PayProfileChanges.Cancel`,
`PayrollReferenceChanges.Cancel` → `Payroll.ChangeCancel`. `activate-due` is also central-only.

Guards: `tests/architecture/write-policy-allowlist/payroll.txt` is deleted, Payroll's ceiling is 0, `CeilingList.Owners` =
`IAM-F`, `IAM-H`, and `GuardSelfTests` proves that a row signed `PLAN-53` is refused. `PayrollWriteActionsTests` fixes each
action's policy and proves who reaches it role by role.

## 5. The salary change

**States** (`PayProfileChangeState`): `PendingApproval` 0, `Scheduled` 1, `Applied` 2, `Rejected` 3, `Cancelled` 4,
`Superseded` 5. Open = 0 or 1; one open request per employee (a filtered unique index decides a race).

| From | Action | To | Who |
|------|--------|----|-----|
| — | submit | `PendingApproval` | `Payroll.Process` |
| `PendingApproval` | approve, effective today or earlier | `Applied` | `Payroll.Approve`, not the requester |
| `PendingApproval` | approve, effective later | `Scheduled` | `Payroll.Approve`, not the requester |
| `PendingApproval` | reject (a note is required) | `Rejected` | `Payroll.Approve`, not the requester |
| `PendingApproval`, `Scheduled` | cancel (a reason is required) | `Cancelled` | the requester, or an approver |
| `Scheduled` | its date comes (the job, or `activate-due`) | `Applied`, or stays `Scheduled` with `LastApplyError` | the system |
| `PendingApproval`, `Scheduled` | an automatic change of the same employee | `Superseded` | the system |
| — | an automatic source (`ApplySystemChangeAsync`) | `Applied` at once, no decider | the system |

**HTTP answers** (the API and the screens answer alike; a refused form shows its page with the same status): 201 a request (202
on the PUT alias, with `requestId` and `state`); 200 a decision; **400** a broken rule (not the first day of a month, before the
current month, outside the grade's bounds with both bounds named, no reason, no legal reference, nothing proposed); **409** a
second open request, a decision that lost a race, a month whose run is settled (the message names the month), a profile changed
since the request; **403** the requester deciding his own request, or a cancel by neither the requester nor an approver;
**404** outside the caller's agency or branch. A refusal changes nothing.

**The applier** (`IPayProfileChangeApplier`) is the only place that creates a version for a change, in one save: it closes the
previous version the day before, writes its history row, creates the new version, marks the request applied and stages
`IPayProfileChangeApplied`. Copied from the previous version: `TenantId`, `EmployeeId`, `OrgUnitId`, `RegimeCode`,
`BankAccountIban`, `BankName`, `BankAccountVerifiedAt`; set by the change: `BaseSalary`, `PayGradeId`, `EffectiveFrom`,
`EffectiveTo`, `IsActive`; deliberately not copied: `Id`, `CreatedAt`, `UpdatedAt`, the `PayGrade` navigation.
`Iam053ApplierCopyForwardTests` reads the entity by reflection: a new property must be copied or named, or the test fails.

**The month rule** uses Damascus dates (`IPayrollCalendar`, `Localization:TimeZone`): at submit the first day of a month and
not before the current month; at approval and at activation P-6's rule (§3).

**The events** (outbox, schema version 1): `IPayProfileChangeSubmitted` (no amount), `IPayProfileChangeDecided`,
`IPayProfileChangeApplied`. The publish and the one save are in the same method (PLAN-40's guard).

**The activation job** (`PayProfileChangeActivationJob`): every 60 minutes, one replica (advisory lock `4_001_013`), each
agency under its own tenant, every due request once a cycle, a refused one left `Scheduled` with its Arabic reason and counted
by the gauge `payroll_salary_change_apply_blocked`. Results of its tests: a due request is applied once though the job runs
twice; three agencies, each in its own tenant, as a non-superuser role; a failing agency does not stop the others.

**The scope:** see §7.

## 6. Scales and grades

A create, an edit or a delete of a salary scale or a pay grade submits a `PayrollReferenceChangeRequest` (the current and the
proposed values as JSON of an allowlisted record, a reason and a legal reference, both required); a payroll approver who is not
the requester approves or rejects; an approval applies the change in its own save. One open request per record. New bounds
change no pay profile: the profiles they leave outside are counted on the request's page before the approval and listed by the
«خارج حدود الدرجة» filter.

Delete protection: a grade that a pay profile names (any version) or an open salary change names is refused at the request and
again at the approval (409, «لا تُحذف الدرجة…»); a scale with grades likewise; and the database refuses it too (both relations
`Restrict`, migration PAY053). `PayrollReferenceWritersTests` names the only writers of a scale or a grade.

With `Payroll:MasterData:RequireApproval=false` (Development only; a start-up warning says so elsewhere) a request is recorded
`Applied` with no decider.

## 7. The scope

| Controller | Stance |
|------------|--------|
| `PayProfileChangesApiController`, `PayProfileChangesController` | `[OrgScoped(typeof(PayProfileChangeRequest))]`, key `OrgUnitId` |
| `EmployeePayProfilesController` | `[OrgScoped(typeof(EmployeePayProfile))]`, key by `EmployeeId`: the unit of his active profile |
| `activate-due` | `[OrgScopeCentralOnly]` |
| `PayrollReferenceChangesController`, `SalaryScalesController`, `PayGradesController` | `[OrgScopeNotApplicable]`: the agency's reference data (the counts of pay profiles on their pages follow the caller's branch) |

Keys (`PayrollOrgScopeKeys`): ids `employeeId`, `requestId`; not keys `payGradeId`, `proposedPayGradeId`. A record with no unit
is central and hidden from a restricted caller. Rows that left the lists: `EmployeePayProfiles` from the org-scope list
(24 → 23) and five rows from the entity-binding list (16 → 11: `EmployeePayProfiles.Create` and the Create and Edit of
`PayGrades` and `SalaryScales`). `Iam053PayrollScopeHttpTests` (PostgreSQL) is PLAN-52's generated test on Payroll.Web plus a
restricted user's walk: a foreign request or employee is 404 on every action and absent from every list and tile.

## 8. Payroll in acceptance

**Measured on the lab stack** (HR.Core's log at the first start is the «before»; SQL the «after»):

| Agency | Current employment records without a regime, before | Open regime exceptions (any type), before | Without a regime, after | `law50` after | Transition rows written |
|--------|------|------|------|------|------|
| AGENCY-A | 1138 | 0 | 0 | 1138 | 1138 |
| AGENCY-B | 261 | 0 | 0 | 261 | 261 |
| AGENCY-C | 132 | 0 | 0 | 132 | 132 |

The acceptance seed holds no `ServiceRegimeExceptions` row at all, so nothing was resolved there; the resolving path is proved
by `Iam053AcceptanceRegimePatchTests` (the patch writes exactly what `ResolveAsync` and the transition service write, proved by
running both on equal data). `AcceptancePayrollRegimePatch` runs in the Acceptance seed only (`AcceptanceSeedOrchestrator`),
each agency under its own tenant, one save per agency, idempotent. It publishes nothing.

**The run that passed the gate:** AC-23, AC-24 and AC-25 each create, calculate and validate a Regular run for agency A on the
lab stack; AC-IAM-53a does on the prod-like stack, where the national-scope and legal-control guards are enforced (1102 lines,
validated, frozen). `DATA-PAYPROFILE` still counts 1489. The classes that read or set a regime keep their meaning
(`AcHcm29` 3/3, `AcIam50` 5/5; `AcS2s42`, `AcMsg40`, `AcInt37`: §11).

PLAN-54's step 0.7 carries «نُفّذت في PLAN-53» with these numbers.

## 9. The existing tests that changed

| Test | What changed | The meaning it keeps |
|------|--------------|----------------------|
| `Pay023FrozenRunIsolationTests` (PostgreSQL) | The profile is changed by the manual request (its approval is refused in the frozen month: 409, state pending, nothing written) and then by `ApplySystemChangeAsync` **inside** the frozen month | A frozen run is isolated: recalculation refused, its line unchanged; the next month's run reads the new salary |
| `Ac23PayrollFreezeTests` (lab) | The raise is a request through the PUT alias (202); the approver's approval is refused for the frozen month; the months follow the service's clock (13 and 14 months ahead) | A frozen run is isolated from a pay change, proved twice (the change cannot enter the month; the run refuses recalculation and keeps its net). Its old last assertion (a later run's net differs after a direct write) became «the request is still pending and unapplied after a later run»; that a run holds an applied change is AC-IAM-53a's and `Pay023`'s |
| `Ac25FraudHoldTests` (lab) | The fixture is a request from the first day of the current month, approved by the approver (applied); the run is the current month's | Every assertion as it was: the fraud hold blocks pay until a four-eyes release |
| `WriteActionsHaveExplicitPolicyTests`, `GuardSelfTests`, `iam051-validate.ps1`, `ux067-validate.ps1` | Payroll's ceiling 0 and no list; the planted rows moved to an owner that still exists | The guards refuse what they refused, and now a `PLAN-53` row too |
| `Iam053PayrollScopeHttpTests` seed (this plan's own) | The seeded request names the profile version it was made against | As the product writes it |

`AC-12` and `AC-24` were not changed.

**Date limits to know:** AC-25 uses the current month, AC-24 a fixed April 2027 and AC-12 a fixed June 2027; AC-25 will meet a
live run of its month in April and June 2027 (PLAN-58 and PLAN-59 rework these scenarios).

## 10. The guards and their own negative tests

- `PayrollWriteActionsTests` (architecture): each of the sixteen and each new action with its policy; reach per role; the
  settings and their mirror.
- `PayProfileWritersTests` (Roslyn): seven named writers of `EmployeePayProfile`; a new writer fails; a stale name fails.
- `PayrollReferenceWritersTests` (Roslyn): the writers of `SalaryScale` and `PayGrade`.
- `Iam053ApplierCopyForwardTests`: a new property of the profile must be copied or named.
- `GuardSelfTests`: a planted row signed `PLAN-53` is refused.
- `scripts/iam053-validate.ps1 -SelfTest`: twenty planted defects, each refused (a ceiling list that comes back, a direct
  salary write, a setting without its mirror, the migration, an event missing from a document, the scope filter, the regime
  patch called outside the acceptance seed, AC-IAM-53 not required, AC-23 not required, …).
- The acceptance guard: a required class that is Blocked or Fail fails the job.

## 11. Results

| Suite | Result |
|-------|--------|
| Build `dotnet build HRManagement.slnx -warnaserror` | 0 errors, 0 warnings |
| Unit | **6562 / 6562** |
| Architecture | **358 / 358** |
| Integration, Testcontainers, CI's filter (`Category=Testcontainers&Category!=ObjectStorage`) | **248 passed, 1 skipped, 0 failed** |
| `scripts/iam053-validate.ps1` and `-SelfTest`; `iam050`, `iam051`, `iam052`, `ux067` validate scripts | pass |
| Lab job (fresh stack): AC-23, AC-24, AC-25, `AcHcm29HireContractTests`, the lab journey | guard PASS; AC-12 Blocked, not required |
| Prod-like job (fresh stack): `AcIam53SalaryChangeTests` 5/5, `AcIam50` 5/5, `AcIam51` 7/7, `AcIam52` 5/5, `AcHcm25` 8/8, the prod-like journey 1/1 | all passed |
| `AcUx67ReadableInterfaceTests` (browser, three payroll pages added to its key pages) | 7/7 |
| Prod-like `isolated` stage: `AcMsg40` 8/8, `AcWf44` 5/5, `AcWf45` 8/8, `AcWf46` 9/9, `AcPol47` 1/1, `AcOps39` 8/8, `AcInt37` 8/8 (with the journey before it; 7/8 when the stage was first run alone, `Ac37b` Blocked by that order) | all passed |
| `s2s42` stage: `AcS2s42TrustBoundaryTests` | 8/9 locally: `Ac42i` needs `kubectl`, which this machine does not have (CI's runner has it) |
| `leastpriv` stage (PAY053 from empty volumes under the least-privilege roles): `AcOps38FirstBootTests`, `AcTen48LeastPrivilegeTests` | guard PASS |
| Lab job again after the review's fixes: AC-23 (months by the clock), AC-24, AC-25, `AcHcm29` 3/3 | guard PASS; AC-12 Blocked, not required |
| Server: `AcIam53VpsChecks` 3/3, the PLAN-36..67 server suites, the smoke | §12 |

The Testcontainers step and the acceptance classes were run on the owner's Linux machine from locally built images (§3); the
stage names are those of `tests/acceptance/ci-scope.json`. The full suite runs once, in CI (the owner's rule of 2026-11-06).

**The independent review** (one fresh agent, read-only, before the prod-like record run): no high finding; four medium and six
low. Fixed with tests: a person's request is applied only over the profile it was written against; the activation meets every
due request once a cycle; a planned end is carried and closing never extends a period; counts on a grade's page follow the
caller's branch; the decimal binder accepts no thousands separator; a grade-only change of a profile paid its grade's base is
checked with that base; AC-23's months follow the clock; `Pay023` applies the automatic change inside the frozen month.
Decided and tested: a version replaced on its own first day holds no day (a correction in the same month stays possible).
Recorded and mitigated, not changed: a Regular run that is calculated and not frozen does not block a change of its month
(P-6's letter); the approval answers with a warning, and the gap in the run's validation is PLAN-56's (§14).

**A consequence of making AC-23, AC-24 and AC-25 required:** their committed evidence is still the «Fail» of the CI run before
this plan, and evidence is never committed from a local run (D-QA-20). A narrowed local run that does not include them copies
that evidence in, and its guard reports the three. The full run in CI rewrites them. Until the next evidence-of-record commit,
a narrowed local run should include the three, or be read by its class table.

**The evidence of record** is the acceptance artifact of the PR's CI run (never `acceptance-evidence/AC-IAM-53/` from a local
run; nothing of the local runs is committed). Its link is in the PR and in the merge note of `reports/plan53-progress.md`.

## 12. The server

**Before (read-only, P0 and again before the deploy):** tag `1ee455bf1a6f`, 22 containers, 37 GB free; 425 active pay
profiles of 425 (checksum of employee and salary `3b2e10719627b05f17069bfca7a784f2`), 10 grades, all in use, 1 scale, 6 runs
(2026-01 to 2026-05 settled, 2026-06 calculated and not frozen); the users `e2e-payroll-processor` and `e2e-payroll-approver`
present and enabled; `hradmin` holds `hr-admin`.

**Would a Regular run be refused on the demo (P-16)? Yes.** The demo holds 500 current employment records without a regime
and 500 open `UnclassifiedRegime` exceptions, so the service-regime gate refuses a new Regular run. The acceptance patch does
not run on the demo, by design; classifying the demo's data is not this plan's.

**The backup:** `/opt/hrm/backups/pre-plan53-20261122T224310Z` (700; `pg_dumpall.sql` 33.3 MB, `env.backup`, `SHA256SUMS`,
each 600). Verified: both sums OK, 17 `CREATE DATABASE` lines, the dump complete.

**Build and deploy, twice:**

| | First tag `0640c2ac6db0` | Final tag `27cbf35db9cc` |
|-|--------------------------|--------------------------|
| Upload | 31.0 MB in 139 s | 31.0 MB in 22 s |
| Build (detached, `.done` file) | 1,542 s, push 44 s, 17 of 17 images | 2,035 s, push 34 s, 17 of 17 images |
| Deploy | 23:49:30Z to 23:54:06Z: pull 18 s, up 149 s, bootstrap 63 s (PAY053 applied) | 01:47:39Z to 01:51:27Z: pull 13 s, up 138 s, bootstrap 60 s, reconcile 13 s, healthy 15 s |
| Services away | about 3 minutes | about 3 minutes |
| Smoke (`-RequireIntegrationWiring`) | 50 pass, 0 fail, 1 warn | 50 pass, 0 fail, 1 warn |

- The first build was started by `build-on-host.ps1` attached to its ssh session (as that script is written); it was stopped
  after ten minutes and the same server script run detached. A first deploy attempt stopped on the owner's machine before it
  touched the server: `make-deploy-bundle.ps1` called `tar.exe` (fixed: `tar` when not on Windows).
- **Why a second tag:** the new browser check found that the four payroll screens with a form raised one
  `style-src-attr` violation each (an empty `asp-validation-summary="All"` writes `<li style="display:none">`). Fixed in the
  twelve payroll forms (`Views/Shared/_FormErrors.cshtml`), proved by an HTTP test and by AC-UX-67 (a form is now one of its
  key pages; 7/7 on the prod-like stack with AC-IAM-53 5/5 again), built and deployed. The prompt's «build once» was not kept,
  for a defect the server check exists to find.

**The checks on the final tag (`AcIam53VpsChecks`, evidence under `artifacts/plan53/vps/`, not committed):**
1. *The policies:* PAY053 recorded, row security on both tables; as `e2e-payroll-approver` `EmployeePayProfiles/Create`,
   `DeductionTypes/Edit` and `PayGrades/Delete` answer 403; the processor's approvals answer 403; no row written. **Passed.**
2. *A salary change:* as `e2e-payroll-processor`, through the API, from the first day of next month (Damascus), the marker
   as its `SourceReference` → 201 `PendingApproval`; his own approval 403; `e2e-payroll-approver` approves → `Scheduled`, the
   employee's profiles as they were, the outbox sequence advanced; cancelled → `Cancelled`. **Passed.**
3. *A reference change:* the processor requests a grade's higher maximum (pending, the grade as it was); his own approval
   403; the approver rejects with a note → `Rejected`. *A grade in use:* its delete request → 409 «لا تُحذف الدرجة…», the grade
   as it was. **Passed.**
4. *The screens:* eight payroll screens in a real Chromium as the processor (the two lists, the two details, the request
   form, the grade's details and edit form, the outside-bounds list), at two sizes: HTTP 200, one policy, **no violation**
   each; Arabic and right-to-left; the requester named by the registry's span, no user id printed. **Passed.**
5. *After the checks:* no request open; 425 active profiles of 425, checksum `3b2e10719627b05f17069bfca7a784f2`; the grades'
   rows as before; 6 runs; no history row. Sign-in (`AC_OPS_36b`) and the portal check pass on the final tag.

**The PLAN-36..67 server suites** ran on the first tag (the final tag differs by the payroll views only): `AcOps36VpsBootTests`
without 36j, `AcInt37VpsServerTests` and the server checks of PLAN-38..67 — 50 of 55 in the run (44 min 52 s), 53 of 55 after
three were run again alone (`AC_WF_46` sweeps after a sweep cycle, as in every plan; a page-load timeout over this link; and
one check disturbed by the agent switching on ssh connection reuse mid-run). The two not passed, both for causes on the
owner's machine, not on the server: `AC_POL_47_vps_the_migration…` reads a «before» file that was an uncommitted artifact of
the formatted Windows machine (its assertions before that line passed); `AC_UX_67_vps_every_key_page…`, a five-minute browser
tour, met `net::ERR_NETWORK_CHANGED` twice because a dev container of the owner restarts every minute on this machine (the
same tour passes on the local prod-like stack, and check 4 above covers the payroll screens on the server).

**Changes on the server, and their undo:**

| Change | Undo |
|--------|------|
| The backup folder `pre-plan53-20261122T224310Z` | delete it when no longer needed |
| Release `27cbf35db9cc` (previous `0640c2ac6db0`, before it `1ee455bf1a6f`); migration `PAY053_SalaryChangeRequests` | `scripts/vps/deploy.ps1 -Tag 1ee455bf1a6f` (the rollback note below) |
| Local images of the releases `d0e446fe3280` and `92a69960c826` removed before the second build (28 GB → 40 GB free) | `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` (they are in the loopback registry) |
| Three salary change requests `Cancelled` and two reference change requests `Rejected` (the checks; one of each per run, and one salary request from a run that failed on the test's own expectation), with their audit rows | none: requests are records; none is open |
| Build files `/opt/hrm/build-<tag>.start|log|done`, `build-on-host-<tag>.sh` for the two tags | `rm` them; the uploads were removed by the build |

**The rollback note** (in `docs/OPS_VPS_SINGLE_HOST.md`): PLAN-67's tag never reads the two new tables; the extra migration
row does not harm it; its direct salary write comes back (G-IAM-02 as it was); its delete of a used grade, or of a scale with
grades, becomes an error (500 from the `RESTRICT` constraints) instead of silently emptying the profiles' grade; scheduled
requests wait for this plan's job and are refused at activation if the profile changed meanwhile.

The demo is up on `27cbf35db9cc`, 22 containers, 20 of 20 health checks healthy, 32 GB free.

## 13. Changes for API clients

`PUT api/payroll/employees/{employeeId}/pay-profile` no longer writes a pay profile. It submits a salary change request (source
`Api`) and answers **202** with `{ requestId, state }` (`state` = `PendingApproval`); another person approves it through
`POST api/payroll/pay-profile-changes/{id}/approve`. Its body keeps the old field names (`baseSalary`, `payGradeId`,
`effectiveFrom`, `changeReason`) and adds `legalReference` (required by default). `effectiveFrom` must be the first day of a
month, not before the current one; the old `effectiveTo` and `isActive` fields are ignored. The policy is `Payroll.Process`, as
before. New: `api/payroll/pay-profile-changes` (list, get, submit 201, approve, reject, cancel, `activate-due`). An approval's
answer may carry `warning`.

## 14. Items other plans own, and defects for later plans

**Other plans own:**
- The consumers that still write a pay profile directly, named in `PayProfileWritersTests`: contract activation and employee
  creation (PLAN-55), transfer (PAY-G), separation and disciplinary action (PLAN-64). They do not supersede an open request;
  since the review's fix a person's request is refused (409) when the profile changed under it.
- Manual allowances, deductions and adjustments with a second person; the statutory deductions and holidays inside the
  framework's version (PLAN-54). `CompensationTypes` and `DeductionTypes` now need `Payroll.MasterData` but are still written
  directly.
- **AC-12's rewrite** (PLAN-59, G-PAY-05); the strength of AC-23..25 as proofs (PLAN-58, PLAN-59).
- A bank account as its own maker-checker entity (PLAN-55); retroactive effect and proration inside a month (PAY-H); a
  delegate for `payroll-approver` (WF-F); a branch-restricted payroll persona (wave 2; the scope holds already).

**Defects for later plans:**
- **PLAN-56 — the version a run reads:** the engine reads the active profile only and prorates only when
  `0 < employee days < period days`, so an earlier month calculated after a later month's change has been applied is paid the
  full new salary.
- **PLAN-56 — validating a calculated run after a change of its month:** `ValidatePayrollRunHandler` reconciles the lines with
  their input snapshots and does not compare them with the profile in force; a run calculated, then a change applied in its
  month, then validated without recalculation, freezes the old salary. Older than this plan (a direct edit did the same). This
  plan warns at the approval.
- **A decimal form field with a fraction fails to bind under the service's Arabic culture, in every service** («8250.75» is
  read with the culture's separator). This plan's forms bind through `HrInvariantDecimalModelBinder`; the app-wide fix is for a
  UX plan.
- `scripts/policy-inventory.ps1` could not read the catalog since PLAN-67 (fixed here: its pattern tolerates the `NameAr` line).
- The acceptance regime patch classifies, at every HR.Core start of the Acceptance profile, every current record without a
  regime in the three agencies, records made by tests included, and every current record of an employee who has more than one.
- The demo holds 500 current employment records without a regime and 500 open `UnclassifiedRegime` exceptions (§12).
- The local acceptance job's Chromium step uses `--with-deps` (sudo) on Linux; on a developer's Linux machine it stops. The
  browser class was run with the installed Chromium instead.

## 15. Timing

All times UTC. The stamps are in `reports/plan53-progress.md`.

| | |
|-|-|
| Started | 2026-11-21T00:15:16Z |
| Stop 1 | 2026-11-21T00:16:30Z to 2026-11-22T12:32:25Z — **36 h 15 min 55 s.** Cause: `BLOCKED:` on the preconditions (the plan's Windows machine had been formatted; this machine had no push rights and no server access until the owner added its key) |
| Stop 2 | 2026-11-22T12:51:44Z to 15:16:33Z — **2 h 24 min 49 s.** Cause: the owner stopped a running command and the machine restarted |
| This report written | 2026-11-23T02:00Z |
| Wall-clock to this report | 49 h 45 min, of which **38 h 41 min stopped** and about **11 h 4 min working** |

Inside the working time, what was waiting rather than work (the phases overlapped these with writing code, tests and documents):

| Waiting for | Time |
|-------------|------|
| The registry's SDK image that never arrived (14 to 17 KB/s), stopped by the agent | 49 min |
| Local image builds: 15 images 31 min 30 s; one image again 3 min; 15 images again after the review 23 min 36 s; the migrator 21 min; payroll again 1 min 31 s | 81 min |
| Acceptance stacks: nine lab and prod-like starts of 14 to 15 min each (two were the agent's own redo), leastpriv 3 min | about 135 min |
| Acceptance, integration and unit runs | about 110 min |
| The server: two builds (25 min 42 s, plus ten minutes of the attached start; 33 min 55 s), two deploys (4 min 36 s; 3 min 48 s), the server suites 44 min 52 s, this plan's checks about 25 min | about 150 min |
| The independent review (ran beside the lab job) | 16 min |

**CI:** the wait for the PR's checks, the Actions minutes, the merge and the post-merge run are recorded in the merge note of
`reports/plan53-progress.md` (this report is committed before the push).

**`scripts/plan-timing.ps1 -From 25 -To 67`** (local time, UTC+3):

```

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
PLAN-53      13 2026-11-21 03:15 2026-11-22 15:34 2026-11-23 04:11  False 12:36
PLAN-66      12 2026-11-06 20:46 2026-11-06 21:03 2026-11-07 00:36  False 3:33 
PLAN-67      15 2026-11-13 11:06 2026-11-13 18:39 2026-11-22 15:34  False 212:…

Span = first commit -> merge (or last commit if not merged). WallClock = "Started (UTC)" in the progress file -> merge.
Note: without a merge commit, a later commit with the same plan scope (for example a live-run fix) extends that plan's End.
```

## 16. Readiness note for PLAN-54..59

- **The contract** is `docs/PAYROLL_SALARY_CHANGE.md`: the entity, the states and transitions, the answers, the events, the
  applier, the job, the settings. Allowed: new fields, new sources in `PayProfileChangeSource`, new events. Not allowed: a new
  meaning for a state or a transition, or a second writer of pay profile versions.
- **The names they take:** policies `Payroll.Process`, `Payroll.Approve`, `Payroll.MasterData`, `Payroll.ChangeCancel`
  (`HrManagementPolicies`, rules in `PayrollPolicyRules`); `PayProfileChangeResult` and its failure kinds for the HTTP status;
  `IPayrollCalendar` for «today» and «the current month»; `HrInvariantDecimalModelBinder` for amounts on forms;
  `PayrollChangeIndexNames`; the meter `HRManagement.PayrollSalaryChange`.
- **PLAN-54** (manual inputs with a checker): the same pattern — a request command and a decision command, the decider is not
  the requester, the publish and the save in one method. It adds no state to this entity.
- **PLAN-55** (the consumers): `IPayProfileChangeApplier.ApplySystemChangeAsync(employeeId, kind, source, sourceReference,
  proposedBaseSalary, proposedPayGradeId, effectiveFrom)`. No bounds check, no month rule; an active profile is required and
  the version may not start before the one it replaces; an open request is superseded. When a consumer moves, its name leaves
  `PayProfileWritersTests.AllowedWriters`, and the boundary day of a contract's activation stops being counted twice.
- **PLAN-56** (the run's state machine): `PayProfileChangeRules.FindBlockingRunAsync` reads the run's status as it is; change it
  there with the state machine. The two run limits of §14 are PLAN-56's.
- **PLAN-58 / PLAN-59:** AC-23, AC-24 and AC-25 compute and are required; AC-12 waits for its rewrite; the two date limits of §9.
