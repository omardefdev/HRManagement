Started (UTC): 2026-11-24T05:14:03Z

# PLAN-55 — progress (the session's checkpoint)

Main folder: `/home/omar/HRManagement` (the owner's Linux notebook). One session, no second clone, no locks.
Branch: `feature/plan-55-pay-bank-account-pay-profile` from `origin/main` `12b83970`.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 baseline | done | 2026-11-24T05:21:55Z |
| P1 the account | next | |
| P2 the pinning | | |
| P3 the versions | | |
| P4 the repair | | |
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

- P0: `reports/plan54-progress.md` (the cherry-picked merge note), `docs/acceptance-plans/execution/PLAN-55-AGENT-PROMPT.md`,
  `reports/plan55-baseline.md`, `reports/plan55-progress.md`.

## Server changes and their undo

None yet (P0 only read).

## Open issues

None.

## The exact next step

P1: the entity `EmployeeBankAccount`, `BankAccountNumberValidator`, the commands under `Features/BankAccounts/`, migration
`PAY055_EmployeeBankAccounts` with its data move, the API and the screens, and their tests.
