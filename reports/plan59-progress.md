Started (UTC): 2026-11-27T05:19:45Z

# PLAN-59 progress (checkpoint)

Main folder: `/home/omar/HRManagement` (the owner's Linux notebook). Branch `feature/plan-59-pay-full-cycle-production-proof` from
`origin/main` = `708d2004`. Keep-awake: `systemd-inhibit` PID 3066561 (`artifacts/plan59/keepawake.pid`), stop by that PID at the end.

## Preconditions (P0, 05:19–05:22Z)
1. No other plan session working: five older Claude processes in this folder, all idle (no child processes); worktree list: this folder
   and the owner's `/home/omar/HRManagement-ghcr` (untouched). On `docs/plan-58-merge-note` at start.
2. `git fetch origin`: `origin/main` = `708d2004` holds `Merge pull request #59 from omardefdev/release/plan-58`.
3. Working tree clean (only this progress file, untracked).
4. `ssh -o BatchMode=yes hrm-vps whoami` → `hrm`; ED25519 host key `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto` (known_hosts).
5. `gh auth status`: `omardefdev` active, scopes `repo`, `workflow`.
- Machine at start: `/` 23 G free of 233 G; Docker images 17.7 GB, build cache 13.9 GB; 15.7 GB RAM; load 1.4 on 8 CPUs.

## P0 — baseline
- Commits: `a5d26bb7` (PLAN-58's merge note, cherry-picked), `2dfd4548` (this prompt, written from the session's text); `docs/plan-58-merge-note` deleted.
- Build 4 min 49 s, 0 warnings. Integration filter: SQLite 208/0/91 skipped (66 s); Testcontainers 299/0/0 (3 min 51 s). The fourteen green.
- Plan inventory 0.4: 22 SQL lines in two scripts. Reads: 4 missing (P-9's). `pol049-validate.ps1` FAIL (28), as on main.
- Demo picture 05:37Z: tag 25be64cf9fa8, 54 G free, checksums in `reports/plan59-baseline.md`.
- Durations: inventories ~5 min (grep-generated, no subagent); demo picture one ssh call.
Finished (UTC): 2026-11-27T05:42:00Z

## Decisions
- BlockRunRefusal names the first ten blocked employees **in the order of their ids** (so the name lookup asks HR.Core for ten ids, under
  its per-request limit), by display name, else by id when the lookup answers «غير متاح» (the lookup's placeholder, now a constant).

## Next step
P1: build; Syr007/Pay058 tests follow the new text; write Pay059RegimeBlockerTests and Pay059PayrollSodHandlerTests; run the filters.

Correction (UTC 2026-11-27T05:51:11Z): P0's «Finished» stamp above (05:42:00Z) was typed, not read; P0 actually ended at 05:38:47Z (read
with date -u just after its commit). The stamp is left as written; this line corrects it.

## P1 — the fourteen, the regime refusal, the handler's rules
- `PayrollRegimeGovernance.BlockRunRefusal(blocked, displayNames)`: the first ten blocked employees (by id) by display name, else by id,
  each with HR.Core's reason code, then «و{n} آخرون»; `DecideAsync` asks `IEmployeeLookup` (when registered) for those ten only; still
  decided before `BeginCalculating`. `EmployeeLookupLabels.Unavailable` names the lookup's «غير متاح».
- New: `Pay059RegimeBlockerTests` (4: BlockRun refuses by name, run as it was; no client → ClientMissing; no client + BlocksFinalPayroll=false
  → calculated; ten names and «و2 آخرون»), `Pay059PayrollSodHandlerTests` (2: creator → CreatorCannotApprove, nothing written; approver
  posts → PosterIsApprover before and after a closed review by two auditors, no batch; then the poster posts).
- Changed: `Pay058HeldEmployeeTests` (BlockRun: the new text, and the employee's id in it), `Syr007RegimeGovernanceTests` (the id and reason).
- Tests: plan filter + Pay059 on SQLite 214/0/91 skipped (53 s); with Testcontainers 305/305 (3 min 39 s); unit Syr007|Pay058 30/30;
  Pay059 + Pay058HeldEmployeeTests 13/13; solution build 0 warnings; format clean.
- CI picks the new classes in «Integration tests (MassTransit InMemory)» (`Category!=Testcontainers`); the four PostgreSQL ones carry
  `Category=Testcontainers` (the Testcontainers step).
Finished (UTC): 2026-11-27T05:51:11Z

## Next step
P2: the run read's revision/approvals/batch; GET payment-batches/{id}/orders; tests (shape, 404 across agencies, no account number, stance).

## P2 — the reads an SQL-free cycle needs
- `GET api/payroll/runs/{id}` (Payroll.RunRead, central-only): `revision`, `approvalsGiven` (different approvers of the present version),
  `approvalsRequired` (SeparationOfDutiesService with the run's total, as the approval counts), `paymentBatchId` (the run's disbursement
  batch, the posting's first; null before). Added at the end of `PayrollRunDetailsDto`.
- `GET api/payroll/payment-batches/{id}/orders?page=` (Module.Payroll, central-only by the controller's stance; 500 a page, by employee id):
  id, employeeId, salaryLineId, status, amount, holdReason, destinationPinned, fileSequence. No account number, no field named like one.
- Nothing else missing (the read inventory): no new write, no migration.
- Tests: `Pay059CycleReadsHttpTests` (PostgreSQL: shape, an earlier version's approval not counted, two required above 50,000,000, null
  batch before the posting, the account number absent and no property named account/iban/beneficiary, page 2 empty, unknown 404, another
  agency 404 on both, the auditor reads the run and is 403 on the orders) with Pay058DisbursementHttpTests and Lva057's reads: 4/4.
  Architecture (PayrollWriteActions, WriteActionsHaveExplicitPolicy, OrgScopeCoverage, GatewayAuditorReach, GatewayApproverReach,
  ModuleOverridesAreDeclared, Pay058SinglePaymentPath, InterfaceTextGuard): 303/303. Solution build 0 warnings; format clean.
- Docs: `docs/PAYROLL_RULES.md` lists both reads for API clients, and the BlockRun refusal's names.
Finished (UTC): 2026-11-27T05:59:43Z

## Next step
P3: scripts on the cancel API with their month from the class; testdata/acceptance/payroll-months.json + unit test; AcceptancePayrollMonths;
AC-21/22/23 [RequiredAcceptance], windows, clean-up, classification.

## P3 — the scripts without SQL, AC-21..23, the months
- `acceptance-payroll-determinism.ps1` and `acceptance-payroll-resume-chaos.ps1`: `Invoke-PayrollSql`, `Cancel-PayrollRunInDb`,
  `Cancel-TestRun` and `Reset-ReservedPeriod` gone; `Cancel-PayrollRun` = `POST api/payroll/runs/{id}/cancel` with a reason, the
  antiforgery header and its cookie from `GET api/payroll/run-lifecycle/antiforgery`, in a `finally`; month from `-PeriodStart`
  (mandatory) and the class's run from `-RunAId`/`-RunId`; the chaos script's psql reads stay. ASCII, parse clean. The plan's
  `Select-String` finds 0 lines (was 22).
- `testdata/acceptance/payroll-months.json`: the seed (posted 2025-08..2026-07; scenario runs 2026-08..2027-02), AC-LVA-57 (2025-01..07, its
  file named), AC-IAM-53 (current month), AC-PAY-54 (2031-01..07), 55 (2029-05..06), 56 (2028-01..06), 58 (2030), AC-12 and AC-PAY-59
  (D-PAY-22; main and cycle stacks), AC-21 window 2024-01..06, AC-22 window 2024-07..12, AC-23 (current+2, two months), AC-24, AC-25 (lab).
- `AcceptancePayrollMonths`: the registry, `WindowOf`, `CycleCandidates` (D-PAY-22), `AfterNextCandidates` (current+2, seed included),
  `CreateRunAsync` (201, or skip at the live-period refusal; none left → RequireData asking for acceptance-reset.ps1), `CancelRunAsync`,
  `ReadRunAsync`, `RunStatus`.
- AC-21 and AC-22 `[RequiredAcceptance]`; AC-21/22/23 classify the records added since the seed before their first calculation, take
  their month from the picker, and cancel what they do not keep in `finally` (AC-21 both runs, AC-22 its run, AC-23 both runs and its
  request); AC-21 and AC-22 assert their runs read «Cancelled»; AC-23 picks the first employee of its run with no open request (API).
- Tests: `Pay059PayrollMonthsRegistryTests` 3/4 — the fourth (every class named exists) waits for `AcPay59FullCycleTests.cs` (P5).
  Acceptance project builds; solution build 0 warnings; format clean.
Finished (UTC): 2026-11-27T06:22:05Z

## Next step
P4: rewrite Ac12PayrollSodTests on a calculated, validated run of D-PAY-22's month.

## P4 — AC-12
- `Ac12PayrollSodTests` rewritten, `[RequiredAcceptance]`: D-PAY-22's month on the main stack (`PayrollCycleDriver.CycleMonthAsync`:
  the registry's candidates, the attendance-closed skip read by central-a, created by the processor); classify before calculating;
  calculate + validate (ResultsFrozen); processor approve 403; a-payroll-multirole approve 403 with two payroll roles named; submit,
  approve (+ a-payroll-approver-2 when required) → Approved with approvalsGiven = approvalsRequired (P-9's read); approver post 403;
  poster post before review 400 = ReviewRequired's text; auditor opens, a-auditor-2 closes; poster posts → Posted with its batch.
  `finally`: `CancelUnlessPostedAsync` (processor before review, approver after).
- New `PayrollCycleDriver.cs`: `PayrollApi` (one person's client, captured calls) and the D-PAY-22 picker; extended in P5.
- Acceptance project builds; solution build 0 warnings; format clean. (Run on the local main stack in P6.)
Finished (UTC): 2026-11-27T06:25:51Z

## Next step
P5: AcPay59FullCycleTests + PayrollCycleDriver steps; ci-scope (cycle group, prodlike-cycle job, ci.yml, prodlike-final), the moved
classes, ac-ownership.json; scripts/pay059-validate.ps1 with -SelfTest.
