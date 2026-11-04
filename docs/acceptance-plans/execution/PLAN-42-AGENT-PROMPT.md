# Task: PLAN-42 — The trust boundary between services (S2S)

The deliverables:
- **Remote policy enforcement fails closed.** In Enforce, a denial stays a denial and an outage blocks the action with a clear message. Shadow never changes a result.
- **The internal enforce endpoint is for services only.** The server derives mode, tenant and actor; it starts a workflow only for an allowed entity.
- **The gateway proof is an ECDSA signature only the gateway can make,** bound to the user's token and never reusable.
- **Each container sees only its own secrets,** and a placeholder value fails the boot.
- **Scopes say exactly what a caller may do:** erasing is not reading, and only owning services issue official numbers, in the request's tenant.
- **The legal-hold gate is authenticated** and fails closed, at creation and at publication of a separation.
- **The demo server** runs it.

It also lands a small **parallel-readiness** step, so that from PLAN-43 on two plans can run side by side on the owner's machine.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

**There are no owner actions in this plan.** Every decision is taken (see «Decisions»).

**Standing owner instruction on merges** (PLAN-38, PLAN-39, PLAN-40, PLAN-41): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-12).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan42-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan42-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Check the server's state, and the PR's runs, before any server or remote step.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan42-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**. Good uses:
  - the P0 inventories (signers, internal routes, secrets read per service), read-only;
  - root-causing separate failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan42/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Waiting for CI** (PLAN-41 rule). One `Acceptance` run takes about an hour.
