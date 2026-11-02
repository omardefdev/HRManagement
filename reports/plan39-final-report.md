# PLAN-39 final report — the production Keycloak realm as code, and Data Protection keys that are never silently lost

Plan: [PLAN-39](../docs/acceptance-plans/PLAN-39-ops-keycloak-dataprotection.md) · prompt:
[PLAN-39-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-39-AGENT-PROMPT.md) · checkpoint:
[plan39-progress.md](plan39-progress.md) · baseline: [plan39-baseline.md](plan39-baseline.md) · acceptance realm before:
[plan39-realm-acceptance-export.json](plan39-realm-acceptance-export.json) (secrets masked by Keycloak).

## 1. Summary per phase

| Phase | Result |
|---|---|
| P0 | PLAN-38 PR #33 already merged by the owner (34 s before the session started; all checks green, post-merge dedupe guard skipped the heavy jobs). main = origin/main. PLAN-38's deferred commit 6287e41a merged first. Baseline: acceptance realm export (no secrets), client/scope and Data Protection inventories, server before-picture with a **read-only canary: ok** (and a negative control with another ring: failed). |
| P1 | `docker/keycloak/realm-manifest.json` (no secret) + `deploy/wiring/service-wiring.json` → `scripts/keycloak/render-realm.ps1` for Production, Demo, Acceptance; `prepare-keycloak-import.ps1` a wrapper (`Local` keeps the developer realm); the dev sync reads roles and user-profile attributes from the manifest; the two legacy realm files marked deprecated. |
| P2 | `keycloak` imports from the volume `keycloak_import`; `deploy/keycloak/reconcile.sh` as the one-shot `keycloak-reconcile` (Keycloak image) on every deploy; the 15 applications wait for it; `keycloak-sync-clients.sh` deleted. Testcontainers Keycloak 24 + PostgreSQL proves import, tokens, idempotence and rotation. |
| P3 | Role `keycloak` (bootstrap step 3), documented object-by-object ownership transfer behind a switch, Keycloak on its own role; directory-sync secret optional unless enabled. |
| P4 | `IHrStartupCheck` hook at the top of `TryMigrateAsync`: configuration guard, `DataProtectionStartupGuard`, HR.Core's `EmployeePiiKeyRingCanary` run before any migration or seed. Optional certificate protection; `make-dp-cert.sh`, `export-dp-keys.sh` (not enabled on the server). |
| P5 | Prod-like stack on a new keycloak_db: **AC-OPS-39a..h 8/8 Pass**; AcHcm25 8/8 (prod-like) and AcHcm30 8/8 (lab profile). |
| P6 | AcOps36VpsBootTests evidence numbers with the invariant culture. |
| P7 | Demo server moved in place (S-1): verified backup, transfer (285 relations), deploy of `817304f5bd88`, reconcile applied the Demo security settings; canary passed on 500 protected national IDs; downtime 3 min 10 s; PLAN-36..39 server checks green. |
| P8 | Docs, one independent review, local CI and pre-push, this report, release branch. |

## 2. Commits

```text
71d45cb6 test(ops039): demo server moved in place (Keycloak on its own role, realm security settings reconciled, key-ring canary green on existing data), PLAN-36..38 server checks still green
817304f5 fix(ops039): server acceptance evidence formats numbers with the invariant culture
3713f354 test(ops039): AC-OPS-39a..h pass on the prod-like stack with a realm rendered from the manifest, AcHcm25/30 still green, evidence
86cee2cb feat(ops039): persistent key rings guarded before the database is touched, PII key-ring canary in HR.Core, optional certificate protection and key export, configuration guard moved earlier
8092417e feat(ops039): Keycloak on its own database role, documented in-place ownership transfer for existing servers, directory-sync secret required only when enabled
320a3619 feat(ops039): automatic realm import and one idempotent reconcile on every deploy (clients, scopes, secrets, URIs, named security fields; never users), applications wait for it
7f8f09b4 feat(ops039): realm manifest plus wiring matrix rendered for Production, Demo and Acceptance with placeholder secrets, parity with the matrix, legacy realm files retired
dd318ddb chore(ops039): baseline, realm export without secrets, client and scope inventory, Data Protection inventory, server before-picture
46363c2a docs(ops038): PR merge note and post-merge run
0cade083 docs(ops039): PLAN-39 agent prompt
509f9584 Merge PLAN-38 deferred commit 6287e41a (P6 checked through the demo server UI)
```

Then:

