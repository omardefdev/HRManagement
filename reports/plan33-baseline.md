# PLAN-33 — Baseline (P0)

**Date:** 2026-10-29
**Branch:** `feature/plan-33-verification-docs`, created from `main` @ `4342bb6` (PLAN-26..32 merged).

## Build

`dotnet build HRManagement.slnx` → **0 errors**, 4 existing warnings (NU1903 SSH.NET).

## Unit tests

Filter of the prompt, run as `FullyQualifiedName~` clauses: `Hcm0|Acceptance|E2e|Architecture`

| Run | Total | Passed | Failed | Skipped |
|-----|-------|--------|--------|---------|
| Baseline filter | 1192 | 1191 | 1 | 0 |

The one failure is the known environment/baseline failure
`BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard` (offenders in HR.Contracts
`CommencementRecords`, `Contracts`, `HireWizard`, `ProbationRecords`, `SeparationRecords` `Index` actions). It is listed in
`reports/plan32-baseline.md` / `reports/plan31-baseline.md` as a pre-existing failure and is not in PLAN-33 scope (BOLA
org-scoping of list actions belongs to PLAN-25, G-15 family). Any other failure after a PLAN-33 change is a regression.

The other known environment-only failures of the full unit suite (ar-SA locale with the Um Al-Qura calendar, no `pwsh`,
`*_validate_script_passes` under the default execution policy) are the set recorded in `reports/plan32-final-report.md`.

## Tooling on this machine

| Tool | State |
|------|-------|
| .NET SDK | as pinned by `global.json` (local modification of `global.json` is not committed) |
| Docker Desktop | running; acceptance stack `hrm-acceptance-*` up (see stack check) |
| Python | **not installed** (`python3` is the Windows Store alias only). Relevant to P7 (`scripts/extract_database_inventory.py`). |
| `actionlint` | not installed (checked in P1) |

## Stack check (2026-10-29)

Running containers (`docker ps`): `hr-core-web` (image 16:28), `dev-analytics-web` (15:56), `hr-gateway` (14:33), and the
other web services built 14:06–14:11 from `main` before the PLAN-32 merge; `keycloak` 24.0, `postgresql` 16, `rabbitmq`
3.13, **`clamav` 1.4 (healthy)**.

**`file-storage-web` is not running and no container exists for it** (`docker ps -a` empty), although
`docker-compose.acceptance.yml` declares it (port 6340) and an image `hrm-acceptance-file-storage-web` exists (built
06:39). This is decision 6: PLAN-32 recorded AC-30c skipped and `Ac27_f_g` failing for this reason.

What P6 needs:

- rebuild `hr-core-web`, `hr-contracts-web`, `hr-gateway` (and any service touched) from this branch;
- start `file-storage-web` (fix `scripts/acceptance-up.ps1` / compose if it is not started by default);
- re-import the Keycloak realm only if a persona used by the new scenarios is missing.
