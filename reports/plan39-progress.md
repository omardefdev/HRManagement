Started (UTC): 2026-11-01T23:11:21.1975013Z

# PLAN-39 progress

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-01T23:28Z |
| P1 | done | 2026-11-01T23:41Z |
| P2 | done | 2026-11-02T00:07Z |
| P3 | done | 2026-11-02T00:16Z |
| P4 | done | 2026-11-02T00:29Z |
| P5 | done | 2026-11-02T01:24Z |
| P6 | done | 2026-11-02T01:24Z |
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

### P4 — Data Protection: guard, canary, optional certificate

Finished (UTC): 2026-11-02T00:29:30Z

- Early hook (P-3): `HrStartupChecks` (`IHrStartupCheck`, run once per host in registration order) called at the very
  top of `TryMigrateAsync` — before its `RunMigrationsAtStartup=false` early return and outside its try (a refusal is
  never downgraded to a warning) — which all 15 hosts call before migrations and seeds; a hosted fallback runs them for
  a host that never calls it. `ProductionConfigurationGuard` is now such a check (registered first in
  `AddHrProductionNetworking`; PLAN-38 review F5 ordering part). The "Production default for an absent posture" part of
  F5 is left to OPS-D (recorded).
- `DataProtectionStartupGuard` (registered by `AddHrDataProtection`): in Production or with
  `DataProtection:RequirePersistentKeyRing=true`, refuses an empty `KeyRingPath`, a path it cannot write (probe file
  written and removed), a missing/different application name, a configured certificate file that does not exist.
  Messages name the service and the path.
- Certificate protection (D-39-3, off on the demo server): `DataProtection:CertificatePath` (+ `CertificatePassword` from
  the environment) → `ProtectKeysWithCertificate`, `UnprotectKeysWithAnyCertificate(current + PreviousCertificatePaths)`;
  legacy unencrypted keys stay readable. `deploy/vps/make-dp-cert.sh` (RSA 3072, 10 years, PFX 600, password generated
  into .env, never overwrites; verified in a container: the PFX loads in .NET and encrypts the key ring) and
  `deploy/vps/export-dp-keys.sh` (encrypted tar of every dp volume + certificate, AES-256-CBC/PBKDF2 600000, passphrase
  from the environment, SHA-256 beside it; verified on two test volumes, wrong passphrase refused). Neither enabled on
  the server.
