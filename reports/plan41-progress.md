Started (UTC): 2026-11-04T08:48:18.5456337Z

# PLAN-41 progress

## P0 preconditions (checked 2026-11-04)

1. PLAN-40 merged: PR 35 MERGED at 2026-11-04T08:44:28Z, merge commit e5071b42. Nothing to finish.
2. main = origin/main = e5071b42.
3. Tree clean except the allowed items (global.json, tmp-vendors.html, this prompt, reports/plan40-progress.md).
4. No other plan session on this working tree. One side session, "Retry keycloak-reconcile after PostgreSQL recreate", runs in its own
   worktree (.claude/worktrees/peaceful-dhawan-dfb51a); it started from the PLAN-40 session and does not share this tree.
5. gh: omardefdev, scopes include repo and workflow.

Interrupted once by the owner right after the Started stamp; the owner's next message was "اكمل", so work continued at once.

Correction: the first two commits were made with the wrong author address, then recreated locally with the repository identity
(omardefdev <omar@defdev.sy>) before anything was pushed.

## Timing notes (P0)

- Class inventory (one subagent, read-only): 912 s, 74 classes, 203 test methods; full table in artifacts/plan41/class-inventory.md.
- Image build for the baseline (reset plus 16 images one at a time, warm cache): 688 s.

## P0

Baseline in reports/plan41-baseline.md. Before picture: 38 Pass, 2 Blocked, 0 Fail, 20 not run (old generator);
0 proven, 41 stale, 6 incomplete, 30 not run (new rules).

Durations: image build 688 s; lab up 254 s; reset 6 s; prod-like up 240 s; lab-main 814 s; AcHcm26 136 s; AcHcm27 276 s; AcHcm25 43 s;
AcInt37 143 s; AcMsg40 95 s; AcOps39 79 s.

GitHub, read-only: PUBLIC; protection 404 (available); no ENABLE_ACCEPTANCE_JOB variable.

Decisions from the inventory (each will be in the final report):
- AcOps36VpsBootTests and AcInt37VpsServerTests excluded (SSH) although D-QA-4 names wave 1: P-6 names *Vps* classes.
- The 7 infrastructure classes, on which D-QA-4 is silent, are required: they are the plan's own guards.
- AC-QA-34/35 stay required: their inputs are provided (CI job artifact, base stack, job token), not their scenarios dropped.
  Ac34_h alone is excluded: it needs a native Windows run with ar-SA.
- AC-OPS-38 runs as the group leastpriv of the prod-like step (P-6).

Finished (UTC): 2026-11-04T09:52:00Z (P0)

Correction: the P0 Finished stamp above was typed by hand and runs about 4 minutes ahead of the clock; the commit was at 09:48Z. Later stamps come from the clock.

## P1

Persona counts derive from AcceptancePersonaCatalog (ApprovedRegularPersonas, ApprovedNegativePersonas). The fixed count left
scripts/keycloak/acceptance-personas.ps1 (PLAN-39 moved it there from prepare-keycloak-import.ps1). The file scan reads the password from
.acceptance/credentials.json. Parity test plus a negative on a fixture copy (AC-QA-41c). The last ACCEPTANCE_PERSONA_PASSWORD, in ci.yml, goes with
the job in P5. Static tests green (69/69 with P2-P5 test files present); dotnet build -warnaserror 0 errors.

Finished (UTC): 2026-11-04T09:49:35Z (P1)

## P2

EvidenceRecorder records profile, sourceSha, codeTree, imageRevisions, source, ciRunId, ciEvent; AcceptanceRunContext (in the E2E project, so the journey
evidence uses it too) reads them once per process. Scenario key defaults to the test method, and every scenario keeps testClass, profile and codeTree.
ACCEPTANCE_SIMULATE_MISSING in AcceptanceGate. One summarizer (scripts/acceptance-evidence-summarize.ps1, states in scripts/acceptance-evidence-lib.ps1);
AcceptanceEvidenceSummary.cs deleted. tests/acceptance/ac-ownership.json: 71 entries. scripts/acceptance-baseline-validate.ps1 with -Committed.
Added beyond the plan: summary.json for the validator; rows for nested folders (AC-HCM-33/prodlike, AC-OPS-36/vps), owner from the parent.
Tests: AcceptanceEvidenceSummaryTests, AcceptanceGateTests green.

