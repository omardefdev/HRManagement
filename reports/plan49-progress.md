Started (UTC): 2026-11-10T08:35:40.2725416Z

# PLAN-49 — progress checkpoint

Specification: `docs/acceptance-plans/execution/PLAN-49-AGENT-PROMPT.md` (it wins over the plan
`docs/acceptance-plans/PLAN-49-pol-engine-scope-orphans.md`). Branch `feature/plan-49-pol-engine-scope-orphans`, cut from
`origin/main` f99950bf. One session, in the main folder, no second clone.

## Stamps

- Started (UTC): 2026-11-10T08:35:40Z (first line of this file).
- Resumed (UTC): 2026-11-10T09:00:38Z — the desktop session ended during P0 (while the baseline integration filter ran; Docker
  Desktop had restarted) and the owner said «اكمل». Nothing was lost: the two first commits were in place, the baseline build and
  the unit filter had finished, and only the integration filter had to be run again. No time waited for the owner beyond the
  restart itself (about 17 minutes between the last tool call and the resume).
- Resumed (UTC): 2026-11-10T09:58:14Z — the context was compacted in the middle of P0 (the inventories were being read; P1's
  code was already in the working tree, uncommitted). Read this file and `git log --oneline -20` first, as the prompt orders. No
  owner wait: the session went on by itself.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-10T10:10Z |
| P1 | in progress (code and tests in the working tree, not committed) | |
| P2 | pending | |
| P3 | pending | |
| P4 | pending | |
| P5 | pending | |
| P6 | pending | |
| P7 | pending | |
| P8 | pending | |

## Durations

| What | Time |
|------|------|
| Baseline build `-warnaserror` | 109 s |
| Baseline unit filter (620 tests) | 51 s of tests, 181 s with the start |
| Baseline integration filter, without Testcontainers (13 passed, 22 skipped) | 15 s of tests, 182 s |
| Prod-like stack start on `ec9dca37`, images of PLAN-48 (`acceptance-up.ps1 -Profile ProdLike -NoBuild -SkipLiveVerification`) | 497 s |
| The four inventories (one read-only workflow, two agents at a time) | 61 min, beside the stack start and P1's first code |
| Full unit suite on the P1 working tree (5915 tests) | 114 s of tests |
| PostgreSQL filter `Pol047\|S2s042\|Ten048\|Hcm025` with `RUN_TESTCONTAINERS=true` on the P1 tree (47 tests) | 129 s of tests, 178 s |
| Waiting for CI | 0 so far |
| Waiting for the owner | about 17 min (the session end of 08:43Z to 09:00Z; nothing was asked of the owner) |

## P0 — done

**Preconditions (all hold, checked 08:36Z and again at each resume):**
1. Main folder, on `docs/plan-48-merge-note` at the start; no stack and no container of this repository was running.
2. `origin/main` = f99950bf (PLAN-48, PR 46); still f99950bf at 10:06Z.
3. Working tree: the owner's `global.json` edit, `tmp-vendors.html`, the untracked prompt file; test-rewritten
   `testdata/*/out/*.json` and the two nested reports restored by path (never a blanket checkout of `testdata`).
4. `ssh hrm-vps whoami` = `hrm`; the host key matches the plan; running tag `8abd5aa7fe93`.
5. `gh` signed in as `omardefdev` with `repo` and `workflow`. Side PRs 40 and 42 are still open: not touched.

**Commits:** `ec1b2729` (PLAN-48's merge note, cherry-picked from `ea91a632`, identical tree), `626a0022` (this plan's prompt),
`ec9dca37` (ClamAV keeps no host port), then the P0 commit (the baseline report and this file). `docs/plan-48-merge-note`
deleted after the check (its only commit is on this branch). The feature branch tracks nothing (`--unset-upstream`), so a bare
`git push` cannot reach `main`.

**The ClamAV port (P-2):** the mapping is dropped (`ports: !reset []`, and the fixed container name with it). Nothing on the
host read 53310. On this machine the port lies in a range Windows reserves (52576-53381 today), which is why PLAN-48 could not
start the stack here. With the fix the prod-like stack starts (497 s). Reserved ranges today: 49722-49921, 50000-50259,
52576-53381, 58480-58579, 64957-65056; no other host port of the stack is in one.

**Baseline and inventories:** `reports/plan49-baseline.md`. Raw material in `artifacts/plan49/` (four inventory files, the
acceptance and server before-pictures).

