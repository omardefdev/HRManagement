# Task: PLAN-37 — Every service gets, in the real production settings, exactly what its code reads (other services' addresses, its service identity, scopes and secret, and the service-regimes legal pack); one machine-readable wiring matrix checked automatically; start-up fails clearly when wiring is missing; archive consumers stop dropping messages; the prod-like stack fails where production fails; the demo server runs the cross-service flows (one session)

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P9** in order.

**Owner actions (O)** are few in this plan: merging a PR, and anything only the owner can do. At each owner action:
1. Write `OWNER ACTION:` in the progress file, with the exact commands, and stamp `Waiting for owner (UTC): <timestamp>`.
2. Commit what is complete.
3. End the turn with the commands in a short numbered list.

When the owner replies (for example «تم»), stamp `Owner action done (UTC): <timestamp>`, verify the result yourself, and continue. If the owner replies «نفذها انت», run those commands yourself, except anything this prompt reserves for the owner.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan37-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan37-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Check the server's state before any server step.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan37-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Every owner action records the `Waiting for owner` / `Owner action done` pair.
- Record the duration of the acceptance runs, the server build, the deploy, and every GitHub Actions run (with its billable minutes).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**, for broad read-only searches (the code inventory in P0 is a good use) or for root-causing separate failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (compose `config` dumps, logs, test runs) to files under `artifacts/plan37/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET platform engineer:
- ASP.NET Core configuration and hosting;
- service-to-service authentication with Keycloak client credentials and scopes;
- MassTransit consumers and retries;
- Docker Compose and Kustomize;
- MSBuild content items and `dotnet publish`;
- PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH). You also operate the demo server through `ssh hrm-vps`, as PLAN-36 did.

**Where things stand:**
- **PLAN-36:** the production profile boots on one server, in demo mode, at `https://hr.161-97-185-71.sslip.io/`.
- **What does not work there yet:** flows that cross services. The services lack each other's addresses, their identities, scopes and secrets, and the legal pack — the smoke test reports these as `Deferred(PLAN-37)`.
- **This plan** fixes that at the source: in the shared compose files, the production files and k8s. It also makes the prod-like acceptance stack take its wiring from the production files, so it can no longer pass while production is broken.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. `reports/plan36-final-report.md`:
   - §11 deviations (images are built **on the server**);
   - §12 deferred items;
   - §13 defects;
   - §15 readiness note for PLAN-37.
3. `docs/OPS_VPS_SINGLE_HOST.md`: build-on-host, deploy, rollback, smoke, the Keycloak `kcadm.sh` procedure, and the server change table.
4. `docs/OWNER_DECISIONS_REGISTER.md` after P0 step 3: D-INT-1..5 are accepted.
5. The studies the plan cites:
   - `docs/COD_ORG_GAP_STUDY.md` (G-COD-01, 06);
   - `docs/IAM_001_SCOPE_GAP_STUDY.md` (G-IAM-03);
   - `docs/POL_001_GAP_STUDY.md` (G-POL-01);
   - `docs/FUN_003_MOVEMENTS_GAP_STUDY.md` (G-MOV-02);
   - `docs/FUN_002_SEPARATION_GAP_STUDY.md` (G-SEP-02).
6. The files the plan cites:
   - `docker-compose*.yml`, `deploy/k8s/**`;
   - `scripts/sync-keycloak-dev-realm.ps1`, `scripts/prepare-keycloak-import.ps1`, `scripts/acceptance-up.ps1`;
   - `scripts/vps/*.ps1`, `deploy/vps/*.sh`;
   - `ServiceAuthorizationExtensions.cs`, `ServiceIdentityClient.cs`, `CodeGenerationClient.cs`;
   - the `ServiceRegime*` readers;
   - the seven archive consumer sites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **PLAN-36 is merged:** PR #30, merge `7d602f9c`.
