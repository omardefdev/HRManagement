# PLAN-66 final report: a faster CI

PR: https://github.com/omardefdev/HRManagement/pull/39, merged 2026-11-06T22:03:16Z at `f77dd28b` by the agent on the owner's instruction.

A merge now waits about 45 minutes instead of about 100. A commit that leaves the code tree unchanged waits 16 minutes. One stage reruns in 24 minutes. Every acceptance class still runs; the governance classes run by rule (D-QA-66-1).

One limit on the headline: `Install Playwright browsers` took 33 and 52 minutes in two of four runs, which puts `E2E` above the 60-minute target when it happens. See section 9.

## 1. Summary per phase

| Phase | Result |
|-------|--------|
| P0 | Baseline in `reports/plan66-baseline.md`, plan document, D-QA-66-1 and D-QA-66-2 in the register |
| P1 | `acceptance-job.ps1 -Stage`, `-MergeStages`, `-ListStages`; `acceptance-merge.ps1`; `acceptance-images.ps1`; local proof on `lab:main` |
| P2 | `Acceptance images`, `Acceptance stage (<name>)`, and `Acceptance` as the aggregate |
| P3 | `run-identity`, `Reuse check`, the short-circuit of `CI`, `E2E`, the build job, the stages and `Acceptance` |
| P4 | `metaRule` in `ci-scope.json`, the guard, the validator, the summary, the nightly schedule |
| P5 | `E2E` beside `CI`, docs, one independent review, local gates |
| P6 | Three PR runs, one stage rerun, the merge, three post-merge proofs |

## 2. Commits

`835a5105` P0, `1dbcd70a` P1, `3a6bf1fd` P2, `5e9cc5c4` P3, `159f81dc` P4, `cbd06a3d` P5 and review fixes, `921b18cb` and `a46b1044` progress, `c8d4d919` formatting, `600ccde0` job timeouts, `af52f605` evidence. Merge `f77dd28b`.

## 3. Decisions applied and deviations

Applied: D-QA-66-1, D-QA-66-2, P-1 to P-10.

Deviations:

1. **Stage jobs are not free of registries (P-5).** `docker load` does not restore a reference by digest, so the `meta` stage pulls gitleaks and actionlint from Docker Hub. `Verify` warns about it in every stage job. Both are pinned by digest, so a stale image is not possible, but a Docker Hub outage turns `meta` red. The other stage jobs pull nothing. The two images are still saved in the archive, where they are of no use.
2. **Reuse is stricter than D-QA-66-2 says.** It also compares `outsideTree`: `docs/` except `*.md`, plus what the governance checks read. The review showed that `docs/*.json` are runtime content and inputs of the CI validators, yet sit outside the code tree. A change to them now runs in full. Evidence, reports and Markdown still reuse.
3. **`AcQa41AcceptanceBaselineTests` is in neither a plain stage nor the aggregate.** It needs the folders of the other stages (41a, 41e, 41g) and a live prod-like stack (41f). `prodlike-final` starts its stack in parallel and takes their results when they are ready.
4. **`ENABLE_SCHEDULED`.** `ci.yml` is a second exception to the rule, because D-QA-66-1 needs the nightly and the variable is `false`. `Scheduled_jobs_wait_for_ENABLE_SCHEDULED...` was changed to say so.
5. **Cancelling.** `concurrency` moved to the workflow, so a new push cancels `CI`, `E2E` and `CodeQL` of the replaced run too, not only `Acceptance`. Non-PR runs have a group of their own, so a dispatch no longer cancels another run.
6. **Commits.** P1 to P4 were written together and committed by file, so those single commits do not run on their own. Only the release branch was pushed.
7. **Plan document status.** «الحالة» was not updated before the merge. It is updated in the working tree and rides with the next plan's first commit, with this report.
8. **The second clone** was created by the agent, on the owner's reply, not by the owner as P-1 described.

## 4. Before and after (Actions API)

