Started (UTC): 2026-11-01T10:24:39.9524811Z

# PLAN-38 progress

## P0 — in progress

Preconditions (checked 2026-11-01T10:25Z): PR #32 merged (`d1642d41`), post-merge `ci` 41296399636 success (dedupe
skipped the heavy jobs); `main` = `origin/main` + `ecca3657`; tree clean except `global.json` (owner), `tmp-vendors.html`,
this prompt; no other plan session; `ssh hrm-vps whoami` = `hrm`, ED25519 key `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`.

- Branch `feature/plan-38-ops-first-boot-data-broker-secrets`; prompt committed (`1d5b0059`).
- Baseline: build 0/0 (21 s); unit filters Sec004|Ten002|DB001 22/22; seed inventory and broker inventory (two read-only
  subagents) in `reports/plan38-baseline.md`; live topology on `hrm-acceptance` after the AC-HCM-33 journey (Pass, 20 s):
  200 exchanges, 122 queues, 0 unclassified → `reports/plan38-rabbit-topology.json`.
- Local: `docker builder prune -f` freed 18 GB (P-3); `hrm-acceptance` stack started (it was stopped in PLAN-37).
- Server before-picture (read-only): in the baseline.

Finished (UTC): 2026-11-01T10:45:00Z

## Decisions (notes)

- Gateway has no bus: `mq_gateway` gets no broker user (the production file's `RabbitMQ__Username: mq_gateway` is
  unused and is removed rather than given a password).
- Broker `configure` per service: `^(<prefix>-.*|HRManagement\.MessageContracts\.(Events|Commands):.*|MassTransit:Fault.*|.*_bus_.*)$`
  (+ `HR\.Contracts\.Infrastructure\.Sagas:.*` for HR.Contracts), to be proven on Testcontainers with the new image (P2).

## P1 — in progress (notes)

- P-4 plugin: URL `https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v3.13.0/rabbitmq_delayed_message_exchange-3.13.0.ez`,
  downloaded once 2026-11-01T11:12Z (45 228 bytes), **SHA-256 `3479180df03fa830fd59ccf638a60eee8f0e8261524a8616685522d7b8d9a9ee`**
  (computed with `sha256sum` on that download). Base `rabbitmq:3.13-management-alpine@sha256:606d8c0d6b3c18d1da9afc53bc7cdb2a8d5486df91b5a9830e9e07626c9ae281`
  (RabbitMQ 3.13.7, image created 2025-12-02).
- Bundles: the plan's host-builder risk is real — an efbundle without a factory ran HR.Health's Program and stopped on
  `Integration:HrCoreBaseUrl is required`. Fixed with an `IDesignTimeDbContextFactory` for the 12 contexts that had none
  (3 existed). Self-contained linux-x64 bundle: 157 MB (framework-dependent 57 MB); kept self-contained as the prompt says.
- `dotnet ef migrations bundle` does not restore: the script now restores each startup project for the RID first.
- Bundles get no password on the command line: `--connection` without Password, Npgsql reads `PGPASSWORD` (P-1).
- `hrm-bootstrap` image is set only in the production file (`${HRM_IMAGE_REPO:-hrmanagement}/hr-migrate:${IMAGE_TAG:-local}`,
  no build section): the image overlay comes before the production file in the chain, so a value there would be overridden.
- Backup role: `GRANT pg_read_all_data` (all tables/sequences/schemas of every database, present and future) instead of
  per-database `SELECT` + default privileges per migrator; also covers `keycloak_db` (administrator-owned tables).
- DB posture switch (D-38-1): `HRM_DB_POSTURE` (default `demo`). In demo mode the vps-demo file keeps the administrator
  connections; `least-privilege` with demo mode is refused by remote-deploy (OPS-D), and is proven on the
  `hrm-leastpriv` acceptance stack.

## P1 — done