2. **The HARD-001 follow-up is merged with green checks.** The branch `fix/hard001-anchor-scan` (commit `71c15aed`, "HARD-001 scan accepts extra anchors…") is pushed. Check its PR with `gh pr list --state all --head fix/hard001-anchor-scan`.
   - **Open and all checks green:** **OWNER ACTION** `gh pr merge <n> --merge`.
   - **Open with a red check:** root-cause it first and report.
   - **No PR yet:** **OWNER ACTION** `gh pr create --base main --head fix/hard001-anchor-scan --fill`, then wait for green.
3. **`main` is in sync and green.**
   - `git rev-parse main` = `git rev-parse origin/main`, after `git switch main` and `git pull --ff-only`.
   - The last `push` run on `main` is green, or its only red job is the HARD-001 scan that the follow-up fixes.
4. **The working tree is clean** except for these, which may be present:
   - the owner's local `global.json` edit;
   - the untracked `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - this prompt file;
   - the uncommitted `reports/plan36-progress.md` note. The PLAN-36 prompt said that note rides with the next plan's first commit.
5. **No other plan session** is running on this working tree.
6. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The server's ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.

## Decisions — already taken (no owner questions in this plan)

The owner accepted every recommended default on 2026-10-31 through the wave-1 decisions questionnaire.

For this plan those are **D-INT-1..5, as written in the plan's «قرارات مطلوبة قبل التنفيذ» table:**
- `Content` from `HRManagement.SharedKernel.csproj`, plus the optional external path;
- a strategic `wiring-patch.yaml` per k8s Deployment;
- the old HR.Core keys deprecated with a warning;
- services that register the HR.Core client get the URL but not `hr-core.workflow`;
- start-up fails on a missing URL outside `Development`, and `localhost` is refused inside a container.

Do **not** ask the owner about them again. If the code inventory (P0) shows that one cannot work as written, record the smallest deviation that keeps its intent, and report it.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Secrets never enter your context, the repository, the evidence or the transcript** (as in PLAN-36 P-2). New `SVC_*` secrets for the server are generated **on the server** with `openssl rand` into `/opt/hrm/shared/.env`, without echoing them. Keycloak gets each client's secret from that file through a server-side command, never through a value you type or print. Check `.env` by key names only. Lab values (`dev-svc-secret-local-only` and the like) are allowed only in acceptance and lab files. |
| P-2 | **PowerShell 5.1.** Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block with that form. **The acceptance stack:** `scripts/acceptance-up.ps1` needs PowerShell 7 only for its live verification, so run it with `-SkipLiveVerification`. The `dotnet test` acceptance run is the verdict. If `Get-Command pwsh` finds PowerShell 7 (the owner may install it with `winget install --id Microsoft.PowerShell -e`), you may also run the live verification. |
| P-3 | **YAML in tests.** Add `YamlDotNet` 16.3.0 as a **test-only** package (`Directory.Packages.props`, lock files refreshed with `dotnet restore --force-evaluate`). Never reference it from production projects. The rendered `docker compose config --format json` in `int037-wiring-check.ps1` remains the final judge (plan Risks). |
| P-4 | **The server's Keycloak realm is never dropped or re-imported.** It was imported once by PLAN-36, and the demo users and data depend on it. Add the missing service clients, client scopes and scope mappings **idempotently**, using `kcadm.sh` inside the Keycloak container over `ssh`, driven by `deploy/wiring/service-wiring.json` — the same matrix `sync-keycloak-dev-realm.ps1` reads. Also make the `VpsDemo` profile of `prepare-keycloak-import.ps1` generate every matrix client and scope, so a fresh server gets them at its first import. Keycloak admin credentials and client secrets are read inside the server command from `/opt/hrm/shared/.env` (P-1). |
| P-5 | **Server images are built on the server** (PLAN-36 §11): `scripts/vps/build-on-host.ps1`, then `scripts/vps/deploy.ps1 -Tag <sha12>`. The owner's upload link is slow (~0.16 MB/s), so build on the server **once**, on the final code of P6. If a server step fails: `deploy.ps1 -Rollback`, record the downtime, fix, and redeploy. The demo stays usable at the end of every turn. |
| P-6 | **Merge only after the checks are green.** PR #30 was merged while CI, CodeQL and Gitleaks were still pending, because the free private repository has no branch protection (PLAN-35 defect 14). From this plan on, every merge goes through `gh pr checks <n> --watch --fail-fast` and runs `gh pr merge <n> --merge` **only if that exits 0**. This applies to the owner's commands and to yours under «نفذها انت». |
| P-7 | **Actions minutes.** The free private quota is about 2,000 Linux minutes a month, and a full CI run costs 90–115. The owner asked to cut the duplicate run after each merge, so P8 adds a guarded skip for it. Push **only** in P9, once, after local CI is green. Do not add scheduled workflows. |
| P-8 | **The contract for later plans** (plan §1 rule 5): any later plan that adds an S2S client, call or scope, changes a server policy for a called endpoint, or removes a registration updates `service-wiring.json` in the same commit, and `ProductionWiringMatrixTests` enforces it. State this in `docs/SERVICE_WIRING_MATRIX.md` and in the final report's readiness note. |
| P-9 | **Legal data is not invented.** The legal pack's source is `docs/syr/service-regimes.json` as it is. Missing or zero required counts make the readiness check fail. If the repository's file has a zero count for a category the check must require, do not weaken the check and do not add rules: **Stop** (see below). |

## Scope boundaries

**Out of scope** (the plan's table):
- **PLAN-42:** per-service ExternalSecrets, rejecting placeholder secrets, an unforgeable gateway proof, fail-closed enforcement and legal-hold gate, and ignoring `TenantId` in the code request.
- **PLAN-39:** the production realm template.
- **PLAN-38:** first-boot secrets and broker users.
- **PLAN-48:** number prefixes.
- **PLAN-45/46:** the custody approval loop.
- **PLAN-61:** the separation catalogue.
- **PLAN-50:** approver access to movements.
- **PLAN-49:** removing unused enforce-client registrations.
- **Wave 2:** COD-C and OPS-D.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- test-rewritten `testdata/*/out/*.json` — use `git checkout -- testdata/*/out`, never a blanket `git checkout -- testdata`;
- any real secret, token, password or unredacted `docker compose config` output.

**Never:**
- push to `main` or force-push;
- merge with a pending or red check (P-6);
- disable host-key checking;
- drop the server's realm (P-4);
- weaken a readiness check, lower a coverage floor, or delete a CI step to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **A call the code makes needs a scope or a server-policy change** that the plan's table does not list, and granting it would widen a service's privilege beyond what that call justifies. Present the call, the policy and the options.
- **Making an AC pass would need PLAN-42 behaviour**: authentication of the legal-hold gate, fail-closed enforcement, or rejecting placeholder secrets.
- **The legal pack in the repository is empty** for a category the readiness check must require (P-9).
- **A server deploy fails and `-Rollback` also fails.**

---

## P0 — Timing, preconditions, carry-over, baseline

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions; owner actions there come first.
2. `git switch -c feature/plan-37-int-production-wiring-legal-pack` from the updated `main`.
   - Commit this prompt file.
   - Commit the PLAN-36 progress note: `docs(ops036): PR #30 merge note and the HARD-001 follow-up`.
