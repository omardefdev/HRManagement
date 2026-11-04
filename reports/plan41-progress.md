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