- `hr-migrate` (docker/Migrator.Dockerfile + .dockerignore): SDK stage runs `build-ef-bundles.sh --self-contained -r linux-x64`
  (15 bundles, 2.3 GB), runtime `runtime-deps:10.0` (Ubuntu 24.04) + postgresql-client 16.15, curl, jq; image 3.77 GB.
  Bootstrap at /app/bootstrap, bundles at /app/efbundles, run-migrations.sh + sql at /app/scripts (k8s Job layout).
  The bundle stage copies `--exclude=deploy --exclude=scripts`, so editing the bootstrap rebuilds only the last layers.
- Image builds (local, one at a time, P-3): 255 s (failed: `dotnet ef` does not restore), 1356 s (first full, cold
  NuGet cache), 359 s (after the COPY change, warm cache).
- `deploy/bootstrap/`: bootstrap.sh (steps 0-8), ensure-databases.sql, ensure-roles.sql, ensure-backup-role.sql,
  postgres-roles.conf. `docker/init-service-roles.sh` is a wrapper over the same SQL (dev path);
  `docker/init-databases.sql` removed (the postgres container mounts ensure-databases.sql as 01-init).
- Compose: `hrm-bootstrap` in docker-compose.production.yml (one-shot, read-only, cap_drop ALL, user 1654, data network,
  result volume `bootstrap_results`); the 15 applications wait for `service_completed_successfully`; one password per
  role (`x-db-role-passwords`, `:?` without fallback) passed to postgresql and hrm-bootstrap; connection strings
  `Password=${POSTGRES_<X>_APP_PASSWORD:?}`; vps-demo no longer sets RunMigrationsAtStartup.
- remote-deploy.sh: accepts the exited-0 hrm-bootstrap, prints its log/result and `timing bootstrap_seconds`, fails
  cleanly (failed-tag) when `up` fails on the bootstrap, refuses HRM_DB_POSTURE=least-privilege in demo mode.
- deploy.yml: the runner `migrate` job and the `run_migrations` input removed; hr-migrate and hr-rabbitmq added to the
  build matrix (`matrix.context`).
- Tests: `Ops038BootstrapIdempotencyTests` 6/6 (Testcontainers): SQL path (first run, second run 0 applied + pg_class
  and role counts unchanged, rotation + old password 28P01, hr_core_app DDL 42501 + cross-db 42501, hrm_backup reads /
  write 42501) and the real image twice on empty PostgreSQL + RabbitMQ (47 s; second run 0 applied in 15 databases,
  no generated secret in the logs). Unit regression filters 155/155; lint-compose both chains PASS; build 0/0.

Finished (UTC): 2026-11-01T11:52:13Z

## P2 — done

- `docker/rabbitmq/Dockerfile` (hr-rabbitmq): base pinned by digest, plugin 3.13.0 downloaded and checked against the
  SHA-256 recorded in P1 notes (`sha256sum -c`), enabled offline. Local build 8 s.
- docker-compose.yml `rabbitmq`: built from docker/rabbitmq (`hr-rabbitmq:local`), `hostname: rabbitmq`,
  `RABBITMQ_NODENAME=rabbit@rabbitmq`, volume `rabbitmq_data:/var/lib/rabbitmq`; images overlay pulls
  `${HRM_IMAGE_REPO}/hr-rabbitmq:${IMAGE_TAG}`.
