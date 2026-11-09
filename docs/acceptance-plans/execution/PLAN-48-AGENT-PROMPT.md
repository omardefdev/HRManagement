# Task: PLAN-48 — National settings visible to every agency, the agency ceiling, and sound numbering in every tenant

The deliverables:
- **One explicit rule for national visibility.** National settings rows (policies, workflows, code policies, national reference) are readable by every agency and writable only by the national tenant. The rule works in EF **and under forced row security**, through a separate `SELECT` policy.
- **Numbers with their format in every tenant.** No silent fallback without a prefix, and no `-2026-00031`.
- **Code policies that do not collide.** This needs:
  - a validator;
  - the three defective policies corrected in existing databases, with their counters reseeded;
  - a deterministic choice of template;
  - one scope key.
- **An agency inherits the national policy and cannot override it.** No «national» inside an agency, and an agency's rules only add stricter constraints (D-POL-4).
- **AC-13 is rebuilt** as a real, required test of parallel code generation.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks.

**Standing owner instruction on merges** (PLAN-38 to PLAN-47 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-14).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan48-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan48-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan48-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI (`WAITING-CI:`).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the single server build carries its fixes. Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan48/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Waiting for CI:**
- Wait with whatever mechanism this session allows: a background `gh run watch <id> --exit-status` if permitted, or the app's run monitor.
- If the session cannot wait, end the turn with a line that starts with **`WAITING-CI:`** and names the PR, the run id, its URL and the exact next step.
- On the owner's next message (even «اكمل»), check **once** (`gh pr checks <n>`) and continue from that step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET engineer for multi-tenant platforms:
- EF Core global query filters, interceptors, and PostgreSQL row level security (permissive and restrictive policies, `FORCE`, non-owner roles);
- code generation with counters (formats, scope keys, idempotent issuance, concurrency, reseeding);
- policy engines (national templates, tenant overrides, merge order, ceilings);
- MassTransit and the outbox patterns of PLAN-40;
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..47 did.

**Where things stand** when this prompt was written (2026-11-09):
- **`main`** = `61816ed7`. It holds PLAN-46 (PR 44) and PLAN-47 (PR 45).
- **PLAN-47's merge note** is a single commit on the local branch `docs/plan-47-merge-note`, not yet on `main`.
- **The demo server** runs `ae95ec2fec82`: PLAN-47 without its last fix, the antiforgery tokens of the policy lifecycle API. This plan's deploy brings it up to date (PLAN-47's open item).
- **Open side PRs** about the E2E Playwright install: #40 and #42. Never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI since PLAN-66:**
  - a full PR run takes about 45 minutes, and one acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs.