3. **The owner's wave-1 decisions.** Copy `artifacts/plan-decisions/OWNER_DECISIONS_REGISTER.md` over `docs/OWNER_DECISIONS_REGISTER.md`.
   - **First verify the diff:** only the «القرار المعتمد» column of decision rows and **one** added header line («تحديث 2026-10-31…») may change. Any other difference means the register changed on `main` since 2026-10-31: merge by hand, keeping `main`'s rows, then fill the column.
   - Keep `artifacts/plan-decisions/wave1-answers-2026-10-31.md` as the source; do not commit it.
   - Commit: `docs(program): owner accepted the wave-1 decisions (PLAN-37..65)`.
4. **Plan baseline** («المرحلة 0» steps 0.2–0.5) into `reports/plan37-baseline.md`:
   - **0.2** the build and the listed unit filters;
   - **0.3** the code inventory (service × key × client × registration), compared with the plan's §1 table, every difference explained. Use a read-only subagent for the `rg` sweep if it helps.
   - **0.4** the effective environment of both production chains and the single-host chain, using the chain definitions in `scripts/vps/lint-compose.ps1` with a dummy `.env`. Add `kubectl kustomize` only if `kubectl` exists; otherwise record "k8s rendered check skipped: no kubectl" and rely on the YAML reader.
   - **0.5** whether `/app/seed-packs` exists in the current `hr-contracts-web` image.

   Save the `config` dumps under `artifacts/plan37/`, not in the report.
