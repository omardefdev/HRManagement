Started (UTC): 2026-11-06T17:46:42.0451360Z

# PLAN-66 progress

Resumed (UTC): 2026-11-06T17:49:34Z - the session opened in the primary clone and stopped at precondition 1 (PR 38 open, no second clone). The owner replied "Yes, create it", so the agent created D:\my projects\HRManagement-b and continues there.

## Status
- P0: done

## Second clone setup
- origin -> https://github.com/omardefdev/HRManagement.git, hooks installed, local global.json edit, .acceptance/, .env, .env.leastpriv, generated Keycloak import copied without printing; none committed.
- Branch feature/plan-66-qa-ci-fast-pipeline from origin/main (7480843e).

## Preconditions
1. Clone: HRManagement-b, no other session. OK.
2. origin/main contains 7480843e. OK.
3. Tree clean except global.json, artifacts, prompt. OK.
4. gh: omardefdev, repo + workflow. OK.
5. Repository PUBLIC. OK.


## P0
Baseline in reports/plan66-baseline.md. Plan document and D-QA-66-1/2 written.
Decisions:
- prodLike:final gets its own stage job: it needs the lab and prod-like folders (41a, 41e, 41g) and a live prod-like stack (41f). It starts its stack in parallel and takes the stage results by polling the artifacts of the run, so it adds about a minute, not a stack start, to the critical path.
- Stage jobs skip free-runner-disk (86 GB free, 272 s saved).
- Reuse also compares a hash of the meta trigger paths, because docs/qa/** is outside the code tree.
- The CI job short-circuits in run-steps.sh, so its 90 steps stay equal to steps.json; the committed-evidence step still runs under reuse.
- The nightly is not gated by vars.ENABLE_SCHEDULED (false today): D-QA-66-1 needs it. No failure-to-issue path exists for ci.yml, so a red nightly shows in Actions.
Finished (UTC): 2026-11-06T18:01:51Z

## P1 to P4
Commits: 1dbcd70a (P1), 3a6bf1fd (P2), 5e9cc5c4 (P3), 159f81dc (P4). Written together, committed by file: the P1 commit of acceptance-job.ps1 already holds the P4 hook (Write-NotRunByRule), and the P2 commit of ci.yml holds the P3 and P4 gating, so the single commits do not run on their own. Only the release branch is pushed, once.
Tests: dotnet build HRManagement.slnx -warnaserror 0 errors. Qa066*, CiStepsParity, WorkflowYamlLint, Scm001, DedupePushRun: 161 passed. actionlint: no findings.
Decisions:
- stageJobs[] in ci-scope.json groups order[]: lab-main; lab-rest; prodlike; prodlike-final; leastpriv; meta. The matrix of ci.yml must equal it (Qa066WorkflowTests).
- Each stage scans its own results: passwords are generated per runner, so the aggregate cannot look for them.
- Stage folder and artifact name come from stageJobs[].name; acceptance-merge.ps1 refuses a stage of another commit, another tree or other stages.
- Workflow-level concurrency replaces the job-level group: a new push cancels the whole superseded PR run; non-PR runs get a group of their own, so a dispatch no longer cancels a nightly.
- WorkflowYamlLintTests: the ENABLE_SCHEDULED rule gets ci.yml as a second exception, by D-QA-66-1. The variable stays false: the owner set it for Actions minutes when other schedules were on; the nightly is the owner's newer decision.
- Tools by digest: docker load may not restore a digest reference, so the meta stage might still fetch gitleaks and actionlint. Verify warns; measure in the first run.
Lock: acceptance-stack acquired 2026-11-06T18:07:39Z for the P1 local proof (lab:main).
Finished (UTC): 2026-11-06T18:26:30Z

## P1 local proof and P5 review
Local -Stage lab:main under the lock, 18:10:47Z to 18:36:43Z: Prepare 1 s, Stack lab 687 s (build included), Tests lab-main 863 s. 163 tests, the same count as run 42146523940's Tests lab-main.trx; guard PASS (57 classes); AC-01 holds the same files; the stage wrote 52 folders, all of them in the sequential layout. Non-required failures: the 4 of the CI run plus Ac08, Ac19, Ac30 (not required, local environment). Lock released 18:37:12Z after acceptance-reset.
Review (one fresh subagent, read only): 1 high, 2 medium, 4 low. Accepted the high after checking it against .dockerignore (docs/*.json are runtime content): reuse now also compares outsideTree, a hash of docs/ except *.md plus the trigger paths. Fixed: --no-renames in the rule, pipefail in Save and Load, identity.pullRequest compared, the comment on a failed reuse check, pull-requests permission dropped from reuse-check and Acceptance, overwrite on acceptance-evidence and run-identity for reruns. Open: tool images by digest, to measure in the first run. No refuter: the high was confirmed by reading the code.
PLAN-43 merged (PR 38, f8f5cd04) during the run: origin/main merged into the feature branch, no conflict.

## P5 gates
- ci-local run 1: red at Check formatting, 190 s (an anonymous object on one line in Qa066StageTests.cs). Fixed by c8d4d919.
- ci-local run 2 on c8d4d919: red, 758 s: 3 of 148 Testcontainers tests lost the PostgreSQL connection mid-read, and Install ReportGenerator lost its download (ResponseEnded). No product or test change between runs 2 and 3.
- ci-local run 3 on c8d4d919: green, 1305 s, 91 of 91 steps; unit 5124, integration 366, Testcontainers 148, 0 failed. So run 2 was the environment.
- Pre-push on c8d4d919: green, 124 s.
- S4: E2E runs beside CI. CI is not split: without meta the longest path is the Acceptance chain, to be measured; decide from the first full run.
P5 finished.
Finished (UTC): 2026-11-06T19:35:40Z

## P6
Release branch release/plan-66 at 62da6634, pushed 2026-11-06T19:36:16Z. PR: https://github.com/omardefdev/HRManagement/pull/39
Run 1: 42281107074, https://github.com/omardefdev/HRManagement/actions/runs/42281107074, the full run with meta (the PR changes .github/).
WAITING-CI: from 2026-11-06T19:36:16Z.
Next step: read run 42281107074 once. Green: record per-job durations, rerun one stage job, then the evidence commit. Red: fix on release/plan-66 and push that branch.

Resumed (UTC): 2026-11-06T20:21:11Z - the app restarted the session; state read from this file and git log.
Run 1 42281107074, 19:36:23Z to 20:16:59Z, 40.6 min, red on E2E only. Minutes: Dedupe 0.2, Gitleaks 1.0, Reuse check 0.4, CodeQL 13.0, CI 30.6, E2E 12.2 (failed), Acceptance images 14.9, stages: lab-main 23.5, lab-rest 19.3, prodlike 22.4, prodlike-final 24.4, leastpriv 10.5, meta 18.7 (698 s of it waiting for CI, tests 167 s, 15 of 15), Acceptance 0.5. Runner minutes about 192. Guard PASS, 71 classes, 13 trx. Image artifact 2.43 GB. meta=run, reuse=false (no earlier run).
E2E: 56 of 57; LEV_H1_Contracts_Persona_Reaches_Leave_Requests, 30 s timeout in WaitForLoadState inside PlaywrightFixture.LoginAsync. This PR changes neither E2E nor product code; the test passed in every earlier run recorded. Not rerun: the next commit gives it a fresh run. If it fails again it is treated as real.
Tool images: confirmed, docker load does not restore gitleaks and actionlint by digest, so the meta stage pulls those two from Docker Hub. Deviation from P-5, recorded for the final report; both are pinned by digest, so a stale image is not possible.
S4: CI 30.6 min is below the Acceptance chain (about 40), so CI is not split.
P-5: timeouts set to about 1.5 x measured: images 25, lab-main 35, lab-rest 30, prodlike 35, prodlike-final 40, leastpriv 20, meta 60, Acceptance 10. This code commit is also the not-reused proof of AC-QA-66c.

Run 2 42287724602 on 600ccde0, 20:21:56Z to 21:06:44Z, 44.8 min, all green. Minutes: Gitleaks 0.8, Reuse check 0.4, CodeQL 17.5, CI 36.3, E2E 17.5, Acceptance images 19.2, lab-main 23.1 (stack 517 s, tests 727 s), lab-rest 15.2, prodlike 18.4, prodlike-final 24.3, leastpriv 10.4, meta 20.4, Acceptance 0.5. LEV_H1 passed: the timeout of run 1 did not come back.
Stage rerun started 2026-11-06T21:09:36Z: gh run rerun 42287724602 --job 128142439567 (lab-main).
WAITING-CI: rerun of lab-main.
Next step: read attempt 2 once, record the stage and Acceptance durations; then copy acceptance-evidence from the green run, validate with -Committed, commit, push: that run must reuse.

Stage rerun measured: started 21:09:36Z, lab-main 21:09:42Z to 21:33:24Z (23.7 min), Acceptance 21:33:27Z to 21:33:50Z: 24.2 min in all, attempt 2 green.
Evidence: acceptance-evidence copied from the artifact of run 42287724602 attempt 2; acceptance-baseline-validate -Committed PASS; 79 ACs: 33 proven, 26 passed but not proving, 8 stale, 4 incomplete, 6 not run, 0 fail, 2 blocked.

Evidence commit af52f605, pushed 21:42:58Z. Locally first: validator -Committed PASS, gitleaks and forbidden PASS on the range, unit suite 5124 of 5124 (a reusing run skips it, and PLAN-41's evidence commit once broke unit tests).
Run 3 42299004110 on af52f605, 21:43:04Z to 21:59:10Z, 16.1 min, all green, set by CodeQL (15.9). CI 0.7, E2E 0.1, Acceptance images 0.1, six stage jobs 0.1 to 0.2 each, Acceptance 0.3. reusedFrom 42287724602 in run-identity and reuse.json, same codeTree and outsideTree. committed-evidence ran under reuse: 1502 files, validator PASS. Runner minutes about 20.
Merged by agent on owner instruction (UTC): 2026-11-06T22:03:16Z. PR https://github.com/omardefdev/HRManagement/pull/39, merge commit f77dd28b, 14 checks green. Local main = origin/main = f77dd28b in the second clone.
Post-merge:
- Push run 42301677430: Dedupe success; CI, CodeQL, E2E, Reuse check, Acceptance images, stages, Acceptance skipped. Gitleaks was still running when read.
- simulate_missing run 42301740446, dispatched 22:03:44Z on AcHcm26AssessmentInterviewTests.Ac26_i_intake_close_job_closes_expired_posting: must end red.
- Manual nightly run 42301745454: meta must run and pass.
WAITING-CI: the two dispatches.
Next step: read both once, record them, write reports/plan66-final-report.md, set the plan document status. Those stay in the working tree: they ride with the next plan's first commit.

Post-merge proofs read 2026-11-07T08:22Z (the background watch stopped at 22:35:32Z on a connection error).
- Run 42301740446 (simulate_missing): red as required, 22:03:46Z to 23:11:28Z. lab-rest failed, Acceptance failed in 0.6 min, the guard names AC-HCM-26/26i, needs-check FAIL. Whole run 67.7 min only because E2E took 67.2 (Playwright install 52.3).
- Run 42301745454 (manual nightly): green, 22:03:49Z to 22:54:19Z, 50.5 min, meta 15 of 15 in 144 s, guard PASS, 71 classes, 13 trx, no not-run-by-rule.json. E2E 49.8 (Playwright install 32.9).
- Push run 42301677430: green, dedupe skipped the heavy jobs.
- No scheduled run yet at 08:23Z: schedules here run hours late (advisory-to-issue 0 6 * * * fires near 13:20Z).
Report: reports/plan66-final-report.md. Plan document status updated. All three stay uncommitted for the next plan's first commit.
Finished (UTC): 2026-11-07T08:25:13Z
