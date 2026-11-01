# Task: PLAN-38 — A new server builds itself from empty volumes with no manual step

The deliverables:
- **The `hrm-bootstrap` init container:** one run creates the databases, the per-service roles with their own passwords, the migrations through `*_migrator`, and least-privilege RabbitMQ users, then exits or fails the deploy.
- **A RabbitMQ image** with the delayed-message plugin, on a fixed node with a volume.
- **Generated secrets** that match every `${VAR:?}`, and no placeholder value left in production settings.
- **Mandatory seeds** that stop start-up when they fail.
- **Pooled connections.**
- **A least-privilege acceptance stack.**
- **The demo server rebuilt by the same path.**
- **Two demo-breaking defects from PLAN-37 fixed.**

It runs in one session; the owner decided S-1 in advance.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

**Owner actions (O)** are few: merging the PR, and anything only the owner can do. There is **no owner question** in this plan: S-1 is decided (see «Decisions»). At each owner action:
1. Write `OWNER ACTION:` in the progress file, with the exact commands or the question, and stamp `Waiting for owner (UTC): <timestamp>`.
2. Commit what is complete.
3. End the turn with a short numbered list.

When the owner replies (for example «تم»), stamp `Owner action done (UTC): <timestamp>`, verify the result yourself, and continue. If the owner replies «نفذها انت», run those commands yourself, except anything this prompt reserves for the owner.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan38-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan38-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Check the server's state before any server step.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan38-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Every owner action records the `Waiting for owner` / `Owner action done` pair.
- Record the duration of image builds, bootstrap runs, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run (with its billable minutes).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**, for broad read-only searches (the seed inventory and the RabbitMQ name inventory in P0 are good uses) or for root-causing separate failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (compose `config` dumps, logs, `rabbitmqctl` listings, test runs) to files under `artifacts/plan38/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior platform engineer:
- PostgreSQL roles, grants and RLS;
- EF Core migration bundles;
- RabbitMQ permissions and plugins, and MassTransit topology;
- Docker Compose init containers;
- ASP.NET Core hosting;
- PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH). You also operate the demo server through `ssh hrm-vps`, as PLAN-36 and PLAN-37 did.

**Where things stand:**
- **PLAN-36** put the production profile on one server in demo mode: the applications use the PostgreSQL and RabbitMQ administrator accounts, migrations run at application start-up, and `.env` was filled by hand.
- **PLAN-37** wired every service from one matrix. The demo server runs `9d457dab36d7`, with all cross-service checks green.
- **This plan** makes first boot automatic and least-privilege-ready. After it, the broker runs with `mq_*` users by default. The database stays in demo posture until wave 2 (OPS-D), and the least-privilege posture is proven behind a switch (D-38-1).

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. `reports/plan37-final-report.md`:
   - §3 deviations;
   - §9 CI minutes (the post-merge skip works);
   - §11 defects 1 and 2, fixed in P6 here;
   - §13 readiness note for PLAN-38 (new `.env` keys go into `deploy/vps/.env.vps.example`; the `SVC_<NAME>_CLIENT_SECRET` names stay).
3. `reports/plan36-final-report.md` §5 (server changes) and §11 (images are built **on the server**).
4. `docs/OPS_VPS_SINGLE_HOST.md`:
   - build-on-host, deploy, rollback, smoke;
   - `keycloak-sync-clients.sh`;
   - `.env` at `/opt/hrm/shared/.env`;
   - the server change table.
5. `docs/OWNER_DECISIONS_REGISTER.md`: D-38-1..7 are accepted (see «Decisions»).
6. `deploy/wiring/service-wiring.json` and `docs/SERVICE_WIRING_MATRIX.md`. **The PLAN-37 contract:** any plan that adds an S2S client, call or scope, changes a server policy for a called endpoint, or removes a registration updates the matrix in the same commit.
7. The files the plan cites:
   - `docker-compose*.yml`, `docker/init-databases.sql`, `docker/init-service-roles.sh`;
   - `scripts/run-migrations.sh`, `scripts/build-ef-bundles.sh`;
   - `scripts/init-rabbitmq-service-users.sh`, `scripts/sql/*`, `scripts/backup-postgres.sh`;
   - `MassTransitExtensions.cs`, `ProductionConfigurationGuard*`;
   - the `Program.cs` files with seed calls;
   - `deploy/k8s/base/migrate-job.yaml`, `.github/workflows/deploy.yml`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **PLAN-37 is merged:** PR #32, merge `d1642d41`. The post-merge run is green or skipped by the dedupe guard.
2. **`main` = `origin/main` plus at most the PLAN-37 merge-notes commit `ecca3657`.** That commit rides in this plan's PR, as PLAN-36 carried earlier local commits.
3. **The working tree is clean** except for these, which may be present:
   - the owner's local `global.json` edit;
   - the untracked `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - this prompt file.
4. **No other plan session** is running on this working tree.
5. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The server's ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.

## Decisions

**Already taken.** The owner accepted every recommended default on 2026-10-31. For this plan that means D-38-1..7 as the plan's table states:
- **D-38-1:** DB posture `demo` on the server until OPS-D; `least-privilege` proven behind `HRM_DB_POSTURE`.
- **D-38-2:** MQ posture `least-privilege` by default; `admin` only to fall back.
- **D-38-3:** one password per `*_app`, `*_migrator` and `hrm_backup` role.
- **D-38-4:** `rabbitmq_delayed_message_exchange` 3.13.0 in a repository-built image.
- **D-38-5:** secrets in a 600 `.env` on the server, with no secret manager.
- **D-38-6:** a failed mandatory seed stops Production start-up, with the `Seed:AllowMandatoryFailure` emergency switch.
- **D-38-7:** MinIO user only with the `minio` profile. The ObjectStorage-CI obligation is renewed by a decision in `decision-log.json`, because no `MINIO_IMAGE` is set.

Do not ask the owner about these again.

**Deviation already known (D-38-5).** The server's `.env` is `/opt/hrm/shared/.env`, mode 600, owned by `hrm` (PLAN-36 P-11), not `/opt/hrm/.env` owned by root. `hrm` runs every deploy without `sudo`. Keep PLAN-36's location and record the deviation.

**S-1 — decided by the owner on 2026-11-01: (a) rebuild the demo server from empty volumes.** Do not ask again.

1. **Back up first.** Take a full backup on the server: `pg_dumpall`, plus a tar of the file-storage, Data Protection and Seq volumes, under `/opt/hrm/backups/pre-plan38-<utc>/` (mode 600, owner `hrm`). Verify it (see Stop).
2. **Remove only these volumes:** the database, broker, file-storage and Data Protection volumes. Keep `caddy_data` and `caddy_config` (so the certificates are not re-issued) and `seq_data`.
3. **Deploy.** `hrm-bootstrap` builds everything; the demo seed refills synthetic data. Keycloak re-imports the `VpsDemo` realm with every matrix client, and `keycloak-sync-clients.sh` sets their secrets. The demo password stays the same.
4. **What this gives:** it proves first boot on the real server, and every table is then owned by `*_migrator`, so moving to least-privilege later needs no ownership surgery. Existing demo sessions and demo records are replaced by fresh synthetic ones.
5. **Undo:** restore the backup and roll back to `9d457dab36d7`.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Secrets never enter your context, the repository, the evidence or the transcript** (as in PLAN-36 and 37).<br>**On the server:** `deploy/vps/init-secrets.sh` fills only the missing keys of `/opt/hrm/shared/.env` with `openssl rand`, prints key names only, never overwrites a value, and keeps mode 600.<br>**Locally:** `scripts/vps/init-secrets.ps1` does the same for lab files such as `.env.leastpriv`. It refuses any target inside the git tree that is not git-ignored, and `.env.leastpriv` must be git-ignored (add it if needed).<br>`psql` gets passwords through `PGPASSWORD` per command, and RabbitMQ through the HTTP API body. Neither a command line nor a log carries a secret. |
| P-2 | **PowerShell 5.1.** Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block. Start the least-privilege stack with the plan's `docker compose … up -d --wait` command directly, not through `acceptance-up.ps1`, whose build path needs PowerShell 7 (PLAN-37 defect 5). |
| P-3 | **Local image builds.** BuildKit's garbage collection in Docker Desktop has dropped packages mid-build (PLAN-37 defect 4). Build one image at a time, and run `docker builder prune` when the cache is large. Record the build times. |
| P-4 | **The RabbitMQ plugin.** Download `rabbitmq_delayed_message_exchange-3.13.0.ez` from the plugin's official GitHub release. Compute its SHA-256 **once** from that download, record the URL and the hash in the progress file, and pin both in `docker/rabbitmq/Dockerfile`, which verifies the hash. Never write a hash you did not compute. Pin the base image `rabbitmq:3.13-management-alpine` by digest. The image is built locally for acceptance and **on the server** by `build-on-host` (P-7). |
| P-5 | **The migration role.** As the plan says, `hrm-bootstrap` migrates with `<svc>_migrator` **in both postures**, and prints which role it used. Because the demo server is rebuilt from empty volumes (S-1), no administrator-migration override is needed; do not add one. |
| P-6 | **Merge only after the checks are green** (as in PLAN-37). Merging goes through `gh pr checks <n> --watch --fail-fast`, then `gh pr merge <n> --merge` only if that exits 0. Push **once**, in P8, after local CI is green. The post-merge dedupe guard from PLAN-37 should skip the heavy jobs; record whether it did. |
| P-7 | **Server images are built on the server:** `scripts/vps/build-on-host.ps1`, then `scripts/vps/deploy.ps1 -Tag <sha12>`. `build-on-host.sh` also builds `hr-migrate` and `hr-rabbitmq`. Build **once**, on the final code of P6. The demo stays usable at the end of every turn; if a deploy fails, restore the backup and use `-Rollback`, record the downtime, fix, and redeploy. |
| P-8 | **The broker on the server.** Before the rebuild, record the queues with their message counts. The old broker data is then discarded with the volumes (S-1). No delayed messages can exist yet, because the plugin was missing. |
| P-9 | **No regression of PLAN-36 and 37.** `lint-compose.ps1` (both chains), `Ops036ComposeLintTests`, `ProductionWiringMatrixTests` and `int037-wiring-check.ps1` stay green. Every new `.env` key goes into `.env.example` and `deploy/vps/.env.vps.example`. **Bootstrap writes no wiring key**; the wiring stays in the matrix-checked files. |

## Scope boundaries

**Out of scope** (from the plan):
- **Wave 2 (OPS-D):** the effects of enforced RLS, and making `least-privilege` the default database posture on the server. PLAN-48 covers national rows.
- **Wave 2 (OPS-E):** scheduled backup, restore and off-site copies. Here `hrm_backup` is only created and its connection fixed.
- **PLAN-39:** the Keycloak database user, the production realm, and client secrets inside Keycloak.
- **PLAN-42:** the gateway proof design.
- **PLAN-54:** the payroll legal framework in production.
- **Wave 2:** retry policy, and the `_error` replay tool.
- **k8s:** beyond the `hr-migrate` image existing under the name it requests.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, anything under `artifacts/`, or `.env.leastpriv`;
- test-rewritten `testdata/*/out/*.json` — use `git checkout -- testdata/*/out`, never a blanket `git checkout -- testdata`;
- any real secret, token, password, or unredacted `docker compose config` output.

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- disable host-key checking;
- delete server volumes without a verified backup;
- leave the demo server down at the end of a turn;
- weaken the guard or a readiness check, lower a coverage floor, or delete a CI step to get green.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **The migration bundles run `Program.cs` guards or host code** that cannot be bypassed with `IDesignTimeDbContextFactory` (the plan's Risks), and a bundle cannot run without production configuration it should not need.
- **A RabbitMQ `configure` expression** derived from the live inventory still makes a service's bus fail on a name that would only be allowed by a pattern wide enough to let it create other services' queues.
- **The server backup cannot be verified.** A verified backup means the dump lists all 16 `CREATE DATABASE` statements and every tar file is non-empty. Then do not delete anything.
- **A server deploy fails, and neither `-Rollback` nor the restore brings the demo back.**

---

## P0 — Timing, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions.
2. `git switch -c feature/plan-38-ops-first-boot-data-broker-secrets` from `main`. Commit this prompt file.
3. **Plan baseline** («المرحلة 0» steps 0.2–0.6) into `reports/plan38-baseline.md`:
   - **0.3** confirms the PLAN-36 artifacts.
   - **0.4** produces `reports/plan38-rabbit-topology.json` from a live run on the acceptance stack (administrator user) after the AC-HCM-33 journey. Every exchange and queue is classified by owning service, including `*_bus_*` and `MassTransit:Fault--*`.
   - **0.5** is the seed inventory: what runs in `SeedProfile.None`, and where its failure is swallowed.
   - **0.6** is the baseline unit filters.
4. **The server's current state, read-only:**
   - running tag;
   - the list of named volumes and their sizes;
   - `rabbitmqctl list_queues name messages` (a count summary only);
   - the database list;
   - the `.env` key names (names only).

Commit: `chore(ops038): baseline, live RabbitMQ topology, seed inventory, server before-picture`.

## P1 — The bootstrap image, databases, roles and migrations (plan §1, §1.1)

**The image and its scripts:**
- `docker/Migrator.Dockerfile`, with `scripts/build-ef-bundles.sh --self-contained -r linux-x64` (the new option). The image name `hr-migrate` matches `migrate-job.yaml`.
- `deploy/bootstrap/bootstrap.sh`: the eight steps, each idempotent. Step 4 follows P-5. Step 8 writes the result file **without secrets**.
- `ensure-databases.sql`, and `ensure-roles.sql` as §1.1 describes. `init-service-roles.sh` becomes a thin wrapper, so the development path still works.

**Compose:**
- The `hrm-bootstrap` service, and `service_completed_successfully` on every application.
- The per-service passwords passed to `postgresql` and `hrm-bootstrap`. Connection strings use their own variables, with no shared fallback.
- `docker-compose.vps-demo.yml` stops setting `RunMigrationsAtStartup=true` and keeps only the administrator connection strings.

**`deploy.yml`:** remove the GitHub-runner `migrate` job; `remote-deploy.sh` runs the bootstrap on the host. Keep `run-migrations.sh` for the k8s job.

**Tests:** `Ops038BootstrapIdempotencyTests` (Testcontainers PostgreSQL) covers:
- two runs give "0 migrations applied" and an unchanged `pg_class` count;
- a password rotation;
- `hr_core_app` without DDL (`42501`);
- `hrm_backup` reads but does not write.

If the bundles hit the plan's host-builder risk, add `IDesignTimeDbContextFactory` where needed. If that is not enough, **Stop**.

Commit: `feat(ops038): hr-migrate image and idempotent bootstrap (databases, per-service roles and passwords, migrations by migrator, backup role), applications wait for it`.

## P2 — The broker: least-privilege users, delayed messages, fixed node (plan §2)

- **`docker/rabbitmq/Dockerfile`**, per P-4.
- **In `docker-compose.yml`:** `hostname: rabbitmq`, `RABBITMQ_NODENAME`, and the `rabbitmq_data` volume. The image comes from `${HRM_IMAGE_REPO}/hr-rabbitmq:${IMAGE_TAG}` in the image chain, and from a local build in development and acceptance.
- **`deploy/bootstrap/rabbitmq-users.json`**, derived from the P0 live inventory.
  - Each `configure` expression covers:
    - the endpoint prefix (`ResolveEndpointPrefix`);
    - `HRManagement\.`;
    - `MassTransit`;
    - `.*_bus_.*`;
    - the delayed exchange MassTransit declares.
  - `write` and `read` are `.*`.
- **Bootstrap step 6** creates the users through the management API and rotates their passwords. `init-rabbitmq-service-users.sh` and `definitions-service-users.json` become wrappers of the matrix, or are removed.
- **Posture.** `docker-compose.vps-demo.yml` drops the administrator broker override. `docker-compose.posture-mq-admin.yml` restores it, only through `HRM_MQ_POSTURE=admin`.
- **Tests:**
  - `Ops038RabbitPermissionMatrixTests`: every endpoint prefix has a user and an expression.
  - `Ops038RabbitLeastPrivilegeTests`, on Testcontainers with the **new image**:
    - two buses with different users;
    - one user cannot create the other's queue (`ACCESS_REFUSED`);
    - a 10-second scheduled message arrives.

Commit: `feat(ops038): RabbitMQ image with the delayed-message plugin (pinned hash), fixed node and volume, least-privilege service users from a live inventory`.

## P3 — Secrets, no placeholders, the guard, the backup connection (plan §3)

- **`scripts/vps/init-secrets.ps1` and `deploy/vps/init-secrets.sh`**, per P-1. They read every `${VAR:?` in the production and single-host chains, plus the role and user matrices. Non-secret names such as `APP_DOMAIN` are **required, not generated**.
- **Example parity:** `.env.example` and `deploy/vps/.env.vps.example` get every missing key (P-9).
- **Placeholders removed:**
  - `GatewayCallerProof:Secret` deleted from the 14 `appsettings.Production.json` files; `Enforce` stays;
  - the gateway's `USE_ENV_*`/`USE_SECRET_MANAGER` values removed;
  - the `FileStorage:SecretKey = USE_ENV_OR_USER_SECRETS` entries cleaned (PLAN-36 defect 3).
- **The guard's critical keys** gain:
  - the service's own connection string;
  - `RabbitMQ:Username`/`Password`;
  - `ServiceIdentity:ClientSecret` when a `ClientId` is set;
  - HR.Core's `Recruitment:Portal:OtpHmacKey`;
  - a refusal of the administrator broker user when `HRM_MQ_POSTURE=least-privilege`.
- **`postgres-backup`** connects as `hrm_backup` with its own password, never the administrator's.
- **Tests:**
  - `Ops038EnvParityTests`: every `:?` appears in both examples and in the init-secrets matrix;
  - the extended guard tests, including the AC-OPS-38g negatives in unit form;
  - `Sec004` tests updated so they catch what they missed.

Commit: `feat(ops038): secret generation for missing keys only, example parity, no placeholder in production settings, guard covers connection, broker and service secrets, backup role connection`.

## P4 — Mandatory seeds, connection pools (plan §4, §5)

- **`SeedRunner`** (`RunMandatoryAsync` / `RunOptionalAsync`), applied from the P0 inventory:
  - national and reference seeds are mandatory;
  - demo and acceptance seeds are optional;
  - `ContractsDataSeeder` leaves the `None` condition and runs as mandatory in every profile except Acceptance;
  - Payroll's legal framework is left to PLAN-54 and recorded.
- **`docs/OPS_SEED_INVENTORY.md`.**
- **Pools:**
  - `Maximum Pool Size=${HRM_PG_POOL_MAX:-10}` (20 for HR.Core);
  - `KC_DB_POOL_MAX_SIZE: 20`;
  - `lint-compose.ps1` fails when the configured total reaches 90 % of `max_connections` or more.
- **Tests:** `SeedRunnerTests`, and the lint rule's own negative.

Commit: `feat(ops038): mandatory seeds stop Production start-up (named, no data printed), contracts reference seed in every profile, bounded connection pools checked by lint`.

## P5 — The least-privilege acceptance stack (plan §6, Acceptance, Verification)

1. Run every unit and Testcontainers filter in the plan's Verification block (PS 5.1 forms).
2. Build `hr-migrate` and `hr-rabbitmq` locally (P-3). Then run `init-secrets.ps1 -EnvFile .env.leastpriv`.
3. Start `docker-compose.acceptance-leastpriv.yml` as project `hrm-leastpriv` on new volumes, with the plan's command (P-2). Record the time from empty volumes to all healthy.
4. **`AcOps38FirstBootTests`** (`[RequiredAcceptance]`, `AcceptanceGate`, `EvidenceRecorder("AC-OPS-38", …)`): AC-OPS-38a..h, positive and negative, plus live **AC-20**.
   - Negatives that change roles or passwords restore them and prove the restore.
   - 38f recreates RabbitMQ with a durable message waiting.
5. Run with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`; all must Pass.
6. Run the existing prod-like smoke once, to prove the main acceptance stack still starts with the shared changes.
7. **PLAN-36's local acceptance (`AcOps36`) is not re-run locally.** It runs against the real server in P7, which is the regression that matters. Locally, `lint-compose.ps1` and `Ops036ComposeLintTests` cover the chain. Record this as a deviation.
8. Commit `acceptance-evidence/AC-OPS-38/` and `AC-20/`, and the regenerated summary.
9. Remove the `hrm-leastpriv` project and its volumes when done.

Commit: `test(ops038): AC-OPS-38a..h and AC-20 pass on a least-privilege stack built from empty volumes, evidence`.

## P6 — Two demo-breaking defects from PLAN-37 (prompt-level)

These are small and well diagnosed (`reports/plan37-final-report.md` §11). Fixing them now means the server build in P7 carries them.

1. **HR.Contracts leave approval fails on PostgreSQL.** `ApproveLeaveRequestHandler` claims the row with `ExecuteUpdate`, then `SaveChanges` updates the tracked entity against the old `xmin`, giving `DbUpdateConcurrencyException` and HTTP 500.
   - Make the claim and the update one consistent operation: reload or detach after the claim, or claim through the tracked entity with its concurrency token.
   - Check `AdvanceRequestHandlers` for the same pattern, and fix it if present.
   - **Test:** a **PostgreSQL** (Testcontainers) test that approves a leave and an advance. One concurrent second approval must lose cleanly, with the conflict status, not a 500.
2. **IDCard `IssuedCards/Create` can never succeed.** It binds the `IssuedCard` entity, whose `CardDesign` navigation is implicitly required.
   - Bind a view model instead, and map it.
   - **Test:** a controller or integration test that creates a card, plus the validation negative.

If either fix grows beyond the handler or the view model (for example a domain redesign), **stop that fix**, record it for its owning plan, and continue.

Commit: `fix(ops038): leave and advance approval work on PostgreSQL (one concurrency-consistent claim), ID card creation binds a view model`.

## P7 — The demo server, rebuilt from empty volumes (S-1)

Record every server change and its undo command **as you make it**.

1. Run `build-on-host.ps1` once, on the final P6 code, including `hr-migrate` and `hr-rabbitmq` (P-7).
2. Run `deploy/vps/init-secrets.sh` on the server to add the new keys (P-1).
3. Record the broker queues and counts (P-8).
4. Take the backup, then **verify** it (see Stop).
5. Bring the project down. Remove only the database, broker, file-storage and Data Protection volumes. Keep `caddy_data`, `caddy_config` and `seq_data`.
6. Deploy with `deploy.ps1 -Tag <sha12>`.
7. Check that `hrm-bootstrap` exits 0 and prints its result file (no secrets). Record the time from empty volumes to all healthy.
8. Check that the realm was re-imported and that `keycloak-sync-clients.sh` ran.
9. **Then check**, with evidence in `acceptance-evidence/AC-OPS-38/vps/`:
   1. `smoke -RequireIntegrationWiring` is green.
   2. `rabbitmqctl list_connections user` shows only `mq_*`.
   3. `rabbitmq-plugins list` shows the delayed exchange enabled.
   4. `AcOps36VpsBootTests` passes (36j excepted, as in PLAN-37, if it still needs the post-rollback state), and so do `AcInt37VpsServerTests`.
   5. The two P6 fixes work on the server through the UI with the demo users. Record them as `NotRun(server)` with the reason only if a demo user cannot reach them.
10. **If anything fails and cannot be fixed in the turn:** restore the backup, run `deploy.ps1 -Rollback` to `9d457dab36d7`, prove the demo works, and record it.

Commit: `test(ops038): demo server rebuilt from empty volumes on the first-boot path, mq_* users and delayed exchange live, PLAN-36 and 37 server checks still green`.

## P8 — Docs, review, final report, merge through a PR

1. **Docs:**
   - `PRODUCTION_RUNBOOK.md` §3 onwards (migrations through the bootstrap);
   - `RABBITMQ_OPERATIONS.md`;
   - `SEC_004_SECRET_ROTATION.md`: rotation means editing `.env`, then deploying;
   - `OPS_VPS_SINGLE_HOST.md`: first boot, init-secrets, rotation, the posture switches, the rebuild and the backup location;
   - `OPS_SEED_INVENTORY.md`;
   - the D-38-7 decision in `decision-log.json` and the obligation renewal;
   - the «الحالة» lines for G-OPS-02, G-OPS-13 and the covered part of G-OPS-10;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - a password on a command line or in a log;
   - a non-idempotent bootstrap step;
   - an `*_app` role with DDL or `BYPASSRLS`;
   - a broker expression wide enough to touch another service's queues;
   - an application still able to use the administrator in least-privilege posture;
   - a placeholder left in production settings;
   - a mandatory seed still swallowed;
   - a pool total over the limit;
   - a P6 fix that hides the conflict instead of handling it.

   Use a refuter only for high findings. Fix what survives, with tests.
3. **Local CI, then the pre-push check, before any push:** `powershell -File scripts/ci-local.ps1` on HEAD, then `powershell -File scripts/plan035-prepush.ps1`. Both must be green.
4. **`reports/plan38-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations (D-38-5 location, P-5, the `AcOps36` placement).
   4. The RabbitMQ inventory and expressions.
   5. The roles and pool totals.
   6. Unit, integration and acceptance results.
   7. The server: the rebuild (S-1), before and after, server changes and their undo, the backup location.
   8. The P6 fixes.
   9. Deferred items.
   10. Defects for later plans.
   11. **Timing:** total wall-clock; **active time vs owner-wait time**; builds, first boot and deploy times; Actions minutes and whether the post-merge guard skipped; and the output of `powershell -File scripts/plan-timing.ps1 -From 25 -To 38`.
   12. A readiness note for PLAN-39.
5. **Merge (P-6):**
   - Merge the feature branch into a new `release/plan-38` from `main` (`--no-ff`).
   - Write `reports/plan38-pr.md` (short).
   - **OWNER ACTION:**
     1. `git push -u origin release/plan-38`
     2. `gh pr create --base main --head release/plan-38 --title "PLAN-38: first boot of data, broker and secrets" --body-file reports/plan38-pr.md`
     3. `gh pr checks <n> --watch --fail-fast`
     4. Only if step 3 exits 0: `gh pr merge <n> --merge`
   - Verify that `main` = `origin/main`.
   - Record the PR link and the post-merge result in `reports/plan38-progress.md`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- A new server builds from empty volumes with no manual step. Redeploys are repeatable and rotate passwords.
- The GitHub-runner `migrate` job is removed, and the `hr-migrate` image exists.
- `mq_*` users are the default on the single host; the delayed-message plugin is enabled; the node name and volume are fixed.
- No placeholder value remains in `appsettings.Production.json`, and the examples match every `:?`.
- Mandatory seeds fail Production start-up, and the default contract type is seeded in `None`.
- The ObjectStorage-CI obligation is renewed by decision (D-38-7).
- AC-OPS-38a..h and AC-20 = Pass, with evidence. The demo server is rebuilt from empty volumes, and the smoke, the broker checks and the PLAN-36/37 server tests are green on it.
- Leave and advance approval work on PostgreSQL, and ID card creation works (P6), with tests.
- The final report is merged through a PR **after** green checks, with the timing section.

Then reply with a short summary in Arabic:
- what changed on the demo server;
- the rebuild result and where the backup is;
- the PR link;
- the path of the final report.
