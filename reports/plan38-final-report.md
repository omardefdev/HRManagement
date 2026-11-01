# PLAN-38 final report — first boot of data, broker and secrets

Plan: [PLAN-38](../docs/acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md) · prompt:
[PLAN-38-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-38-AGENT-PROMPT.md) · checkpoint:
[plan38-progress.md](plan38-progress.md) · baseline: [plan38-baseline.md](plan38-baseline.md) · live broker topology:
[plan38-rabbit-topology.json](plan38-rabbit-topology.json).

## 1. Summary per phase

| Phase | Result |
|---|---|
| P0 | Preconditions met (PR #32 merged, main = origin/main, ssh `hrm` with the recorded host key). Baseline build 0/0. Seed and broker inventories; live topology after the AC-HCM-33 journey: 200 exchanges, 122 queues, 0 unclassified. Server before-picture (read-only). |
| P1 | `hr-migrate` image (`docker/Migrator.Dockerfile`: 15 self-contained linux-x64 EF bundles + psql/curl/jq) and `deploy/bootstrap/bootstrap.sh` run as the one-shot `hrm-bootstrap` before every application (`service_completed_successfully`). Idempotent steps: 16 databases, 30 roles with one password each, migrations as `<svc>_migrator` then TEN-002C, `hrm_backup`, `mq_*` users, result JSON. 12 design-time factories so a bundle never runs a service's `Program`. The GitHub-runner `migrate` job is removed; `hr-migrate` and `hr-rabbitmq` are in the image matrix. |
| P2 | `hr-rabbitmq` image (base and delayed-message plugin 3.13.0 pinned by digest and SHA-256), `hostname: rabbitmq`, node `rabbit@rabbitmq`, volume `rabbitmq_data`. `deploy/bootstrap/rabbitmq-users.json`: 14 users from the live inventory. `mq_*` is the default posture; `docker-compose.posture-mq-admin.yml` only with `HRM_MQ_POSTURE=admin`. |
| P3 | `deploy/vps/init-secrets.sh` / `scripts/vps/init-secrets.ps1` fill missing keys only (names printed, never values; refuse a tracked target). Example parity with every `${VAR:?}`. No placeholder in any `appsettings.Production.json`. The production guard covers connection strings, broker credentials, service secrets, a localhost authority and both postures. Backups connect as `hrm_backup`. |
| P4 | `SeedRunner.RunMandatoryAsync` stops Production (and Acceptance) start-up naming the seed, with the emergency key `Seed:AllowMandatoryFailure` and the metric `seed_mandatory_failures_total`; HR.Contracts reference seed in every profile. Bounded pools on every connection string and Keycloak, `max_connections` 250, a lint rule with a self-test. |
| P5 | Least-privilege acceptance stack `hrm-leastpriv` from empty volumes: AC-OPS-38a..h 8/8 and AC-20 Pass, `RecruitmentToHireJourneyProdLike` Pass. |
| P6 | The two PLAN-37 demo defects: leave and advance approval on PostgreSQL, ID card create (and edit) through a form model; 5 PostgreSQL tests. |
| P7 | Demo server rebuilt from empty volumes on the first-boot path (S-1) after a verified backup; bundle-build defect on the server's SDK found and fixed; PLAN-36/37 server checks re-run. |
| P8 | Docs and gap status lines; one independent review; local CI and pre-push; this report; release branch. |

## 2. Commits

```text
041d88b6 test(ops038): AC-OPS-38a..h re-run 8/8 on a fresh least-privilege stack with the review fixes; acceptance volumes owned before first boot, broker reconnect checked for consuming users
f07f7c96 test(ops038): the pool check test writes its scratch .env with mode 600 (remote-deploy check_env enforces it on Linux)
4abac3bb style(ops038): dotnet format whitespace in the AC-OPS-38 and P6 PostgreSQL tests (local CI formatting check)
b7ae3fe7 docs(ops038): first boot, secrets, rotation, postures and the demo rebuild documented; gap status lines; ObjectStorage-CI exclusion renewed by decision; plan implemented with deviations
fb0954df fix(ops038): review findings: leave claim matches the validated request, broker read held to own names, secrets off jq and k8s migration command lines, pool knobs checked at deploy
cd5ea1a9 test(ops038): demo server rebuilt from empty volumes on the first-boot path, mq_* users and delayed exchange live, PLAN-36 and 37 server checks still green
7bd85bc3 fix(ops038): EF bundles build each startup project with the build servers disabled (SDK 10.0.400 Razor generator corrupted consecutive builds on the server)
2deed162 fix(ops038): leave and advance approval work on PostgreSQL (one concurrency-consistent claim), ID card creation binds a view model
8726d09e test(ops038): AC-OPS-38a..h and AC-20 pass on a least-privilege stack built from empty volumes, evidence
233d11d9 feat(ops038): mandatory seeds stop Production start-up (named, no data printed), contracts reference seed in every profile, bounded connection pools checked by lint
54f7ff8d feat(ops038): secret generation for missing keys only, example parity, no placeholder in production settings, guard covers connection, broker and service secrets, backup role connection
fc4d2b86 feat(ops038): RabbitMQ image with the delayed-message plugin (pinned hash), fixed node and volume, least-privilege service users from a live inventory
cff3abe0 feat(ops038): hr-migrate image and idempotent bootstrap (databases, per-service roles and passwords, migrations by migrator, backup role), applications wait for it
505aeff6 chore(ops038): baseline, live RabbitMQ topology, seed inventory, server before-picture
1d5b0059 docs(ops038): PLAN-38 execution prompt
```

Then the final documentation commit (this report, the progress file, the PR text) and the release merge
`release/plan-38` (`--no-ff` from `main`, which also carries the PLAN-37 merge note `ecca3657`).

## 3. Decisions applied and deviations

- **D-38-1** `HRM_DB_POSTURE=demo` on the demo server (applications connect as the administrator; `least-privilege`
  with demo mode is refused by `remote-deploy.sh` until OPS-D). The least-privilege DB posture is proven on
  `hrm-leastpriv` (AC-OPS-38a..c).
- **D-38-2** `HRM_MQ_POSTURE=least-privilege` by default, also on the demo server; `admin` only adds the posture file.
- **D-38-3** one generated password per `*_app`, `*_migrator` and `hrm_backup` role.
- **D-38-4** delayed-message plugin v3.13.0 in the repository's RabbitMQ image.
- **D-38-5 (deviation: location)** the server's secrets stay in `/opt/hrm/shared/.env` (600, owner `hrm`), where
  PLAN-36 put them and where `deploy.ps1` / `remote-deploy.sh` read them, not `/opt/hrm/.env` owned by root.
- **D-38-6** mandatory seed failure stops Production; `Seed:AllowMandatoryFailure=true` logs `Critical` and continues.
- **D-38-7** MinIO user only with the `minio` profile (bootstrap refuses `HRM_MINIO_ENABLED=true` until a client and an
  image are vetted); the ObjectStorage-CI obligation renewed by decision `PG-OPS38-OBJECTSTORAGE-CI` (new exclusion
  entry, review 2027-01-31; the PLAN-34 entry marked `Renewed`, its date not moved).
- **P-5 (deviation)** the bootstrap always migrates as `<svc>_migrator` and applies TEN-002C in both DB postures; the
  posture changes only the applications' connection.
- **AcOps36 placement (deviation)** AC-OPS-36 was not re-run on the local single-host chain; it ran against the rebuilt
  server in P7. Locally `lint-compose.ps1` (both chains) and `Ops036ComposeLintTests` covered the chain.
- Other deviations: `pg_read_all_data` + `BYPASSRLS` for `hrm_backup` (covers future tables and `keycloak_db`); the bus
  pattern `[^-]+_bus_[^-]+` instead of `.*_bus_.*`; `SeedRunner` treats Acceptance like Production; placeholders removed
  beyond the prompt's list (`YOUR_*` AllowedHosts/CORS, `keycloak.example.com`, `FileStorage:SecretKey` in 5 base
  files); `max_connections` 250 instead of 200; IDCard Edit fixed with Create; the leave cancel handler fixed with
  approve; a second cause of the leave-approval failure (§8); the least-privilege acceptance stack gained an
  `acceptance-volume-init` one-shot and 38f checks reconnection of consuming users only (P8 re-run). All in the plan's
  «ملاحظات التنفيذ والانحرافات».