5. **The server's current wiring state, read-only:**
   - `ssh hrm-vps "cd /opt/hrm/current && bash deploy/vps/smoke.sh --require-integration-wiring"`, or the equivalent `smoke.ps1 -RequireIntegrationWiring`;
   - record the five `Deferred(PLAN-37)` items as the before-picture.

Commit: `chore(int037): baseline, code and deployment wiring inventory, server before-picture`.

## P1 — The wiring matrix as the single source of truth (plan §1)

- **`deploy/wiring/service-wiring.json`:**
  - one entry per service, with the fields of §1: `composeService`, `k8sDeployment`, `projects[]`, `clientId`, `secretEnv`, `urls{}`, `scopes[]`, `calls[]`, `legalPacks[]`;
  - the `registrations` section;
  - content from the P0 inventory, not copied blindly from the plan's table.
- **`ServiceWiringMatrix`** (test support): loader and schema check.
- **`scripts/int037-render-wiring-doc.ps1`** → `docs/SERVICE_WIRING_MATRIX.md`, including the P-8 contract paragraph. It replaces §86 of `SERVICE_TRUST_INVENTORY.md` (leave a pointer there).
- **`ServiceAuthorizationExtensions`:** add `svc-compliance` to `FileStorageReferences` and `svc-auth-directory` to the HR.Core lookup list. No other client is added unless a `calls[]` entry justifies it.
- **HR.Core:** remove the two unused registrations (`AddHrPolicyEnforceClient`, `AddHrCodeGeneration`) at `Program.cs:277-278`, after confirming in the inventory that nothing injects them.
- **Tests:**
  - the matrix schema test;
  - the authorization-policy evaluation of every `calls[]` entry, positive and negative (rule 2 of §2), using the real `AddHrManagementAuthorization` and `AddServiceToServiceAuthorizationPolicies`.

Commit: `feat(int037): service wiring matrix with calls, scopes and legal packs, rendered doc, server allow-lists aligned, unused HR.Core registrations removed`.

## P2 — Deployment files follow the matrix, and the automated check (plan §2, §3)

These two land together, so the phase ends green.

**The checker (§2):**
- `ComposeEffectiveEnvironment` and `KustomizeEffectiveEnvironment`.
- `ProductionWiringMatrixTests`, with the six checks of §2:
  - code → matrix;
  - matrix → server policies;
  - matrix → deployment (both production chains and k8s);
  - prod-like parity;
  - Keycloak script ↔ matrix;
  - the negative check of the checker itself.
- `scripts/int037-wiring-check.ps1` (PS 5.1), using the chain definitions from `scripts/vps/lint-compose.ps1` rather than a copy. It writes `artifacts/int037/wiring-report.json` and exits non-zero on any gap.

**The deployment files (§3):**
- the wiring written **once** in `docker-compose.yml` and `docker-compose.services.yml`;
- `docker-compose.production.yml` keeps only the mandatory secrets and client ids, including `auth-directory-web` → `svc-auth-directory`;
- wiring removed from `docker-compose.acceptance.yml`;
- `docker-compose.acceptance-prodlike.yml` header updated, and it recreates **all** services;
- 14 `wiring-patch.yaml` files plus `kustomization.yaml` patches, and `SVC_AUTH_DIRECTORY_CLIENT_SECRET` in the overlays' `hr-app-secrets.yaml`;
- `sync-keycloak-dev-realm.ps1` reads the matrix;
- `acceptance-up.ps1 -ProdLike`;
- `deploy/vps/.env.vps.example` lists every `SVC_*` key the production chain now requires (key names and Arabic comments, no values);
- `scripts/vps/smoke.ps1` / `smoke.sh` read their wiring checks from the matrix and make `-RequireIntegrationWiring` **mandatory**: a deferred check now fails the smoke run.

