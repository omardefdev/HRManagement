# Task: PLAN-47 — The policy rule lifecycle: real four eyes, validated rules, immutable published versions, effective dating and rollback

The deliverables:
- **Nobody publishes what they wrote or changed.** There is no «Lab» flag in production, and the publisher holds an approval role, not the authoring role.
- **No rule is saved in a form the evaluator does not understand.** No empty condition that matches everything, no swallowed effect, no unknown operator that silently returns `false`, and no number parsed with the machine's culture.
- **A published version never changes:** not from the UI, the seeds or SQL. Every change, seed changes and the retirement of `R-REC-HIRE` included, is a new version with a publication record.
- **A draft starts as a copy of the effective version,** and its rules can be edited and removed. A version can take effect later, and evaluation can run «as of» a date.
- **Rollback** to an earlier version is a four-eyes action that produces a new version linked to the old one.
- **Publishing is one path:** it sends `IPolicyPublished` atomically, and every HR.Core instance clears its cache.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks. It starts only after PLAN-46 is merged and its session has finished (precondition 1).

**Standing owner instruction on merges** (PLAN-38 to PLAN-46 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-14).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan47-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan47-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan47-progress.md`. Its first line is:
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
- Write long outputs (inventories, logs, trx files) under `artifacts/plan47/`. Read only the failing parts.
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

You are a senior .NET engineer for rule engines and data integrity:
- policy engines (templates, versions, rules, publication, snapshots and caches), maker-checker publishing;
- strict JSON parsing, culture-invariant number handling, ASP.NET Core authorization policies and Keycloak roles;
- EF Core interceptors, PostgreSQL triggers, row level security, optimistic concurrency (`xmin`);
- MassTransit and the outbox patterns of PLAN-40, per-instance temporary endpoints;
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..46 did.

**Where things stand** when this prompt was written (2026-11-08):
- **`main`** = `c8252dc6` (PLAN-45). **PLAN-46** is in PR #44. Its first CI run failed, and its session in the main folder is fixing it. When it merges, its merge note will be a commit on a local branch `docs/plan-46-merge-note`.
- **Open side PRs** about the E2E Playwright install: #40 and #42. Never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI since PLAN-66:**
  - a full PR run takes about 45 minutes, and one acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs.
- **What earlier plans built, and this plan builds on:**
  - **PLAN-40:** `PolicyVersionPublisher.PublishDraftAsync` publishes `IPolicyPublished` before its save, in the same transaction.
  - **PLAN-42:** the service-only internal enforce endpoint, which ignores mode and actor from the body; `IPolicyModeResolver`.
  - **PLAN-43:** the workflow trigger allow-list is empty, so a `TriggerWorkflow` rule never starts a registered type, and a published rule needs only its binding checked.
  - **PLAN-46** (when merged) retired `HIRE-WIZARD`, `TRANSFER-APPROVAL`, `IT-TICKET` and `SEPARATION-WIZARD`. The policy rules that fire them stay for PLAN-49. **A rule that triggers a retired code fails this plan's validator** (P-5).
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). PLAN-44 lifted and restored `FORCE` inside the migration's transaction. Tables with a `TenantId` column get forced row security through `TenantRlsMigrationHelper`. This plan adds a trigger that reads a version's status (P-3).
  - Testcontainers connects as a superuser, so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-43, PLAN-44).
  - A persona or role change needs `scripts/acceptance-reset.ps1` before the first start (PLAN-43).
  - A user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`, and `keycloak-reconcile` creates the manifest's missing roles (PLAN-43, PLAN-45).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - An acceptance test posts the URL and the token the page renders (PLAN-44).
  - Run the server build detached (PLAN-44), and run the server suites with nothing heavy beside them (PLAN-45).