| Measure | Before | After |
|---------|--------|-------|
| Normal PR run, whole workflow | 98 to 104 min | 44.8 min (run 42287724602), 40.6 min (run 42281107074, red on E2E) |
| `Acceptance` chain | 90 to 104 min | 43.7 min (images 19.2, `prodlike-final` 24.3 incl. wait, aggregate 0.5) |
| `CI` | 26 to 35 min | 30.6 to 36.3 min, unchanged |
| `E2E` | 12 to 17 min after `CI` | 12 to 18 min beside `CI`; 50 and 67 min with a slow Playwright install |
| `CodeQL` | 10 to 17 min | 13 to 17.5 min, unchanged |
| Evidence-only run | 90 min | 16.1 min (run 42299004110), set by `CodeQL` |
| One-stage rerun | a whole run, about 100 min | 24.2 min (`lab-main` 23.7 + `Acceptance` 0.4) |
| Runner minutes, normal run | 146 to 202 | 192 and 204 |
| Runner minutes, evidence run | about 146 | 20 |

Stage jobs in run 42287724602, minutes: `lab-main` 23.1, `lab-rest` 15.2, `prodlike` 18.4, `prodlike-final` 24.3, `leastpriv` 10.4, `meta` 20.4. Image artifact: 2.43 GB.

`lab-meta` took 1054 s in the sequential job and 144 to 167 s in its own job, with the same 15 tests. The cause was not investigated.

## 5. Stage grouping

`order[]` grouped so that the longest job is as short as the tests allow: `lab-main` alone (`lab:main` is the longest group); `lab-rest` (`lab:journey`, `lab:isolated`); `prodlike` (journey, main, isolated, then `s2s42`, last on its stack as the scope requires); `prodlike-final`; `leastpriv`; `meta`. Stage jobs skip `free-runner-disk.sh`: 86 GB were free before it, and it cost 272 s.

## 6. Reuse rules and tests

`scripts/ci/acceptance-reuse.ps1`, tested in `Qa066ReuseTests`. Reused only: the newest successful run, same PR by `run.pull_requests` and by the identity, head repository this one, event `pull_request`, every job a success, scope `full`, same `codeTree`, same `outsideTree`, evidence artifact still there. A chain points at the original run. Refused, each with a test: fork, push, schedule, dispatch, narrowed, failed, cancelled, partly skipped, other tree, other inputs, other PR, no identity, expired evidence, a broken chain, any API error. `CodeQL`, `Gitleaks` and `Committed acceptance evidence` always run.

## 7. The meta rule and the nightly

`metaRule` in `tests/acceptance/ci-scope.json`: stage `lab:meta`, trigger paths for `.github/**`, `scripts/ci/**`, `tests/acceptance/**`, `testdata/qa/**`, `docs/qa/**`, `docs/str-governance/**`, the compose and package files the classes read, and the test classes whose results they count. `scripts/ci/acceptance-meta-rule.ps1` decides; a list it cannot read runs them, and `--no-renames` keeps a file moved out of a trigger path visible. Otherwise `not-run-by-rule.json` records the classes, the reason and the last nightly. The guard and the validator accept that only for those classes.

Nightly: `cron: 0 2 * * *` on `main`. No failure-to-issue path exists for `ci.yml`, so a red nightly shows in Actions.

## 8. Proofs

