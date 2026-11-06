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