```text
a9f97a00 style(ops039): dotnet format whitespace in the AC-OPS-39 acceptance tests (local CI formatting check)
6512c459 fix(ops039): review findings: every deploy refills the realm import from its own release and checks the profile against the deployment mode, key ring must be on a volume, unresolved directory-sync secret regenerated, canary probes every tenant
```

the documentation commit with this report, and the release merge `release/plan-39` (`--no-ff` from `main`).

## 3. Decisions applied and deviations

- **D-39-1** two non-overlapping sources merged per profile. Deviation: the scope **audiences stay in the matrix**
  (`scopes.<name>.audiences`, PLAN-37) instead of a second copy in the manifest; the parity test checks every scope has an
  audience and every `ServiceIdentity__ApiAudience` is a target. `svc-file-storage` is not rendered (not in the matrix).
- **D-39-2** first `--import-realm`, then reconcile on every deploy (clients, scopes, secrets, redirect URIs, named
  security fields; never users).
- **D-39-3** a named volume per service; certificate protection optional, off on the demo server.
- **D-39-4** HR.Core refuses to start on key loss; `DataProtection:AllowKeyRingReset=true` → `Critical` + counter.
- **D-39-5** one bootstrap administrator in Production (`UPDATE_PASSWORD`); the demo server uses the Demo profile.
- **D-39-6** `length(12) and notUsername and passwordHistory(3)`; brute force after 5 failures, 15-minute lock.
- **S-1** the demo server moved in place after a verified backup.
- **P-1 (deviation from the plan):** no secret value in any rendered file: `${NAME}` placeholders for every secret in
  Production and Demo, resolved by Keycloak at import; reconcile sets secrets from `.env`. Acceptance keeps lab secrets.
- **P-2:** one reconcile, `deploy/keycloak/reconcile.sh`, replacing `keycloak-sync-clients.sh` (deleted). Deviation in
  mechanism: the Keycloak image has no JSON tool, so the renderer also writes a flat `reconcile-plan.tsv` (names, flags,
  URLs, secret variable names; no value) that reconcile reads, and kcadm's JSON is read line by line (3 kcadm reads +
  login for an unchanged run: 4–16 s).
