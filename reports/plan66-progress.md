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