- **The demo realm:**
  - `e2e-checker-mgmt` holds both `hr-core` (an author under D-POL-3) and `hr-management-approver` (an approver), so four eyes need a second person;
  - `hradmin` and `e2e-hr-admin` hold `hr-admin`, which is in neither list.

  P-13 adds what the demo needs.
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-47-pol-rule-lifecycle.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report. Its line numbers date from 2026-10-30 and have moved since PLAN-40..46: find the code by its content.
2. The final reports of PLAN-40 (§11, the publisher contract), PLAN-42 (the enforce endpoint), PLAN-43, PLAN-45 and PLAN-46: deviations, deferred items, readiness notes.
3. `docs/POL_001_GAP_STUDY.md` (G-POL-06, G-POL-07, G-POL-15), `docs/ADR-PROD-001a-live-policy-engine.md`, and the `docs/PROD_001_*` documents the plan names.
4. `docs/CI_AND_TESTING.md` (the local policy), `docs/OPS_VPS_SINGLE_HOST.md`, `docs/EVENT_PUBLISHER_INVENTORY.md`.
5. `src/Shared/HRManagement.ServiceDefaults/TenantRlsMigrationHelper.cs` and the `Ten002c*` tests.
6. `docs/OWNER_DECISIONS_REGISTER.md`: D-POL-3, D-POL-10, D-POL-11, D-POL-12.
7. `reports/plan25-final-report.md` §11 (the `R-REC-HIRE` workaround this plan replaces).
8. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **PLAN-46 is merged, and its session has finished:**
   - `git fetch origin`, then `origin/main` holds PR #44's merge;
   - the main folder is on `main`, with no uncommitted change of PLAN-46's except its merge note branch.

   If not, write `BLOCKED: PLAN-46 is not finished; finish it first, then start this prompt again.` and end the turn, changing nothing.
2. **You are in `D:\my projects\HRManagement`, on `main`,** and no other plan session is running.
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

- **D-POL-3, who authors and who publishes:**
  - authoring is the `PolicyAuthor` policy: `admin`, `hr-core`, or the new role `policy-author`;
  - publishing is the `PolicyApprover` policy: `admin`, `hr-management-approver`, or the new role `policy-approver`;
  - nobody publishes what they edited, ever;
  - the roles can change through `PolicyEngine:Roles:Author` and `PolicyEngine:Roles:Approver`, with no code change.