**Docker:** another project's containers (`cgims-*`, `p6check-*`, `p6-sqlcheck`) run on this machine and are not this
repository's: never touched. Their host ports are compared with the stack's before each stack start (P-2).

## Decisions (with reasons)

| # | Phase | Decision | Reason |
|---|-------|----------|--------|
| 1 | P0 | The ClamAV host mapping is removed, not moved to 6350 | nothing reads it from the host; File.Storage reaches `clamav:3310` on the compose network |
| 2 | P0 | Economy mode and the session's workflow tool are reconciled as "at most two agents at a time": the inventories ran as one workflow with a concurrency of two | the prompt's rule is about load and cost, not about the tool |
| 3 | P0/P1 | At the internal endpoint an out-of-scope domain answers 400 before the ownership check (403) | the catalog says the domain has no decision to give, whoever asks; one answer for every caller |
| 4 | P0/P1 | The start-up check is registered by `AddHrProductionNetworking`, so it runs in all 15 hosts (it ran in 12) | "fails the boot of any service" (P-3) |
| 5 | P0/P1 | The validator's settings sections (`Roles`, `Seeds`, `Publish`) are skipped by the start-up check | a latent PLAN-47 defect: setting any of them stopped the boot; fixed with a test |
| 6 | P0/P1 | The finance overlay pack's leave rule (`RequireEvidence`, no consumer) becomes a custody rule bound to `Assign` | the only pack must pass the catalog; an agency rule must pass the capabilities and the ceiling |
| 7 | P0 | AC-POL-49e is adapted: the central actor's submission is the recorded one; the branch manager gets 403 and leaves no ledger row; the deny rule's match is proved by a simulation and by unit tests | both controllers refuse a branch-scoped actor before the service; letting him through would weaken a guard (see the baseline, 0.9) |
| 8 | P0 | `isSelfApproval` is `ApproverId == CreatedBy`, or the approver's `employee_id` equals the request's employee | with the plan's formula alone the fact is never true when the engine is asked (the local check refuses first), so `R-LEAVE-SELF` would be a dead rule |
| 9 | P0 | A self-service leave stores its working days, computed in HR.Contracts, and a local refusal creates it `Rejected` | the portal sends calendar days; the local validation counts working days; a consumer must not throw for a business refusal |
| 10 | P0 | A new database keeps the governed bindings only (seven with the new establishment one); existing rows stay; the establishment binding is unpinned | readers checked: PLAN-46's path reads no binding; a pin would break AC-WF-43c |

## Files changed

- P0: `docker-compose.acceptance.yml` (ClamAV), `reports/plan49-baseline.md`, `reports/plan49-progress.md`, the prompt file,
  PLAN-48's note in `reports/plan48-progress.md`.
- P1 (working tree, not committed yet): see the P1 section when it is done.

## Test results

| Phase | Run | Result |
|-------|-----|--------|
| P0 | build `-warnaserror` | 0 errors, 0 warnings |
| P0 | unit filter of the plan | 620 / 620 |
| P0 | integration filter, no Testcontainers | 13 passed, 22 skipped |
| P1 (in progress) | full unit suite | 5915 / 5915 |
| P1 (in progress) | PostgreSQL filter `Pol047\|S2s042\|Ten048\|Hcm025` | 47 / 47 |

## Server changes and their undo commands

None yet (read-only so far: the running tag, the policy picture in `artifacts/plan49/vps/before-p0-policy-picture.txt`).

## Open issues

- `AcWf46PolicyOrphanTests` probably runs with hr-core-web out of Enforce (the s2s42 class recreates it without the wf46
  overlay). Not this plan's class; the pol49 overlay class asserts the container's mode itself. Recorded for the final report.
- The acceptance overrides of agencies B and C hold rules no validator would accept, on purpose (AC-TEN-48e). They stay.

## Exact next step

P1: build the whole solution `-warnaserror`; run `Pol049*`, `S2s042*`, `Pol047*`, `Ten048*`, `ProductionWiringMatrixTests` and
the `Architecture` filter; a negative run of `scripts/pol049-validate.ps1`; `int037-wiring-check.ps1`, `lint-compose.ps1` (both
chains), `wf043-validate.ps1`, `hcm025-validate.ps1 -SkipTests`, `pol047-validate.ps1`; then the P1 commit. `PolicyDomainGateway.cs`
is deleted in the working tree (it had no caller); it belongs to the P1 commit.