- `deploy/bootstrap/rabbitmq-users.json`: 14 users (the gateway has no bus), configure =
  `^(<prefix>-.*|HRManagement\.MessageContracts\.(Events|Commands):.*|MassTransit:Fault.*|[^-]+_bus_[^-]+)$`
  (+ `HR\.Contracts\.Infrastructure\.Sagas:.*` for HR.Contracts); write/read `.*`. Decision: the bus pattern is
  `[^-]+_bus_[^-]+` instead of `.*_bus_.*` (MassTransit's `host_process_bus_id` never has a hyphen; every service
  prefix has one, so the pattern can never admit another service's queue). The delayed exchange is `<queue>_delay`
  under the prefix (no extra pattern needed). Bootstrap step 6 creates the users (already in P1's script).
- `scripts/init-rabbitmq-service-users.sh` and `docker/rabbitmq/definitions-service-users.json` removed (wired nowhere,
  and their expressions refused every `HRManagement.MessageContracts.*` declare).
- Postures: production file without admin defaults (`Deployment__MqPosture: least-privilege`, one `RABBITMQ_<SVC>_PASS:?`
  per service, no `mq_gateway`); vps-demo drops the broker override; `docker-compose.posture-mq-admin.yml` (14 services,
  `Deployment__MqPosture: admin`) added by remote-deploy only with `HRM_MQ_POSTURE=admin`.
- build-on-host.sh (bake batch 99) and build-push-images.ps1 build hr-migrate and hr-rabbitmq; deploy bundle carries the
  posture file and rabbitmq-users.json.
- Finding: MassTransit's `ScheduleSend` to a short `queue:<name>` address declares a classic queue and loops on
  PRECONDITION_FAILED against the quorum receive queue; the sagas schedule to their own input address (works, tested).
  No production code uses the short form.
- Tests: `Ops038RabbitPermissionMatrixTests` 5/5 (14 bus services = 14 users, own/shared names admitted, every other
  prefix refused, all 200+122 live names admitted for their owner, production chain users/passwords);
  `Ops038RabbitLeastPrivilegeTests` 2/2 on the image built from the Dockerfile (mq_payroll publish consumed by mq_hr_core,
  `hr-core-web-x` declare by mq_payroll = 403 ACCESS_REFUSED, own prefix allowed, saga-style schedule 10 s arrives
  after >= 9 s). Unit 138/138 (Ops036, Ops038, wiring, BLD); lint both chains PASS; build 0/0.

Finished (UTC): 2026-11-01T11:59:12Z

## P3 — done

- `deploy/vps/init-secrets.sh` (server) and `scripts/vps/init-secrets.ps1` (lab, PS 5.1, ASCII): required keys =
  `${VAR:?}` of the single-host chain + .env.vps.example + the role and broker matrices; missing/empty secrets generated
  (32 random bytes, base64url: `openssl rand` / `RandomNumberGenerator`), non-secrets from the example's default or
  `--set`/`-Set` (refused for secret names), allowed-empty keys added empty; never overwrites; names only; mode 600;
  refuses a target inside the git tree unless ignored. Tried on scratch files: 66 secrets + 10 defaults + 3 `--set`,
  second run byte-identical, `--check` 0, `--set POSTGRES_PASSWORD` refused, a tracked target refused; the PS version
  gives the same 82 keys. `.env.leastpriv` added to .gitignore. The deploy bundle carries init-secrets.sh.
- Examples: `.env.example` + the 6 the plan named (4 SVC_*, GRAFANA_*) and GATEWAY_CALLER_PROOF_SECRET, APP_DOMAIN,
  SSO_DOMAIN, DEMO_USERS_PASSWORD, HRM_IMAGE_REPO, IMAGE_TAG; both examples have the 30 role passwords, POSTGRES_BACKUP_PASSWORD,
  14 RABBITMQ_<SVC>_PASS and the two posture keys (`HRM_MQ_POSTURE=least-privilege`, `HRM_DB_POSTURE=demo`).
- Placeholders removed from the 14 appsettings.Production.json files that had them (gateway + 13 modules; File.Storage had
  none): GatewayCallerProof:Secret (Enforce stays), gateway Keycloak:ClientSecret, ConnectionStrings:GatewayAuditDb,
  Cors YOUR_PORTAL_HOST, and (beyond the prompt's list, for the DoD "no placeholder") the YOUR_* AllowedHosts and the
  YOUR_/keycloak.example.com Authority (compose sets both; a localhost authority is now refused in Production).
  FileStorage:SecretKey USE_ENV_OR_USER_SECRETS -> "" in 5 base appsettings.json (PLAN-36 defect 3).
- Guard: every ConnectionStrings:* real and present; RabbitMQ:Username/Password when the service has a RabbitMQ section;
  ServiceIdentity:ClientSecret when a ClientId is set; Keycloak:Authority not localhost in Production;
  Deployment:MqPosture=least-privilege refuses a non-mq_* broker user; Deployment:DbPosture=least-privilege refuses a
  non-*_app database user, =demo only with Deployment:Mode=demo; HR.Core adds Recruitment:Portal:OtpHmacKey. The
  production file sets Deployment__DbPosture=least-privilege and Deployment__MqPosture=least-privilege; vps-demo sets
  Deployment__DbPosture=demo; the admin posture file sets Deployment__MqPosture=admin.
- postgres-backup: BACKUP_PGUSER=hrm_backup, BACKUP_PGPASSWORD=${POSTGRES_BACKUP_PASSWORD:?}; backup-postgres.sh has no
  administrator fallback.
- Tests: Ops038EnvParityTests 7, Ops038ProductionConfigurationGuardTests 15 (incl. AC-OPS-38g negatives in unit form),
  Sec004 + "no shared or administrator fallback"; filters 156/156; full unit suite 4484/4484; lint both chains PASS;
  int037-wiring-check PASS (production, single-host, prodlike, parity, 3 k8s overlays); build 0/0.

Finished (UTC): 2026-11-01T12:09:51Z

## P4 — done

- `SeedRunner` (ServiceDefaults/Seeding): `RunMandatoryAsync` stops a Production start (`MandatorySeedFailedException`
  naming service and seed, cause = exception type + SQLSTATE, no message, no inner exception), emergency switch
  `Seed:AllowMandatoryFailure=true` = Critical + `seed_mandatory_failures_total`; Development warns; the Acceptance
  profile rethrows the original. `RunOptionalAsync` warns (Acceptance rethrows).
- Applied: HR.Core national seed mandatory (demo, acceptance, org-unit paths optional); HR.Contracts `ContractsDataSeeder`
  mandatory in every profile but Acceptance (left the `None` condition), demo optional; HR.Training both optional.
  Payroll legal framework stays Demo-only → PLAN-54 (recorded in docs/OPS_SEED_INVENTORY.md). The other services seed
  demo/acceptance data only and keep their pattern (documented).
- Pools: `Maximum Pool Size=${HRM_PG_POOL_MAX:-10}` on the 15 connection strings (HR.Core `${HRM_PG_POOL_MAX_HR_CORE:-20}`)
  in the production file and the demo file; `KC_DB_POOL_MAX_SIZE: "20"`. **Decision/deviation:** `max_connections`
  200 -> 250 (vps.yml) and 250 in the production file's postgresql (was the default 100): the plan's numbers give 180 + a
  reserve of 5 = 185, which is >= 90 % of 200; with 250 it is 74 %. lint-compose.ps1 adds the pool rule (every
  connection string bounded, Keycloak bounded, total + reserve < 90 % of max_connections) and `-SelfTest` (bounded
  passes; over 90 %, no maximum, unbounded Keycloak fail).
- docs/OPS_SEED_INVENTORY.md written.
- Tests: SeedRunnerTests 8/8 (incl. lint self-test negatives); filters 109/109; lint both chains PASS; build 0/0.

Finished (UTC): 2026-11-01T12:15:12Z

## P5 — in progress (notes)

- Verification filters: unit Ops038|SeedRunner|ProductionConfigurationGuard|Sec004 73/73; integration
  Category=Testcontainers&Ops038 8/8 (with HRM_MIGRATE_IMAGE=hr-migrate:local, HRM_RABBITMQ_IMAGE=hr-rabbitmq:local).
- Full integration suite (RUN_TESTCONTAINERS=true): 431 passed, 5 failed, 4 skipped. Qa003MinioFileScanTests x3: no MinIO
  image (excluded in CI, D-38-7). Int037ArchiveRetry: passed when re-run alone (load flake). RabbitDownOutboxTests: fails
  alone too; an opt-in live test (PLAN-34 listed it as opt-in), PLAN-38 does not touch its path → to compare with main in
  P8 local CI.
- Decision: SeedRunner treats the Acceptance environment like Production for mandatory seeds (the plan: "in Acceptance the
  current behaviour (throw)"); otherwise the least-privilege stack (Acceptance env, profile None) would only warn.
- docker-compose.acceptance-leastpriv.yml: hrm-bootstrap (hr-migrate:local), *_app connections, mq_* users,
  Seed__Profile=None, migrations off, Deployment__EnforceConfigGuard=true with both postures least-privilege. Same ports as
  hrm-acceptance, so hrm-acceptance is stopped (not removed) during the run.
- .env.leastpriv (git-ignored): the developer .env + ACCEPTANCE_GATEWAY_CALLER_PROOF_SECRET from .acceptance/credentials.json
  (no value printed), then `init-secrets.ps1 -EnvFile .env.leastpriv -Example '' -ComposeFile <7 files>`: 45 generated.
  init-secrets.ps1: -Example '' allowed, -Set/-ComposeFile comma lists split (powershell -File passes one string).
- AC-20 path under PS 5.1 (P-2): acceptance-verify-db-isolation.ps1 #Requires 5.1; RunScriptAsync uses pwsh if on PATH,
  else powershell.
- deploy.ps1 -PrepareOnly (copy + extract the release only) for P7's init-secrets step.
- Images: `docker builder prune` (32 GB, BuildKit GC risk) then 15 images one at a time with the local .NET bases
  (artifacts/plan38/build-leastpriv.log). NuGet timeouts on the first image: hr-gateway 771 s.

Resumed (UTC): 2026-11-01T15:35:44Z — the previous session ended during the P5 image builds (11/15 built: gateway, hr-core, hr-contracts, hr-health, hr-movements, hr-training, payroll, idcard, records-archive, admin-logistics, it-infra); resuming with the remaining four.
- Remaining 4 images after the resume: Docker Desktop had stopped with the machine; restarted, then 111/76/71/99 s
  (13 images after the first: 26-111 s each; total for the 15 about 26 min incl. 771 s of NuGet timeouts).
- First run of the stack: HR.Core crash-looped on the guard (`Recruitment:Portal:OtpHmacKey is not set`, enforced here)
  — the lab stack had no OTP key. Fixed in the leastpriv file (`${HR_CORE_OTP_HMAC_KEY:?}`, generated by init-secrets).
  Finding: `up --wait` reported HR.Core healthy during the crash loop, so the boot time is measured to all 15
  `/health/ready` = 200.
- **First boot from empty volumes (down -v, then up): `up --wait` 78 s, all 15 ready 90 s; hrm-bootstrap 57 s
  (264 migrations as *_migrator, 30 roles, hrm_backup, 14 mq_* users).**
- AcOps38FirstBootTests: first run 5/8; fixed in the tests: (1) role checks connected to 127.0.0.1 inside the postgres
  container, which pg_hba trusts — every password was accepted (now `-h postgresql`); (2) 38g needs IMAGE_TAG (a deploy
  exports it); (3) 38f expected 14 broker connections, File.Storage (no consumer) holds none — now the set of users before
  the recreate must reconnect. AC-20 needed the lab Keycloak URL/secret and ACCEPTANCE_EVIDENCE_DIR = acceptance-evidence.
- **Final run (RUN_ACCEPTANCE=true, ACCEPTANCE_REQUIRE_ALL=true, 2026-11-01T16:1xZ): AC-OPS-38a..h 8/8 Pass + AC-20 Pass,
  239 s.** Evidence acceptance-evidence/AC-OPS-38 (db-checks-38a..h), AC-20 (225 role/db pairs, 0 violations,
  hr_core_app -> payroll_db refused); no secret value of .env.leastpriv in the evidence (scanned).
- hrm-leastpriv project and volumes removed (`down -v`, 0 volumes left).
- Prod-like smoke with the shared changes: hrm-acceptance (prod-like chain, existing images) up --wait 20 s, broker
  hr-rabbitmq:local host=rabbitmq on hrm-acceptance_rabbitmq_data, delayed plugin [E*];
  RecruitmentToHireJourneyProdLike Pass (24 s). AC-HCM-33 evidence restored; hrm-acceptance stopped again.
- Deviation (prompt step 7): AcOps36 not re-run locally; it runs against the server in P7. Locally lint-compose (both
  chains PASS) and Ops036ComposeLintTests (in the 165/165 unit filter) cover the chain.

## P5 — done

Finished (UTC): 2026-11-01T16:12:50Z

## P6 — done

- Leave approval on PostgreSQL had **two** causes, both shown by the new PostgreSQL test:
  1. the `xmin` one PLAN-37 diagnosed: after the `ExecuteUpdate` claim the handler set Status/ApprovedBy on the tracked
     entity and saved it against the old `xmin`. Fix: `IContractsRepository.ReloadAsync` re-reads the entity after the
     claim (and after a revert). The leave **cancel** handler had the same pattern and is fixed the same way.
  2. `ServiceTransaction.MoveTo` appends a `ServiceTransactionStateHistory` row whose Guid key the entity sets itself;
     EF took it for an existing row reached through the navigation (`ServiceTransactionStateHistory/Modified`, UPDATE of
     0 rows). Fix inside the handlers: `TrackNewHistoryAsync` registers the appended rows as new (leave approve, cancel,
     reject; advance approve, reject). The model-level fix (`ValueGeneratedNever` on the key + a snapshot-only migration)
     is beyond the handler (the prompt's stop rule) → recorded for a later plan, with the other `MoveTo` call sites
     (HireWizardController, ServiceTransactionsController, InboundEventCommands, SeparationRecordHandlers,
     SagaSupportConsumers) as **likely affected, not verified**.
- Advance approval has no separate claim; with fix 2 it succeeds; a racing second approval gets the `xmin`
  DbUpdateConcurrencyException, which ConcurrencyConflictMiddleware answers with 409 (no second installment plan).
- IDCard: `IssuedCardFormModel` (no navigation, `Guid?` + [Required] for employee and design, serial shown only — it is
  generated, COD-001) for Create and Edit (Edit bound the same entity and could not succeed either); unknown design →
  field error instead of a foreign-key 500. Views updated.
- Tests: `Ops038DemoDefectTests` 5/5 (Testcontainers PostgreSQL): leave approve + racing loser "not pending" with one
  debit; leave cancel after approval; advance approve + racing loser conflict, one installment plan; IDCard create with
  MVC's own validator (the entity fails on CardDesign, the form model passes, card saved with a generated serial);
  negatives (no employee, unknown design → view, nothing saved). OutboxAtomicity 5/5. Unit 4493/4493. Build 0/0.
  IntegrationTests now references IDCard.Web (lock file updated).

Finished (UTC): 2026-11-01T16:29:36Z

## P7 — in progress (server changes as made, with undo)

| Change | Undo |
|---|---|
| Release 2deed1621200 copied/extracted to /opt/hrm/releases/2deed1621200 (`deploy.ps1 -PrepareOnly`), not deployed yet | `rm -rf /opt/hrm/releases/2deed1621200` |
| `/opt/hrm/shared/.env`: 45 generated keys (30 role passwords, POSTGRES_BACKUP_PASSWORD, 14 RABBITMQ_<SVC>_PASS) + HRM_DB_POSTURE=demo, HRM_MQ_POSTURE=least-privilege (init-secrets.sh; 84 keys, mode 600). Copy before: `/opt/hrm/shared/.env.bak-plan38-20261101T163137Z` (600) | `cp -p /opt/hrm/shared/.env.bak-plan38-20261101T163137Z /opt/hrm/shared/.env` |
- Server build 1 (tag 2deed1621200, build-on-host): upload 287 s; the ssh session was reset after 1562 s and the hr-migrate
  bake had failed at the hr-contracts bundle ("Build failed"); the 16 other images were built. Rerun detached (nohup):
  same failure. Root cause found by reproducing on the server (SDK 10.0.400 container, archive of the tag, `--verbose`):
  with the shared Roslyn compiler server alive across the 15 consecutive builds, the Razor source generator produced
  corrupted documents for the next web project (CS0101/CS8802 on HR.Contracts.Web, CS1056 on HRManagement.Web.Shared;
  non-deterministic). Local image builds passed because the owner's local global.json lets SDK 10.0.403 roll forward.
  A plain build without the RID failed too, so the RID was not the cause. Fix: build-ef-bundles.sh builds each startup
  project with `dotnet build --disable-build-servers`, then `dotnet ef migrations bundle --no-build`. Verified on the
  server: 15/15 bundles (repro6). Repro directories removed.
