# PLAN-41 final report

PR: https://github.com/omardefdev/HRManagement/pull/36. Merge commit `8e79a6b2f0f59b6296cbd4b3c8a4b5557c4d86ec`, 2026-11-05T11:28:51Z.
Evidence of record: CI run [41962709297](https://github.com/omardefdev/HRManagement/actions/runs/41962709297), artifact `acceptance-evidence`, kept 30 days; the copy in `acceptance-evidence/` is the record after that.

## 1. Summary per phase

| Phase | Result |
|-------|--------|
| P0 | Baseline, class inventory (74 classes, 203 test methods), GitHub state. Repository is public; no protection on `main`. |
| P1 | Red static tests fixed; persona counts come from the catalog; the file scan reads the generated persona password; no shared password left. |
| P2 | Evidence records profile, image sources, code tree and run source, and keeps every scenario. One summarizer with the honest states, `ac-ownership.json`, baseline validator. |
| P3 | Service images carry their source revision; the acceptance stack builds them from the current commit. `SOURCE_SHA` default stays `unknown`. |
| P4 | `scripts/ci/acceptance-job.ps1` (CI and owner), `tests/acceptance/ci-scope.json`, guard, artifact scan. Local run on the CI path: 4759 s, exit 0. Local negative: 433 s, exit 1, scenario named. |
| P5 | `Acceptance` job in `ci.yml`, parallel with `CI`; `workflow_dispatch` inputs; `-Committed` validator step; `CI-ACCEPTANCE-DISABLED` closed. |
| P6 | Docs, one independent review (no high finding), local CI and pre-push green. |
| P7 | Eight remote runs, four green, each red one fixed (section 7); merged after run 8. |
| P8 | This report. |

## 2. Commits

```
8e79a6b2 Merge pull request #36 from omardefdev/release/plan-41
7b9fcbdc test(qa041): acceptance evidence of record from CI run 41962709297 (both profiles), Acceptance becomes a required check
e6518e97 docs(qa041): progress of runs 2 to 6, and who owns PR 36
f6e3422d fix(qa041): the CI compose build retries a registry that answered 503 for a moment
bc0b5fc8 fix(qa041): the live-settings fixture carries Acceptance, recorded evidence is listed as historical, the evidence waits for the next green run
bb3e1ae2 test(qa041): acceptance evidence of record from CI run 41934216395 (both profiles), Acceptance becomes a required check
2d2a349b fix(qa041): gitleaks allows the auth-directory-web image revision in evidence metadata
3021ea9a fix(qa041): the migrator build picks Dockerfile frontend 1.19 on the runner, AC-QA-35e reads the journeys branch, job timeout from run 1
6019f5e9 Merge PLAN-41: acceptance job in CI and an honest acceptance baseline
c2627683 docs(qa041): local CI history and pre-push results, PR description
17e8162f docs(qa041): acceptance job, scope and evidence contract documented, gap status lines, review fixes
510030b0 ci(qa041): the Acceptance job runs both profiles on every PR and push to main
7e66f080 feat(qa041): one acceptance job script for CI and the owner, CI scope with owned exclusions, a guard
9ba8b578 feat(qa041): service images carry their source revision
1b68f865 feat(qa041): evidence records profile, image sources, code tree and run source
c7f047c4 fix(qa041): persona counts come from the catalog
0596ad33 chore(qa041): baseline, class inventory and needs
520ec8a4 docs(msg040): PR merge note and post-merge run
c88301d4 docs(qa041): PLAN-41 agent prompt
```

History: unpushed commits 600e6c06, f413dfc8, 352420dc were rebuilt as 7e66f080, 510030b0, 17e8162f after gitleaks found a sample JWT in a test. Nothing had been pushed. `bb3e1ae2` carries evidence that was withdrawn one commit later; it stays in the history, no force-push.

## 3. Decisions and deviations

- **P-2:** freshness is by code tree, not by commit: SHA-256 of `git ls-tree -r` without `acceptance-evidence/`, `reports/`, `docs/`. Recorded as a deviation from the literal «gitSha = HEAD».
- **P-3:** evidence comes from the PR run. In practice this flow cost more than planned: any change inside the code tree after run 1 made the evidence stale, and `.gitleaks.toml`, `testdata/` and `scripts/ci/` are all inside it. The evidence of record came from run 7, not run 1.
- **P-3, the evidence commit:** it was refused or broke tests twice for reasons no earlier run could show: gitleaks on image revisions, and two unit tests triggered only by the evidence or by `Acceptance` in the protection file. Later plans should expect this on the first evidence refresh of a new kind of file.
- **P-5:** `timeout-minutes` set to 120 from run 1's partial measure (about 80 minutes), not from a green run, because changing it later would change the code tree. Green runs took 90 to 104 minutes, so the margin is 1.15 to 1.33, below the 1.5 the plan asked. Not changed: see section 9.
- **P-9:** applied after the merge; see section 8. `SCM-NO-GITHUB-PROTECTION` is closed in the commit that carries this report.
- **AC-QA-35e:** reads run 41158446834, the one green manual `e2e-journeys`, through `ACQA35_JOURNEYS_BRANCH: release/plan-35-first-push`. That run tested PLAN-35's code, not this commit: a weak claim, unchanged from PLAN-35.
- **Migrator build in CI:** `BUILDKIT_SYNTAX=docker/dockerfile:1.19` on the command line, so `docker/Migrator.Dockerfile` and the demo server's build are untouched (P-12).
- **Two sessions:** the PLAN-42 session took P7 over during a `WAITING-CI` stop, believing this session had ended: it made 2d2a349b and bb3e1ae2. Settled by message: this session kept PR 36 from then on, working in the clone `D:/hrm-plan41-finish`.

## 4. Class inventory and CI scope

74 classes in the inventory. `ci-scope.json`: 69 entries run (62 lab, 7 prod-like), 39 required, 30 not required, 10 excluded. The run's guard: PASS, 69 classes, 12 trx files.

| Excluded | Owner | Review by |
|----------|-------|-----------|
| Ac14TemporalOrgStructureTests | COD-G | 2027-01-28 |
| Ac17OutboxSurvivesRabbitDownTests | QA-G | 2027-01-28 |
| Ac18ConsumerCatchUpTests | QA-G | 2027-01-28 |
| Ac22PayrollResumeAfterKillTests | QA-G | 2027-01-28 |
| Ac34BackupRestoreLiveTests | QA-G | 2027-01-28 |
| Ac35ScopedLookupPerfLiveTests | QA-G | 2027-01-28 |
| Ac36ArabicPdfLiveTests | QA-G | 2027-01-28 |
| AcInt37VpsServerTests | PLAN-37 | 2027-01-28 |
| AcOps36VpsBootTests | PLAN-36 | 2027-01-28 |
| AcQa34...Ac34_h (one method) | PLAN-34 | 2027-01-28 |

Reasons are in `ci-scope.json` and mirrored in `docs/qa/dated-obligations.json`. Nothing was excluded or un-required because it was red.

## 5. The honest baseline

| | Proven | Passed, not proving | Stale | Incomplete | Not run | Fail | Blocked |
|---|---|---|---|---|---|---|---|
| Before, old generator (P0) | | 38 «Pass» | | | 20 | 0 | 2 |
| Before, new rules (P0) | 0 | 0 | 41 | 6 | 30 | 0 | 0 |
| After, run 41962709297 | **31** | **27** | **8** | **4** | **6** | **0** | **1** |

77 ACs. Every AC's row is in `acceptance-evidence/summary.md`. The state changed for all those below, and the reason for the move from stale or not run to Pass is the same: for the first time they ran on CI with current images and a recorded code tree.

Not Pass after this plan:

| AC | State | Why | Owner |
|----|-------|-----|-------|
| AC-12 | Blocked | The payroll run could not be submitted for review: a defect in the design of the test. Not required. | PLAN-59 |
| AC-21, 23, 24, 25 | Incomplete | Their tests are red on CI and locally. Not required. | PLAN-58 |
| AC-14 | Not run | Not reproducible (fixed org unit and date). | COD-G |
| AC-22, 34, 35, 36 | Not run | Chaos, backup drill, performance, human signature. | QA-G |
| HCM-01 | Not run | A steps file with no writer class: nothing runs it. | PLAN-65 |
| AC-17, AC-18 | Stale | Chaos: excluded from CI, old evidence kept. | QA-G |
| AC-OPS-36 and the `/vps` folders of AC-INT-37, AC-MSG-40, AC-OPS-36, AC-OPS-38, AC-OPS-39 | Stale | Server evidence: no code tree recorded. | Their plans |

The 27 «passed, not proving» have their reasons in `tests/acceptance/ac-ownership.json`: for example AC-01, whose db-checks are fixed values (PLAN-65 strengthens the tests), and AC-QA-34, whose 34h half does not run in CI.

## 6. Test results

- Local: `dotnet build -warnaserror` 0 errors, 0 warnings. Unit governance subset 299/299, static acceptance 73/73 (P4). `ci-local` on 17e8162f green on the 8th run of 9 on the final content (91 passed): the rest failed on this machine's network, see `reports/plan41-progress.md`.
- Local job, CI path: 4759 s, 67 classes, 11 trx (machine sleep inflated it).
- Meta stage before push, at the tested merge commit, on run 1's real artifact: 13 of 15; the other two need the base stack or a non-worktree clone. Both passed in CI.
- Remote: the `CI` job and every required class of `Acceptance` green in runs 7 and 8.

## 7. CI runs

| # | Run | Head | Result | Created (UTC) | Wall-clock | Acceptance | Cause or use |
|---|-----|------|--------|---------------|-----------|------------|--------------|
| 1 | [41899641329](https://github.com/omardefdev/HRManagement/actions/runs/41899641329) | 6019f5e9 | red | 11-04 19:43 | 1h14 | red 57m36s | Runner's Docker lacks `COPY --exclude` |
| 2 | [41918380549](https://github.com/omardefdev/HRManagement/actions/runs/41918380549) | 3021ea9a | green | 11-04 21:41 | 1h44 | 1h43m46s | Evidence refused by gitleaks |
| 3 | [41934216395](https://github.com/omardefdev/HRManagement/actions/runs/41934216395) | 2d2a349b | green | 11-04 23:30 | 1h44 | 1h43m54s | Evidence taken, later stale |
| 4 | [41948564758](https://github.com/omardefdev/HRManagement/actions/runs/41948564758) | bb3e1ae2 | red | 11-05 01:26 | 24m | cancelled | Fixture lacked `Acceptance`, dated key in evidence |
| 5 | [41951228726](https://github.com/omardefdev/HRManagement/actions/runs/41951228726) | bc0b5fc8 | red | 11-05 01:50 | 1h14 | red 1h13m18s | `mcr.microsoft.com` 503 |
| 6 | [41959564898](https://github.com/omardefdev/HRManagement/actions/runs/41959564898) | f6e3422d | red | 11-05 03:07 | 31m | cancelled | `dotnet publish` hung in `Int037PublishOutputTests` |
| 7 | [41962709297](https://github.com/omardefdev/HRManagement/actions/runs/41962709297) | e6518e97 | green | 11-05 03:36 | 1h38 | 1h37m18s | **Evidence of record** |
| 8 | [41998062595](https://github.com/omardefdev/HRManagement/actions/runs/41998062595) | 7b9fcbdc | green | 11-05 08:54 | 1h30 | 1h29m47s | `-Committed` passed; merged |

Runs 7 and 8 are the two consecutive green runs. `Acceptance` started within a minute of the run being created on every run. Other jobs in run 8: CI 25m55s, CodeQL 17m19s, E2E 12m24s, Gitleaks 48s, Dedupe 11s.

`Acceptance` steps of run 7 (seconds): free disk 89; the job 5722, of which build tests 34, stack lab 1102, lab-main 717, journey 9, AcHcm26 99, AcHcm27 240, stack prod-like 439, prod-like journey 17, main 22, AcInt37 130, AcMsg40 100, AcOps39 140, final 55, PII scan 1, stack least-privilege 698, its tests 210, stack base 637, wait for CI 7, meta 1053; artifact scan 2; upload 2. Stacks alone take 2876 s of the 5722.

Runner: memory used at the peak 9.1 of 16 GB (lab), then 8.0, 6.7 and 7.7 GB; disk 54 GB rising to 71 of 145 GB. The standard runner holds the stack. No service had to be stopped and no larger runner is needed.

**Minutes (P-13):** billable 0 on every run (`/timing`): the repository is public. One green PR run is about 150 job-minutes. At 8 PRs a month (October; November had 5 in its first 5 days), two runs each, about 2400 job-minutes a month, or more with fix runs: free while public. If the repository became private, that alone exceeds the 2000 free minutes: the D-QA-19 alternative (a label plus a nightly run) would then be the owner's call. D-QA-19 is unchanged.

## 8. GitHub changes and their undo

| Change | When (UTC) | Undo |
|--------|-----------|------|
| Protection applied to `main` from the file: contexts CI, CodeQL, Gitleaks, E2E, Acceptance; strict; enforce admins; no force-push. `-CheckLiveGitHub` passes. | 2026-11-05T11:29:35Z | `gh api -X DELETE repos/omardefdev/HRManagement/branches/main/protection` |
| `SCM-NO-GITHUB-PROTECTION` closed (file change, in the commit that carries this report). | | Reopen the entry |
| Variable `ENABLE_ACCEPTANCE_JOB`: never existed, nothing to delete. | | |

**`Acceptance` is a live required check.** Direct pushes to `main` are now refused for everyone, admins included, and every PR needs all five checks, so a PR takes about 1h35 to go green.

After the merge: push run [42019294047](https://github.com/omardefdev/HRManagement/actions/runs/42019294047) success, the dedupe guard skipped CI, CodeQL, Acceptance and E2E because the tree was tested. `-Committed` on `main`: PASS.

AC-QA-41a remote negative: [run 42019357506](https://github.com/omardefdev/HRManagement/actions/runs/42019357506), dispatched on `main` with `simulate_missing` and `classes`. `Acceptance` red after 19m15s as expected; the guard named `AC-HCM-26/26i` and `Ac26_i_intake_close_job_closes_expired_posting`; the job warned that a narrowed run says nothing about other classes. A blocked required scenario turns the job red on the real runner.

## 9. Deferred items

- `timeout-minutes: 120` against runs of 90 to 104 minutes: thin, and a slow runner could cancel a good run. Raise it to about 150 in the next change inside the code tree.
- `Int037PublishOutputTests`: test timeout 8 minutes, blame hang 5 minutes, so its own timeout can never fire. Hung once in the 7 runs that reached it, cause not proved (the dump is not in the artifact).
- Review L3 (the summary does not read trx; required classes are safe because the guard does) and L4 (`WriteResult` fills the contract files; older than this plan).
- `scripts/acceptance-up.ps1` builds have no registry retry; only `compose-up-retry.sh` has.
- `strict: true` with parallel plans: when one PR merges, every other open PR must take the new `main` and pass all checks again, about 1h35 each. If that hurts, the owner can decide to turn `strict` off.

## 10. Defects for later plans

- AC-21, 23, 24, 25 red (PLAN-58); AC-12 blocked by a defect in the design of its test (PLAN-59).
- Fresh server evidence for the `/vps` folders needs a recorded code tree (their plans).
- Testcontainers on this machine: 2 green of 8 runs of `ci-local`, every failure a download or an Npgsql timeout through the Docker Desktop gateway.

## 11. Timing

Total wall-clock: 2026-11-04T08:48:18Z to 2026-11-05T11:28:51Z, 26h40m.
Waiting for CI: about 10h of run time across 8 runs, plus 4h40m between a green run and the owner's next message. The rest, about 12h, was active work including local acceptance runs, of which about 1h35 was lost to the machine sleeping. Seven `WAITING-CI:` stops.

Local durations: inventory 912 s; image build 688 s warm, 11 to 15 minutes cold; lab up 254 s; prod-like up 240 s; local job on the CI path 4759 s; local CI 1292 to 1984 s.

```
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47  13:58
PLAN-35      49 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13  11:43
PLAN-36      26 2026-10-31 16:32 2026-10-31 16:41 2026-11-01 00:13   True 7:31   7:40
PLAN-37      23 2026-11-01 01:24 2026-11-01 01:30 2026-11-01 12:27   True 10:57  11:03
PLAN-38      18 2026-11-01 13:24 2026-11-01 13:25 2026-11-02 02:13  False 12:47  12:48
PLAN-39      13 2026-11-02 02:11 2026-11-02 02:13 2026-11-02 11:05  False 8:52   8:53
PLAN-40      20 2026-11-02 11:01 2026-11-02 11:05 2026-11-04 11:56  False 48:51  48:55
PLAN-41      16 2026-11-04 11:48 2026-11-04 11:56 2026-11-05 11:52  False 23:56  24:04
```

`scripts/plan-timing.ps1 -From 25 -To 41`, plans 34 to 41 shown, local time. The script shows PLAN-41 as not merged and stops at its last commit; PR 36's merge at 14:28 local is the real end, which is the 26h40m above.

## 12. Readiness note for PLAN-42

- Every new acceptance class goes into `tests/acceptance/ci-scope.json`: unlisted classes fail the guard. Exclude only for an environment CI cannot provide (P-6).
- Evidence contract (P-4): the evidence of record is a PR run's artifact, a local run is a draft and is never committed, and refreshing committed evidence means copying from a CI artifact. Any change inside the code tree after that run makes it stale; `testdata/`, `scripts/` and `.gitleaks.toml` are inside it, `docs/` and `reports/` are not. Do the evidence commit last, and run the governance tests with the new evidence in place before pushing.
- The PLAN-40 publisher contract still applies.
- Typical `Acceptance` run: 90 to 104 minutes, parallel with CI. `main` is protected: PR only, all five checks.