- **From PLAN-47** (its final report, §3, §4, §8, §9, §11):
  - **Row security on policy tables.** All eight `Policy*` tables have a NOT NULL `TenantId` and ENABLE + FORCE row security. National rows carry the PLATFORM tenant, and the predicate is `TenantId = app.current_tenant`.
  - **Who connects how.** The national seeder runs in the PLATFORM session. **The demo server and the default acceptance stacks connect HR.Core as the PostgreSQL superuser; production and the least-privilege stack connect as `hr_core_app`.** So today, in production, an agency session sees neither national templates, nor national schemas, nor (by the same rule) national workflow definitions or reference rows.
  - **The validator fails closed for agencies.** It reads past the EF filter but not past row security, so an agency session's validator refuses every field until this plan.
  - **Left for this plan:**
    - `AgencyPolicySeeder` through the validator and the publish service (its pack keys fail the key pattern);
    - an agency override per domain;
    - an agency-scoped variant of `PolicySeedChangeSetApplier`, if agency templates need change sets.
  - **What this plan builds on:** `PolicyRuleValidator` and `PolicyRuleValidationService`, `PolicyPublishService` (`PublishAsync`, `PublishSystemSeedAsync`), `PolicyDraftService`, append-only `PolicySeedChangeSets`, and the start-up «PLAN-47 validator inventory» log.
  - **Watch:**
    - the one-draft index;
    - seeders must create a draft and then publish;
    - the triggers refuse any write to a version that is not a draft;
    - the gateway reads `PolicyEngine:Roles:*` from its own configuration.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43).
  - **A job or a start-up step that reads several tenants** must walk them from a source outside row security and set `TenantIdFlow` for each (PLAN-44, PLAN-45). `IgnoreTenantFilterForSystem` lifts only the EF filter, never row security.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-43, PLAN-44, PLAN-47).
  - A persona change needs `scripts/acceptance-reset.ps1` before the first start (PLAN-43).
  - A user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh` (PLAN-45).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - An acceptance test posts the URL and the token the page renders (PLAN-44). Policy API writes need the antiforgery token since PLAN-47's last fix (`AcceptanceHarness.CreatePolicyLifecycleClientAsync`).
  - Run the server build detached (PLAN-44), and the server suites with nothing heavy beside them (PLAN-45).
  - The demo seed wrote requisition numbers without moving the counter (PLAN-43 §10): this plan's reseeding should close that class of defect.
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-48-ten-national-agency-scope.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report. Its line numbers date from 2026-10-30 and have moved since PLAN-40..47: find the code by its content.
2. `reports/plan47-final-report.md` in full, and the PLAN-43 to PLAN-46 reports' row-security notes.
3. `docs/COD_ORG_GAP_STUDY.md` (G-COD-02, G-COD-03, G-COD-20) and `docs/POL_001_GAP_STUDY.md` (G-POL-05).
4. `docs/PROD_006_TENANT_RLS.md`, `docs/TENANT_ENTITY_CLASSIFICATION.md`, `docs/TENANT_THREAT_MODEL.md`, `docs/PROD_001_TENANT_OVERRIDE.md`, `docs/COD_001_CODE_GENERATION.md`, `docs/MIG_003_COD_CODE_LINEAGE.md`, `docs/EST_001_ESTABLISHMENT.md`.
5. `src/Shared/HRManagement.ServiceDefaults/TenantRlsMigrationHelper.cs`, `scripts/sql/ten002c-harden-tenant-rls.sql`, `scripts/run-migrations.sh`, `DatabaseMigrationExtensions.cs`, `TenantFilterBypass.cs`, and `CrossTenantRlsIntegrationTests` (the non-owner pattern).
6. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.
7. `docs/OWNER_DECISIONS_REGISTER.md`: D-TEN-1 to D-TEN-10, D-COD-1, D-COD-3, D-COD-8, D-POL-4.
8. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`, on `main`,** and no other plan session is running.
2. **`git fetch origin`**, and `origin/main` holds PLAN-47 (`61816ed7` or later).
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local `global.json` edit;
   - `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - untracked prompt files under `docs/acceptance-plans/execution/`.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
5. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire, «accept the recommended» for every plan decision). The plan's defaults apply as written:

- **D-TEN-1, the national tenant:** `TenantDefaults.DefaultTenantId` (`PLATFORM`), also the trial agency (D-OPS-3). In tables with a scope, «national» in the application layer is `Scope = National` only.
- **D-TEN-2, the shared national tables:** `PolicyTemplates`, `PolicyTemplateVersions`, `PolicyRules`, `PolicyFactsSchemas`, `PolicyWorkflowBindings`, `WorkflowDefinitions`, `CodePolicyTemplates`, `CodePolicyVersions`, `CodePolicyRules`, `ReferenceCatalogEntries`. No table with personal data joins the list.
- **D-TEN-3 (= D-COD-1):** no agency segment in numbers in this wave. One sequence per tenant; a number is unique within its tenant.
- **D-TEN-4 (= D-COD-3):** numbers already issued without a prefix are reported (`scripts/ten048-find-prefixless-codes.ps1`), never rewritten.
- **D-TEN-5 (= D-COD-8):** the requisition number shows the budget year: `REQ-{BudgetYear}-#####`, one sequence per budget year.
- **D-TEN-6:** the funded position counter is per (unit, grade), **without** the budget year. A carried-over position keeps its code.
- **D-TEN-7:** one **derived** cost-centre code per unit, shared by its positions, with no counter; its issuance is recorded once.
- **D-TEN-8 (= D-POL-4), the agency ceiling:**
  - an agency may add `Deny` and stricter effects: `RequireApproval`, `RequireDualApproval`, `BlockUntilChecklist`, `RequirePrivacyMask`, `RequireOriginReceipt`;
  - it may not change a `SetField` for a field the national policy sets;
  - it may not add a `TriggerWorkflow` in a domain where the national policy has one;
  - it may never use `SelectCodePolicy`.