**PLAN-36's tooling stays green:** `scripts/vps/lint-compose.ps1` for both chains, and `Ops036ComposeLintTests`.

Commit: `feat(int037): deployment wiring written once and checked against the matrix (compose, k8s, Keycloak script, prod-like parity), mandatory wiring smoke`.

## P3 — Explicit failure on missing wiring, one HR.Core key (plan §4)

- **`HrCoreEndpoint.RequireBaseUrl`**, used by every reader the plan lists. Remove `?? "http://localhost:5010"` and every name-only `AddHttpClient` branch. Follow D-INT-3 (old keys warn, conflict = error) and D-INT-5 (no `localhost` in a container; `Development` outside containers only).
- **HR.Contracts:** `HttpEmployeeLifecycleGate` is registered from `Integration:HrCoreBaseUrl`. Change only the key and the wiring; authentication and fail-closed are PLAN-42's.
- **`ServiceIdentityClient`:** a blank secret counts as missing, then the environment variable, then an error.
- **`WorkflowTriggerService`:** sign `GatewayCallerProof` in both places.
- **Tests:**
  - `HrCoreEndpointTests`, simulating `DOTNET_RUNNING_IN_CONTAINER`;
  - `ServiceIdentitySecretTests`;
  - `Int037LifecycleGateRegistrationTests`;
  - a proof-signature assertion for the workflow trigger.

Commit: `feat(int037): services refuse to start without their HR.Core URL or secret, one HR.Core key with deprecated fallbacks, lifecycle gate wired by the approved key, workflow trigger signs the gateway proof`.

## P4 — Archive consumers never drop a message silently (plan §5)

- Add `CodeGenerationUnavailableException`. It derives directly from `Exception`; check MassTransit's ignore list in `MassTransitExtensions.cs`.
- Add `GenerateRequiredAsync`.
- `ArchiveReferenceFactory` uses it, and the seven `if (reference is null) return;` lines are removed.
- Add the `archive_reference_failures_total` counter.
- **Tests:**
  - `Int037ArchiveConsumerTests` (unit);
  - `Int037ArchiveRetryIntegrationTests` with the MassTransit test harness: retries, then `_error`, then exactly one row after recovery.

Commit: `fix(int037): archive consumers retry and dead-letter when no reference can be generated, never acknowledge without an archive item`.

## P5 — The legal pack ships with every service that reads it (plan §6)

- Add the `Content` item in `HRManagement.SharedKernel.csproj`, and remove the line in `HR.Core.Infrastructure.csproj:16`.
- `ServiceRegimePack` becomes the one source, with its three sources in order. Every `ServiceRegime*` reader and the seeder go through it; `ServiceRegimeJsonPackPaths` and the duplicated search lists are deleted.
- Add `ServiceRegimePackHealthCheck` (`ready` tag) and the one-line start-up log, in HR.Core, HR.Contracts and HR.Movements.
- **Tests:**
  - `ServiceRegimePackTests`;
  - `Int037PublishOutputTests`: a real `dotnet publish` of HR.Contracts and HR.Movements to a temp folder, then the file and its SHA-256;
  - the matrix check that every project calling `ServiceRegime*Rules` registers the health check.
- P-9 applies to the counts.

Commit: `feat(int037): service-regimes legal pack shipped in every reading service, one loader, readiness check and start-up fingerprint`.

## P6 — The prod-like stack fails where production fails, acceptance (plan §7, Acceptance, Verification)

1. Run every new unit and integration filter from the plan's Verification block.
2. Run `powershell -File scripts/int037-wiring-check.ps1`; it must be green.
3. Start the prod-like stack: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/acceptance-up.ps1 -ProdLike -SkipLiveVerification` (P-2).
   - Confirm the pack exists inside the three images (the `ls` loop in Verification).
   - Record the stack start time.
4. **`AcInt37ProductionWiringTests`** (`[RequiredAcceptance]`, `AcceptanceGate`, `EvidenceRecorder("AC-INT-37", …)`):
   - AC-INT-37a..h, positive and negative, with the plan's personas;
   - negatives that change a container's environment (37e, 37f) use one-off `docker compose run` containers, or restore and prove the restore;
   - the 37d negative stops and restarts `hr-core-web` and proves exactly one archive row after the replay.
5. Run with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`, together with `RecruitmentToHireJourneyProdLike`. All must Pass.
6. Run the existing acceptance smoke once on the normal (non-prod-like) stack, to prove the shared wiring did not break it.
7. Commit the evidence folder `acceptance-evidence/AC-INT-37/` and the regenerated summary.

