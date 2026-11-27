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