- `EmployeePiiKeyRingCanary` (HR.Core.Infrastructure, registered in HR.Core's Program): raw SQL `LIMIT 1` for one
  `CfDJ8…` NationalId, first without a tenant, then per tenant of `Tenants` with `app.current_tenant` local to a
  transaction (D-P0-2: a least-privilege role sees nothing otherwise); protected data + no `key-*.xml` → refused without
  calling Unprotect (no key created); unprotect failure → refused; missing `Employees` when migrations ran before start →
  refused (not "no data"). Enforced in Production / RequirePersistentKeyRing; `AllowKeyRingReset=true` → Critical + counter
  `dataprotection_key_ring_reset_total` (meter HRManagement.DataProtection). Never prints a value or a key. Index: none on
  NationalId; LIMIT 1 stops at the first protected row (D-P0-2).
- Prod-like file: `DataProtection__RequirePersistentKeyRing: "true"` on all 15 services.
- Tests: `DataProtectionStartupGuardTests` (11 incl. certificate protection, rotation, legacy keys, enforcement matrix)
  and `HrStartupChecksOrderingTests` (5: checks before the first DbContext, refusal before any migration also outside
  Production, also with migrations skipped, once for two contexts, every Program.cs runs the hook before its first
  seed, HR.Core registers the canary) — 28/28 with the related filters; `Ops039PiiKeyRingCanaryTests` (Testcontainers
  PostgreSQL with tenant RLS and a NOBYPASSRLS role) 6/6 in 23 s; full unit suite **4557/4557** (64 s); build 0/0.
- Deviation: the canary is an `IHrStartupCheck`, not an `IHostedService` (P-3).

Next step: P5 — prod-like acceptance: keycloak DB role one-shot for the prod-like stack (39e), AcOps39 tests, image
builds (one at a time, prune BuildKit first), fresh keycloak_db, render Acceptance, up --wait, run AcOps39 + AcHcm25/30.

### P5 — acceptance on the prod-like stack

Finished (UTC): 2026-11-02T01:24:00Z

- Images: 15 service images built one at a time (P-6): 525 s total (30-48 s each, warm cache, no NETSDK1064). The
  BuildKit cache (72 GB "reclaimable") could not be pruned on this Docker Desktop: `docker builder prune`/`buildx
  prune` with `--keep-storage`, `--max-used-space`, `--filter until=48h`, `-a`, on both builders reported 0 B; recorded,
  builds were unaffected.
- Prod-like file additions: one-shot `keycloak-db-role` (postgres:16-alpine running deploy/bootstrap/ensure-keycloak-role.sql,
  transfer off) and keycloak `KC_DB_USERNAME: keycloak` (lab password `ACCEPTANCE_KEYCLOAK_DB_PASSWORD`, lab default) so
  39e runs here too (P-4).
- Stack (plan's command, `up -d --wait`, P-6): Acceptance realm rendered (1 s), keycloak_db dropped and recreated (new
  Keycloak "volume": Keycloak stores in keycloak_db), `up -d --wait` 18 s, last application listening 20 s after `up`,
  all 15 `/health/ready` 200. keycloak-db-role exit 0 in 0.2 s; Keycloak imported the realm as `keycloak`;
  keycloak-reconcile on the new import: `reconcile: 0 changes` (32 s while the stack was starting). HR.Core canary on
  the existing acceptance data: "a protected national ID decrypts with the key ring at /var/hrm/dpkeys".
- Unit/Testcontainers filters (PS 5.1 forms): `dotnet test UnitTests --filter "FullyQualifiedName~Ops039|FullyQualifiedName~DataProtection"`
  green (part of 4557/4557); Testcontainers Ops039: 6 (realm import) + 2 (keycloak role) + 6 (canary) = 14/14.
- **AC-OPS-39a..h: 8/8 Pass** with RUN_ACCEPTANCE=true, ACCEPTANCE_REQUIRE_ALL=true (71 s; 39f's full
  `up -d --force-recreate` inside). First run 7/8: 39f's browser sign-in restarted at Keycloak because .NET's
  CookieContainer sends Keycloak's Version=1 cookies back in RFC 2965 form over http; the test now uses a small manual
  cookie jar (test-only fix). Evidence acceptance-evidence/AC-OPS-39/ (24 files): scanned against 85 secret values of
  .env, the persona passwords, the caller-proof secret and the lab defaults → 0 hits; no CfDJ8 value, no connection
  string. Notes: 39c's account REST API answers 403 to the user (imported users have no account-console role) and
  tenant_id stays unchanged (also a managed attribute, admin edit only); 39g plays the lost dp_hr_core volume with an
  empty scratch ring on a clone of hr_core_db (the acceptance stack shares one key-ring volume between services).
- Regression: on the prod-like stack AcHcm25 8/8 + AcHcm30 6/8 (283 s): 30i needs Recruitment:Retention:Apply=true,
  which the prod-like file sets to false since PLAN-25 (by design, not this plan), 30j's fixtures were consumed by an
  earlier run. After `down --volumes` (project-scoped lab data only) a fresh **lab-profile** stack: AcHcm30 **8/8**
  (240 s). Regression evidence in artifacts/plan39/evidence-regression (not committed).
- Finding for its owning plan (QA / PLAN-41): a fresh plain acceptance stack (without the least-privilege file) has
  root-owned `acceptance_data_protection` and `acceptance_file_storage` volumes; HR.Core cannot persist a key
  ("An error occurred while reading the key ring", not enforced on the lab profile) and its seed then crashes (exit
  139), File.Storage too. PLAN-38 added `acceptance-volume-init` only to the least-privilege file. Worked around for
  this run with a one-time chown 1654 of the two lab volumes; not fixed here (P6: nothing else carried over). The
  prod-like stack enforces the guard, so there the start would stop with the guard's message instead of seeding.
- Summary: acceptance-evidence/summary.md regenerated (scripts/acceptance-evidence-summarize.ps1) with the earlier
  scenario log kept and the 8 AC-OPS-39 lines added (the script drops the appended log and writes a BOM under PS 5.1;
  both corrected).
- Stack left stopped (containers kept, volumes kept).

Next step: P6 — AcOps36VpsBootTests evidence numbers with the invariant culture.

### P6 — carry-over hygiene

Finished (UTC): 2026-11-02T01:24:35Z

- AcOps36VpsBootTests: every number written to the evidence (36e result text, Retry-After, 36g memory result) and the
  assertion messages format with CultureInfo.InvariantCulture (PLAN-38 defect: `22٫9 %` on ar-SA). Runs on the server
  in P7. Nothing else carried over.
- Build 0/0.
- Note on the P5 stamp: written as 01:24:00Z about a minute before that time (the P5 commit 3713f354 carries the real
  time, 01:23Z); stamps are taken from `date -u` at the moment of writing from here on.

Next step: P7 — server: build-on-host once on the final code, init-secrets, verified backup, stop Keycloak and dependents,
ownership transfer, deploy, checks (AC-OPS-39 vps evidence, AcOps36, AcInt37, smoke).