Commit: `test(int037): AC-INT-37a..h pass on the prod-like stack that takes its wiring from the production files, evidence`.

## P7 — The demo server runs the cross-service flows

Record every server change and its undo command **as you make it**.

1. **Secrets on the server** (P-1):
   - For each `SVC_*` key the production chain now requires and `/opt/hrm/shared/.env` lacks, append a value generated on the server, without echoing it.
   - Verify by key names only.
2. **Keycloak** (P-4):
   - Write an idempotent server-side script `deploy/vps/keycloak-sync-clients.sh`, committed. It reads `service-wiring.json` from the release bundle and `.env` on the server, and creates or updates the missing clients, client scopes, scope mappings and client secrets with `kcadm.sh` inside the Keycloak container.
   - It prints only client ids and actions, never a secret. Running it twice changes nothing the second time; prove that.
   - `remote-deploy.sh` calls it after Keycloak is healthy, so later deploys keep the realm in step with the matrix.
3. **Build and deploy** (P-5):
   - Run `build-on-host.ps1` once, on the final P6 code, then `deploy.ps1 -Tag <sha12>`.
   - The smoke run inside the deploy now includes the mandatory wiring checks and must be green.
   - If it fails: `-Rollback`, fix, redeploy.
4. **Server proof** (`VPS_SSH_HOST=hrm-vps` mode, as in `AcOps36VpsBootTests`). Evidence goes to `acceptance-evidence/AC-INT-37/vps/`.
   - **Required:** `smoke.ps1 -RequireIntegrationWiring` green, plus 37e: the `LegalPack loaded … sha256=` line from the three services' logs, matching the repository file.
   - **Best effort:** the 37a/37c positives with the VpsDemo users. Any scenario that needs an acceptance-only persona is recorded `NotRun(server)` with the reason, not faked.
   - The test reads the demo password over `ssh` into memory only (as in PLAN-36).
5. Run `AcOps36VpsBootTests` once more against the server, to prove PLAN-36's guarantees still hold: only 443 exposed, the rate limiter, the guard.

Commit: `test(int037): demo server wired (service clients and secrets, mandatory wiring smoke green), legal pack fingerprint on the server, PLAN-36 checks still green`.

## P8 — CI minutes and local parity (prompt-level, P-7)

1. **Local parity for the HARD-001 scan.** If `hardening-check.yml`'s scan is still not in `scripts/ci/steps.json`, add it as a step, so local CI catches what failed PR #30. Keep `CiStepsParityTests` green.
2. **Skip the duplicate run after a merge**, fail-safe:
   - **On a `pull_request` run:** the heavy workflow(s) record the tested tree (`git rev-parse HEAD^{tree}` of the test merge commit) as a tiny artifact.
   - **On a `push` to `main`:** a first light job finds the PR merged into this commit, finds that PR's **latest successful** run of the same workflow, downloads its tested tree, and compares it with `HEAD^{tree}`.
     - **Equal:** the heavy jobs are skipped, with a job summary naming the PR run that already tested this exact tree.
     - **Anything else** — no PR, not equal, an API or download error, a failed PR run: the full run proceeds. **The guard fails open to running everything, never to skipping.**
   - Put the logic in `scripts/ci/dedupe-push-run.sh` (or `.py`), with a unit test covering: equal tree → skip; different tree → run; API error → run; no PR → run.
   - Inventory which workflows run on `push` (`ci.yml`, `hardening-check.yml`, `iac-plan.yml`; check `deploy.yml`). Apply the skip only to the heavy ones. Keep the cheap ones and the gitleaks job running.
   - Run actionlint and the workflow lint tests.