- **P-3 (deviation from the plan's IHostedService):** the guards are `IHrStartupCheck`s run from the top of
  `TryMigrateAsync` (all 15 hosts call it before migrations and seeds; also with `RunMigrationsAtStartup=false`), with a
  hosted fallback. `ProductionConfigurationGuard` moved there too (PLAN-38 F5, ordering). The "Production default for an
  absent posture" part of F5 is left to OPS-D.
- **P-4 (deviation):** 39a/39e run on the prod-like stack and, for the Production-profile assertions, in
  `Ops039KeycloakRealmImportTests` (Testcontainers Keycloak 24 `start` + PostgreSQL importing the rendered Production
  file); 39e and 39f also on the server.
- **P-5:** Demo carries the same security settings as Production (incl. no gateway password grant); the server tests sign
  in through the browser form — confirmed (smoke login, 36b, 38 P6, 39f).
- **P-6:** PowerShell 5.1 only; new `.ps1` ASCII; the prod-like stack started with the plan's `up -d --wait`.
- **P-7/P-8:** one push in P8; one server build (on the code of P6, tag `817304f5bd88`).
- **P-9:** lint-compose both chains, Ops036, ProductionWiringMatrixTests (+ realm parity), Ops038 env parity green; new
  keys in both examples and init-secrets (`POSTGRES_KEYCLOAK_PASSWORD`, `HRM_BOOTSTRAP_ADMIN_USER`,
  `HRM_BOOTSTRAP_ADMIN_PASSWORD`; the optional `DATAPROTECTION_CERTIFICATE_PASSWORD` is added by `make-dp-cert.sh` only
  when the certificate is made).
- Other deviations (also in the plan's «ملاحظات التنفيذ والانحرافات»): the developer profile renamed `Local`
  (`Demo` = server demo, `VpsDemo` alias); the `keycloak` role in its own SQL file in step 3, active where
  `HRM_KEYCLOAK_DB_USER=keycloak`; the prod-like stack gained a `keycloak-db-role` one-shot (39e); 39g simulated the lost
  `dp_hr_core` volume with an empty scratch ring on a clone of hr_core_db (the acceptance stack shares one key-ring volume);
  no index on `NationalId` (LIMIT 1 stops at the first protected row).

## 4. The realm sources and the profile differences

| | Production | Demo | Acceptance |
|---|---|---|---|
| Users | one bootstrap admin (`HRM_BOOTSTRAP_ADMIN_USER`, temporary `${HRM_BOOTSTRAP_ADMIN_PASSWORD}`, `UPDATE_PASSWORD`, hr-admin) | the 12 demo users, `${DEMO_USERS_PASSWORD}` | the 61 personas (identical to the realm before: ids, roles, attributes), lab passwords |
| Gateway redirect URIs / web origins / post-logout | `https://APP_DOMAIN/...` | `https://APP_DOMAIN/...` | `http://localhost:5000/5200/6200/...`, `+` |
| Gateway password grant | off | off | **on** (acceptance harness) |
| Client secrets | `${NAME}` | `${NAME}` | lab values |
| Everything else (security fields, 28 roles, 6 built-in + 8 S2S scopes, 15 clients, user profile, mappers) | identical | identical | identical |

`Ops039RenderRealmTests` compares the three after removing exactly those fields (with a negative control).

## 5. Unit, integration and acceptance results

| Suite | Result |
|---|---|
| Build `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors (every phase) |
| Unit, full | 4557/4557 (P4) |
| Unit, PLAN-39 filters | Ops039RenderRealmTests 18, Ops039RealmManifestParityTests 8, Ops039DirectorySyncGuardTests 6, DataProtectionStartupGuardTests 18, HrStartupChecksOrderingTests 5; with Ops036/Ops038/ProductionWiringMatrixTests 233/233 after the review fixes |
| Integration, Ops039 (Testcontainers) | 16/16 after the review fixes (125 s): realm import 7 (incl. F4), keycloak role 2 (+ Ops038 bootstrap 5; the hr-migrate image test skipped: needs a local image), PII canary 7 (incl. F5) |
| `lint-compose.ps1` | both chains PASS |
| AC-OPS-39a..h on the prod-like stack | **8/8 Pass** (71 s); again **8/8** (77 s) on images rebuilt with the review fixes |
| Regression | AcHcm25 8/8 (prod-like); AcHcm30 6/8 on the prod-like stack (30i needs Retention:Apply=true, which the prod-like file disables since PLAN-25; 30j's fixtures were consumed) → **8/8 on a fresh lab-profile stack** |
| Server: AC-OPS-39 vps-39e, vps-39f | Pass |
| Server: AcOps36VpsBootTests (36j not run) | 36a..i, 36k Pass (36a after its one-shot list learnt keycloak-reconcile); 36e on the first attempt |
| Server: PLAN-38 UI check, AcInt37 3/3, smoke | Pass; smoke 49/0 (1 known warning) |

**Independent review** (one fresh subagent, read-only, on the diff, the prompt and the plan): 1 high, 2 medium, 3 low.

| # | Finding | Outcome |
|---|---|---|
| F1 high | `fill_keycloak_import` deleted the release's realm copy, so a later rollback to that release kept another release's plan in the volume; reconcile could then die on a variable the older compose does not pass (all applications down) or unlink its scopes | Fixed: the volume is refilled from the deployed release on every deploy (rollback included), the copy is kept (placeholders and names only), a PLAN-39 release without a copy is refused; on the server the copy of `817304f5bd88` was restored from the volume. Confirmed directly in the code (the `rm -rf` was there), so no refuter was spent on it. Test: `Every_deploy_refills_the_keycloak_import_from_its_own_release_and_checks_the_profile` |
| F2 medium | the Data Protection guard accepted a writable path that is not on a volume (a missing mount silently gives a ring in the container layer) | Fixed: in enforced mode the path must not be on the root file system (`/proc/self/mountinfo`; skipped where unreadable; `DataProtection:AllowUnmountedKeyRing=true` for a host install). 7 unit tests; AC-OPS-39 re-run 8/8 in real containers |
| F3 medium | the GitHub deploy workflow always rendered the Demo profile | Fixed: the plan names its profile, `remote-deploy.sh` refuses one that does not match `HRM_DEPLOYMENT_MODE`; the workflow picks the profile from `vars.HRM_DEPLOYMENT_MODE` (default demo). Plans without the line (`817304f5bd88`) are logged, not refused |
| F4 low | with an empty optional variable a first import leaves the directory-sync secret empty or the literal placeholder | Fixed: reconcile has Keycloak generate a secret when it is empty or an unresolved placeholder, then leaves it alone (Testcontainers test) |
| F5 low | the canary probed at most 500 tenants under RLS | Fixed: every tenant is probed (indexed, stops at the first protected value); test with 600 earlier tenants |
| F6 low | `keycloak` can connect to the maintenance database `postgres` (PUBLIC grant) | Recorded (§8), as for every PLAN-38 role |

## 6. The server

**Before** (P0, read-only, tag `7bd85bc3ac9d`): Keycloak and every application on `hrm_admin`; keycloak_db: 92 tables,
193 indexes, 92 row types and the database owned by `hrm_admin`, schema public `pg_database_owner`, no `keycloak` role;
realm `external` + brute force (factor 10, wait 60 s), no password policy, gateway without password grant; 15 key rings
with one key each; 500/500 national IDs protected; read-only canary ok.

**In-place move (2026-11-02):**

| Step | Result |
|---|---|
| Server build `817304f5bd88` | 17 images, 1187 s (upload 16 s, push 12 s) |
| `-PrepareOnly`, `init-secrets.sh` | 2 generated + 1 default key, 87 keys, 600; `.env` copy `/opt/hrm/shared/.env.bak-plan39-20261102T014535Z` |
| **Backup** | `/opt/hrm/backups/pre-plan39-20261102T014546Z` (700/600): pg_dumpall (16 databases), 15 key-ring tars with a key each, env copy, SHA256SUMS — verified, 7 s |
| Stop + transfer | 16 containers stopped at 01:46:13Z; transfer 34 s: owners `hrm_admin` (285 relations + database) → `keycloak` (285 + database) |
| Deploy | 160 s (pull 5, up 107, bootstrap 33, reconcile 16 s / 9 changes, healthy 12); healthy at 01:49:23Z |
| Downtime | **3 min 10 s**; plus 109 s for the required `--force-recreate` check (39f) |

**After:** Keycloak connects as `keycloak` (the only user on keycloak_db), which cannot connect to hr_core_db; realm
`external`, brute force 5 / 900 s / 900 s, password policy set, gateway no password grant, no svc-* browser/password
flow; reconcile second run `0 changes`; canary passed on the existing data and again after the full recreate; 21
containers running.

**Server changes and their undo** (also `docs/OPS_VPS_SINGLE_HOST.md` §10 and §13.5):

| Change | Undo |
|---|---|
| 3 keys in `/opt/hrm/shared/.env` | restore `.env.bak-plan39-20261102T014535Z` |
| role `keycloak`, keycloak_db ownership moved (285 relations + database) | none needed to roll back (the old release uses the superuser); full return: restore the backup (§12.5) + `-Rollback` |
| realm security fields + `fullScopeAllowed=false` on 8 svc-* clients (reconcile) | Keycloak console, or `-Rollback` (does not reset the fields) |
| volume `hrm_keycloak_import` | `docker volume rm` after `down` |
| release `817304f5bd88` (current-tag; previous-tag `7bd85bc3ac9d`), images in the loopback registry | `deploy.ps1 -Rollback` |
| `/opt/hrm/backups/pre-plan39-20261102T014546Z` | remove once no longer needed |

## 7. Deferred items

- Stricter audience checks and the gateway proof → PLAN-42. Provisioning and deprovisioning of accounts → PLAN-62;
  bulk accounts and directory sync → OPS-G.
- Key rings, the certificate and the realm in the scheduled backups, restore drill → OPS-E.
- Enforced RLS and the least-privilege DB posture on the single host → OPS-D; PLAN-38 F5's "Production default for an
  absent posture" → OPS-D.
- k8s: shared key-ring storage between replicas (out of the single-host path).

## 8. Defects for later plans

- Fresh plain acceptance stack (without the least-privilege file): `acceptance_data_protection` and
  `acceptance_file_storage` are created root-owned, so HR.Core cannot persist a key and its seed crashes (exit 139),
  File.Storage too; the least-privilege file has `acceptance-volume-init`, the plain one does not (QA / PLAN-41). Worked
  around once by hand in P5; not fixed here.
- The prod-like file sets `Recruitment:Retention:Apply=false`, so AC-HCM-30i cannot pass there (by design since
  PLAN-25); the acceptance run scope should place 30i on the lab profile (PLAN-41).
- The maintenance database `postgres` keeps PostgreSQL's PUBLIC CONNECT grant: every PLAN-38 role and `keycloak` can
  connect to it (no data there).
- The server realm has no `openid` client scope (the gateway's default list names it; reconcile skips it). Keycloak 24
  does not need it; the rendered realm defines it for new servers.
- Imported users have no account-console role (the account REST API answers 403); if self-service profile pages are
  wanted, a plan must grant `manage-account`/`view-profile` deliberately.
- `scripts/acceptance-evidence-summarize.ps1` drops the appended scenario log of `summary.md` and writes a BOM under
  PowerShell 5.1 (corrected by hand here).
- BuildKit cache on the owner's Docker Desktop could not be pruned with any documented flag (72 GB reported
  reclaimable, 0 B pruned); builds were unaffected.

## 9. Timing

- **Wall-clock:** started 2026-11-01T23:11:21Z; the release branch was ready about 3 h 30 min later (stamped in the
  progress file with the OWNER ACTION). Merge time and the post-merge run are recorded in the progress file after the merge.
- **Active vs owner-wait:** owner-wait 0 until the final OWNER ACTION (no question was needed: D-39-1..6 and S-1 were
  decided). Active time = wall-clock until then.
- **Phases (UTC):** P0 23:11–23:28; P1 –23:41; P2 –00:07; P3 –00:16; P4 –00:29; P5 –01:23; P6 –01:24; P7 –02:06;
  P8 02:06 – release (review fixes, image rebuild and AC-OPS-39 re-run included).
- **Image builds:** local 15 images 525 s and 530 s (one at a time, warm cache); server build of `817304f5bd88` 1187 s
  (upload 16 s, push 12 s).
- **Realm imports:** throwaway Keycloak 24 ready in 15–25 s; Testcontainers Production import + 7 tests 52–125 s; prod-like
  stack import on a new keycloak_db inside `up -d --wait` 17–18 s.
- **Reconcile runs:** unchanged realm 4–7 s; drift repair 11 s; on the prod-like start 32 s (in parallel with 15 starting
  applications); on the server 16 s (9 changes), second run `0 changes`.
- **Stack starts:** prod-like `up -d --wait` 18 s / 17 s, all 15 ready 20 s / 25 s after `up`; fresh lab stack 17 s (+ the
  volume-ownership workaround, §8).
- **Acceptance runs:** AC-OPS-39 71 s and 77 s; AcHcm25/30 283 s (prod-like), AcHcm30 240 s (lab); server: AC-OPS-36 suite
  684 s, AcInt37 + 39e 58 s, 39f 121 s.
- **Server deploy:** 160 s; downtime 3 min 10 s (+109 s for the 39f recreate).
- **Local CI:** run 1 on `71d45cb6` 954 s, 89/90 (formatting only; fixed in a9f97a00, unit 4557, integration 348,
  Testcontainers 109 all passed). The run on the release head and the pre-push check are recorded in the progress file and
  the PR text.
- **Actions minutes:** none used by this plan before the push (one push, P-7). PLAN-38's PR checks (all billable 0, the
  repository is public) and its post-merge run (the dedupe guard skipped CI, CodeQL, E2E and Acceptance) are in
  reports/plan38-progress.md. This plan's PR checks and post-merge run are recorded in the progress file after the merge.
- `powershell -File scripts/plan-timing.ps1 -From 25 -To 39` (before the merge; times in the machine's local time, UTC+3;
  PLAN-38's End moved with its merge-note commit on this branch):

```text
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47  13:58
PLAN-35      49 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13  11:43
PLAN-36      26 2026-10-31 16:32 2026-10-31 16:41 2026-11-01 00:13   True 7:31   7:40
PLAN-37      23 2026-11-01 01:24 2026-11-01 01:30 2026-11-01 12:27   True 10:57  11:03
PLAN-38      18 2026-11-01 13:24 2026-11-01 13:25 2026-11-02 02:13  False 12:47  12:48
PLAN-39       9 2026-11-02 02:11 2026-11-02 02:13 2026-11-02 05:06  False 2:53   2:55
```

(PLAN-25..33 rows unchanged from the PLAN-38 report; the PLAN-39 row was taken before the last three commits.)

## 10. Readiness note for PLAN-40

- The realm is code: a new role, client, scope or user-profile attribute goes into `docker/keycloak/realm-manifest.json`
  (non-service content) or `deploy/wiring/service-wiring.json` (service clients and scopes), never into the Keycloak
  console. `Ops039RenderRealmTests`/`Ops039RealmManifestParityTests` keep the profiles equal and the matrix in step; the
  next deploy reconciles the server. Reconcile never creates users or role mappings: a plan that needs a new persona adds it
  to `personas.json` (Acceptance) and, for the demo, to `demoUsers` of the manifest (first import only).
- Every deploy refills the import volume from its own release and refuses a realm profile that does not match
  `HRM_DEPLOYMENT_MODE`; releases keep their realm copy (rollback-safe).
- Start-up checks go through `AddHrStartupCheck<T>()` (run before migrations and seeds). In containers, key rings must be
  on a volume; HR.Core refuses a ring that cannot read stored national IDs.
- Server: tag `817304f5bd88` (the review fixes reach it with the next deploy); backup
  `/opt/hrm/backups/pre-plan39-20261102T014546Z`; Keycloak on `keycloak`; 87 keys in `.env`.
