# Task: PLAN-39 — The production Keycloak realm as code, and Data Protection keys that are never silently lost

The deliverables:
- **One realm source** — a manifest plus the PLAN-37 wiring matrix — rendered for the Production, Demo and Acceptance profiles, imported automatically, and reconciled on every deploy.
- **Keycloak on its own database role**, with production security settings.
- **Persistent key rings guarded at start-up**, and a canary that stops HR.Core rather than silently generating a new ring over protected data.
- **The demo server moved in place**, after a verified backup.

It runs in one session with no owner questions.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

**Owner actions (O)** are only these: merging this plan's PR, and anything only the owner can do. If PLAN-38's PR is still open, **you merge it yourself** (the owner's instruction, Preconditions 1). There is **no owner question** in this plan: every decision is already taken (see «Decisions»). At each owner action:
1. Write `OWNER ACTION:` in the progress file, with the exact commands, and stamp `Waiting for owner (UTC): <timestamp>`.
2. Commit what is complete.
3. End the turn with a short numbered list.

When the owner replies (for example «تم»), stamp `Owner action done (UTC): <timestamp>`, verify the result yourself, and continue. If the owner replies «نفذها انت», run those commands yourself, except anything this prompt reserves for the owner.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan39-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan39-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Check the server's state before any server step.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan39-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Every owner action records the `Waiting for owner` / `Owner action done` pair.
- Record the duration of image builds, realm imports, reconcile runs, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run (with its billable minutes).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**, for broad read-only searches (the client and scope inventory and the Data Protection inventory in P0 are good uses) or for root-causing separate failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (realm exports, `kcadm` listings, logs, test runs) to files under `artifacts/plan39/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior identity and platform engineer:
- Keycloak 24 (realm import, the admin CLI `kcadm.sh`, client scopes, audience mappers, brute-force and password policies, running behind a proxy);
- ASP.NET Core Data Protection (key rings, certificate protection, application isolation);
- PostgreSQL roles and object ownership;
- Docker Compose one-shot services;
- PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..38 did.

**Where things stand:**
- **The demo server** runs `7bd85bc3ac9d`, rebuilt from empty volumes by PLAN-38's first-boot path. `hrm-bootstrap` creates the databases, roles and broker users. The broker uses `mq_*` users. The database is in demo posture (applications on the administrator).
- **Keycloak** still connects to its database as the administrator, so its tables in `keycloak_db` are owned by the administrator. Its realm came from PLAN-36's `VpsDemo` import (re-imported by the PLAN-38 rebuild). It is kept in step with the matrix by PLAN-37's `deploy/vps/keycloak-sync-clients.sh`, which `remote-deploy.sh` runs after every deploy.
- **Data Protection volumes** (`hrm_gateway_dataprotection_keys`, 14 `hrm_dp_*`) are mounted, but nothing guards them.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-39-ops-keycloak-dataprotection.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. `reports/plan38-final-report.md`:
   - §3 deviations;
   - §7 the server (rebuild, backup location, changes and undo);
   - §10 defects (especially review F5: the configuration guard runs as a hosted service, after migrations and seeds have already connected);
   - §12 readiness note for PLAN-39 (the `keycloak` role belongs in `ensure-roles.sql` step 3; Data Protection volumes start empty on a new server).
3. `reports/plan37-final-report.md` §13: the matrix contract. `deploy/wiring/service-wiring.json` and `docs/SERVICE_WIRING_MATRIX.md`.
4. `docs/OPS_VPS_SINGLE_HOST.md`:
   - build-on-host, deploy, rollback, smoke;
   - `keycloak-sync-clients.sh`;
   - the realm placeholders (`${NAME}` resolved by Keycloak at import);
   - the backup and restore section;
   - the server change table.
5. `docs/OWNER_DECISIONS_REGISTER.md`: D-39-1..6 are accepted.
6. The files the plan cites:
   - `docker/keycloak/realm-hrmanagement-dev.template.json`, `realm-hr-services.json`, `realm-rel001-staging.json`;
   - `scripts/prepare-keycloak-import.ps1`, `scripts/sync-keycloak-dev-realm.ps1`, `scripts/export-keycloak-realm.ps1`;
   - `deploy/vps/keycloak-sync-clients.sh`, `deploy/vps/remote-deploy.sh`, `deploy/bootstrap/*`;
   - `DataProtectionExtensions.cs`, `PiiFieldEncryptionService.cs`, `EmployeePiiProtectionInterceptor.cs`, `CandidatePortalSession.cs`;
   - `ProductionConfigurationGuard*`;
   - `AcceptanceHarness.cs`, `testdata/acceptance/personas.json`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **PLAN-38 is merged with green checks — and if it is not, you merge it yourself.** The owner said on 2026-11-02: «إن لم يكن مدموجاً اجعله يدمجه هو». Its release branch `release/plan-38` (head `f76db4e1ba04`) is pushed. Check with `gh pr list --state all --head release/plan-38`.
   - **Merged:** continue.
   - **Open:** run `gh pr checks <n> --watch --fail-fast`; only if it exits 0, run `gh pr merge <n> --merge`.
   - **No PR yet:**
     1. Push `release/plan-38` if the remote branch is behind the local one.
     2. `gh pr create --base main --head release/plan-38 --title "PLAN-38: first boot of data, broker and secrets" --body-file reports/plan38-pr.md`
     3. Watch and merge as above.
   - **A red check:**
     1. Root-cause it with `gh run view <id> --log-failed`.
     2. Fix it on `release/plan-38` with a small commit naming the step, and rerun that step locally.
     3. Push the release branch only (never `main`).
     4. Watch again.

     If the fix needs more than a small change, record `BLOCKED:` with the cause.
   - **Never merge with a red or pending check.** The merge goes through `gh pr merge`; there is no local push to `main`.
   - **Record it** in `reports/plan38-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`: the PR link, each check result with its billable minutes, the merge commit, and whether the post-merge dedupe guard skipped the heavy jobs. That note rides with this plan's first commit.
2. **`main` is in sync.** `git switch main; git pull --ff-only`; then `git rev-parse main` = `git rev-parse origin/main`.
3. **The working tree is clean** except for these, which may be present:
   - the owner's local `global.json` edit;
   - the untracked `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - this prompt file;
   - the uncommitted `reports/plan38-progress.md` merge note, which rides with this plan's first commit.

   Test-rewritten `testdata/*/out/*.json` files are present now: restore them with `git checkout -- testdata/*/out` (never a blanket `git checkout -- testdata`).
4. **No other plan session** is running on this working tree.
5. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The server's ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.

## Decisions — already taken (no owner questions)

**D-39-1..6**, accepted by the owner on 2026-10-31, as the plan's table states:
- **D-39-1:** two non-overlapping sources. Service clients come from `service-wiring.json`; everything else comes from `docker/keycloak/realm-manifest.json`. `render-realm.ps1` merges them per profile.
- **D-39-2:** a first `--import-realm`, then an idempotent reconcile on every deploy, for clients, scopes, secrets, redirect URIs and named realm security fields only. **Never users.**
- **D-39-3:** a named volume per service. Certificate protection is optional, **off on the demo server**.
- **D-39-4:** HR.Core refuses to start on key loss. `DataProtection:AllowKeyRingReset=true` is the emergency switch (`Critical` plus a metric).
- **D-39-5:** one bootstrap administrator in the **Production** profile only, with `UPDATE_PASSWORD` at first login. The demo server uses the Demo profile and its demo users.
- **D-39-6:** `length(12) and notUsername and passwordHistory(3)`, and brute-force protection after 5 failures with a 15-minute lock.

**S-1 for this plan — the demo server moves in place.** Decided in the owner's standing direction to embed the recommended option so the session does not stop.
- **Backup first.** Back up on the server: `pg_dumpall`, plus a tar of the Data Protection volumes, under `/opt/hrm/backups/pre-plan39-<utc>/` (700, files 600). Verify it as PLAN-38 did: all 16 `CREATE DATABASE` statements, non-empty tars, `SHA256SUMS`.
- **Then move in place:**
  1. Stop Keycloak and its dependents.
  2. Run the documented ownership transfer, inside `keycloak_db` only, to the new `keycloak` role.
  3. Deploy.
  4. Reconcile applies the new realm security settings to the existing realm.
- **Why not rebuild again:** it keeps the demo data, so the new key-ring canary is exercised against data that really is protected. It also proves the plan's existing-server path, which a rebuild would skip.
- **Undo:** restore the backup and roll back to `7bd85bc3ac9d`.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **No secret value in any rendered realm file.** `render-realm.ps1` writes `${NAME}` placeholders for every secret (client secrets, `DEMO_USERS_PASSWORD`, the bootstrap administrator's temporary password). Keycloak resolves them from its own environment at import; this is the PLAN-36 mechanism, already proven on the server. Reconcile sets the secrets from `.env` on every deploy. This deviates from the plan's "merge secrets from `.env` at render time"; it keeps every secret off the owner's machine and out of `artifacts/`. `Ops039RenderRealmTests` asserts that the output contains no secret value. Lab secrets for the Acceptance profile may stay as they are today. |
| P-2 | **One reconcile mechanism, not two.** `deploy/keycloak/reconcile.sh` **replaces** PLAN-37's `deploy/vps/keycloak-sync-clients.sh` (delete it, or reduce it to a one-line wrapper). It runs as the one-shot compose service `keycloak-reconcile` from the Keycloak image, after `keycloak` is healthy. Applications that need an S2S token at start-up wait for `service_completed_successfully`, and `remote-deploy.sh` no longer calls the old script.<br>Reconcile:<br>• covers every matrix client, its scopes, secrets and redirect URIs, `directory-sync`, the scope audiences, and the named realm security fields (`sslRequired`, `bruteForceProtected` and its parameters, `passwordPolicy`, the gateway client's `directAccessGrantsEnabled` per profile);<br>• never touches users or role mappings;<br>• prints client ids and actions only, never a secret;<br>• prints `0 changes` on a second run, which must be proven.<br>The server-side sync idempotence test from PLAN-37 moves to the new script. |
| P-3 | **The guards run before the database is touched.** `DataProtectionStartupGuard` and `EmployeePiiKeyRingCanary` must run **before** any migration or seed can write protected data with a fresh ring, because a hosted service starts too late. Call them explicitly in the start-up path ahead of migrations and seeds, or in an equivalent early hook. PLAN-38 review F5 found the same ordering problem for `ProductionConfigurationGuard`: move it into the same early hook. Leave the "Production default for an absent posture" part of F5 to OPS-D, and record it. |
| P-4 | **Where AC-OPS-39a and 39e run.** The plan also runs them on the local single-host chain with the Production profile. Instead:<br>• **39a** runs on the prod-like stack (Acceptance profile, fresh Keycloak volume) and, for the Production-profile assertions (`sslRequired=external`, brute force, password policy, password grant refused, the bootstrap administrator's `UPDATE_PASSWORD`), in `Ops039KeycloakRealmImportTests` with Testcontainers Keycloak 24 and PostgreSQL, importing the rendered Production file.<br>• **39e** runs on the prod-like stack and on the server (P7).<br>Record this as a deviation. |
| P-5 | **Demo profile security.** The Demo profile carries the same security settings as Production (the plan's rule 2: profiles differ only in users, redirect URIs and the Acceptance exceptions). That includes the gateway client's `directAccessGrantsEnabled=false`. The server tests sign in through the browser form (`AcOps36VpsBootTests.SignInAsync`), so they are not affected; confirm this. The 16-character `DEMO_USERS_PASSWORD` meets the policy. |
| P-6 | **PowerShell 5.1.** Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block. Start the prod-like stack with the plan's `docker compose … up -d --wait` command directly, not through `acceptance-up.ps1`, whose build path needs PowerShell 7. Build local images one at a time, and prune the BuildKit cache when it is large. |
| P-7 | **Merge only after the checks are green.** Merging goes through `gh pr checks <n> --watch --fail-fast`, then `gh pr merge <n> --merge` only if that exits 0. Push **once**, in P8, after local CI is green. Record whether the post-merge dedupe guard skipped the heavy jobs. |
| P-8 | **Server images are built on the server** (`build-on-host.ps1`, then `deploy.ps1 -Tag <sha12>`). Build **once**, on the final code of P6. The demo stays usable at the end of every turn; if a step fails and cannot be fixed in the turn, restore the backup, roll back to `7bd85bc3ac9d`, prove the demo works, and record the downtime. |
| P-9 | **No regression of PLAN-36..38.** These stay green:<br>• `lint-compose.ps1` (both chains), `Ops036ComposeLintTests`;<br>• `ProductionWiringMatrixTests`, which gains the realm parity rule (the plan's §1 table), and `int037-wiring-check.ps1`;<br>• `Ops038EnvParityTests`.<br>New `.env` keys go into both examples and `init-secrets`: `POSTGRES_KEYCLOAK_PASSWORD`, `HRM_BOOTSTRAP_ADMIN_USER` and its generated temporary password, and the optional `DataProtection` certificate password. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-37:** the wiring of services in compose and k8s.
- **PLAN-42:** stricter audience checks and the gateway proof.
- **PLAN-36:** the edge (verified here, not rebuilt).
- **PLAN-62:** provisioning and deprovisioning user accounts at hire and separation.
- **Wave 2 (OPS-G):** bulk account creation and directory sync.
- **Wave 2 (OPS-E):** scheduled backups of key rings, the certificate and the realm.
- **Wave 2 (OPS-D):** RLS.
- **k8s:** shared key-ring storage.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/` (including rendered realms);
- test-rewritten `testdata/*/out/*.json`;
- any real secret, token, password, certificate, or unredacted `docker compose config` output.

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- disable host-key checking;
- touch Keycloak users or role mappings in reconcile;
- transfer ownership outside `keycloak_db`;
- delete a key ring or a server volume without a verified backup;
- leave the demo server down at the end of a turn;
- weaken a guard, lower a coverage floor, or delete a CI step to get green.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **Keycloak 24's import does not resolve `${NAME}` placeholders** in a field the realm needs (P-1), and the only alternative would put a secret value in a file outside the server.
- **The `keycloak_db` ownership transfer cannot be done object by object inside `keycloak_db`** without touching other databases or the administrator role itself. Note that `REASSIGN OWNED BY` the bootstrap superuser is refused by PostgreSQL.
- **The server backup cannot be verified.** Then change nothing on the server.
- **The canary on the server reports a key-ring mismatch** before any change of this plan. That means existing demo data is already unreadable: report it, do not reset the ring.
- **A server step fails, and neither the restore nor `-Rollback` brings the demo back.**

---

## P0 — Timing, preconditions, carry-over, baseline

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions; owner actions there come first.
2. `git switch -c feature/plan-39-ops-keycloak-dataprotection` from the updated `main`.
   - Commit this prompt file.
   - Commit the PLAN-38 merge note: `docs(ops038): PR merge note and post-merge run`.
3. **Plan baseline** («المرحلة 0» steps 0.2–0.5) into `reports/plan39-baseline.md`:
   - **0.3:** `reports/plan39-realm-acceptance-export.json`, an export of the current acceptance realm **with secrets removed**;
   - **0.4:** the client and scope inventory, compared with the matrix and the dev template;
   - **0.5:** every `AddHrDataProtection`, `SetApplicationName` and `CreateProtector` use, and which service stores protected data.
4. **The server's current state, read-only:**
   - Keycloak's database user (`pg_stat_activity`) and the owner of the `keycloak_db` objects (counts per owner);
   - the realm's `sslRequired`, `bruteForceProtected`, `passwordPolicy` and gateway `directAccessGrantsEnabled`;
   - the Data Protection volumes and whether each holds a key file;
   - **the canary as a read-only check:** whether the first `CfDJ8…` `NationalId` decrypts with the current ring, run by a one-off command against HR.Core's container. If it does not, **Stop** (see above).

Commit: `chore(ops039): baseline, realm export without secrets, client and scope inventory, Data Protection inventory, server before-picture`.

## P1 — The realm manifest and the renderer (plan §1)

- **`docker/keycloak/realm-manifest.json`** (no secrets) holds:
  - the roles;
  - the gateway client with `{APP_URLS}`;
  - `directory-sync`;
  - every S2S scope with its audience;
  - the user-profile attributes;
  - the three claim mappers;
  - the security settings per profile.
- **Service clients are not written here.** They come from `service-wiring.json`.
- **`scripts/keycloak/render-realm.ps1`** (`-Profile Production|Demo|Acceptance -EnvFile`), per P-1 and P-5:
  - **Production:** one bootstrap administrator with `UPDATE_PASSWORD`;
  - **Demo:** the demo users;
  - **Acceptance:** every persona from `personas.json`, with `directAccessGrantsEnabled=true`.
  - Output goes to `artifacts/keycloak/<profile>/`.
- **`prepare-keycloak-import.ps1`** becomes a wrapper; its `VpsDemo` profile becomes `Demo` with the `APP_DOMAIN` URIs.
- **`sync-keycloak-dev-realm.ps1`** reads the manifest for roles and audiences.
- **Retire** `realm-hr-services.json` and `realm-rel001-staging.json`: remove them, or mark them deprecated with a pointer.
- **Tests:**
  - `Ops039RealmManifestParityTests`, added to `ProductionWiringMatrixTests` rule 5. Every matrix client and scope is present and assigned, every scope has an audience, and every `ServiceIdentity__ApiAudience` is a scope target.
  - `Ops039RenderRealmTests`:
    - no secret value in any profile's output;
    - the profiles differ only in users, redirect URIs and the Acceptance exceptions;
    - no S2S client with direct or standard flows.

Commit: `feat(ops039): realm manifest plus wiring matrix rendered for Production, Demo and Acceptance with placeholder secrets, parity with the matrix, legacy realm files retired`.

## P2 — Import and reconcile (plan §2, P-2)

- **Compose (`docker-compose.production.yml`):**
  - `keycloak` with `--import-realm` and the `keycloak_import` volume, which `remote-deploy.sh` fills from the rendered file and then cleans;
  - the `keycloak-reconcile` one-shot;
  - `depends_on: service_completed_successfully` for the applications that request an S2S token at start-up.
- **`deploy/keycloak/reconcile.sh`**, per P-2. The PLAN-37 script is replaced, and `remote-deploy.sh` is updated.
- **Tests:** `Ops039KeycloakRealmImportTests` (Testcontainers Keycloak 24 + PostgreSQL):
  - import of the rendered **Production** file, with the P-4 assertions;
  - a token for each `svc-*` with its scopes and the expected `aud`;
  - reconcile run twice → `0 changes`;
  - a secret rotation through reconcile: the old secret is refused, the new one accepted.

Commit: `feat(ops039): automatic realm import and one idempotent reconcile on every deploy (clients, scopes, secrets, URIs, named security fields; never users), applications wait for it`.

## P3 — Keycloak on its own role (plan §3)

- **`ensure-roles.sql`** (PLAN-38 bootstrap step 3) gets the `keycloak` role:
  - `LOGIN`, owner of `keycloak_db`, password from `POSTGRES_KEYCLOAK_PASSWORD`;
  - `REVOKE CONNECT` on the other databases.
- **The existing-server ownership transfer:** `deploy/bootstrap/transfer-keycloak-ownership.sql`.
  - It runs only when asked, through a bootstrap switch that is off by default.
  - Inside `keycloak_db` only, it does `ALTER … OWNER TO keycloak` object by object (tables, sequences, views, functions, the `public` schema), then `ALTER DATABASE keycloak_db OWNER TO keycloak`.
  - It is idempotent, and it refuses to run while Keycloak is connected.
  - `OPS_VPS_SINGLE_HOST.md` documents it, with the backup step that must come first.
- **Keycloak's connection:** `KC_DB_USERNAME: keycloak` and `KC_DB_PASSWORD: ${POSTGRES_KEYCLOAK_PASSWORD:?…}`. The pool stays at 20.
- **`directory-sync`:**
  - `KEYCLOAK_DIRECTORY_SYNC_SECRET` becomes `${…:-}` in compose;
  - the guard requires it only when `Keycloak:DirectorySync:Enabled=true`;
  - the client keeps its generated secret.
- **Tests:** an extension of `Ops038BootstrapIdempotencyTests`:
  - the `keycloak` role cannot connect to `hr_core_db`;
  - the transfer moves every object in `keycloak_db`, and nothing elsewhere;
  - a second transfer run changes nothing.

Commit: `feat(ops039): Keycloak on its own database role, documented in-place ownership transfer for existing servers, directory-sync secret required only when enabled`.

## P4 — Data Protection: guard, canary, optional certificate (plan §4, P-3)

- **`DataProtectionStartupGuard`** fails in Production, or when `RequirePersistentKeyRing=true`, if:
  - the key-ring path is empty;
  - the path is not writable (probe file);
  - `SetApplicationName` is missing.
- **Certificate protection** through `DataProtection:CertificatePath`, with `PreviousCertificatePaths` and readable legacy unprotected keys.
- **`deploy/vps/make-dp-cert.sh` and `deploy/vps/export-dp-keys.sh`**: committed and documented, **not enabled on the demo server** (D-39-3).
- **`EmployeePiiKeyRingCanary`**:
  - raw SQL, `LIMIT 1`, on an indexed path;
  - **runs before seeds and migrations (P-3)**;
  - in Production, refuses to start unless `AllowKeyRingReset=true`, which logs `Critical` and increments a metric;
  - messages name the service and the path, and never print a key or a protected value.
- **`ProductionConfigurationGuard`** moves into the same early hook (P-3).
- **The prod-like file** sets `DataProtection__RequirePersistentKeyRing: "true"`.
- **Tests:**
  - `DataProtectionStartupGuardTests`;
  - `Ops039PiiKeyRingCanaryTests` (Testcontainers PostgreSQL): data protected with ring A, a start with ring B → refused; with the reset switch → `Critical`;
  - an ordering test proving the guards run before the first seed or migration call.

Commit: `feat(ops039): persistent key rings guarded before the database is touched, PII key-ring canary in HR.Core, optional certificate protection and key export, configuration guard moved earlier`.

## P5 — Acceptance on the prod-like stack (plan Acceptance, Verification, P-4)

1. Run every unit and Testcontainers filter in the plan's Verification block (PS 5.1 forms).
2. Render the Acceptance profile, then start the prod-like stack on **new Keycloak volumes** with the plan's command (P-6). Record the time to all healthy and the reconcile output.
3. **`AcOps39KeycloakDataProtectionTests`** (`[RequiredAcceptance]`, `AcceptanceGate`, `EvidenceRecorder("AC-OPS-39", …)`): AC-OPS-39a..h, positive and negative, with the P-4 placement. Negatives that delete or change things (39d rotation, 39g volume deletion, 39h missing path) restore their state and prove the restore.
4. Run the regression filters `AcHcm25` and `AcHcm30` on the same stack.
5. Run with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`; all must Pass.
6. Commit `acceptance-evidence/AC-OPS-39/` (no secrets: scan it) and the regenerated summary.

Commit: `test(ops039): AC-OPS-39a..h pass on the prod-like stack with a realm rendered from the manifest, AcHcm25/30 still green, evidence`.

## P6 — Carry-over hygiene (prompt-level, small)

- `AcOps36VpsBootTests` evidence formats numbers with the invariant culture (PLAN-38 defect: `22٫9 %` on ar-SA).
- Nothing else is carried over. If something else looks urgent, record it for its owning plan.

Commit: `fix(ops039): server acceptance evidence formats numbers with the invariant culture`.

## P7 — The demo server, moved in place (S-1)

Record every server change and its undo command **as you make it**.

1. Run `build-on-host.ps1` once on the final code (P-8). Run `deploy/vps/init-secrets.sh` on the server to add the new keys (P-1, P-9).
2. Take the backup, then **verify** it (see Decisions).
3. **Stop Keycloak and its dependents**, then run the ownership transfer through the bootstrap switch. Record per-owner object counts before and after.
4. Deploy with `deploy.ps1 -Tag <sha12>`. `hrm-bootstrap` ensures the `keycloak` role, `keycloak` connects with it, and `keycloak-reconcile` applies the Demo profile's security settings to the existing realm. The canary runs before the seeds and **must pass on the existing data**.
5. Record the downtime and the time to all healthy.
6. **Then check**, with evidence in `acceptance-evidence/AC-OPS-39/vps/`:
   1. `pg_stat_activity` shows Keycloak as `keycloak`, and `keycloak` cannot connect to `hr_core_db` (39e).
   2. The realm's `sslRequired=external`, the brute-force settings, the password policy and the gateway's direct grants are off.
   3. A demo user signs in through the browser form.
   4. Reconcile run a second time prints `0 changes`.
   5. **The canary passes**: an employee's national ID still shows after `--force-recreate` of all services (39f on the server).
   6. Smoke `-RequireIntegrationWiring` is green.
   7. `AcOps36VpsBootTests` (36j excepted, as before) and `AcInt37VpsServerTests` pass.
7. **If anything fails and cannot be fixed in the turn:** restore the backup, roll back to `7bd85bc3ac9d`, prove the demo works, and record it.

Commit: `test(ops039): demo server moved in place (Keycloak on its own role, realm security settings reconciled, key-ring canary green on existing data), PLAN-36..38 server checks still green`.

## P8 — Docs, review, final report, merge through a PR

1. **Docs:**
   - `KEYCLOAK_AND_SECRETS.md`: the manifest, the renderer and the reconcile replace the manual «ابدأ من هنا»;
   - `OPS_VPS_SINGLE_HOST.md`: reconcile, the ownership transfer, the key rings, the certificate and key export, the backup location;
   - `IAM_001_JWT_KEYCLOAK.md`: the production settings;
   - the «الحالة» lines for G-OPS-03 and G-OPS-04;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - a secret in a rendered file, a log, the evidence or the repository;
   - a reconcile step that touches users or is not idempotent;
   - an S2S client with interactive flows;
   - a profile difference beyond the allowed ones;
   - an ownership transfer that can reach outside `keycloak_db`;
   - a guard or canary that runs after the first seed, or that a missing table or empty data can bypass;
   - a key ring silently regenerated anywhere.

   Use a refuter only for high findings. Fix what survives, with tests.
3. **Local CI, then the pre-push check, before any push:** `powershell -File scripts/ci-local.ps1` on HEAD, then `powershell -File scripts/plan035-prepush.ps1`. Both must be green.
4. **`reports/plan39-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations (P-1, P-2, P-3, P-4).
   4. The realm sources and the profile differences.
   5. Unit, integration and acceptance results.
   6. The server: in-place move, ownership counts, before and after, server changes and their undo, the backup location.
   7. Deferred items.
   8. Defects for later plans.
   9. **Timing:** total wall-clock; **active time vs owner-wait time**; builds, imports, reconcile and deploy times; Actions minutes and whether the post-merge guard skipped; and the output of `powershell -File scripts/plan-timing.ps1 -From 25 -To 39`.
   10. A readiness note for PLAN-40.
5. **Merge (P-7):**
   - Merge the feature branch into a new `release/plan-39` from `main` (`--no-ff`).
   - Write `reports/plan39-pr.md` (short).
   - **OWNER ACTION:**
     1. `git push -u origin release/plan-39`
     2. `gh pr create --base main --head release/plan-39 --title "PLAN-39: Keycloak realm as code and persistent Data Protection keys" --body-file reports/plan39-pr.md`
     3. `gh pr checks <n> --watch --fail-fast`
     4. Only if step 3 exits 0: `gh pr merge <n> --merge`
   - Verify that `main` = `origin/main`.
   - Record the PR link and the post-merge result in `reports/plan39-progress.md`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- **The realm:**
  - it is rendered from the manifest plus the matrix for every profile, with no secret value in any file;
  - it is imported automatically on a new server;
  - it is reconciled on every deploy, idempotently, and never touches users.
- **The secrets and flows:**
  - no shared service-client secret;
  - no `sslRequired=none`, and no password grant on the gateway client, in Production or Demo.
- **Keycloak** runs on its own database role; on the demo server after an in-place, documented ownership transfer.
- **The key rings:**
  - every Production service keeps its key ring on a persistent path, guarded before the database is touched;
  - HR.Core refuses to start on key loss;
  - the canary passed on the demo server's existing data.
- **The tests:**
  - AC-OPS-39a..h = Pass, with evidence;
  - AcHcm25/30 are green;
  - the PLAN-36..38 server checks are green.
- **The merge:** the final report is merged through a PR **after** green checks, with the timing section.

Then reply with a short summary in Arabic:
- what changed on the demo server;
- where the backup is;
- the PR link;
- the path of the final report.