3. Record the expected saving per merge in the final report: the minutes of the jobs skipped.

Commit: `ci(int037): HARD-001 scan in local CI, post-merge run skipped only when the PR already tested the identical tree (fails open)`.

## P9 — Docs, review, final report, merge through a PR

1. **Docs** (plan Deliverables):
   - `SERVICE_TRUST_INVENTORY.md`, `COD_001_CODE_GENERATION.md`, `SYR_003_SERVICE_REGIMES.md`, `KEYCLOAK_AND_SECRETS.md`, `OPS_VPS_SINGLE_HOST.md` (the Keycloak sync script, the new `.env` keys, and smoke now mandatory);
   - the «الحالة» lines for G-COD-01, G-COD-06, G-IAM-03, G-POL-01, G-MOV-02 and the shipping part of G-SEP-02;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - a scope granted without a call;
   - a client added to a server allow-list without a matrix entry;
   - wiring still written in an acceptance-only file;
   - a code path that still falls back to `localhost` or registers a client without a URL;
   - an archive consumer that can still acknowledge without an item;
   - a readiness check that passes on an empty pack;
   - a secret printed anywhere;
   - a CI skip that can skip untested code.

   Use a refuter only for high findings. Fix what survives, with tests.
3. **Local CI, then the pre-push check, before any push:**
   - `powershell -File scripts/ci-local.ps1` on HEAD, then `powershell -File scripts/plan035-prepush.ps1`. Both must be green.
4. **`reports/plan37-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and any deviations.
   4. The inventory differences from the plan's table.
   5. The matrix summary (services, clients, scopes, calls).
   6. Unit, integration and acceptance results.
   7. The server before and after (the five wiring checks).
   8. Server changes and their undo.
   9. The CI-minutes change and its expected saving.
   10. Deferred items.
   11. Defects for later plans.
   12. **Timing:** total wall-clock; **active time vs owner-wait time**; acceptance, build and deploy times; Actions minutes; and the output of `powershell -File scripts/plan-timing.ps1 -From 25 -To 37`.
   13. A readiness note for PLAN-38, including P-8's contract.
5. **Merge (P-6, P-7):**
   - Merge the feature branch into a new `release/plan-37` from `main` (`--no-ff`).
   - Write `reports/plan37-pr.md` (short).
   - **OWNER ACTION:**
     1. `git push -u origin release/plan-37`
     2. `gh pr create --base main --head release/plan-37 --title "PLAN-37: production wiring and the legal pack" --body-file reports/plan37-pr.md`
     3. `gh pr checks <n> --watch --fail-fast`
     4. Only if step 3 exits 0: `gh pr merge <n> --merge`
   - Verify that `main` = `origin/main`. On the post-merge `push` run, confirm the P8 guard **skipped** the heavy jobs because the tree matched, and record that run's minutes.
   - Record the PR link and the post-merge result in `reports/plan37-progress.md`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- `deploy/wiring/service-wiring.json` exists.
  - `ProductionWiringMatrixTests` is green.
  - `int037-wiring-check.ps1` passes on both production chains, the prod-like stack and k8s (or records that `kubectl` is absent).
- No production code path falls back to `localhost:5010`, and no S2S client is registered without a URL outside `Development`.
- `docker-compose.acceptance.yml` contains no wiring key.
- The seven archive consumers never acknowledge a message without an archive item.
- The legal pack is in the three images, its fingerprint is in their start-up logs, and readiness fails without it.
- AC-INT-37a..h = Pass, with evidence in `acceptance-evidence/AC-INT-37/`.
- **On the demo server:**
  - `smoke -RequireIntegrationWiring` is mandatory and green;
  - the legal-pack fingerprint matches;
  - the realm holds every matrix client;
  - PLAN-36's checks still pass.
- The duplicate post-merge run is skipped only for an identical, already-tested tree, and the guard fails open.
- The final report is merged through a PR **after** green checks, with the timing section.

Then reply with a short summary in Arabic:
- what now works on the demo server that did not before;
- the PR link;
- the path of the final report;
- the Actions minutes saved by the skip on the post-merge run.