## 4. RabbitMQ inventory and expressions

- Live topology (P0): 200 exchanges, 122 queues, every name owned by one service or shared
  ([plan38-rabbit-topology.json](plan38-rabbit-topology.json)). Endpoint prefix = entry assembly in kebab case
  (`hr-core-web`); exchanges per message type `HRManagement.MessageContracts.(Events|Commands):*`; faults
  `MassTransit:Fault*`; HR.Contracts also `HR.Contracts.Infrastructure.Sagas:*`; saga timeouts declare
  `<saga queue>_delay` (`x-delayed-message`) under the service prefix.
- 14 users (`mq_<service>`; the gateway has no bus, so no `mq_gateway`), vhost `/`:
  - configure `^(<prefix>-.*|HRManagement\.MessageContracts\.(Events|Commands):.*|MassTransit:Fault.*|[^-]+_bus_[^-]+)$`,
    plus `HR\.Contracts\.Infrastructure\.Sagas:.*` for `mq_hr_contracts`;
  - read = the same expression as configure (review F2: consume, get and purge only on the service's own queues;
    binding from the shared exchanges it declares);
  - write `.*` (publishing to shared exchanges and sending to another service's endpoint; the plan's decision).
  - Residual: `[^-]+_bus_[^-]+` matches every service's bus temporary queue (no request client is in use).
