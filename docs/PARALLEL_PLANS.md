# Running two plans side by side (PLAN-42, P-11)

From PLAN-43 on, two plan sessions can work at the same time on the owner's machine. Three things are shared by every clone: the `hrm-acceptance` stack, Docker volumes and the demo server. This protocol keeps two sessions from breaking each other. The locks are `scripts/parallel/hrm-lock.ps1`.

## The second clone

Use a second **full clone**, not a git worktree: PLAN-41 found that gitleaks scans nothing in a worktree, because `.git` is a file there.

```bash
git clone "D:/my projects/HRManagement" "D:/my projects/HRManagement-b"
```

In the new clone:

1. Point `origin` at GitHub (`git remote set-url origin https://github.com/omardefdev/HRManagement.git`).
2. Install the hooks: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1`.
3. Copy the owner's local `global.json` edit, and never commit it.

## The locks

| Lock | Take it for | File |
|------|-------------|------|
| `acceptance-stack` | every `scripts/acceptance-up.ps1` run and everything until the stack is down | `%USERPROFILE%\.hrm-locks\acceptance-stack.lock` |
| `demo-server` | every write to the server: `build-on-host.ps1`, `deploy.ps1`, `init-secrets`, a restore | `%USERPROFILE%\.hrm-locks\demo-server.lock` |

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel/hrm-lock.ps1 -Action Acquire -Resource acceptance-stack -Plan PLAN-43 -Purpose "AC-43 on prod-like"
```

- A lock lasts 4 hours. Renew it with `-Action Renew` during a long run.
- Exit 1 from `Acquire` means another plan holds it: do other work, and try again later.
- An expired lock may be taken over. `Acquire` says so; write that line in your progress file.
- `-Action Status` shows the holder.

## The acceptance stack

- Take the lock before `acceptance-up.ps1`. Keep it until the stack is down.
- Release with the stack gone: `docker compose ... down -v` (or `scripts/acceptance-reset.ps1`), then `-Action Release`.
- **Never use `-NoBuild` after you re-acquire the lock:** the images may be the other plan's.

## The demo server

- Take the lock for every write. Reads need no lock.
- **Deploy only when the running tag is on `origin/main` or on your own branch.** Read `/opt/hrm/current-tag` and check it with `git branch -r --contains <tag>`.
- Never deploy over, or roll back to, the other plan's unmerged tag.

## After the other plan merges

1. `git fetch origin`, then merge `origin/main` into your branch.
2. If both plans added migrations to the same `DbContext`, remove yours and add it again on top, then run `dotnet ef migrations has-pending-model-changes`.
3. If branch protection is strict, update the PR and wait for the rerun.

## Files both plans append to

Keep both sides when they conflict:

- `tests/acceptance/ci-scope.json`
- `tests/acceptance/ac-ownership.json`
- `testdata/acceptance/personas.json`, with its catalog
- `docs/qa/dated-obligations.json`
- `docs/OWNER_DECISIONS_REGISTER.md`
- `docs/EVENT_PUBLISHER_INVENTORY.md`
- `deploy/wiring/service-wiring.json`, then `scripts/int037-render-wiring-doc.ps1`

## Can two acceptance stacks run at once?

No. Plans take turns on the stack.

Measured on this machine during PLAN-42 (P6):

| Item | Value |
|------|-------|
| Memory Docker can use | 15.48 GiB (16621195264 bytes), 32 CPUs |
| Prod-like stack, 19 containers, in use | 6784 MiB (6.63 GiB) |
| Two stacks plus 4 GiB for the host and the build | 2 x 6.63 + 4 = 17.25 GiB |

17.25 GiB is more than 15.48 GiB. There is also one set of acceptance ports and one set of Docker volumes, so a second stack would need its own project name and ports as well. Take `acceptance-stack.lock` for the whole of a run, and write code, unit tests and reports while the other plan holds it.

## What PLAN-43's run taught

PLAN-43 was written for an early start beside PLAN-42. It did not get one: the session opened in the primary clone with no second clone, stopped at the precondition, and the owner chose to wait for PR 37. It then ran in normal mode.

- **Early start needs its clone ready first.** Make the second clone before opening the session. A precondition that fails costs the whole overlap.
- **`rebase --onto` is the way to move an early branch.** It moves only your commits, so it also works when the other plan rewrites its branch. It is safe only before your first push. This run did not need it.
- **A session that ends its turn does not watch its PR.** PR 37 went red at 21:21Z while its session sat idle, and the waiting session's watcher fired only on merge or close. A watcher on a peer must also report failed checks, or the wait has no end.
- **`WAITING-PEER:` needs a named next step**, and a check of the peer's state, not just its merge.
- **Docker etiquette.** The stack lock covers a whole run. A session that holds no lock starts nothing longer than 30 minutes while the plan on the critical path may still build. Never prune while another session may build.
- **A fresh clone needs** the owner's `global.json` edit, `.acceptance/` (generated by `scripts/prepare-keycloak-import.ps1`) and the git hooks.
- **Volumes outlive images.** A persona change makes old acceptance volumes refuse to seed (`Acceptance seed receipt with a different fingerprint`). Run `scripts/acceptance-reset.ps1` under the lock before the first start after one.
- **Docker Desktop may be stopped** when a session resumes. Check `docker version` before a run.