- Wait with whatever mechanism this session allows: a background `gh run watch <id> --exit-status` if permitted, or the app's run monitor.
- If the session cannot wait, end the turn with a line that starts with **`WAITING-CI:`** and names the PR, the run id, its URL and the exact next step.
- On the owner's next message (even «اكمل»), read `gh pr checks <n>` **once** and continue from that step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET security engineer:
- ASP.NET Core authentication and authorization (JWT bearer, policies, requirements, middleware order);
- ECDSA P-256 signatures, key formats (PKCS#8, SPKI, PEM), replay protection;
- Keycloak client credentials, scopes and audiences;
- EF Core migrations on PostgreSQL;
- Kubernetes manifests (kustomize, ExternalSecrets) read as text;
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..40 did.

**Where things stand:**
- **The gateway proof** is HMAC over a secret shared by the gateway and 14 modules; services sign with it too.
- **The enforce client** turns a remote denial (422) or an outage into an allow; Shadow changes caller behaviour; the mode is resolved in four places.
- **One shared Kubernetes secret** holds every service's secrets.
- **CI (PLAN-41):** the `Acceptance` job runs both profiles on every PR and is a required check. Every acceptance class must be listed in `tests/acceptance/ci-scope.json`, and every AC needs an entry in `tests/acceptance/ac-ownership.json`. The evidence of record is the PR run's artifact; local evidence is a draft and is **never committed**.
- **The demo server** runs `3d0b2df84f82` (PLAN-40). Its `.env` is generated on the server by `init-secrets` (PLAN-38); the realm is code and `keycloak-reconcile` applies it at every deploy (PLAN-39); start-up checks go through `AddHrStartupCheck<T>()`.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-42-s2s-trust-boundary.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report. Its line numbers date from 2026-10-30 and may have moved; find the code by its content.
2. `reports/plan41-final-report.md`: the readiness note (scope file, ownership file, evidence contract, the job's duration, how groups and isolated classes run).
3. `reports/plan40-final-report.md` §9 and §11 (the publisher contract; `keycloak-reconcile` after a PostgreSQL restart).
4. `reports/plan39-final-report.md` (realm as code, `render-realm.ps1`, reconcile) and `reports/plan38-final-report.md` (`init-secrets`, env parity, the broker matrix).
5. `reports/plan37-final-report.md` (`deploy/wiring/service-wiring.json`, the prod-like profile, `ProductionWiringMatrixTests`) and `reports/plan36-final-report.md` (`ProductionConfigurationGuard`).
6. `docs/OPS_VPS_SINGLE_HOST.md`: build-on-host, deploy, rollback, smoke, backup and restore, the server change table.
7. `docs/OWNER_DECISIONS_REGISTER.md`: D-POL-2 and D-S2S-1..4 are accepted.
8. `docs/SERVICE_TRUST_INVENTORY.md`, `docs/POLICY_ENGINE_UAT.md`, `docs/EVENT_PUBLISHER_INVENTORY.md`, and every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **PLAN-41 is merged with green checks — finish and merge it yourself if it is not** (standing owner instruction). Its release branch is `release/plan-41`, PR #36.
   - **Merged:** continue. If `reports/plan41-progress.md` shows P7 step 7 (the post-merge items) not done, do them as PLAN-41's prompt says, and record them there.
   - **Open:** read its state in `reports/plan41-progress.md`. Wait for the checks as described in «Waiting for CI». Only when every check is green: `gh pr merge 36 --merge`.
   - **A red check:** root-cause it (`gh run view <id> --log-failed`), fix it on `release/plan-41` with a small commit naming the step, rerun that step locally, push the release branch only, and wait again. If the fix needs more than a small change, record `BLOCKED:`.
   - **Never merge with a red or pending check.**
   - **Record** the PR link, check results, the merge commit and whether the post-merge dedupe guard skipped, in `reports/plan41-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`.
2. **`main` is in sync.** `git switch main; git pull --ff-only`; then `git rev-parse main` = `git rev-parse origin/main`.
3. **The working tree is clean** except for these, which may be present:
   - the owner's local `global.json` edit;
   - the untracked `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - this prompt file;
   - the uncommitted `reports/plan41-progress.md` and `reports/plan41-pr.md` notes, which ride with this plan's first commit.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **No other plan session** is running on this working tree or on another clone of this repository. The PLAN-41 session may still be finishing; wait for it, never run beside it.
5. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
6. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire), as the plan's table states:
- **D-POL-2:** `FailClosed` for every domain. `AllowWithAlert` is possible per domain through `PolicyEngine:FailureMode:{Domain}` and is **never** set in Production.
- **D-S2S-1:** ECDSA P-256; the private key lives in the gateway only, and the modules hold the public key. mTLS is not used now.
- **D-S2S-2:** the old HMAC proof is accepted in `Development` only, with `GatewayCallerProof:AllowV1InDevelopment=true`. No transition period in Production.
- **D-S2S-3:** the code-kind owners as the plan lists them; every other kind is HR.Core-internal only.
- **D-S2S-4:** a failed legal-hold check rejects creation and publication with «تعذّر التحقق من الحجز القانوني». No manual override in this plan.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Inventories first, then code.** P0 re-runs the plan's 0.5 inventory, because PLAN-37 added a signer (`WorkflowTriggerService`) and later plans may have added internal routes. It also produces the per-service secret inventory for `docs/S2S_SECRET_MATRIX.md`, from the code (`configuration["…"]`, `GetConnectionString`, options binding), never from guesswork. |
| P-2 | **Key format and generation.**<br>• `.env` and Compose do not carry multi-line values safely. The options accept **either** PEM **or** a single-line base64 of the DER (PKCS#8 private, SPKI public). The environment files use the single-line form: `GATEWAY_PROOF_PRIVATE_KEY_B64` for the gateway only, `GATEWAY_PROOF_PUBLIC_KEY_B64` in the shared anchor.<br>• **Windows PowerShell 5.1 runs on .NET Framework, which cannot export PKCS#8 or SPKI.** `scripts/s2s042-new-gateway-proof-keys.ps1` uses `openssl` when it is on the PATH, otherwise a small `dotnet` tool in the repository (`tools/S2sKeyGen`, net10, no network). It never calls an online service.<br>• On the server, `deploy/vps/init-secrets.sh` generates the pair with `openssl`, **only if missing**, with mode 600, and never prints a key.<br>• Record only the SHA-256 fingerprint of the public key, never a key. |
| P-3 | **Rollback safety on the server.**<br>• Keep `GATEWAY_CALLER_PROOF_SECRET` in the server's `.env` for now. The new code ignores it outside `Development`, and the old tag `3d0b2df84f82` still needs it if you roll back.<br>• The S2S042 migrations add nullable columns, which the old code ignores, so `deploy.ps1 -Tag 3d0b2df84f82` stays safe.<br>• Record the HMAC secret's removal as a deferred item for the next plan that deploys. |
| P-4 | **The stricter placeholder guard must not take the demo down.** In P0, read-only:<br>• list every configuration key whose value is a placeholder (`USE_ENV_OR_SECRET_MANAGER`, `CHANGE_ME…`, `__X__`) in any `appsettings*.json` that Production loads;<br>• for each one, check that the server's environment overrides it (`docker compose config` on the server, **key names only, never values**).<br>A placeholder that is not overridden: if its feature is off, remove the placeholder from the settings file, so that absent means «not configured»; never invent a value. If it is required and the server has no real value, that is a **Stop**. |
| P-5 | **Removing the IDCard policy snapshot** (plan §1):<br>• `PolicySnapshotStore`, `PolicyPublishedConsumer` and their registration go.<br>• `AC-MSG-40c` changes as the plan says: it checks the delivered outbox row in HR.Core.<br>• `EVENT_PUBLISHER_INVENTORY.md` records that `IPolicyPublished` has no consumer until PLAN-47 (PLAN-40 contract).<br>• If `deploy/bootstrap/rabbitmq-users.json` names the IDCard policy queue, remove that name in the same commit; never widen another pattern.<br>• On the server, after the deploy, delete the orphaned queue if it exists, recording its message count first. |
| P-6 | **Service identities in acceptance and CI.**<br>• `ACCEPTANCE_SVC_<CLIENT>_SECRET` values are generated by `scripts/acceptance-local-env.ps1` and by the PLAN-41 job script on the runner, and imported into the acceptance realm's `svc-*` clients.<br>• No secret in the repository or in `ci.yml`.<br>• A missing secret makes the scenario `Blocked` through `AcceptanceGate`, never `Pass`.<br>• `AcceptanceHarness.CreateServiceClientAsync` is the helper later plans reuse. |
| P-7 | **Acceptance placement (PLAN-41 rules).**<br>• `AcS2s42TrustBoundaryTests` goes in `prodLike[]`, `required: true`, in its own **isolated** group: it stops `hr-core-web` and recreates containers with the s2s42 overlay.<br>• If the job script cannot yet apply an overlay to one group, extend it minimally: a group may name extra compose files; they are applied before that group, and the stack is torn down after it. Isolated groups run last. Write that contract into `docs/CI_AND_TESTING.md`.<br>• `Ac11PolicyWorkflowTests` and `Ac13ParallelCodeGenerationTests` stay where D-QA-4 put them (lab, not required), but must pass.<br>• Add `AC-S2S-42` to `ac-ownership.json` (owner PLAN-42, `provesClaim: true`). Update the AC-11 and AC-13 entries to what they now prove: AC-11's real journey stays with PLAN-45, and AC-13's tenants and three kinds with PLAN-48. |
| P-8 | **PowerShell 5.1.** Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block. Start the prod-like stack with `scripts/acceptance-up.ps1 -Profile ProdLike` plus the s2s42 overlay. Build local images one at a time. |
| P-9 | **Kubernetes is text here.** There is no cluster. `S2s042K8sPerServiceSecretsTests` reads the manifests as text. Run `kubectl kustomize deploy/k8s/overlays/production-ha` where `kubectl` exists (the CI runner has it); record its absence locally. `deploy/k8s/base/<svc>/wiring-patch.yaml` and rule 3 of `ProductionWiringMatrixTests` follow the rename to `<svc>-secrets`. |
| P-10 | **Realm and wiring.** The new scope `dsar.subject.erase` enters through `deploy/wiring/service-wiring.json` and `docker/keycloak/realm-manifest.json` only. `render-realm.ps1` and `keycloak-reconcile` pick it up; on the server it applies at deploy. `Ops039RenderRealmTests`, `Ops039RealmManifestParityTests` and `ProductionWiringMatrixTests` stay green. |
| P-11 | **Parallel readiness** (small; the owner wants two plans to run side by side from PLAN-43 on). One `hrm-acceptance` stack, one demo server, and Docker volumes are shared by every clone on this machine. So add:<br>• **`scripts/parallel/hrm-lock.ps1`** (`Acquire`, `Release`, `Status`, `Renew`). Lock files live in `%USERPROFILE%\.hrm-locks\` (`acceptance-stack.lock`, `demo-server.lock`). Each holds the plan, the clone path, the purpose, the acquired time and an expiry (4 h, renewable). An expired lock may be taken over with a note in the progress file. ASCII, PS 5.1.<br>• **`docs/PARALLEL_PLANS.md`**, the protocol:<br>&nbsp;&nbsp;– a second **full clone** (not a worktree: PLAN-41 found gitleaks scans nothing in a worktree), with its hooks installed and the owner's local `global.json` edit copied;<br>&nbsp;&nbsp;– the stack lock for any `acceptance-up.ps1` run, with `down -v` on release and never `-NoBuild` after re-acquiring;<br>&nbsp;&nbsp;– the server lock for every server write;<br>&nbsp;&nbsp;– **deploy only when the running tag is on `origin/main` or on your own branch**; never deploy over, or roll back to, the other plan's unmerged tag;<br>&nbsp;&nbsp;– after the other plan merges: merge `origin/main` into your branch; if both added migrations to the same `DbContext`, remove yours and add it again on top, then `has-pending-model-changes`;<br>&nbsp;&nbsp;– list files both plans append to (`ci-scope.json`, `ac-ownership.json`, `personas.json` with the catalog, `dated-obligations.json`, the decisions register, the publisher inventory): keep both sides;<br>&nbsp;&nbsp;– if branch protection is strict, update the PR after the other merge and wait for the rerun.<br>• **Measure** (P6): `docker info` total memory and a prod-like stack's peak (`docker stats --no-stream` after all healthy). Record whether two stacks would fit (Docker memory ≥ 2 × peak + 4 GB). The protocol uses the stack lock either way.<br>• A unit test of the lock script through `RepoScriptRunner` (second acquire fails, release frees it, an expired lock can be taken over). |
| P-12 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green.<br>2. `gh pr create`; wait («Waiting for CI»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again.<br>5. If branch protection requires the branch to be up to date, update it and wait again.<br>6. Record whether the post-merge dedupe guard skipped.<br>**The evidence of record is the PR run's artifact** (PLAN-41): link it in the final report, and commit no evidence. Never push to `main` directly, never force-push. |
| P-13 | **The demo server.**<br>• Back up first with `pg_dumpall` under `/opt/hrm/backups/pre-plan42-<utc>/` (700/600), verified as before (all 16 `CREATE DATABASE` statements, `SHA256SUMS`).<br>• Generate the missing keys with `init-secrets` (P-2), check P-4, then build **once** on the final P6 code (`build-on-host.ps1`, then `deploy.ps1 -Tag <sha12>`).<br>• The demo stays usable at the end of every turn. If a step fails and cannot be fixed in the turn: `deploy.ps1 -Rollback`, safe by P-3. Restore the backup only if data was damaged, prove the demo works, and record the downtime.<br>• If the deploy stops at `keycloak-reconcile` after PostgreSQL restarts (PLAN-40 incident), run the same deploy command again, as `OPS_VPS_SINGLE_HOST.md` documents. |
| P-14 | **No regression of PLAN-36..41.** These stay green:<br>• `lint-compose.ps1` (both chains), `Ops036ComposeLintTests`, `Ops036`/`Ops038` `ProductionConfigurationGuardTests` (the AC-OPS-36f and AC-OPS-38g negatives change as the plan says: a missing or broken public key makes the container exit);<br>• `ProductionWiringMatrixTests` and `int037-wiring-check.ps1`;<br>• `Ops038EnvParityTests` (every new environment key is in `.env.example`, `deploy/vps/.env.vps.example` and `init-secrets`), `Ops038RabbitPermissionMatrixTests`;<br>• `Ops039RenderRealmTests`, `Ops039RealmManifestParityTests`;<br>• `OutboxPublishOrderingArchitectureTests` and the PLAN-40 publisher contract;<br>• the PLAN-41 baseline validator and the CI scope guard. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-37:** service addresses, scopes and secrets in compose and k8s, the `SVC_*` binding, the legal-hold gate's settings key, `service-regimes.json`, and the prod-like file. Consumed only; fix a missing item minimally, and record it.
- **PLAN-40:** atomic publishing.
- **PLAN-47:** the rule lifecycle.
- **PLAN-48:** national templates and the agency override ceiling.
- **PLAN-49:** the domain catalog, fact and schema alignment, orphan rules, and G-POL-09.
- **PLAN-60, PLAN-61:** the legal-hold call in the new separation approval path, and the single entry point.
- **PLAN-65:** rewriting AC-09 and AC-10 on domain services.
- **Wave 2:** mTLS, automatic key rotation, a shared nonce cache (Redis), and a meaningful Shadow ledger (G-POL-10).

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- any key, secret, token or password — including a generated ECDSA key, even a test one. Test keys are generated at run time.

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- disable host-key checking;
- print, log or copy a private key, or any server secret value;
- rotate an existing server secret (`init-secrets` adds the missing keys only);
- set `AllowWithAlert` or `AllowV1InDevelopment` outside `Development`;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** — including a PLAN-41 check that stays red after a small fix.
- **A required placeholder has no real value on the server** (P-4).
- **A service needs a secret that the per-service split would hand to another service**, or a key cannot be placed without giving one container another's secret.
- **A real caller is refused by the new policies** and the fix would widen a policy beyond its owner list (the wiring matrix says who calls what).
- **A server deploy fails, and neither `-Rollback` nor the restore brings the demo back.**

---

## P0 — Timing, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions; finishing PLAN-41's merge comes first.
2. `git switch -c feature/plan-42-s2s-trust-boundary` from the updated `main`.
   - Commit this prompt file.
   - Commit the PLAN-41 notes: `docs(qa041): PR merge note and post-merge run`.
3. **The plan's baseline** («المرحلة 0», 0.2–0.5) into `reports/plan42-baseline.md`:
   - **0.2:** the build, and `has-pending-model-changes` with the repository's tool manifest (`dotnet tool restore`).
   - **0.3:** the listed filters.
   - **0.4:** what PLAN-37 delivered.
   - **0.5:** every caller of `GatewayCallerProof.Sign` and every `api/internal` route, with its current policy.
4. **The secret inventory** (P-1): per service, the configuration keys and connection strings its code reads. This becomes the matrix in P4.
5. **The server, read-only:**
   - the running tag;
   - the environment key names (never values) and the P-4 placeholder check;
   - whether the IDCard policy queue exists, and its depth;
   - the realm's client scopes for `svc-compliance`.

Commit: `chore(s2s042): baseline, signer and internal-route inventory, per-service secret inventory, server before-picture`.

## P1 — The enforce client fails closed, Shadow changes nothing (plan §1)

- `IPolicyModeResolver` / `PolicyModeResolver`, `PolicyEngineConfigurationValidator`, and `PolicyClientMetrics`.
- `PolicyEnforceClient` rewritten to rules 1–6, with `forwardActingUser: true`.
- `PolicyEnforceResponse` gains `EffectiveMode`, `Degraded`, `FailureReason` and `ShadowDecision`.
- The custody shim for `Assets.Custody` only; a neutral allow for every other domain.
- The four HR.Core mode copies are replaced by the resolver; the request's default mode is `Off`.
- The snapshot store and the IDCard consumer go (P-5).
- `FakePolicyEnforceClient` is extended.
- **Tests:** `S2s042PolicyEnforceClientTests`:
  - a denial with 200;
  - 401, 403, 500 and a timeout under `FailClosed` and under `AllowWithAlert`;
  - Shadow with and without an outage;
  - Off;
  - a mode mismatch between the two sides;
  - an unknown value at boot.

  Plus the plan's regression filters, and `AC-MSG-40c` adjusted.

Commit: `fix(s2s042): the remote enforce client fails closed, Shadow never changes the caller's result, one mode resolver, the IDCard policy snapshot is gone`.

## P2 — The internal enforce endpoint (plan §2)

- `PolicyEnforceInternalController` behind `ServicePolicies.PolicyEnforceS2S`.
- `PolicyDomainOwners` and `PolicyWorkflowTriggerAllowlist`.
- The `enforce` action and the internal route leave `PoliciesController`; `evaluate` stays read-only.
- The `S2S042_PolicyLedgerCaller` migration.
- The reuse rule in `ApprovalStartCoordinator`.
- The gateway rule change.
- `Ac11PolicyWorkflowTests` on a service identity (P-6).
- **Tests:**
  - `S2s042PolicyEnforceInternalTests`: ownership, a spoofed body, the allow-list, `SubjectType`, size, tenant;
  - `S2s042PolicyLedgerCallerPostgresTests`;
  - `has-pending-model-changes`.

Commit: `feat(s2s042): enforce is a service-only internal endpoint; the server derives mode, actor and tenant, starts workflows only for allowed entities, and records the caller`.

## P3 — Gateway proof v2 (plan §3, P-2, P-3)

- `GatewayCallerProof` v2, `GatewayCallerProofOptions`, `GatewayProofReplayCache`, and the middleware: service accounts pass without a proof, users need v2.
- The gateway signs after setting `Authorization`. The five service signers (and `WorkflowTriggerService`) stop signing.
- `scripts/s2s042-new-gateway-proof-keys.ps1` (and `tools/S2sKeyGen` if needed), called from `acceptance-local-env.ps1` and from the CI job.
- Configuration in `appsettings.Production.json` (gateway and 14 modules), `docker-compose.production.yml`, `docker-compose.acceptance.yml`, `docker-compose.vps.yml`, `.env.example`, `deploy/vps/.env.vps.example`, both `init-secrets` scripts, and `ProductionConfigurationGuardOptions.CriticalKeys`.
- The AC-OPS-36f and AC-OPS-38g negatives become «public key missing or broken → the container exits».
- **Tests:** `S2s042GatewayProofV2Tests`:
  - a valid signature;
  - a replay;
  - a token swap;
  - skew;
  - a service account without a proof;
  - v1 refused outside Development;
  - both key formats;
  - the simple 100-request timing.

  Plus `Ops038EnvParityTests` and the guard tests.

Commit: `feat(s2s042): gateway proof v2 is an ECDSA signature only the gateway can make, bound to the token and never replayable; services stop signing`.

## P4 — One secret per service, placeholders fail the boot, DSAR erase scope (plan §4, §5, P-4, P-9, P-10)

- **Secrets:**
  - `docs/S2S_SECRET_MATRIX.md` from the P0 inventory;
  - 15 `ExternalSecret` files and their `kustomization.yaml`;
  - the 15 Deployments and `wiring-patch.yaml` files;
  - `hr-app-secrets` and the shared placeholder are gone.
- **The guard:** `ProductionConfigurationGuard` extended to every key outside `Development`/`Testing`/`Acceptance`, naming the key without its value. Remove any placeholder P-4 found for a feature that is off.
- **DSAR:**
  - `dsar.subject.erase` and `ServicePolicies.DsarSubjectErase` on `AnonymizeCandidate`;
  - the scope in `service-wiring.json` and `realm-manifest.json`.
- **Tests:**
  - `S2s042K8sPerServiceSecretsTests` and `S2s042PlaceholderGuardTests`;
  - `ProductionWiringMatrixTests` (rule 3) and the realm tests.

Commit: `feat(s2s042): every container gets its own secrets, placeholder values fail the boot, erasing a DSAR subject needs its own scope`.

## P5 — Official numbers, the legal-hold gate, internal routes for services only (plan §6, §7, §8)

- **Official numbers:**
  - `generate` only on `api/internal/codes/generate` behind `ServicePolicies.CodeGenerate`;
  - `CodeKindOwners`;
  - the tenant comes from the request context;
  - the `S2S042_CodeIssuanceCaller` migration;
  - `Ac13ParallelCodeGenerationTests` on a service identity.
- **The legal-hold gate:**
  - an authenticated `hr-core-lifecycle` client;
  - `HttpEmployeeLifecycleGate` fails closed;
  - `AssertCanStartSeparationAsync` runs at creation;
  - the pass-through gate is registered only in `Development`/`Testing`, and a missing address elsewhere fails the boot.
- **Internal routes:**
  - `ServicePolicies.HrCoreInternalS2S` on the lifecycle and regime controllers;
  - `S2s042InternalRoutesAreServiceOnlyTests` over every `*.Web` assembly;
  - `AcceptanceHarness.CreateServiceClientAsync`;
  - the `serverPolicy` values in `service-wiring.json`.
- **Tests:**
  - `S2s042CodeGenerationAuthTests`, `S2s042CodeIssuanceTenantPostgresTests`, `S2s042LifecycleGateTests`;
  - the internal-routes guard, with its own negative: a sample controller with a user policy fails it;
  - `has-pending-model-changes`.

Commit: `feat(s2s042): only owning services issue official numbers in the caller's tenant, the legal-hold gate is authenticated and fails closed at creation and publication, every internal route is service-only`.

## P6 — Acceptance on the prod-like stack (plan Acceptance, Verification, P-7)

1. Run every unit, integration and Testcontainers filter in the plan's Verification block (PS 5.1 forms), plus `scripts/s2s042-validate.ps1`.
2. Start the prod-like stack (`acceptance-up.ps1 -Profile ProdLike`) with `docker-compose.acceptance-s2s42.yml`. Record the time to all healthy, and the P-11 memory measurements.
3. Run **`AcS2s42TrustBoundaryTests`**: AC-S2S-42a..i, positive and negative, including `s2s042-placeholder-boot-check.ps1` for 42e and the 42b switch to Shadow. Then rerun **AC-11 and AC-13** on the lab profile.
4. **Regression:**
   - `AcMsg40` (with 40c adjusted), `AcHcm25`, the prod-like journey, `AcInt37ProductionWiringTests`, `AcOps38FirstBootTests`;
   - the PLAN-41 job script on the groups this plan touches: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/acceptance-job.ps1 -Profile ProdLike -Classes <the changed classes>`, so that the isolated group with the overlay is proved before CI.
5. Run with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`; all must Pass.
6. Copy what you need from the evidence into `artifacts/plan42/`, then restore the committed evidence (local evidence is never committed).
7. Update `ci-scope.json` and `ac-ownership.json` (P-7).

Commit: `test(s2s042): AC-S2S-42a..i pass on the prod-like stack, AC-11 and AC-13 on service identities, PLAN-40 and PLAN-41 suites still green, CI scope and ownership updated`.

## P7 — The demo server (P-2, P-3, P-4, P-13)

Record every server change and its undo command **as you make it**.

1. Take the backup, then **verify** it.
2. Run `init-secrets` to add the missing keys only: the gateway proof pair, and any `SVC_*` key the per-service split needs. Record the public key fingerprint and the key names, never a value. Undo: remove the added keys from `.env`, which is safe with the old tag.
3. Re-check P-4 on the server's rendered configuration (names only).
4. Run `build-on-host.ps1` once, on the final P6 code. Deploy with `deploy.ps1 -Tag <sha12>`: `hrm-bootstrap` applies the `S2S042_*` migrations as `*_migrator`, and `keycloak-reconcile` applies the new scope. Record the downtime and the time to all healthy.
5. **Then check**, with evidence in `artifacts/plan42/vps/` (copy what the report cites into the final report, not into `acceptance-evidence/`):
   1. The demo users log in and use the UI through the gateway (a short tour: a leave approval and an ID card).
   2. From inside the network, a direct call to `hr-core-web` with a user token and a v1 HMAC proof is refused (403).
   3. The new image with a placeholder public key exits (the 42e script, run on the server with `docker run --rm`, no network).
   4. `svc-compliance` has the `dsar.subject.erase` scope in the realm.
   5. The orphaned IDCard policy queue is gone (P-5).
   6. The ledger columns and `IssuedByClientId` exist.
   7. Smoke `-RequireIntegrationWiring` is green.
   8. `AcOps36VpsBootTests` (36j excepted), `AcInt37VpsServerTests`, the PLAN-38 server scenario, the PLAN-39 server checks and the PLAN-40 server checks all pass.
6. **If anything fails and cannot be fixed in the turn:** roll back (P-13), prove the demo works, and record it.

Commit: `test(s2s042): demo server on the S2S trust boundary (gateway proof v2 keys added by init-secrets, migrations by the bootstrap, new scope reconciled), PLAN-36..40 server checks still green`.

## P8 — Parallel readiness, docs, review, final report, merge through a PR (by you)

1. **Parallel readiness** (P-11): `scripts/parallel/hrm-lock.ps1` with its test, and `docs/PARALLEL_PLANS.md` with the measured numbers.
2. **Docs:**
   - `docs/ADR-S2S-042-trust-boundary.md`;
   - `docs/S2S_SECRET_MATRIX.md`;
   - `docs/SERVICE_TRUST_INVENTORY.md` and `docs/POLICY_ENGINE_UAT.md`;
   - `OPS_VPS_SINGLE_HOST.md`: the new keys, how `init-secrets` adds them, the backup location, the change table, and the deferred HMAC-secret removal;
   - `docs/CI_AND_TESTING.md`: the group-overlay contract, if you added it;
   - the «الحالة» lines for G-POL-03, G-POL-08, G-IAM-13, G-COD-05 and G-SEP-12;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
3. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - a path where a denial or an outage still becomes an allow in Enforce;
   - a Shadow decision that leaks into the caller's result or effects;
   - a body field (mode, actor, tenant) the server still trusts;
   - a proof that can be made without the gateway's private key, replayed, or moved to another token;
   - a route under `api/internal` that a user can reach;
   - a container that can read another service's secret;
   - a placeholder that still boots;
   - a code issuance a user can trigger, or in another tenant;
   - a legal-hold path that allows on failure;
   - a private key in a file, a log or an artifact.

   Use a refuter only for high findings. Fix what survives, with tests.
4. **Local CI, then the pre-push check, before any push:** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD, then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`. Both must be green.
5. **`reports/plan42-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. The inventories before and after: signers, internal routes and their policies, secrets per service.
   5. The guards and their own negative tests.
   6. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   7. The server: before and after, keys added (names and fingerprint only), changes and their undo, the backup location.
   8. Parallel readiness: memory measured, whether two stacks fit, the lock and the protocol.
   9. Deferred items (including the HMAC secret's removal).
   10. Defects for later plans.
   11. **Timing:**
       - total wall-clock;
       - active time against time waiting for CI, and the number of `WAITING-CI:` stops;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 42`.
   12. **A readiness note for PLAN-43 and PLAN-47,** which the owner will run in parallel:
       - the internal endpoints and policies they build on;
       - `AcceptanceHarness.CreateServiceClientAsync`;
       - the files both will touch (HR.Core migrations, `service-wiring.json`, the realm manifest, the scope and ownership files);
       - the parallel protocol.
6. **Merge (P-12), by you:**
   1. Merge the feature branch into a new `release/plan-42` from `main` (`--no-ff`).
   2. Write `reports/plan42-pr.md` (short).
   3. `git push -u origin release/plan-42`
   4. `gh pr create --base main --head release/plan-42 --title "PLAN-42: the S2S trust boundary" --body-file reports/plan42-pr.md`
   5. Wait for the checks («Waiting for CI»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan42-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- **Enforcement:** no path in the remote client turns a denial or an outage into an allow in Enforce, and Shadow changes neither `Allowed` nor the effects the caller uses.
- **The internal endpoint:** no `enforce` endpoint for an interactive user, and the server ignores mode, actor and tenant from the body.
- **The proof:** no service client signs a gateway proof, and no module holds what makes one.
- **Secrets:** no Deployment references a shared secret, and booting with a placeholder fails.
- **Scopes:** `dsar.subject.read` erases nothing, and no user consumes an official number over HTTP.
- **The legal-hold gate:** authenticated and fail-closed, at creation and publication.
- **Internal routes:** every `api/internal/*` route is service-only, with an architecture guard.
- **The tests:** AC-S2S-42a..i = Pass (the PR run's artifact is the evidence of record), AC-11 and AC-13 pass, and the PLAN-36..41 suites are green.
- **The server:** runs this code with the keys added by `init-secrets`, and the PLAN-36..40 server checks are green.
- **Parallel readiness:** the lock script and the protocol document exist, with the memory measurements.
- **The documents:** the status lines are updated.
- **The merge:** done by you, through a PR, after green checks, with the timing section.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server;
- whether two acceptance stacks fit on the machine, or the plans will take turns on one;
- the PR link;
- the path of the final report.