Finished (UTC): 2026-11-04T09:49:35Z (P2)

## P3

docker/DotnetService.Dockerfile and docker/Migrator.Dockerfile: ARG SOURCE_SHA=unknown and the revision label, last, so a new commit reuses the layers.
docker-compose.yml and docker-compose.services.yml: build.args.SOURCE_SHA on all 16 build definitions. acceptance-up.ps1: -Profile Lab|ProdLike (a thin name for
-ProdLike), -SourceSha, -dirty when a tracked file changed, one image at a time on Windows (P-1). lint-compose both chains PASS (production 22, single-host 24
services): the server's compose renders. The server builds with docker/service-build-matrix.json and passes no SOURCE_SHA, so its label stays "unknown" until a later
plan wants it; build-on-host was not changed.

Finished (UTC): 2026-11-04T09:50:09Z (P3)


## P4

Resumed (UTC): 2026-11-04T10:54:51Z (first local job run ended; no reason other than waiting for it)

One job script for CI and the owner (scripts/ci/acceptance-job.ps1), tests/acceptance/ci-scope.json (lab 62, prodLike 7, excluded 10, each with owner and review date,
registered in docs/qa/dated-obligations.json), the guard (scripts/ci/acceptance-guard.ps1) and the artifact scan (scripts/ci/acceptance-artifact-scan.ps1).
AC-EST classes go through EstablishmentChangeFlow (PLAN-25 approval rule), seeds fixed where the AC-EST triage showed a test or seed issue, not a product defect.

Local proof, five runs of the job (durations are the job's own):
- Run 1 (PS 5.1): 3875 s, stopped at leastpriv: a second compose build hit NETSDK1064 after 1077 s. Fix: leastpriv reuses the tested images by tag. Also two PS 5.1 defects of the job itself: an empty -Classes
  argument and a Generic.List inside an ordered hash. PII scan: double quotes stripped by 5.1, now on stdin. File scan: -UseBasicParsing.
- Partial runs: up --wait refused keycloak-reconcile exiting first (race on any slow host): the gateway now waits for it, as production does. AC-OPS-39g depended on an earlier restart: the test restarts HR.Core itself when the canary line is absent.
- Run 2: 3297 s, 40f timed out on one 15 s /metrics scrape (now 60 s), and hrm-bootstrap failed with "tuple concurrently updated" against keycloak-db-role: it now waits for that one-shot.
- Run 3: failed because the machine slept 12:55-14:28Z and the kcadm session expired (Windows log, Kernel-Power 42). Not a code result. keep-awake requested from then on.
- Run 4 (pwsh 7.6 from a tool folder in the session scratchpad, the same path as CI, with -RequireCleanBaseline): 4759 s, EXIT 0, acceptance-job: PASS, 67 classes in 11 trx files, acceptance-verify Pass on both stacks.
  Stack lab 385 s (no build), lab-main 730 s, journey 18 s, AcHcm26 148 s, AcHcm27 293 s, stack prodlike 363 s, journey 22 s, main 39 s, AcInt37 158 s, AcMsg40 100 s, AcOps39 103 s, final 59 s, PII 2 s, stack leastpriv 546 s, leastpriv tests 1779 s.
  The last figure is inflated: the machine slept again at 15:33Z during 38f (a closed lid is outside keep-awake); the same tests took 193 s before.
- AC-QA-41a local negative (ACCEPTANCE_SIMULATE_MISSING=AcHcm26AssessmentInterviewTests.Ac26_i_intake_close_job_closes_expired_posting): 433 s, exit 1, the scenario named by the guard. Guard negatives by fixtures in AcceptanceCiScopeTests.
Image build, when needed: 11-15 min here, one image at a time.

Not required and red locally (shown only): Ac08, Ac21, Ac23, Ac24, Ac25, Ac12 (NotExecuted). Their owners are in ac-ownership.json. Ac13, Ac19, Ac26, Ac28, Ac30 went green once the scripts ran under pwsh or -UseBasicParsing.
git status acceptance-evidence: clean after every local run.

Tests: unit governance and compose subset 299/299; static acceptance 73/73; dotnet build HRManagement.slnx -warnaserror 0 errors, 0 warnings (46 s).

Finished (UTC): 2026-11-04T16:08:24Z (P4)
