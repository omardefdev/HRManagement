Started (UTC): 2026-11-01T23:11:21.1975013Z

# PLAN-39 progress

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-01T23:28Z |
| P1 | pending | |
| P2 | pending | |
| P3 | pending | |
| P4 | pending | |
| P5 | pending | |
| P6 | pending | |
| P7 | pending | |
| P8 | pending | |

## Log

### P0 — timing, preconditions, carry-over, baseline

Finished (UTC): 2026-11-01T23:28:00Z

- Preconditions:
  1. PLAN-38 PR #33 was already **merged by the owner** at 2026-11-01T23:10:47Z (34 s before this session started;
     all checks green, billable minutes 0, post-merge dedupe guard skipped CI/CodeQL/E2E/Acceptance). Recorded in
     reports/plan38-progress.md (not "merged by agent": the owner merged it).
     Post-merge `hardening-check` run 41360824103: success.
  2. main = origin/main = e31d8bb0.
  3. Tree clean after `git checkout -- testdata/*/out` and the two nested outs
     (`testdata/rel002/payroll-parallel/out/compare-report.json`, `testdata/rel003/game-day/out/mtta-mttr-ledger.json`);
     left: owner's `global.json` edit, `tmp-vendors.html`, this prompt (committed), `reports/plan38-pr.md` (committed with
     the merge note).
  4. No other plan session on this tree. One unrelated container runs on Docker Desktop (`exciting_germain`, a Flutter
     build of another project) — not touched.
  5. `ssh -o BatchMode=yes hrm-vps whoami` = `hrm`; ED25519 host key `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`.
- Carry-over: PLAN-38 commit 6287e41a merged into this branch first (owner's 2026-11-02 choice, memory note).
- Commits: 509f9584 (merge 6287e41a), 0cade083 (prompt), 46363c2a (PLAN-38 merge note), P0 baseline commit.
- Baseline: reports/plan39-baseline.md; export reports/plan39-realm-acceptance-export.json (no secrets, checked).
  Throwaway Keycloak 24 for the export: ready 25 s, dev sync 202 s (~0.8 s per kcadm call).
- Build: `dotnet build HRManagement.slnx -warnaserror` 0 warnings, 0 errors (34 s).
- Server (read-only): Keycloak on `hrm_admin`; keycloak_db: 92 tables, 193 indexes, 92 row types owned by `hrm_admin`,
  no sequences/views/functions, schema public = pg_database_owner; realm already `external` + brute force (factor 10),
  no password policy; 15 key rings each with one key; 500/500 national IDs protected; **canary ok** (read-only probe),
  negative control with another ring fails. No server change in P0.

Design decisions taken in P0 (reasons):
- D-P0-1 **Early hook** = a shared `IHrStartupCheck` runner called at the top of `TryMigrateAsync` (before its
  `RunMigrationsAtStartup` early return): every one of the 15 hosts calls it before migrations and seeds; no shared
  start-up helper exists otherwise. ProductionConfigurationGuard, DataProtectionStartupGuard and (HR.Core)
  EmployeePiiKeyRingCanary run there.
- D-P0-2 **Canary under RLS:** `Employees` is under tenant RLS; with `hr_core_app` (least-privilege) a query without
  `app.current_tenant` sees nothing and would pass vacuously. The canary reads without a tenant first, then per tenant
  from `"Tenants"` (set_config local to a transaction). No index on `NationalId`: `LIMIT 1` stops at the first
  protected row (all rows are protected in practice); no migration added for an index.
- D-P0-3 **Canary never creates a key:** with protected data present and no `key-*.xml` in the ring directory it
  refuses without calling Unprotect (Unprotect on an empty ring would generate a key).
- D-P0-4 **`svc-file-storage` leaves the realm** (not in the matrix, plan §1); reconcile leaves an existing one alone
  (disabled on the server), it does not delete clients.
- D-P0-5 **Reconcile in the Keycloak image** (P-2): no awk/jq/curl there; sed, grep, cut, sort, comm, timeout, mktemp and
  kcadm (0.8 s per call). Written for bash + kcadm `--format csv`.
- D-P0-6 **Profiles:** `prepare-keycloak-import.ps1` keeps the local developer realm as profile `Local` (the old default
  "Demo": template, localhost, `hradmin`), because CI (`prepare-ci-compose-env.sh`), `dev-profile-up.ps1` and
  `fun005ext-docker-up.ps1` call it without a profile; `Demo` now means the server demo (APP_DOMAIN); `VpsDemo` stays as
  an alias of `Demo`.
- D-P0-7 Acceptance keeps its lab secrets (persona passwords from `.acceptance/credentials.json`, the shared lab service
  secret); Production and Demo carry `${NAME}` placeholders only (P-1).

Next step: P1 — write docker/keycloak/realm-manifest.json and scripts/keycloak/render-realm.ps1.