- **D-TEN-9:** `CodeGeneration:MissingPolicy=Refuse` everywhere except `Development` and `Testing`, where `Fallback` uses `FallbackPrefix`, never an empty text.
- **D-TEN-10, who authors national policy:** PLAN-47's `policy-author`, **inside the national tenant only**.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-48-ten-national-agency-scope` from `origin/main`, in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-47's merge note from `docs/plan-47-merge-note`;<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **Docker hygiene.**<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run (`ci-local.ps1`, Testcontainers suites, a stack), check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*` and Testcontainers.<br>• Never prune volumes or images of other projects. |
| P-3 | **The national read under row security (plan §1).**<br>• **The policy:** `national_shared_read` is a PERMISSIVE `FOR SELECT` policy on each table of `NationalTenant.TableNames`, `USING ("TenantId"::text = '<PLATFORM id>')`. Writes stay under `tenant_isolation` alone.<br>• **It is applied on both paths:**<br>&nbsp;&nbsp;– `scripts/run-migrations.sh`, after `harden_tenant_rls`, for `hr-core`;<br>&nbsp;&nbsp;– `DatabaseMigrationExtensions.ApplyTenantRlsHardeningAsync`, for `HRCoreDbContext`.<br>&nbsp;&nbsp;Its check block fails if the policy is missing on a listed table, or present on any other.<br>• **No EF migration changes data here.**<br>• **The three lists are one:** `NationalTenant.TableNames`, the tables of entities marked `INationalShared` in the EF model, and the script's tables. A test proves it.<br>• **Prove it as a non-owner role without `BYPASSRLS`**, on the `CrossTenantRlsIntegrationTests` pattern (`Ten048NationalRlsIntegrationTests`):<br>&nbsp;&nbsp;– agency B reads the national templates, definitions, references and code policies, and not agency A's rows;<br>&nbsp;&nbsp;– an `UPDATE` or `INSERT` on a national row is refused;<br>&nbsp;&nbsp;– `OwnOrNational` works through EF in that session;<br>&nbsp;&nbsp;– **PLAN-47's validator, for an agency session, now accepts the fields of the national schema;**<br>&nbsp;&nbsp;– **PLAN-47's immutability triggers** still refuse writes to a non-draft version in that session.<br>• **The least-privilege stack** connects HR.Core as `hr_core_app` under forced row security. If its stage can carry one more class, add a short one there that proves the national read through the running services: agency B's snapshot holds the national rules, and an agency user's start finds a national definition. If it cannot, the integration test is the proof; record why. |
| P-4 | **One read path, and the write guard.**<br>• **The bypasses:** P0 recounts every `SystemTenantBypassPurpose.PolicyResolution` and `NationalReferenceLookup` use (the plan counted 14 in 10 files; there are more since PLAN-43..47, PLAN-47's validator included). Every read of a national table goes through `OwnOrNational` or `NationalOnly`, with «my agency first». The architecture test forbids a general bypass on an `INationalShared` entity.<br>• **`NationalSharedWriteGuard`** refuses an `Added`, `Modified` or `Deleted` national row from a session that is not national and not in an explicit seed scope (`TenantStampScope`). It must not refuse PLAN-47's seeders, which run in the PLATFORM session.<br>• **What stays out of scope** (OPS-D, wave 2): background jobs and `/careers` under row security. PLAN-47 found that production already connects as `hr_core_app`, which the plan did not assume. List in the final report every job and anonymous path that reads a tenant table with no tenant set, for OPS-D. |
| P-5 | **Code policies, the upgrade, and the reseeding (plan §2, §3).**<br>• `CodeFormat`, `CodeScopeKey`, `CodePolicyValidator`, the derived cost centre, `MissingPolicy`, and the 422 and 409 translations, as the plan says.<br>• **`CodePolicyUpgrades` runs once at start-up,** through `NationalSeedOrchestrator.ApplyIncrementalPatchesAsync`, recorded in `SeedPackApplications` as `ten048-code-policy-upgrade-v1`.<br>• **`CodeCounterReseeder` walks every tenant** from a source outside row security (the `Tenants` platform table, as PLAN-44's job does), with `TenantIdFlow` set before each scope opens a connection. **It never trusts `IgnoreTenantFilterForSystem` to see other tenants.**<br>• **The counters:** a counter is never lowered (`GREATEST`). An unparsed code is reported, never guessed.<br>• **Prove the upgrade and the reseeding as a non-owner role under forced row security,** in `Ten048CodePolicyUpgradeIntegrationTests`, with issued codes in **two tenants**, and a second run that changes nothing.<br>• **Rollback safety:** the scope key changes. Find out what the previous tag does with the new counters and the v2 policies (a collision, or a different counter row), and record it. If a rollback would collide, say so in `OPS_VPS_SINGLE_HOST.md`: after this deploy, roll forward. |
| P-6 | **The agency ceiling (plan §4).**<br>• **`PolicyCeiling` applies twice:**<br>&nbsp;&nbsp;– at publish, it refuses, inside PLAN-47's `PolicyPublishService` and `PolicyRuleValidator`;<br>&nbsp;&nbsp;– at evaluation, it drops the effect with an explanation.<br>• **`PolicyRuleDefinition.Origin`** defaults to `National`, compatible with the stored JSON.<br>• **Agency seeding:** `AgencyProvisioningService.ApplyPackAsync` and `AgencyPolicySeeder` add every rule of a pack, then validate (rules and ceiling), then publish through `PolicyPublishService` as a system seed, draft first. Pack keys that fail the key pattern are renamed in the pack data, and the rename is recorded.<br>• **Outside the national tenant,** `CreateDraft` makes a `TenantOverride` draft, and a request for `Scope=National` gets 403. Inside it, D-TEN-10.<br>• **Before switching the ceiling on,** list the agency overlays it changes into `reports/plan48-ceiling-impact.md`, from the acceptance data and from the server. |
| P-7 | **The persona and the tests.**<br>• `platform-central` (`hr-core`, PLATFORM) goes into `testdata/acceptance/personas.json` **and** `AcceptancePersonaCatalog`, and its realm entry. Run `scripts/acceptance-reset.ps1` before the first start.<br>• **AC-13 is rebuilt** as the plan's §5 says: required, 120 parallel generations through the service path (a service token and the tenant header, PLAN-42), real `db-checks.json`.<br>• **The fake funded-position service** is replaced by the real one in `Est002`, `Est005` and `Est024` where numbering is part of what they test. |
| P-8 | **Existing tests that the new rules change.** A test that relied on a silent fallback, a calendar-year requisition number, or a national rule overridden by an agency changes to the new rule. Never weaken an assertion. **AC-HCM-25's assertions and `Hcm025PanelServerParityTests` do not change.** If a requisition number format appears in an AC-HCM-25 assertion, the format is test data, not the claim: record any such change. |
| P-9 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• `AcTen48NationalScopeTests` goes in `prodLike[]`, `required: true`. Its group is `main`, unless it stops or recreates containers.<br>• `Ac13ParallelCodeGenerationTests` becomes `required: true` where it runs.<br>• `ac-ownership.json`: `AC-TEN-48` and `AC-13` (owner PLAN-48, `provesClaim: true`).<br>• `scripts/ten048-find-prefixless-codes.ps1` writes to `artifacts/ten048/`.<br>• Copy what the report needs into `artifacts/plan48/`, then restore the committed evidence: local evidence is never committed. |
| P-10 | **Local testing (owner's rule of 2026-11-06).** This plan changes the read path of most HR.Core features, so its list is long. Run it with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcTen48NationalScopeTests`, `AcPol47RuleLifecycleTests`, `AcHcm25ProductionApprovalTests`, `AcWf43DefinitionGovernanceTests`, `AcWf44InstanceIntegrityTests`, `AcWf45AssetApprovalLoopTests`, `AcWf46CrossServiceApprovalTests`, `AcS2s42TrustBoundaryTests`, and the prod-like journey;<br>• **lab:** `Ac13ParallelCodeGenerationTests`, `Ac10EnforcePolicyTests`, `Ac11PolicyWorkflowTests`, `Ac32LogisticsScopeLiveTests`, `Ac33AssetLossLiveTests`, the `AcEst1*` and `AcEst24` classes, `AcHcm21RequisitionApprovalUiTests`, `AcHcm30CandidateExperienceTests`, `AcWf23WorkInboxUiTests`, and the lab journey;<br>• **the least-privilege stage,** if P-3 adds a class there;<br>• and any class P0 finds that reads a national table or generates a code.<br>Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-11 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile ProdLike`. |
| P-12 | **The demo server facts this plan must respect.**<br>• It connects HR.Core as the superuser, so row security is not enforced there: the national read cannot be shown on the demo, only proved in the tests (P-3).<br>• Its numbering is real data: the upgrade and the reseeding run on it, and must not collide or lower a counter.<br>• P0 lists its tenants, to know whether an agency exists there. |
| P-13 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan48-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** the prefixless codes report, the counters with the highest issued code per scope, and the published code policies.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn: roll back to PLAN-47's tag, unless P-5 found that a rollback collides; then fix forward. Restore the backup only if data was damaged, prove the demo works, and record the downtime. |
| P-14 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green.<br>2. `gh pr create`; wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>` and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-15 | **No regression of PLAN-25 and PLAN-36..47, and PLAN-66.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests` (P-8);<br>• `AcPol47`, `AcWf43`..`AcWf46`, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, and both recruitment journeys;<br>• the `Ten002c` row-security tests, `CrossTenantRlsIntegrationTests`, `CodeGenerationConcurrency` tests, the PLAN-47 immutability tests, `OutboxPublishOrderingArchitectureTests`, `Data001ConcurrencyArchitectureTests`, `ProductionWiringMatrixTests`, `S2s042*`, `GatewayPathAuthorizationMiddlewareTests`, `Qa066*`;<br>• `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/pol047-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **Wave 2:**
  - **OPS-D:** running every app as `*_app` with forced row security, and jobs and `/careers` under it. P-4 lists what OPS-D must take.
  - **COD-C:** linking an issuance to its entity and revoking it (G-COD-04); the unified decision record (G-COD-07).
  - **COD-H:** the agency segment in numbers, the policy editing screen (G-COD-17), the time zone and code length (G-COD-19).
  - **COD-F:** merging national reference with agency copies (G-COD-14). Here only the read path changes; the either/or behaviour stays.
  - **POL-D:** a traceable decision ledger and a meaningful Shadow (G-POL-10).
  - **COD-G:** AC-14 and the AC-EST evidence (the rest of G-COD-20).