- Proof: `Ops038RabbitPermissionMatrixTests` (every live name admitted for its owner, every other prefix refused for
  configure and read), `Ops038RabbitLeastPrivilegeTests` (403 ACCESS_REFUSED when `mq_payroll` declares or reads an
  `hr-core-web` queue; publish/consume and the delayed schedule work), AC-OPS-38c..f on the stack, and on the server only
  `mq_*` connections (14). The server got the matrix with read `.*` (tag `7bd85bc3ac9d`); the narrowed read reaches it
  with the next deploy (`hrm-bootstrap` sets permissions on every deploy).

## 5. Roles and pool totals

- PostgreSQL: 15 `<svc>_app` (DML only, no DDL, no `BYPASSRLS`) + 15 `<svc>_migrator` (owner of its schema) +
  `hrm_backup` (`pg_read_all_data`, `BYPASSRLS`, CONNECT on the 16 databases, no write), each with its own password
  from `.env`. Matrix: `deploy/bootstrap/postgres-roles.conf`. Keycloak still connects to `keycloak_db` as the
  administrator: its own `keycloak` role is PLAN-39 (out of scope here).
- Pools: `Maximum Pool Size=10` on every connection string (`HRM_PG_POOL_MAX`), HR.Core 20 (`HRM_PG_POOL_MAX_HR_CORE`),
  Keycloak `KC_DB_POOL_MAX_SIZE=20`: 180 + reserve 5 = 185 of `max_connections` 250 (74 %, rule: below 90 %).
  `lint-compose.ps1` fails a connection string without a maximum, an unbounded Keycloak and a total at 90 % or more.

## 6. Results

