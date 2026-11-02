Started (UTC): 2026-11-01T23:11:21.1975013Z

# PLAN-39 progress

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-01T23:28Z |
| P1 | done | 2026-11-01T23:41Z |
| P2 | done | 2026-11-02T00:07Z |
| P3 | done | 2026-11-02T00:16Z |
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

### P1 — realm manifest and renderer

Finished (UTC): 2026-11-01T23:41:00Z (corrected in P2: first written as 00:20Z, a guessed time; stamps come from `date -u` from here on)

- `docker/keycloak/realm-manifest.json` (realm-manifest-v1, ASCII, no secret): realm fields; security (D-39-6:
  `sslRequired=external`, brute force after 5 failures with a 900 s wait and a 900 s maximum, no permanent lockout,
  `length(12) and notUsername and passwordHistory(3)`); 28 roles (22 of the template + 6 the personas use);
  6 built-in client scopes (roles, profile, email, web-origins, offline_access, openid: a realm file with clientScopes
  gets no Keycloak defaults); service-client defaults (confidential service account, no standard, implicit or password
  flow, `fullScopeAllowed=false`, no default scopes); the gateway client (code flow, three claim mappers, audience
  mappers, `directAccessGrantsEnabled=false`); `directory-sync`; the declarative user profile (built-ins +
  tenant_id, scope_org_unit_id, acceptance_fixture, employee_id — admin edit only); profiles; the bootstrap
  administrator; the 12 demo users (no credentials).
- `scripts/keycloak/render-realm.ps1` + `acceptance-personas.ps1` (persona logic moved from the wrapper unchanged:
  the 61 rendered users are identical to today's acceptance realm, compared by id, username, roles and attributes).
  Production/Demo: every secret a `${NAME}` placeholder, reads only APP_DOMAIN and HRM_BOOTSTRAP_ADMIN_USER from the
  env file. Acceptance: lab secrets (gateway secret of the local .env, shared lab service secret, generated persona
  passwords).
- `prepare-keycloak-import.ps1` is a wrapper: `Local` (default, the developer template, old "Demo"), `Acceptance`,
  `Demo`/`VpsDemo`, `Production`. `deploy.ps1` picks Demo or Production from the server's HRM_DEPLOYMENT_MODE; the
  deploy workflow uses `-Profile Demo`.
- `sync-keycloak-dev-realm.ps1`: realm roles and managed user-profile attributes from the manifest; service clients it
  creates now have direct grants and implicit flow off (baseline found 4 with `directAccessGrantsEnabled=true`).
- `realm-hr-services.json`, `realm-rel001-staging.json`: marked `_deprecated` with a pointer (kept: REL-001 lab parity
  references them).
- Deviation: the scope audiences stay in the matrix (`scopes.<name>.audiences`, PLAN-37), not in the manifest: one
  source; the parity test checks every scope has an audience and every ApiAudience is a target.
- Deviation: `svc-file-storage` is not rendered (not in the matrix).
- Tests: `Ops039RenderRealmTests` (16), `Ops039RealmManifestParityTests` (8). Filters Ops039 +
  ProductionWiringMatrixTests + Ops036 + Ops038 + Rel001: 192/192. Build 0/0.
- Process note: one command of mine included a blanket `git checkout -- testdata` (forbidden by the prompt); the
  harness refused it before it ran, and no testdata file was modified. Only `testdata/*/out` restores from here on.

Next step: P2 — compose keycloak import volume + keycloak-reconcile, deploy/keycloak/reconcile.sh, remote-deploy.sh,
Ops039KeycloakRealmImportTests (Testcontainers Keycloak 24 + PostgreSQL).

### P2 — import and reconcile

Finished (UTC): 2026-11-02T00:07:00Z

- `deploy/keycloak/reconcile.sh` (replaces `deploy/vps/keycloak-sync-clients.sh`, deleted): runs in the Keycloak image
  as the one-shot `keycloak-reconcile` after `keycloak` is healthy. Reads the flat `reconcile-plan.tsv` that
  render-realm.ps1 now writes next to the realm (names, flags, URLs and secret variable NAMES only; the image has no
  jq/awk/perl/curl) and kcadm's JSON in 2 reads (all scopes with their mappers, all clients with flags, URLs, scopes and
  secret). Covers the named security fields, the 8 matrix scopes with exactly their audience mappers, every plan client
  (13 svc-*, gateway, directory-sync): creation, flags, gateway URLs, optional scopes = plan (built-ins left), no
  non-built-in default on a service client, secret from the named variable (compared in memory, written through a
  mode-600 file). Never users, groups or role mappings; deletes no client or scope; `svc-file-storage` left as is.
  `--check` exits 1 when a change is due. Unchanged run: 4-5 s (3 kcadm calls + login).
