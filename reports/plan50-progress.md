Started (UTC): 2026-11-11T10:00:59.4954239Z

# PLAN-50 progress — IAM-A: the management approver reaches his approvals, and delegation has a real effect

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-11T10:16:47Z |
| P1 | in progress | |
| P2 | pending | |
| P3 | pending | |
| P4 | pending | |
| P5 | pending | |
| P6 | pending | |
| P7 | pending | |
| P8 | pending | |

## P0 — timing, preconditions, baseline, inventories

Finished (UTC): 2026-11-11T10:16:47Z

- Preconditions: main folder, branch `docs/plan-49-merge-note`; `origin/main` = `1ee60c47` (PLAN-49); tree clean but for `global.json`,
  `tmp-vendors.html` and the untracked prompt; `ssh hrm-vps whoami` = `hrm`, the ED25519 host key is the recorded one; `gh` signed in as
  `omardefdev` with `repo` and `workflow`. No other plan session (the `.claude/worktrees` are old, idle).
- Branch `feature/plan-50-iam-approver-reach-delegation` from `origin/main`. PLAN-49's merge note cherry-picked (`a1bb135a`, from
  `2570ba04`), the prompt committed (`148ce2c5`); `docs/plan-49-merge-note` deleted (its one commit is on this branch). It was the only
  merge-note branch left.
- Baseline (`reports/plan50-baseline.md`): build 0/0 (52.7 s); the plan's filter 957/957 (43 s); the reach table; delegation readers;
  0.6; the inventories (two read-only agents, 10:04Z to 10:15Z, about 11 and 12 min, beside the build and the filter).
- Server (read-only): tag `046316bde9e7` on `origin/main`; `ApprovalDelegations` 0 rows, `WorkflowDelegations` 0 rows; the live
  realm's user profile declares neither `employee_id` nor `tenant_id`; `svc-hr-movements` has `hr-core.lookup` (optional scope).

## Decisions

| # | Decision | Reason |
|---|----------|--------|
| 1 | The prompt's "three Onboarding API actions" are two (`apply`, `apply-file`); the table carries both | the controller has two |
| 2 | `RequestedBy` of a secondment, loan, promotion and assignment is always the caller (was `??=`, which kept a posted value) | "nobody approves a request he created" would otherwise rest on a client-supplied field; transfers already did this |
| 3 | The gateway opens the movement approval screens on `/ui/hr-movements/...` only, by verb (Index, Details GET; Approve, Reject POST), with at most one GUID id; the module root (the default route, `TransferRequests/Index`) is one of them | P-3/P-4: exact segments; the `/api/hr-movements/...` form of the same MVC actions keeps `Module.HrMovements` |
| 4 | The module filter now passes the `HttpContext` as the resource of its policy check | so `RoleOrDelegationRequirement` can read the action's `[ApprovalEntityType]`, as it does for an `[Authorize]` attribute; no existing module policy reads the resource |

## Files changed

P0: `reports/plan50-progress.md`, `reports/plan50-baseline.md`.

## Test results

P0: build 0/0; unit filter 957/957.

## Server changes and their undo

None yet (P0 was read-only).

## Open issues

- The live realm's user profile has no `employee_id`: the provisioning fix has no effect there until the profile declares it (P3, P-5).
- `scripts/wf001-workflow-validate.ps1` expects the `WorkflowDelegations` DbSet and table: it changes in P3.

## Timing

| What | Start | End | Duration |
|------|-------|-----|----------|
| P0 inventories (two agents, read-only) | 10:04Z | 10:15Z | about 12 min |
| Baseline build | 10:02:59Z | 10:03:51Z | 52.7 s |
| Baseline unit filter | 10:05Z | 10:09:11Z | 43 s of tests (about 4 min with discovery) |

## Next step

P1: finish the views, build, write the P1 tests, commit P1.