| Suite | Result |
|---|---|
| Build `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors (every phase) |
| Unit (full) | 4503/4503 after the review fixes (4484 at P3, 4493 at P6) |
| Unit, PLAN-38 filters | Ops038 37/37 (bootstrap matrices, broker matrix incl. read, env parity, guard, review findings, lint self-test) |
| Integration, Ops038 (Testcontainers) | 14/14: bootstrap idempotency 6, broker least privilege 2 (incl. the read refusal), PostgreSQL defects 6 (incl. F1) |
| Integration, full (P5, `RUN_TESTCONTAINERS=true`) | 431 passed, 5 failed: `Qa003MinioFileScanTests` ×3 (no vetted MinIO image, excluded in CI by D-38-7), `Int037ArchiveRetry` (passed alone: load flake), `RabbitDownOutboxTests` (opt-in live test, fails alone too; PLAN-38 does not touch its path) |
| AC-OPS-38a..h + AC-20 on `hrm-leastpriv` from empty volumes | 9/9 Pass (P5; `up --wait` 78 s, all 15 applications ready at 90 s, bootstrap 57 s). **P8 re-run with the review fixes** (narrowed broker read, jq on stdin): AC-OPS-38a..h 8/8 Pass from empty volumes (`up --wait` 46 s, bootstrap 29 s, tests 218 s) after two fixes the re-runs exposed: an `acceptance-volume-init` one-shot (the shared acceptance volumes could be root-owned on a fresh start, File.Storage then failed) and 38f checking reconnection for consuming users only (a publish-only bus reconnects on its next publish) |
| `RecruitmentToHireJourneyProdLike` on the least-privilege stack | Pass (24 s) |
| Server: smoke | 48 pass / 0 fail (fixed `smoke.sh`) |
| Server: AC-OPS-36 (36j not run) | a..i and k Pass (36e on the fifth attempt, after the owner's link recovered; §7) |
| Server: AcInt37 | 3/3 Pass |
| `lint-compose.ps1` | both chains PASS; pool self-test PASS |
| Local CI (`scripts/ci-local.ps1`, full, Linux container, clean clone of HEAD) | 90/90 passed on `f07f7c96` (4736 s, incl. Testcontainers and the coverage gate). Two earlier runs found two defects of this phase, fixed: whitespace in two test files (`dotnet format`), and the pool-check test's scratch `.env` without mode 600 (the check enforces it on Linux) |
| Pre-push (`scripts/plan035-prepush.ps1`) and local CI on the release head | run on `release/plan-38` after this report is committed; results in `reports/plan38-pr.md` and the progress file |

Review: one independent subagent, 0 high, 2 medium, 4 low; five fixed with tests, F5 recorded (§10). No refuter was
needed (no high finding).

## 7. The server

**Before** (P0, read-only; tag `9d457dab36d7`): 21 containers; the applications and Keycloak on the PostgreSQL
administrator; the 30 `*_app`/`*_migrator` roles from the first init with one shared password, no `hrm_backup`; 14
broker connections all as the administrator (no `mq_*` users), an anonymous RabbitMQ volume and a node name that
followed the container id; no delayed-message plugin; migrations run by each application at start-up
(`RunMigrationsAtStartup` in the demo file); `.env` 37 keys (the copy taken before `init-secrets`).

**Rebuild (S-1, 2026-11-01):**

| Step | Result |
|---|---|
| `init-secrets.sh` on the server `.env` | 45 generated secrets (30 role passwords, `POSTGRES_BACKUP_PASSWORD`, 14 `RABBITMQ_<SVC>_PASS`) + 2 posture keys; 84 keys, 600; copy before: `/opt/hrm/shared/.env.bak-plan38-20261101T163137Z` |
| Server build of `7bd85bc3ac9d` | 17 images (incl. `hr-migrate` 3.76 GB, `hr-rabbitmq`) in the loopback registry; upload 465 s, build 640 s, push 15 s (1147 s). The first build (`2deed1621200`) failed in the bundles: SDK 10.0.400's Razor generator with a shared compiler server; fixed by `--disable-build-servers` |
| Broker drained check | 120 queues, 0 messages (17:54:50Z) |
| Backup | `/opt/hrm/backups/pre-plan38-20261101T175541Z` (700, files 600): `pg_dumpall.sql` 25.2 MB (all 16 databases), tars of `file_storage_data` (21 entries), `gateway_dataprotection_keys`, 14 `dp_*`, `seq_data`; `SHA256SUMS`; verified in 47 s |
| Down and volumes removed | `hrm_postgres_data`, `hrm_file_storage_data`, `hrm_gateway_dataprotection_keys`, 14 `hrm_dp_*`, the anonymous RabbitMQ volume; kept Caddy (certificates), Seq, ClamAV and both outboxes |
| Deploy `7bd85bc3ac9d` | 342 s: pull 2 s, `up` 139 s, `hrm-bootstrap` 48 s (exit 0, 264 migrations as `*_migrator`), all healthy 12 s after `up`, Keycloak sync 98 s |
| Downtime | 5 min 53 s (17:56:40Z → 18:02:33Z) |

**After:** 21 running containers healthy + `hrm-bootstrap` exited 0; 14 broker connections, all `mq_*`; plugin
`rabbitmq_delayed_message_exchange 3.13.0` enabled; node `rabbit@rabbitmq` on `hrm_rabbitmq_data`; 30 database roles +
`hrm_backup`; DB posture demo (applications on the administrator, D-38-1); realm imported and synced
(`Server_realm_has_every_matrix_client_and_the_sync_is_idempotent` Pass). Evidence:
`acceptance-evidence/AC-OPS-38/vps/`.

**Server checks** (PLAN-36/37; 36j not run, it redeploys): smoke 48/0 with the fixed `smoke.sh` (the first run failed
only on the unknown one-shot container); AC-OPS-36a, b, c, d, f, g, h, i, k Pass; AcInt37 smoke, realm and legal-pack
fingerprint Pass. AC-OPS-36e failed four times from the owner's machine (60 s client timeouts while the link crawled)
while the server answered every request that reached it within 70 ms and answered the same burst sent from the server
itself in 0.5 s; the fifth attempt, once the link recovered, **passed** (anonymous 297 of 400 in 18 s, the rest 429
with Retry-After 60). Details: `acceptance-evidence/AC-OPS-38/vps/ac-ops-36e-network.md`.

**Server changes and their undo** (also in `docs/OPS_VPS_SINGLE_HOST.md` §10):

| Change | Undo |
|---|---|
| 47 keys added to `/opt/hrm/shared/.env` | restore `/opt/hrm/shared/.env.bak-plan38-20261101T163137Z` |
| Releases `2deed1621200` (failed build) and `7bd85bc3ac9d` in `/opt/hrm/releases`; images in the loopback registry | `rm -rf` the release; registry tag delete |
| Data volumes rebuilt from empty | restore from the backup (§12.5 of the single-host runbook) |
| New volumes `hrm_rabbitmq_data`, `hrm_bootstrap_results` | `docker volume rm` after `down` |
| `/opt/hrm/backups/` (700) | remove once the backup is no longer needed |
| `smoke.sh` of release `7bd85bc3ac9d` replaced by the fixed one (original `smoke.sh.orig-plan38`) | move the original back |
| current-tag `7bd85bc3ac9d`, previous-tag `9d457dab36d7` | `deploy.ps1 -Rollback` only after restoring the backup (the previous tag has no `hrm-bootstrap`) |

## 8. The P6 fixes

- **Leave approval** failed on PostgreSQL for two reasons, both reproduced by the new PostgreSQL test:
  1. after the `ExecuteUpdate` claim the handler saved the tracked entity against the old `xmin`
     (`DbUpdateConcurrencyException`). Fix: `IContractsRepository.ReloadAsync` re-reads it after the claim and after a
     revert; the leave cancel handler had the same pattern and is fixed the same way. The claim stays the only gate: a
     racing second approver gets "not pending" and no second debit.
  2. `ServiceTransaction.MoveTo` appends a `ServiceTransactionStateHistory` row whose Guid key the entity sets itself,
     and EF took it for an existing row reached through the navigation (UPDATE of 0 rows). Fix in the handlers:
     `ServiceTransactionHistoryTracking.TrackNewHistoryAsync` registers the appended rows as new (leave approve,
     cancel, reject; advance approve, reject).
- **Advance approval** has no claim; with fix 2 it succeeds, and a racing second approval gets the `xmin` conflict,
  answered 409 by `ConcurrencyConflictMiddleware` (one installment plan).
- **ID card create** bound the entity (with a required `CardDesign` navigation), so validation never passed. Create and
  Edit now bind `IssuedCardFormModel` (no navigation; employee and design required; the serial is generated); an
  unknown design is a field error, not a foreign-key 500.
- **Review F1** (P8): the claim matched only `Id` and `Pending`, so an edit committed between the handler's read and its
  claim (another employee, type, dates or duration) was taken in by the re-read and approved without validation. The
  claim now also matches the validated employee, type, start, end and duration; a changed request answers
  "changed since read" and stays Pending with no debit.
- Tests: `Ops038DemoDefectTests` 6/6 on PostgreSQL (Testcontainers); the F1 test fails with the fix taken out.
- On the server: NotRun(server). The UI check needs a demo sign-in, and `DEMO_USERS_PASSWORD` is read by the owner
  only (it never enters the agent's context). Suggested owner check on the demo: approve a pending leave, create an ID
  card. Note the deployed tag `7bd85bc3ac9d` carries the P6 fixes but not F1 (next deploy).

## 9. Deferred items

- MinIO user provisioning and the ObjectStorage test category (D-38-7, `PG-OPS38-OBJECTSTORAGE-CI`, review 2027-01-31).
- DB posture `least-privilege` on the single host (OPS-D, enforced RLS).
- Secrets on the server in a plain 600 file; a secret store is wave 2.
- Payroll legal framework stays a Demo seed → PLAN-54.

## 10. Defects for later plans

- `ServiceTransactionStateHistory` key generation: the model-level fix (`ValueGeneratedNever` on the key + a
  snapshot-only migration) is beyond the handlers. Other `MoveTo` call sites are **likely affected, not verified**:
  `HireWizardController`, `ServiceTransactionsController`, `InboundEventCommands`, `SeparationRecordHandlers`,
  `SagaSupportConsumers`.
- MassTransit `ScheduleSend` to a short `queue:<name>` address declares a classic queue and loops on
  PRECONDITION_FAILED against a quorum queue; no production code uses it (sagas schedule to their own input address).
- Review F5: `ProductionConfigurationGuard` checks the postures only when `Deployment:DbPosture` /
  `Deployment:MqPosture` are set (compose sets both; k8s and appsettings do not), and the guard runs as a hosted service,
  after `TryMigrateAsync` and the seeds have already connected. A Production default for an absent posture and an
  earlier guard change other deployment targets (k8s), so they are left to a later plan (OPS-D / PLAN-39).
- Leave approval: claim, debit and `SaveChanges` are not one transaction (a failed `SaveChanges` leaves the request
  Approved and debited); two different advances of one employee can both pass `ValidateNoOpenAdvance` concurrently.
  Both predate PLAN-38 (review notes).
- `AcOps36VpsBootTests` evidence formats numbers with the machine culture (`22٫9 %` on ar-SA); use the invariant culture.

## 11. Timing

- **Wall-clock:** started 2026-11-01T10:24:39Z; the release branch was ready about 11.5 h later (owner action stamped
  in the progress file). Merge time and the post-merge run are recorded in the progress file after the owner's merge.
- **Active vs owner-wait:** owner-wait 0 until the final OWNER ACTION (no decision or approval was needed during the
  run: D-38-1..7 were accepted on 2026-10-31). Not active: the gap between the previous session's end (during the P5
  image builds, after P4 finished at 12:15Z) and the resume at 15:35:44Z, about 3 h 20 min including roughly 30 min of
  image builds before the session ended. Active time is therefore about 8 h.
- **Phases (UTC):** P0 10:24–10:45; P1 –11:52; P2 –11:59; P3 –12:09; P4 –12:15; P5 15:35–16:12 (after the resume);
  P6 –16:29; P7 –18:51 (incl. two server builds and the rebuild); P8 18:51 – release.
- **Builds:** 15 local service images for the least-privilege stack about 26 min (771 s of NuGet timeouts on the first);
  `hr-rabbitmq` 8 s; server build 1 (`2deed1621200`) failed after the upload (bundle defect), server build 2
  (`7bd85bc3ac9d`) 1147 s (upload 465 s, build 640 s, push 15 s).
- **First boot:** `hrm-leastpriv` from empty volumes `up --wait` 78 s, all applications ready 90 s, bootstrap 57 s
  (P5); 46 s / bootstrap 29 s in the P8 re-run. Server: bootstrap 48 s, all healthy 12 s after `up`.
- **Deploy:** 342 s (Keycloak sync 98 s); downtime 5 min 53 s.
- **Local CI:** full runs 504 s (failed formatting), 789 s (failed unit), 4736 s (90/90). Server acceptance runs: first
  1350 s (13 scenarios), re-runs 330 s, 157 s, 124 s, 72 s.
- **Actions minutes:** none used before the push (one push, P-6). The PR's checks and the post-merge run (and whether
  the post-merge dedupe guard skipped the heavy jobs) are recorded in the progress file after the merge.
- `powershell -File scripts/plan-timing.ps1 -From 25 -To 38` (before the merge; PLAN-38's End moves to the merge):

```text
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
----    ------- -------          -----------      ---              ------ ----   ---------
PLAN-25      11                  2026-10-29 20:31 2026-10-29 23:21   True 2:50            
PLAN-26      12                  2026-10-21 13:38 2026-10-21 22:49  False 9:10            
PLAN-27      11                  2026-10-21 22:57 2026-10-22 09:43  False 10:45           
PLAN-28      13                  2026-10-22 10:03 2026-10-22 15:03  False 5:00            
PLAN-29      13                  2026-10-22 15:21 2026-10-22 20:36  False 5:15            
PLAN-30      21                  2026-10-23 00:45 2026-10-29 07:45  False 150:59          
PLAN-31      14                  2026-10-29 10:24 2026-10-29 15:03  False 4:38            
PLAN-32       9                  2026-10-29 14:12 2026-10-29 16:31   True 2:19            
PLAN-33      11                  2026-10-29 17:49 2026-10-29 20:18   True 2:28            
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47  13:58    
PLAN-35      49 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13  11:43    
PLAN-36      26 2026-10-31 16:32 2026-10-31 16:41 2026-11-01 00:13   True 7:31   7:40     
PLAN-37      23 2026-11-01 01:24 2026-11-01 01:30 2026-11-01 12:27   True 10:57  11:03    
PLAN-38      15 2026-11-01 13:24 2026-11-01 13:25 2026-11-02 00:53  False 11:28  11:28    
Span = first commit -> merge (or last commit if not merged). WallClock = "Started (UTC)" in the progress file -> merge.
Note: without a merge commit, a later commit with the same plan scope (for example a live-run fix) extends that plan's End.
```

## 12. Readiness note for PLAN-39

- Keycloak's realm on the server is synced from the matrix after every deploy (`keycloak-sync-clients.sh`) and was
  re-imported once by the rebuild; Keycloak has a bounded pool (20) but still the administrator account: the `keycloak`
  role in `ensure-roles.sql` (+ `POSTGRES_KEYCLOAK_PASSWORD` in `postgres-roles.conf` and `init-secrets`) is PLAN-39's;
  the bootstrap's role step (step 3, run on every deploy) is where it belongs.
- Data Protection volumes start empty on a new server (`hrm_gateway_dataprotection_keys`, 14 `hrm_dp_*`): sessions are
  lost on a rebuild, which PLAN-39 (persisted, protected key rings) addresses.
- `hrm-bootstrap` is the place for any further first-boot step (it runs before every application on every deploy and
  stops the deploy on failure).