- **Done earlier:** ignoring `TenantId` from the body of `codes/generate` and limiting it to service accounts (PLAN-42); the author and publisher roles, four eyes, rule validation (PLAN-47).
- **PLAN-49:** what the engine governs, and removing orphans.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the open side PRs (#40, #42) or their branches;
- disable host-key checking;
- rewrite an issued code, or lower a counter, on any database;
- add a table with personal data to the national list;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **The national read policy cannot be written** so that agency sessions read national rows and never write them, under forced row security (P-3).
- **The reseeder cannot see every tenant** without new trust between services (P-5).
- **An existing code cannot be parsed** by the new format, and its counter would collide.
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning (P-8).
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions.
2. `git switch -c feature/plan-48-ten-national-agency-scope origin/main`. Bring in PLAN-47's merge note and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan48-baseline.md`:
   - **0.2:** the build, and the listed filters (Testcontainers included);
   - **0.3:** the real names from PLAN-37, PLAN-42 and PLAN-47 that this plan uses;
   - **0.4:** every bypass use (P-4);
   - **0.5:** prefixless codes on the acceptance data, when you next start the stack;
   - **0.6:** the published code policies of the national tenant.
4. **Inventories:**
   - the ten tables: their `TenantId`, their row security, and their national rows;
   - every reader of a national table;
   - every writer of a national row (seeders, admin pages, provisioning);
   - every code generation call and its kind and key;
   - the agency overlays in the seed packs and what the ceiling would change;
   - the jobs and anonymous paths that read tenant tables with no tenant (P-4);
   - the acceptance classes of P-10.
5. **The server, read-only:**
   - the running tag;
   - its tenants;
   - the prefixless codes;
   - the counters and the highest issued code per scope;
   - the published code policies and policy templates;
   - the overlays the ceiling would change.

Commit: `chore(ten048): baseline, national tables and row security, readers and writers, code generation calls, overlays under the ceiling, the server's codes, counters and tenants`.

## P1 — The national read: EF and row security (plan §1, P-3, P-4)

- **The markers and the read path:**
  - `INationalShared` and `NationalTenant`;
  - `NationalScopeQueryExtensions` (`OwnOrNational`, `NationalOnly`) and `SystemTenantBypassPurpose.NationalSharedRead`;
  - every bypass replaced.
- **The write guard:** `NationalSharedWriteGuard`.
- **The policy:**
  - `TenantRlsMigrationHelper.NationalSharedReadSql` and `scripts/sql/ten048-national-shared-read.sql`;
  - applied on both paths, with its check block.
- **Tests:**
  - `NationalSharedArchitectureTests`: no general bypass on a national entity, and the three lists equal;
  - `Ten048NationalRlsIntegrationTests` (P-3).

Commit: `feat(ten048): national settings are read through one path in EF and through a select-only policy under forced row security; only the national tenant writes them`.

## P2 — Code policies for every tenant, and no silent fallback (plan §2)

- **Resolution and missing policies:**
  - `ResolveSnapshotAsync` through `OwnOrNational`, with the plan's order and `PolicyKey`;
  - the overlay `default` flag, and `code-ministry-finance-v1.json`;
  - `CodeGenerationOptions` (`MissingPolicy`), and the 422 translation.
- **The callers:**
  - `/careers` with the posting's tenant;
  - the snapshot cache key with `PolicyKey`;
  - `/CodePolicies` with the «وطني (موروث — للقراءة)» badge.
- **Scripts:** `scripts/ten048-find-prefixless-codes.ps1`.
- **Tests:** `Ten048ResolutionTests` (overlay before national, `PolicyKey`, `MissingPolicy`).

Commit: `feat(ten048): every tenant sees the national code policies; a missing policy is an explicit error, never a number without a prefix`.

## P3 — The validator, the corrected policies, one scope key (plan §3, P-5)

- **The model:** `CodeFormat`, `CodeScopeKey`, `CodePolicyValidator`, and the derived cost centre.
- **The policies:** the corrected policies in `CodeGenerationSeeder`, and `CodePolicyUpgrades`.
- **The counters:**
  - `CodeCounterReseeder`, walking every tenant;
  - `CodSequenceBootstrapService` rebuilt on the same format and key;
  - the collision error (409).
- **Tests:**
  - `CodePolicyValidatorTests`, `CodeFormatTests`, `CodeScopeKeyTests`, `Ten048DerivedCostCenterTests`;
  - the real funded-position service where numbering is tested (P-7);
  - `Ten048CodePolicyUpgradeIntegrationTests` and `Ten048FundedPositionRealCodeServiceTests` (PostgreSQL).

Commit: `feat(ten048): code policies are validated, the three defective ones corrected with counters reseeded per tenant and never lowered, and one scope key serves the generator and the bootstrap`.

## P4 — The agency inherits and cannot override (plan §4, P-6)

- **Loading and merging:**
  - `PolicyOrchestrator` loads the national templates through `NationalOnly`, and the schema through `OwnOrNational`;
  - `PolicyRuleDefinition.Origin`, and `PolicyCeiling` in `PolicyEvaluator`;
  - the ceiling at publish, in PLAN-47's validator and publish service.
- **Who writes what:**
  - `CreateDraft` scope rules, in the UI and the API;
  - agency seeding through the validator, the ceiling and the publish service, with pack keys renamed if needed.
- **The report:** `reports/plan48-ceiling-impact.md`.
- **Tests:** `PolicyCeilingTests`, `Ten048CreateDraftScopeTests`, `Ten048OverlayProvisioningTests`.

Commit: `feat(ten048): an agency inherits the national policy and only adds stricter rules; the ceiling refuses at publish and drops at evaluation with an explanation; no national template inside an agency`.

## P5 — Acceptance, then the independent review (plan Acceptance, P-7, P-9, P-10)

1. **The persona and AC-13** (P-7).
2. **Acceptance.** Run `scripts/acceptance-reset.ps1`, then start the stack (`acceptance-up.ps1 -Profile ProdLike`) and record the time to ready.
   - Run **`AcTen48NationalScopeTests`** (AC-TEN-48a..g) and the rebuilt **AC-13** (AC-TEN-48h), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   - Run the prefixless codes report after the run: zero new codes starting with `-`.
   - Then the P-10 list through the job script with `-Classes`, per profile, the least-privilege stage included if P-3 added a class. All required classes must Pass.

   `down -v`. Restore the committed evidence. Update `ci-scope.json` and `ac-ownership.json` (P-9).
3. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - an agency session that writes a national row, or reads another agency's rows, through the new policy;
   - a national read left on a general bypass;
   - the national list carrying personal data;
   - a step that reads several tenants without `TenantIdFlow`;
   - a counter that can go down, or a code that can collide after the upgrade;
   - a silent fallback left anywhere;
   - an agency rule that weakens a national decision;
   - `Scope=National` created inside an agency;
   - something the previous tag cannot run beside, and whether P-5's rollback note covers it.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(ten048): AC-TEN-48a..h and AC-13 pass on the prod-like stack; the national read and the upgrade proved as a non-owner role under forced row security; review fixes; CI scope and ownership updated`.

## P6 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(ten048): main merged, conflicts resolved, migrations on top`.

## P7 — The demo server (P-12, P-13)

Record every server change and its undo command **as you make it**.

1. Check P-13's condition.
2. Take the backup, then **verify** it. Take the read-only before-picture of P-13.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`:
   - the bootstrap applies the national read policy;
   - start-up applies `ten048-code-policy-upgrade-v1` and reseeds;
   - if the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident).

   Record the downtime and the time to all healthy. This deploy also brings PLAN-47's antiforgery fix to the server.
4. **Then check**, with evidence in `artifacts/plan48/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them.
   1. `national_shared_read` exists on exactly the listed tables (`pg_policies`).
   2. `SeedPackApplications` holds `ten048-code-policy-upgrade-v1`. The corrected code policies are published as v2, and v1 is archived.
   3. **No counter went down:** every counter is at least the highest issued code of its scope, compared with the before-picture. The unparsed codes report is recorded.
   4. **New codes on the demo:**
      - a requisition gets `REQ-<budget year>-#####`;
      - two funded positions in one unit share one cost centre;
      - the prefixless report shows nothing new since the deploy.
   5. `/CodePolicies` shows the inherited national templates.
   6. The ceiling's impact on the server's overlays matches `reports/plan48-ceiling-impact.md`.
   7. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   8. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..47 server checks.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-13 says, prove the demo works, and record it.

Commit: `test(ten048): demo server on national visibility and sound numbering (the read policy by the bootstrap, code policies upgraded, counters reseeded and never lowered), PLAN-36..47 server checks still green`.

## P8 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `COD_001_CODE_GENERATION.md`: the order, `MissingPolicy`, the validator, derived kinds, the full list of kinds;
   - `TENANT_ENTITY_CLASSIFICATION.md`: the «national shared» class;
   - `PROD_006_TENANT_RLS.md`: `national_shared_read`;
   - `PROD_001_TENANT_OVERRIDE.md`: the ceiling;
   - `MIG_003_COD_CODE_LINEAGE.md`: the unified bootstrap;
   - `EST_001_ESTABLISHMENT.md`: D-TEN-6 and D-TEN-7;
   - `TENANT_THREAT_MODEL.md`: the trial agency inside the national tenant (the plan's first risk);
   - `OPS_VPS_SINGLE_HOST.md`: the backup location, the change table, the rollback note of P-5;
   - the «الحالة» lines for G-COD-02, G-COD-03 and the AC-13 part of G-COD-20, and for G-POL-05;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local CI, then the pre-push check, before any push** (P-2): `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD, then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`. Both must be green.
3. **`reports/plan48-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. The inventories: national tables and row security, readers and writers, code generation calls, the ceiling's impact, prefixless codes (acceptance and server).
   5. The guards and their own negative tests.
   6. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   7. The server: before and after (counters, policies, codes), changes and their undo, the backup location, the rollback note.
   8. **What OPS-D must take** (P-4): every job and anonymous path that reads a tenant table with no tenant, now that production connects as `hr_core_app`.
   9. Deferred items.
   10. Defects for later plans.
   11. **Timing:**
       - total wall-clock;
       - active time against waiting for CI;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 48`.
   12. **A readiness note** for PLAN-49: what it builds on (`OwnOrNational`, the national read, the ceiling, the validator with national fields now visible to agencies, agency seeding through the publish service), and the files it shares with this plan.
4. **Merge (P-14), by you:**
   1. Merge the feature branch into a new `release/plan-48` from `main` (`--no-ff`).
   2. Write `reports/plan48-pr.md` (short).
   3. `git push -u origin release/plan-48`
   4. `gh pr create --base main --head release/plan-48 --title "PLAN-48: national settings for every agency, the agency ceiling, and sound numbering in every tenant" --body-file reports/plan48-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan48-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. Commit that note on a local branch `docs/plan-48-merge-note`, as the plans before did, so the next plan brings it in; **do not open a second PR for it.**

## Definition of done

- **One read path:** no code reads a national table through a general bypass. `OwnOrNational` and `NationalOnly` are the only path, and `national_shared_read` is applied and checked on both migration paths.
- **Proved under row security:** `Ten048NationalRlsIntegrationTests` is green as a non-owner role under forced row security, and PLAN-47's validator accepts national fields for an agency.
- **Numbering:** every seeded code policy passes the validator; existing databases were upgraded without collisions, and no counter went down. No new number lacks its prefix, in an agency or in the national tenant.
- **The ceiling:** an agency with no override receives the national decision; no agency rule weakens it; no agency authors «national».
- **The tests:** AC-TEN-48a..h and AC-13 = Pass (the PR run's artifact is the evidence of record), and the PLAN-25 and PLAN-36..47 suites are green.
- **The server:** runs this code, with its counters intact and PLAN-47's last fix deployed.
- **The documents:** the status lines and the documents P8 lists are updated, with what OPS-D must take.
- **The merge:** done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server (counters, code policies);
- what OPS-D must take, in two lines;
- how long the plan took, and how long it waited for CI;
- the PR link;
- the path of the final report.
