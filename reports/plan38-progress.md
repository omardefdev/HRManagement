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