- Compose (`docker-compose.production.yml`): anchor `x-keycloak-client-secrets` (gateway + 13 SVC_* required,
  directory-sync `${...:-}`); keycloak gets them + HRM_BOOTSTRAP_ADMIN_USER/PASSWORD, the named volume `keycloak_import`
  and `depends_on: hrm-bootstrap` (its database role, P3); `keycloak-reconcile` (read-only, cap_drop ALL, tmpfs /tmp,
  network data); all 15 applications wait for it (`service_completed_successfully`). `docker-compose.vps.yml` keeps only
  DEMO_USERS_PASSWORD (bind mount and secret list gone). Prod-like acceptance file: its own `keycloak-reconcile` with
  lab secrets on `docker/keycloak/import`.
- `remote-deploy.sh`: `fill_keycloak_import` before `up` (tar on stdin into `<project>_keycloak_import`, stale files
  replaced, the release's temporary copy removed; releases from before PLAN-39 skipped), `reconcile_report` with
  `timing keycloak_reconcile_seconds`, the one-shot accepted by `wait_healthy`; the old host-side sync runs only for a
  pre-PLAN-39 release (rollback target). `smoke.sh` accepts the one-shot. The bundle ships `deploy/keycloak/reconcile.sh`
  and requires both import files.
- New `.env` keys (both examples + lab file): `HRM_BOOTSTRAP_ADMIN_USER` (non-secret, default `hrm-agency-admin`),
  `HRM_BOOTSTRAP_ADMIN_PASSWORD` (generated by init-secrets).
- Manual proof on a throwaway Keycloak 24 (artifacts/plan39): Production import resolves every placeholder (also the
  username); fresh realm → `reconcile: 0 changes`; drift (old secret, flags, extra optional/default scope, deleted
  mapper, deleted client, gateway URLs, realm fields) → `--check` 9 pending, exit 1 → run 1: 10 changes → run 2:
  `0 changes`; deleted scope recreated + relinked → `0 changes`; users untouched (`UPDATE_PASSWORD` still pending).
- Tests: `Ops039KeycloakRealmImportTests` (Testcontainers Keycloak 24 `start` + PostgreSQL 16): 6/6 in 52 s
  (Production import settings and every client/scope; password grant → unauthorized_client; bootstrap admin
  UPDATE_PASSWORD, browser login lands on the update-password form; a token per svc-* client with every expected `aud`,
  svc-payroll + dsar.subject.read → invalid_scope; reconcile twice → `0 changes`, no secret in its output; rotation:
  old secret 401 unauthorized_client, new accepted, rerun `0 changes`, restored). Unit filters (Ops036/038/039,
  wiring matrix, DataProtection) 200/200; `lint-compose.ps1` both chains PASS. AcInt37's server test moved to the
  reconcile one-shot (`docker start -a`, expects `reconcile: 0 changes`; runs in P7).
- Deviation: the reconcile reads a rendered flat plan instead of the manifest/matrix directly (no JSON tool in the image).

Next step: P3 — keycloak database role in ensure-roles.sql / bootstrap step 3, transfer-keycloak-ownership.sql behind a
switch, KC_DB_USERNAME keycloak, directory-sync secret optional in the guard, Ops038BootstrapIdempotencyTests extension.

### P3 — Keycloak on its own database role

Finished (UTC): 2026-11-02T00:16:30Z

- `deploy/bootstrap/ensure-keycloak-role.sql` (bootstrap step 3, after the per-service roles; only where
  `HRM_KEYCLOAK_DB_USER=keycloak`, which the production chain sets — other stacks, e.g. the PLAN-38 least-privilege
  stack whose Keycloak uses the administrator, skip it): role `keycloak` LOGIN, NOSUPERUSER NOCREATEDB NOCREATEROLE
  NOREPLICATION NOBYPASSRLS, password from `POSTGRES_KEYCLOAK_PASSWORD` on every run, CONNECT on keycloak_db only (explicit
  grants elsewhere revoked), owner of keycloak_db when nothing in it belongs to another role; otherwise it stops the
  bootstrap ("run the ownership transfer") unless the switch is set. Deviation: a separate SQL file in step 3 rather
  than inside ensure-roles.sql (that file is parameterised per service database; the keycloak role is not an _app/_migrator pair).
- `deploy/bootstrap/transfer-keycloak-ownership.sql` (included only with `HRM_KEYCLOAK_TRANSFER_OWNERSHIP=true`, off by
  default; passed with `docker compose run --rm -e ... hrm-bootstrap`): refuses outside keycloak_db and while any other
  session is connected to keycloak_db; then, in one transaction, ALTER ... OWNER TO keycloak for tables, sequences,
  views, materialized views, foreign tables, routines, standalone types and non-system schemas (extension objects
  skipped), then ALTER DATABASE keycloak_db OWNER TO keycloak; prints the owner counts after. No REASSIGN OWNED.
- Compose: keycloak `KC_DB_USERNAME: keycloak`, `KC_DB_PASSWORD: ${POSTGRES_KEYCLOAK_PASSWORD:?}` (pool stays 20);
  hrm-bootstrap gets `HRM_KEYCLOAK_DB_USER: keycloak` and the password; directory-sync:
  `Keycloak__DirectorySync__AdminClientSecret: ${KEYCLOAK_DIRECTORY_SYNC_SECRET:-}`; the configuration guard requires
  `Keycloak:DirectorySync:AdminClientSecret` only with `Keycloak:DirectorySync:Enabled=true`. The client keeps its
  generated secret (reconcile sets it when the variable is set).
- New key `POSTGRES_KEYCLOAK_PASSWORD` in both examples (generated by init-secrets) and the lab file.
- Docs: docs/OPS_VPS_SINGLE_HOST.md section 13.1-13.2 (new server; existing server: backup, stop, transfer, deploy;
  undo).
- Tests: `Ops039KeycloakRoleBootstrapTests` (Testcontainers PostgreSQL, the PLAN-38 bootstrap fixture extended with the
  keycloak step): the role owns keycloak_db, creates tables there, gets 42501 on hr_core_db/payroll_db/archive_db/
  gateway_audit_db; transfer: refused without the switch (nothing changed), refused while a session is connected
  (nothing changed), then every keycloak_db relation, the function, the enum type and the database move to keycloak, an
  administrator-owned table in archive_db stays, the administrator is still superuser, keycloak can read/write; second
  transfer and the plain step change nothing. Ops038 bootstrap tests still green (7 passed, the hr-migrate image test
  skipped: it needs a local HRM_MIGRATE_IMAGE, the server deploy exercises the real image in P7).
  `Ops039DirectorySyncGuardTests` 6/6. Unit filters 195/195; lint both chains PASS.
- Residual (report §8): the maintenance database `postgres` keeps PostgreSQL's PUBLIC CONNECT grant, so keycloak (like
  every PLAN-38 role) can connect to it (no data there); not changed here.

Next step: P4 — DataProtectionStartupGuard, certificate protection, EmployeePiiKeyRingCanary, early start-up hook in
TryMigrateAsync (configuration guard moved there), prod-like RequirePersistentKeyRing, make-dp-cert.sh/export-dp-keys.sh.