| AC | Proof | State |
|----|-------|-------|
| AC-QA-66a | `Qa066WorkflowTests`; run 42287724602: guard PASS, 71 classes, 13 trx | Met |
| AC-QA-66b | `Qa066WorkflowTests`, `Qa066StageTests`; [run 42301740446](https://github.com/omardefdev/HRManagement/actions/runs/42301740446) red: the guard names `AC-HCM-26/26i`, the stage and `acceptance-needs-check` also fail | Met |
| AC-QA-66c | `Qa066ReuseTests`; [run 42299004110](https://github.com/omardefdev/HRManagement/actions/runs/42299004110) reused 42287724602 (`run-identity`, `reuse.json`); run 42287724602 did not reuse | Met, see below |
| AC-QA-66d | `Qa066MetaRuleTests`; this PR: `meta=run`; [manual nightly 42301745454](https://github.com/omardefdev/HRManagement/actions/runs/42301745454): green, `meta` 15 of 15, no `not-run-by-rule.json` | Met, see below |
| AC-QA-66e | 44.8, 16.1, 24.2 min | Met, see below |
| AC-QA-66f | `Qa066StageTests`; local `-Stage lab:main`: 163 tests as in the sequential run, guard PASS | Met |

Limits of these proofs:

- **AC-QA-66c:** the code commit `600ccde0` was not reused, but the run before it was red, so it could not have been reused anyway. The "different tree" case rests on the unit test.
- **AC-QA-66d:** the skip path has unit tests and has not run live: every run so far had `meta=run`. The first PR that touches no trigger path is its first live use.
- **AC-QA-66d:** no scheduled run had fired by 2026-11-07T08:23Z. Schedules here run hours late: `advisory-to-issue`, `0 6 * * *`, fires near 13:20Z. The manual run is the proof the plan asked for.
- **AC-QA-66e:** the 25-minute rerun target has 0.8 minutes of margin.
- **Run 1 E2E failure:** `LEV_H1_Contracts_Persona_Reaches_Leave_Requests` timed out once, at 30 s, in the login helper, and passed in every later run. The cause is unknown.

Post-merge: dedupe skipped the heavy jobs of push run 42301677430, which ended green.

## 9. What is left, and the next levers

1. **Playwright install.** 0.7, 1.2, 32.9 and 52.3 minutes in the four runs, and 40.6 in PLAN-42's. When slow, `E2E` is the critical path: 67 minutes in run 42301740446, above the 60-minute target. Cache the browsers, or install them while the stack starts. This is the next lever.
2. **Keycloak sync:** about 5 minutes of every lab and prod-like stack start.
3. **`lab:main`:** the longest stage and the rerun's margin. Splitting the group would help both.
4. **`CodeQL`** sets the evidence-only time.
5. **Local gates:** `ci-local.ps1` 1305 s and pre-push 124 s. Out of scope here.
6. **The two tool images** of deviation 1.

## 10. Timing

- Started 2026-11-06T17:46:42Z, merged 22:03:16Z: 4 h 17 min wall-clock.
- Waiting for CI before the merge: about 2 h 14 min (runs 1, 2, the rerun, the evidence run).
- Active: about 2 h 3 min, including about 25 minutes of the local stage run and 38 minutes of local gates.
- Locks: `acceptance-stack` held 18:07:39Z to 18:37:12Z.
- Post-merge proofs ended 23:11:28Z. The background wait for them stopped at 22:35Z on a connection error, so they were read and this report was written on 2026-11-07 at about 08:25Z.

Local gate runs: first red on formatting (190 s), second red on a dropped PostgreSQL connection and a cut-off download (758 s), third green (1305 s).

`scripts/plan-timing.ps1 -From 25 -To 66`, last rows:

```text
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-41      17 2026-11-04 11:48 2026-11-04 11:56 2026-11-05 15:04  False 27:08  27:16
PLAN-43      14 2026-11-06 10:12 2026-11-06 10:21 2026-11-06 19:03  False 8:42   8:50
PLAN-66      12 2026-11-06 20:46 2026-11-06 21:03 2026-11-07 00:36  False 3:33   3:50
```

Times are Riyadh. `End` is the last commit, before the merge at 01:03.

## 11. A note for the next plans

- **Local acceptance:** run only the classes the plan touches (`-Profile Lab -Classes A,B`). The full suite runs once, in CI.
- **`-Stage`:** `acceptance-job.ps1 -Stage lab:main` runs one stage with its stack and takes the stack down. `-ListStages` shows what would run.
- **A new class** goes into `ci-scope.json` as before. A new group must join `stageJobs[]` and the matrix of `ci.yml`; `Qa066WorkflowTests` fails otherwise.
- **A class in `meta` that reads a new file** adds it to `metaRule.triggerPaths`.
- **Reading a reused run:** the summary of `Reuse check` says why; `reuse.json` in the artifact names the original run. A red evidence check on a reusing run is real: the evidence is still validated.
- **Unit tests do not run on an evidence commit.** Run them locally before pushing it.
- **A failed stage:** `gh run rerun <id> --job <stage-job-id>`, within a day (the image archive's life).
- **When meta runs:** a PR that touches its trigger paths, every night on `main`, every manual run.