- **D-POL-10, no retroactive effect:** `EffectiveFrom ≥ now − 5 min`. The setting `PolicyEngine:Publish:AllowRetroactive=true` opens it.
- **D-POL-11, system seed changes:** they publish without four eyes, with the kind `SystemSeed`, the change set's id and digest, and they show in the version history. `PolicyEngine:Seeds:RequireApproval=true` turns them into drafts waiting for an approver.
- **D-POL-12, one draft per template:** a filtered unique index. No parallel drafts.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-47-pol-rule-lifecycle` from `origin/main`, in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-46's merge note from `docs/plan-46-merge-note`;<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **Docker hygiene.**<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run (`ci-local.ps1`, Testcontainers suites, a stack), check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*` and Testcontainers.<br>• Never prune volumes or images of other projects. |
| P-3 | **The migration, the trigger, and row security.**<br>• **`POL047_PolicyRuleLifecycle`** (no byte order mark):<br>&nbsp;&nbsp;– `PolicyVersionEdits` and `PolicySeedApplications`;<br>&nbsp;&nbsp;– `PublicationKind` and `EvaluatedAsOf`;<br>&nbsp;&nbsp;– the one-draft index, and `xmin` on `PolicyTemplate`;<br>&nbsp;&nbsp;– the immutability trigger, `migrationBuilder.Sql` guarded by `ActiveProvider == Npgsql`.<br>New columns are nullable or have a database default. No data change in the migration.<br>• **Find out first**, read-only, on the acceptance stack and on the server: which policy tables carry `TenantId`; whether they are under forced row security; how national rows (no tenant) are read today, by the seeder at start-up and by tenant sessions.<br>• **The trigger looks up a version's status.** That lookup must work for the role that performs each write: the app role in a tenant session, at start-up, and for a national template. A lookup that row security hides would refuse every write, or let one through. Prove it with an integration test **as a non-superuser role under forced row security**, for a national template and for a tenant override:<br>&nbsp;&nbsp;– a draft accepts inserts, updates and deletes;<br>&nbsp;&nbsp;– a published version refuses them;<br>&nbsp;&nbsp;– `EffectiveTo`, `UpdatedAt` and `UpdatedBy` stay writable.<br>• **The historic rows** that `DeactivateRuleIfPresentAsync` rewrote before this plan stay as they are. `scripts/pol047-history-report.ps1` lists them (names starting «[معطّلة]») for the final report.<br>• **Rollback safety:** the previous tag must run beside the new schema. **It still rewrites published rows at start-up, and the trigger will refuse that.** Find out what the previous tag does when its seeder is refused (a logged error, or a boot failure), and record it. If it fails to boot, the rollback path is: `scripts/vps/deploy.ps1 -Rollback`, then drop the trigger with the documented SQL, as `*_migrator`. Put that SQL in `OPS_VPS_SINGLE_HOST.md` and in the progress file. |
| P-4 | **Four eyes and the roles (plan §2).**<br>• `PolicyVersionEdits` records every change to a draft: create, add, update, remove, effective date, copy.<br>• The publisher is not among the editors, and holds `PolicyApprover`.<br>• `ForceSameActorForLab` disappears from the API, the controller and the view. The only exception is `PolicyEngine:Publish:AllowSameActorInDevelopment=true` together with `IsDevelopment()`.<br>• **The gateway:** `…/PolicyTemplates/PublishFromUi` and `…/api/policies/templates/{id}/publish` need `PolicyApprover`.<br>• **The roles:**<br>&nbsp;&nbsp;– `policy-author` and `policy-approver` go into `docker/keycloak/realm-manifest.json` with no default holder;<br>&nbsp;&nbsp;– `HrManagementRoles` gets the constants, and `HrManagementRoles.All` stays complete (PLAN-43's test);<br>&nbsp;&nbsp;– `scripts/keycloak/render-realm.ps1`, `Ops039RealmManifestParityTests` and `Ops039RenderRealmTests` stay green.<br>• **Personas** that need the new roles go into `testdata/acceptance/personas.json` **and** `AcceptancePersonaCatalog` together. The plan's scenarios use `central-a` (author) and `a-management-approver` (approver). |
| P-5 | **The validator, and rules that exist today (plan §1, §4).**<br>• `PolicyRuleValidator` and strict parsing apply to **new and changed** rules, to whole versions at publish, and to seed change sets.<br>• **A stored rule that is malformed** never matches (`PolicyConditionGroup.Never`). It is listed in `InvalidRuleKeys`, and logged once per snapshot load.<br>• **Numbers are parsed with the invariant culture.** Test under `ar-SA` and `en-US`.<br>• **PLAN-46's retired codes:** a seeded rule that triggers `HIRE-WIZARD`, `TRANSFER-APPROVAL`, `IT-TICKET` or `SEPARATION-WIZARD` fails the validator. The baseline change set must not add such a rule. Where such rules already sit in an effective version, leave them for PLAN-49: a rollback or a new draft that carries them is refused with the reason. **The boot must never fail on them.** Record which rules these are, on the acceptance data and on the server.<br>• `scripts/pol047-validate.ps1` reports every published rule that fails the validator (a text inventory). Fix none in place: a fix is a new change set. |
| P-6 | **Seed change sets (plan §4).**<br>• `PolicySeedChangeSets` and `PolicySeedChangeSetApplier` replace `SeedIncrementalRulesAsync`, `AddRuleIfMissingAsync`, `DeactivateRuleIfPresentAsync` and `SeedSelectCodePolicyRuleIfMissingAsync`. `SeedAllDomainsAsync` stays, only to create v1 on an empty database.<br>• **The applier runs at start-up inside the tenant** of each template it changes, or as the national seeder does today. Check that it sees the rows under forced row security (P-3): a blind applier would publish a duplicate version on every boot.<br>• **A digest that changed** for an applied change set fails the boot. Prove the tamper case in an integration test. AC-POL-47f's tampered image is optional: record what you chose.<br>• **The consumers** the plan names move to the applier. **AC-HCM-25g changes as the plan says:** `R-REC-HIRE` is absent from the effective `Core.Recruitment` version. This is the only change allowed in AC-HCM-25, and `Hcm025PanelServerParityTests` stays as it is. |
| P-7 | **Publishing, effective dating, rollback (plan §5–§8).**<br>• **`PolicyPublishService` is the only publish path,** for the API, the UI, rollback and seed sets. The event stays inside `PolicyVersionPublisher.PublishDraftAsync` (PLAN-40); the service does not publish it twice. Widen its payload as the plan says, and update `EVENT_PUBLISHER_INVENTORY.md` in the same commit.<br>• **No empty version** is published, except through the explicit «withdraw every rule of the domain» action, with a written reason.<br>• **`AsOfUtc`** is honoured by in-process `EvaluateAsync` and `EnforceAsync`, by `POST api/policies/evaluate` and by the simulation. **PLAN-42's internal endpoint ignores it.** A cache entry lives at most 5 minutes, or until the template's next future `EffectiveFrom`, whichever is sooner.<br>• **Cache invalidation in every instance:** `PolicyPublishedCacheInvalidationConsumer` runs on a temporary endpoint per instance. It is excluded from the inbox receipt filter and from the consumer outbox, as the plan explains. Prove it with two buses in `Pol047PublishOutboxPostgresTests`.<br>• **Two concurrent publishes of one template:** the second gets 409 «نُشر إصدار آخر للتو؛ أعد المحاولة». |
| P-8 | **AC-10 and the other existing tests.** `Ac10EnforcePolicyTests` stops using the removed flag and publishes with two personas: the minimum, since PLAN-65 rewrites it. Update the `Hcm025`, `Hcm027` and `Prod001` tests the plan names to the applier, never by weakening an assertion. |
| P-9 | **The fingerprints.**<br>• `scripts/pol047-policy-fingerprint.ps1` (PS 5.1, ASCII) hashes every published `(VersionId, RuleKey, WhenJson, ThenJson, Priority)`.<br>• The local before-and-after files go to `artifacts/plan47/`, **not** to a committed `reports/` file.<br>• **AC-POL-47e takes its «before» inside the test, before the restarts.** Ids differ on every stack, so a fingerprint from another database proves nothing.<br>• On the server, take a fingerprint before the deploy and after it (P-13). |
| P-10 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• `AcPol47RuleLifecycleTests` goes in `prodLike[]`, `required: true`, in the **isolated** group: AC-POL-47e restarts `hr-core-web`.<br>• The seeded v3 draft of `AcceptanceDataSeeder` and the one-draft index: AC-POL-47a uses that draft or discards it first (the plan's risk table).<br>• `ac-ownership.json` gets `AC-POL-47` (owner PLAN-47, `provesClaim: true`). Update AC-10's entry to what it now proves.<br>• Tests post the URL and the token the page renders.<br>• Copy what the report needs into `artifacts/plan47/`, then restore the committed evidence: local evidence is never committed. |
| P-11 | **Local testing (owner's rule of 2026-11-06).** Locally, run only the acceptance classes this plan touches and their direct regressions, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcPol47RuleLifecycleTests`, `AcHcm25ProductionApprovalTests`, `AcS2s42TrustBoundaryTests`, `AcMsg40AtomicPublishingTests`, `AcWf43DefinitionGovernanceTests`, and the prod-like journey;<br>• **lab:** `Ac09ShadowPolicyTests`, `Ac10EnforcePolicyTests`, `Ac11PolicyWorkflowTests`, and the lab journey;<br>• and any class P0 finds that publishes a policy, or reads a policy decision on a path you change.<br>Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-12 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile ProdLike`, after `scripts/acceptance-reset.ps1` (new roles).<br>• `scripts/sync-keycloak-dev-realm.ps1` changes only if it still exists and still reads the roles. |
| P-13 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan47-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **A fingerprint of the published versions** before the deploy, read-only.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`. `keycloak-reconcile` creates the two roles.<br>• **A demo author:** add one demo user with `policy-author` to the manifest's `demoUsers`, and to the live realm through `scripts/vps/add-demo-user.sh`, with the demo password already in the server's `.env`, never printed. `e2e-checker-mgmt` is the approver. Record the username for the owner's QA guide.<br>• **A harmless publish tour:** the author creates a draft (a copy of the effective version) of one demo template; `e2e-checker-mgmt` publishes it; then the author makes a rollback draft to the previous version, and `e2e-checker-mgmt` publishes that. The rules end as they began, and the history shows both publications. Pick a template whose rules a copy does not change.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn: roll back to PLAN-46's tag as P-3 says. Restore the backup only if data was damaged, prove the demo works, and record the downtime. |
| P-14 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green.<br>2. `gh pr create`; wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>` and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-15 | **No regression of PLAN-25 and PLAN-36..46, and PLAN-66.** These stay green:<br>• AC-HCM-25a..h (25g changed as P-6 says), and `Hcm025PanelServerParityTests` unchanged;<br>• `AcWf43`..`AcWf46`, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, and both recruitment journeys;<br>• `OutboxPublishOrderingArchitectureTests`, `Data001ConcurrencyArchitectureTests` (with `PolicyTemplate`), the `Ten002c` row-security tests, `ProductionWiringMatrixTests`, `S2s042*`, `GatewayPathAuthorizationMiddlewareTests`, the `Ops039` realm tests, `Qa066*`;<br>• `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **Done earlier:** separating the enforcement point, the unified mode, the fail-closed client (PLAN-42); atomic event publishing everywhere (PLAN-40).
- **PLAN-48:** national templates for every agency, the agency ceiling, no `National` scope inside an agency.
- **PLAN-49:**
  - the domain catalog and its capabilities (implementing `IPolicyDomainCapabilities`; this plan adds the extension point with an allow-all default);
  - retiring dead and orphan rules, the four PLAN-46 retired codes included;
  - aligning the facts.
- **PLAN-65:** rewriting AC-09 and AC-10 on the domain services. Here AC-10 changes only as P-8 says.
- **Wave 2:**
  - editing the facts schema from the UI, the structured editor, conflict detection, batch impact simulation (G-POL-13, G-POL-14);
  - `legal_source_id` on the rule (G-POL-12).

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`). That includes the demo password.

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the open side PRs (#40, #42) or their branches;
- disable host-key checking;
- edit a published version in place, on any database, the demo server's included;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.** Precondition 1 has its own line.
- **The immutability trigger cannot see a version's status** under forced row security for a role that must write drafts, and no safe form of the lookup exists (P-3).
- **The seed applier cannot see the effective version** under forced row security, so it would publish on every boot (P-6).
- **AC-HCM-25 would need a change** beyond 25g, or `Hcm025PanelServerParityTests` any change.
- **A server deploy fails,** and neither the rollback of P-3 nor the restore brings the demo back.

---

## P0 — Timing, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions.
2. `git switch -c feature/plan-47-pol-rule-lifecycle origin/main`. Bring in PLAN-46's merge note and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan47-baseline.md`:
   - **0.2:** the build, and `has-pending-model-changes` with the repository's tool manifest;
   - **0.3:** the listed filters;
   - **0.4:** the fingerprint of the acceptance data, into `artifacts/plan47/` (P-9), when you next start the stack.
4. **Inventories:**
   - the policy tables, their `TenantId`, and their row security flags; how the seeder and tenant sessions read national rows (P-3);
   - every writer of a published version, seeds and test helpers included;
   - every published rule that fails the new validator, including rules that trigger PLAN-46's retired codes (P-5);
   - every caller of publish, of the snapshot cache and of evaluate;
   - the gateway rules and policies for the policy pages and APIs;
   - the acceptance classes of P-11;
   - the demo users (P-13).
5. **The server, read-only:**
   - the running tag;
   - the published versions per template, and their fingerprint;
   - the historic «[معطّلة]» rows;
   - the rules that fail the validator;
   - the realm's roles and users that matter here.

Commit: `chore(pol047): baseline, policy tables and row security, writers of published versions, rules the new validator refuses, the server's published versions and their fingerprint`.

## P1 — The validator, and no silent formats (plan §1, P-5)

- **The parsing and the validator:** `PolicyRuleJson` (strict), `PolicyRuleValidator`, and `IPolicyDomainCapabilities` with an allow-all default.
- **The evaluator:** invariant numbers in `WorkflowCondition`, and `LessThanOrEqual`.
- **The mapper:** a malformed stored rule becomes `Never`, is listed in `InvalidRuleKeys`, and is logged once per load.
- **Where validation runs:** on `AddRule` and `UpdateRule` (UI and API), and on the whole version at publish, with `ValidateInvalidPublish` called. The messages are in Arabic and name the JSON path.
- **Tests:** `Pol047RuleValidatorTests` (every rule of §1, under `ar-SA` and `en-US`), and `Pol047MapperNeverMatchTests`.

Commit: `feat(pol047): a policy rule is validated strictly before it is saved, numbers are culture-invariant, and a malformed stored rule matches nothing`.

## P2 — Four eyes, the roles, immutability (plan §2, §3, P-3, P-4)

- **Four eyes and the roles:**
  - `PolicyVersionEdit` and the migration of P-3;
  - the `PolicyAuthor` and `PolicyApprover` policies, and the two roles in the realm manifest;
  - the flag removed, and the gateway rules.
- **Immutability:**
  - `PublishedPolicyImmutabilityInterceptor`, registered beside the audit interceptor;
  - the PostgreSQL trigger;
  - `xmin` on `PolicyTemplate`.
- **Tests:**
  - `Pol047FourEyesTests`;
  - `Pol047ImmutabilityInterceptorTests`;
  - the gateway, policy and realm tests.

Commit: `feat(pol047): nobody publishes a version they edited, publishing needs the approver policy, and a published version cannot change through the application or SQL`.

## P3 — Seed change sets (plan §4, P-5, P-6)

- **The change sets:**
  - `PolicySeedChangeSets`, with `POL-2026-10-30-BASELINE` (without rules that trigger retired codes) and `POL-2026-10-30-RETIRE-R-REC-HIRE`;
  - `PolicySeedChangeSetApplier`, and `PolicySeedApplication`.
- **The old writers:** delete them.
- **The consumers:** move them, AC-HCM-25g included.
- **Scripts:** `scripts/pol047-history-report.ps1` and `scripts/pol047-validate.ps1`.
- **Tests:** `Pol047SeedChangeSetApplierTests` (no repeat, NoOp, retirement, tampered digest).

Commit: `feat(pol047): seed changes become new versions through recorded change sets; R-REC-HIRE is retired by a version, not rewritten in place`.

## P4 — Drafts, effective dating, rollback, one publish path (plan §5–§8, P-7, P-8)

- **Drafts, dating and rollback:**
  - `PolicyDraftService`: copy, update, remove, discard, effective date, rollback draft;
  - the controller and the views (`EditRules`, `Details`).
- **The publish path:**
  - `PolicyPublishService` as the only path;
  - `PublicationKind`, and `AsOfUtc` with `EvaluatedAsOf`;
  - the cache's lifetime at effective boundaries;
  - `PolicyPublishedCacheInvalidationConsumer`.
- **AC-10** as P-8 says.
- **Tests:** `Pol047DraftServiceTests`, `Pol047EffectiveDatingTests` (cache boundaries included), and `Pol047RollbackTests`.

Commit: `feat(pol047): drafts copy, edit and remove rules; versions take effect at a set time and evaluation runs as of a date; rollback is a new four-eyes version; one publish path clears every instance's cache`.

## P5 — PostgreSQL, acceptance, review (plan Verification and Acceptance, P-9, P-10, P-11)

1. **PostgreSQL** (Testcontainers):
   - `Pol047PolicyImmutabilityPostgresTests`, as a non-superuser role under forced row security (P-3);
   - `Pol047ConcurrentPublishPostgresTests`: one wins, the other gets 409;
   - `Pol047PublishOutboxPostgresTests`: the event in the outbox, and two buses both clearing their cache;
   - the applier under forced row security, booted twice: no second version (P-6).

   Then the plan's integration filters.
2. **Acceptance.** Run `scripts/acceptance-reset.ps1`, then start the stack (`acceptance-up.ps1 -Profile ProdLike`) and record the time to ready.
   - Take the local fingerprint (P-9).
   - Run **`AcPol47RuleLifecycleTests`**: AC-POL-47a..g, positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   - Then the P-11 list through the job script with `-Classes`, per profile. All required classes must Pass.

   `down -v`. Restore the committed evidence. Update `ci-scope.json` and `ac-ownership.json` (P-10).
3. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a path that publishes what the publisher edited, or that skips the approver policy;
   - a way to change a published version (an interceptor gap, a trigger blind under row security, a raw SQL path);
   - a rule saved that the evaluator does not understand, or a culture-dependent number;
   - a seed applier that publishes on every boot, or fails the boot on a retired code;
   - a second publish path, or an event published twice;
   - an instance whose cache never clears;
   - `AsOfUtc` honoured by the internal endpoint;
   - something the previous tag cannot run beside, and whether P-3's rollback note covers it.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(pol047): AC-POL-47a..g pass on the prod-like stack; immutability under row security, concurrent publishing, the outbox and cache clearing proved on PostgreSQL; review fixes; CI scope and ownership updated`.

## P6 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(pol047): main merged, conflicts resolved, migrations on top`.

## P7 — The demo server (P-13)

Record every server change and its undo command **as you make it**.

1. Check P-13's condition.
2. Take the backup, then **verify** it. Take the fingerprint of the published versions.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`:
   - `hrm-bootstrap` applies `POL047_PolicyRuleLifecycle` as `*_migrator`;
   - `keycloak-reconcile` creates the two roles;
   - if the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident).

   Record the downtime and the time to all healthy.
4. **The demo author** of P-13, and proof that the user logs in.
5. **Then check**, with evidence in `artifacts/plan47/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them.
   1. **Published versions unchanged:** the fingerprint after the deploy equals the one before, except for new versions. `PolicySeedApplications` holds both change sets with their outcomes. `R-REC-HIRE` is absent from the effective `Core.Recruitment` version and present in an older one.
   2. **A direct `UPDATE`** of a published rule is refused by the trigger. Run it inside a transaction you roll back.
   3. **The harmless publish tour** of P-13. The author cannot publish his own draft. The rules end as they began.
   4. **Rules the validator refuses** on the server are listed (P-5), and the boot logged no failure for them.
   5. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   6. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..46 server checks.
6. **If anything fails and cannot be fixed in the turn:** roll back (P-3, P-13), prove the demo works, and record it.

Commit: `test(pol047): demo server on the policy rule lifecycle (migration and trigger by the bootstrap, roles reconciled, seed change sets applied, published versions unchanged, a four-eyes publish and rollback), PLAN-36..46 server checks still green`.

## P8 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/PROD_001_FOUR_EYES_PUBLISH.md`, `PROD_001_ROLLBACK.md`, `PROD_001_EFFECTIVE_DATING.md`, `PROD_001_VERSIONING.md`;
   - `ADR-PROD-001a-live-policy-engine.md`: its «دورة الحياة» section;
   - `EVENT_PUBLISHER_INVENTORY.md`, if not already done;
   - `OPS_VPS_SINGLE_HOST.md`: the backup location, the change table, and the trigger's rollback SQL (P-3);
   - the «الحالة» lines for G-POL-06 and G-POL-07 in `POL_001_GAP_STUDY.md`;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local CI, then the pre-push check, before any push** (P-2): `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD, then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`. Both must be green.
3. **`reports/plan47-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. The inventories: policy tables and row security, writers of published versions, rules the validator refuses (acceptance and server), the historic «[معطّلة]» rows.
   5. The guards and their own negative tests.
   6. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   7. The server: before and after (fingerprints), changes and their undo, the backup location, the rollback note, **the demo author for the owner's QA guide** (username only).
   8. Deferred items (what PLAN-48 and PLAN-49 must take).
   9. Defects for later plans.
   10. **Timing:**
       - total wall-clock;
       - active time against waiting for CI;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 47`.
   11. **A readiness note** for PLAN-48 and PLAN-49: what they build on (the validator, `IPolicyDomainCapabilities`, change sets, `PolicyPublishService`), and the files they share with this plan.
4. **Merge (P-14), by you:**
   1. Merge the feature branch into a new `release/plan-47` from `main` (`--no-ff`).
   2. Write `reports/plan47-pr.md` (short).
   3. `git push -u origin release/plan-47`
   4. `gh pr create --base main --head release/plan-47 --title "PLAN-47: the policy rule lifecycle (four eyes, validated rules, immutable versions, effective dating, rollback)" --body-file reports/plan47-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan47-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. Commit that note on a local branch `docs/plan-47-merge-note`, as the plans before did, so the next plan brings it in; **do not open a second PR for it.**

## Definition of done

- **Four eyes:** no version is published by someone who edited it, there is no override flag in production, and publishing belongs to the approval role.
- **Valid rules only:** no rule is saved that the validator refuses. A malformed rule stored before this plan matches nothing and is reported.
- **Immutable versions:** no published row changes, as the interceptor, the trigger (under row security) and the fingerprints before and after prove. `AddRuleIfMissingAsync` and `DeactivateRuleIfPresentAsync` are gone from the code.
- **Drafts, dating and rollback:** drafts copy, edit and remove rules; future effect and `asOf` evaluation work; rollback makes a new linked version.
- **Publishing:** one path sends the event atomically, and every HR.Core instance clears its cache.
- **The tests:** AC-POL-47a..g = Pass (the PR run's artifact is the evidence of record). AC-HCM-25g passes as changed, and the PLAN-25 and PLAN-36..46 suites are green.
- **The server:** runs this code with its published versions unchanged, and the demo has an author and an approver for a four-eyes publish.
- **The documents:** the status lines, the PROD-001 documents and the ADR are updated.
- **The merge:** done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server, with the new demo username (no password);
- how long the plan took, and how long it waited for CI;
- the PR link;
- the path of the final report.
