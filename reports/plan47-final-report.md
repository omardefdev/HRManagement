# PLAN-47 — final report: the policy rule lifecycle

Plan: [PLAN-47-pol-rule-lifecycle.md](../docs/acceptance-plans/PLAN-47-pol-rule-lifecycle.md). Prompt: [PLAN-47-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-47-AGENT-PROMPT.md). Progress log: [plan47-progress.md](plan47-progress.md). Baseline: [plan47-baseline.md](plan47-baseline.md).

Started (UTC) 2026-11-08T12:06:33Z, in one session in the main folder, after the session merged PLAN-46 (PR 44) on the owner's instruction.

## 1. Summary per phase

| Phase | What was done |
|-------|---------------|
| P0 | Preconditions. Branch from `origin/main` 49a5f08d. PLAN-46's merge note was cherry-picked, the prompt committed, and four stale doc branches deleted. Baseline: build, no pending model change, unit filter 353/353, integration filter 6/6. Two read-only inventories, of the code and of row security. Server read-only: 20 national templates, each v1 Published; fingerprint of 20 versions and 37 rules. |
| P1 | `PolicyRuleJson` (strict parsing) and `PolicyRuleValidator` with `IPolicyDomainCapabilities` (allow-all). `WorkflowCondition` reads numbers in the invariant culture and gains `LessThanOrEqual`. An empty group now matches nothing. A malformed stored rule becomes `Never` and is listed in `InvalidRuleKeys`, logged once per snapshot load. Validation runs on AddRule and UpdateRule (UI and API) and on the whole version at publish, with `ValidateInvalidPublish`. |
| P2 | `PolicyVersionEdits`, the migration `POL047_PolicyRuleLifecycle` with its triggers, and `xmin` on the template. Policies `Policy.Author`/`Approver`/`Reader` take their roles from configuration. Roles `policy-author` and `policy-approver` added. The flag is gone. Gateway rules added. `PublishedPolicyImmutabilityInterceptor`. `PolicyDraftService` complete, and `PolicyPublishService` as the only path. The seeders that created published versions now write a draft first, then publish. The event payload is widened. |
| P3 | `PolicySeedChangeSets` (BASELINE, RETIRE-R-REC-HIRE), `PolicySeedChangeSetApplier` and `PolicySeedApplications`. The four in-place writers are deleted. Consumers moved, AC-HCM-25g included. Scripts `pol047-history-report` and `pol047-validate`. Two unpinned recruitment bindings added. A start-up inventory logs the published rules the validator refuses. |
| P4 | `AsOfUtc` with `EvaluatedAsOf`. The internal endpoint clears it. The cache holds "now" only, with a TTL bounded by the next `EffectiveFrom`. `PolicyPublishedCacheInvalidationConsumer` runs on a temporary queue per instance, outside quorum, the consumer outbox and receipts. AC-10 publishes with two personas. |
| P5 | PostgreSQL tests: immutability as a non-superuser under FORCE (national and override), concurrent publish 409, applier booted three times, two-bus outbox, review cases. AC-POL-47a..g and the P-11 classes pass on the prod-like and lab stacks. One independent review: 0 High, 3 Medium and 8 Low, 10 fixed. The run found and fixed an S2s42 boot crash. Docs written. |
| P6 | `origin/main` did not move: no merge. |
| P7 | Server: see §7. |
| P8 | Docs, local CI, pre-push, PR, merge: see §6 and §10. |

## 2. Commits

Feature branch `feature/plan-47-pol-rule-lifecycle`:

| Commit | Message |
|--------|---------|
| 76ee9ad4 | docs(wf046): merge note for PR 44 (cherry-picked) |
| 03cefb23 | docs(pol047): the PLAN-47 agent prompt |
| aad51dc6 | chore(pol047): baseline, policy tables and row security, writers of published versions, rules the new validator refuses, the server's published versions and their fingerprint |
| 8cefe2ec | feat(pol047): a policy rule is validated strictly before it is saved, numbers are culture-invariant, and a malformed stored rule matches nothing |
| c927ec1f | feat(pol047): nobody publishes a version they edited, publishing needs the approver policy, and a published version cannot change through the application or SQL |
| 5f74e7a6 | feat(pol047): seed changes become new versions through recorded change sets; R-REC-HIRE is retired by a version, not rewritten in place |
| 7af3d428 | feat(pol047): drafts copy, edit and remove rules; versions take effect at a set time and evaluation runs as of a date; rollback is a new four-eyes version; one publish path clears every instance's cache |
| 4c61a7eb | test(pol047): AC-POL-47a..g pass on the prod-like stack; immutability under row security, concurrent publishing, the outbox and cache clearing proved on PostgreSQL; review fixes; CI scope and ownership updated |
| ae95ec2f | test(pol047): the demo policy author in the realm manifest, and the demo server checks of the policy rule lifecycle |

Later commits (the server phase, this report, the release merge) are listed in the progress log.

## 3. Decisions applied and deviations

**Decisions (as given):**
- **D-POL-3:** author is admin, hr-core or policy-author; approver is admin, hr-management-approver or policy-approver; both come from `PolicyEngine:Roles:*`.
- **D-POL-10:** `EffectiveFrom ≥ now − 5 min`, or `PolicyEngine:Publish:AllowRetroactive=true`.
- **D-POL-11:** SystemSeed publication without four eyes; `PolicyEngine:Seeds:RequireApproval=true` leaves a draft instead.
- **D-POL-12:** a filtered unique index.

**Prompt-level decisions:** applied as written (P-1 to P-15).

**Deviations from the plan, and choices the plan left open:**
1. **Strict parsing and case.** Property names of When/Then match in any case (`"Field"` from serialized models; `PolicyConditionGroupTests` keeps that). Unknown and duplicate names are refused. Effect names match in any case, as before, and never as a number. Operators and field names must match exactly.
2. **Validator context.** It reads schemas, definitions, bindings and code keys through `IgnoreTenantFilterForSystem(PolicyResolution)`, for the session tenant plus the national one. Under forced row security an agency session cannot see the national schema, so every field is refused (fail closed) until PLAN-48's shared national read.
3. **Seed change sets** validate the rules they add or replace. Carried-over rules are published as they were, so PLAN-46's retired triggers never fail the boot. `SeedAllDomainsAsync` creates v1 as a draft, then publishes it.
4. **Two unpinned bindings** are added for `Core.Recruitment`: `RECRUITMENT-SELECTION-COMMITTEE` and `RECRUITMENT-APPEAL-REVIEW`. The validator accepts a `TriggerWorkflow` only for a bound code. This is a server change: two rows.
5. **The agency overlay seeder** (`AgencyPolicySeeder`) is not validated, because its keys fail the key pattern. It writes a draft, then publishes as SystemSeed. Left for PLAN-48.
6. **AcceptanceS2s42PolicyPatch** discards the seeded v3 draft (one draft per template) and takes the next version number, not 4. It crashed `hr-core-web` when AC-POL-47 ran first on the same stack; caught locally (see §6).
7. **Fingerprints** go to `artifacts/plan47/` (P-9), not `reports/`.
8. **AC-POL-47f's tampered image** was not built. The tamper case is proved by `Pol047SeedChangeSetApplierTests` and `Pol047SeedApplierPostgresTests`, and `scripts/pol047-changeset-tamper-check.ps1` was not written.
9. **AC-POL-47e "present in an older record".** It cannot be seen on a fresh stack or on the demo server: neither ever had `R-REC-HIRE`. `Hcm025RecruitmentHireRetiredTests` proves the older version keeps it.
10. **AC-POL-47 is one ordered journey** (a, g, b, c, d, e, f) with a recorder per scenario. It shares the AcMsg40 collection, because both restart `hr-core-web`.
11. **The validator inventory** is a start-up log ("PLAN-47 validator inventory") that `scripts/pol047-validate.ps1` reads. There is no separate database tool.
12. **The UI simulation** stays "the published policy of the domain", with an as-of field.

**Review hardening** (beyond the plan):
- The approver publishes the revision they reviewed: `xmin` on the version, the page and API revision, 409 otherwise.
- Trigger lookups take `FOR SHARE`.
- The applier takes an advisory lock per template.
- An approver who is not an author reviews and publishes from the draft page.
- Author and approver require a GUID user id.
- The validation context is limited to the session tenant plus national.
- Field names must match exactly.
- Copy and rollback work only from Published versions.
- The publication records the checker's real role.
- `evaluate` drops `AsOfUtc` for service accounts, and `AsOfUtc` is normalised to UTC.

## 4. Inventories

**Policy tables and row security.** All eight `Policy*` tables have a NOT NULL `TenantId` and ENABLE + FORCE row security, applied by TEN-002C's hardening at start-up and by the bootstrap. The two new tables get it the same way. National rows carry the PLATFORM tenant.
- The predicate is `TenantId = app.current_tenant`, set at connection open.
- The national seeder runs in the PLATFORM session and sees national rows. A tenant session sees neither national templates nor schemas (PLAN-48).
- The demo server and the default acceptance stacks connect HR.Core as the PostgreSQL superuser. Production and the least-privilege stack connect as `hr_core_app`.
- Details: `artifacts/plan47/inventory-rls.md` (not committed).

**Writers of published versions before PLAN-47:**

| Writer | Kind of write |
|--------|---------------|
| `SeedIncrementalRulesAsync` / `AddRuleIfMissingAsync` | 15 rules into the latest published version |
| `DeactivateRuleIfPresentAsync` | `R-REC-HIRE` in every version |
| `SeedSelectCodePolicyRuleIfMissingAsync` | `R-PRIV-CODE` |
| `SeedAssetsCustodyAsync`, `SeedDomainIfMissingAsync`, `AcceptanceDataSeeder`, `AcceptanceS2s42PolicyPatch`, `AgencyPolicySeeder` | created a published version with its rules in one save |

All of them are gone or changed. Details: `artifacts/plan47/inventory-code.md`.

**Rules the validator refuses** (start-up inventory):

| Rule | Reason |
|------|--------|
| `R-HIRE-START`, `R-SEP-START`, `R-TKT`, `R-TRF` | THEN_WORKFLOW_RETIRED: they fire workflows PLAN-46 retired |
| `R-EST-SUBMIT` | THEN_WORKFLOW_UNBOUND: `ESTABLISHMENT-CHANGE-APPROVAL` has no binding to `Core.Establishment` |

- **Acceptance stack:** these 5 rules.
- **Server:** see §7.
- None is fixed in place. They are left for PLAN-49.

**Historic «[معطّلة]» rows** (the old in-place rewrites):
- acceptance stack: 0;
- server: 0 before and after (the server was seeded after PLAN-25, so no `R-REC-HIRE` row ever existed).

## 5. The guards and their own negative tests

| Guard | Negative test |
|-------|---------------|
| Strict When/Then and the validator (empty, unknown property, unknown operator, field outside the schema or of the wrong case, non-invariant number, empty Then, numeric effect, missing or unknown parameter, unknown, unbound or retired workflow, unknown code key, key, name and priority bounds, duplicate key) | `Pol047RuleValidatorTests` (ar-SA and en-US), AC-POL-47b |
| A malformed stored rule never matches | `Pol047MapperNeverMatchTests` |
| Four eyes: an editor, a non-approver, an unidentified publisher, a draft with no editor; the development-only exception; roles from configuration; no user id | `Pol047FourEyesTests`, AC-POL-47a, 47d |
| Immutability (interceptor) | `Pol047ImmutabilityInterceptorTests` |
| Immutability (trigger): as `hrm_rls_app` under FORCE, national and override; a version the session cannot see is refused; the superuser is bound; `ExecuteUpdate` refused | `Pol047PolicyImmutabilityPostgresTests`, AC-POL-47e, the server check |
| Concurrent publication 409; a draft changed after review 409; one-draft index | `Pol047ConcurrentPublishPostgresTests`, `Pol047DraftServiceTests` |
| Retroactive effect refused; as-of; cache bounded by the next `EffectiveFrom`; ledger as-of; the internal endpoint ignores `AsOfUtc` | `Pol047EffectiveDatingTests`, AC-POL-47c |
| Rollback needs a reason and four eyes, targets a Published version, and is re-validated | `Pol047RollbackTests`, AC-POL-47d |
| Seed change sets: no repeat, NoOp, retirement, tampered digest, an invalid set, retired triggers carried over, RequireApproval, an open draft, two instances at once | `Pol047SeedChangeSetApplierTests`, `Pol047SeedApplierPostgresTests` |
| Every instance clears its cache | `Pol047PublishOutboxPostgresTests` (two buses), AC-POL-47g |
| Gateway mapping | `Pol047FourEyesTests.The_gateway_…` |

## 6. Results

**Unit:**
- full suite 5632/5632 at P5;
- `Pol047*` 97 tests;
- the plan's filters green.

**Integration (Testcontainers):**
- full suite at P5: 551 passed, 4 skipped, 3 failed (`Qa003MinioFileScanTests`, environment only: `HRM_MINIO_IMAGE` not set, as before);
- after the review fixes, `Pol047|Hcm025ProductionApproval|RecruitmentRequisitionPolicy|Msg040|NationalSeed`: 44/44.

**Acceptance (local, P-11 list):**
- **prod-like, run 3 (fresh images):** journey 1/1, main 14/14 (AcHcm25 incl. the changed 25g, AcWf43), AcMsg40 8/8, AcPol47 1/1, AcS2s42 9/9, AcWf46PolicyOrphan 1/1, leastpriv (AcOps38) 8/8; PASS in 1846 s.
- **lab:** Ac10 and Ac11 pass, Ac09 skipped (blocked since PLAN-42), lab journey 1/1; PASS.
- **Run 2's catch:** AcPol47 passed, then the s2s42 overlay crash-looped `hr-core-web`. The patch's fixed version 4 collided with AC-POL-47's versions on the same stack; CI's prodlike job runs the same order. Fixed before run 3.

**Validators:** `pol047-validate`, `hcm025-validate`, `wf043-validate`, `int037-wiring-check` and `lint-compose` (both chains) all PASS.

**Evidence of record:** the PR run's artifact (see §10). No evidence is committed; local evidence stays under `artifacts/plan47/evidence/`.

## 7. The server

**Before:**
- read-only fingerprint: 20 national templates, each v1 Published; 20 versions, 37 rules (P0 and again before the deploy, identical);
- backup `/opt/hrm/backups/pre-plan47-20261108T144522Z` (700; `pg_dumpall.sql`, `env.backup`, `SHA256SUMS`, all 600; 17 `CREATE DATABASE`; `sha256sum -c` ok).

**Build and deploy of `ae95ec2fec82`:**
- one build on the host, about 22 min; the local ssh session reset at 1245 s and the host finished and pushed 17 of 17 images;
- deploy 156 s: pull 2, up 91, bootstrap 42 (the migration `POL047_PolicyRuleLifecycle` as `hr_core_migrator`), keycloak-reconcile 10 (roles `policy-author` and `policy-approver` created), healthy 12;
- current-tag `ae95ec2fec82`, previous-tag `dbf53280121d`; 22 containers, 0 unhealthy; smoke 50 pass, 0 fail, 1 warn.

**Start-up on the server:**
- change sets: BASELINE NoOp on four domains, RETIRE-R-REC-HIRE NoOp (the server never had R-REC-HIRE);
- two unpinned bindings added (`RECRUITMENT-SELECTION-COMMITTEE`, `RECRUITMENT-APPEAL-REVIEW`);
- validator inventory: R-HIRE-START, R-SEP-START, R-TKT, R-TRF (retired workflows), R-EST-SUBMIT (unbound); no unhandled exception.

**Demo user:** `e2e-policy-author` (role `policy-author`, national tenant), added with `scripts/vps/add-demo-user.sh`; the password is the demo password and is not written here. The approver of the tour is `e2e-checker-mgmt`.

**Checks:**
- run 1 lost the network from this machine to the server (7/33); the server stayed healthy and the tour left nothing;
- run 2: 32/33 in 1988 s; `AC_WF_46_vps_the_sweeps_run_and_the_metrics_exist` ran before idcard-web's first sweep cycle (as in PLAN-46) and passed alone after it: 33/33 (AC-OPS-36j excepted as before);
- PLAN-47 schema check: the migration, both triggers, `PolicyVersionEdits` forced, the two realm roles, the change sets recorded, no R-REC-HIRE row, every earlier version unchanged, a direct `UPDATE` (in a rolled-back transaction) refused by the trigger;
- tour on `Analytics.ETL`: the author copies v1 to v2; the author's own publish gets 403; the approver publishes v2 (Manual; maker the author, checker the approver); the author's rollback draft to v1 is published by the approver as v3 (Rollback, linked to v1); the effective rules are v1's.

**After:** fingerprint 22 versions, 39 rules; the 20 earlier versions unchanged (changed 0, missing 0), 2 new (`Analytics.ETL` v2 and v3 of the tour). The demo is up.

**Undo:** `scripts/vps/deploy.ps1 -Rollback`, then, only if the previous tag does not boot, the trigger-drop SQL in `docs/OPS_VPS_SINGLE_HOST.md` as `hr_core_migrator`. The previous tag's seeder writes nothing on this server, so it boots with the triggers in place.

## 8. Deferred items

- **PLAN-48:**
  - national templates and the shared national read under forced row security; until then an agency session's validator refuses every field;
  - `AgencyPolicySeeder` through the validator and the publish service (its pack keys need the key pattern);
  - an agency override per domain.
- **PLAN-49:**
  - retire `R-HIRE-START`, `R-SEP-START`, `R-TKT`, `R-TRF` (retired workflows) and fix `R-EST-SUBMIT`'s binding, each as a change set;
  - implement `IPolicyDomainCapabilities` from the domain catalog.
- **Wave 2:** a facts schema editor, the structured rule editor, conflict detection.
- **PLAN-65:** rewrite AC-09/AC-10 on the domain services. AC-10 now uses two personas.

## 9. Defects for later plans

- `InboxConsumeFilter` lets a per-instance consumer through only because HR.Core registers the consumer outbox. A service without it would dedupe across instances. The review rated this latent.
- `PolicyRuleValidationService` and the orchestrator read national rows past the EF filter but not past row security (PLAN-48).
- The gateway reads `PolicyEngine:Roles:*` from its own configuration. A change must be made in both HR.Core and the gateway (documented in OPS).
- The server check for AC-OPS-36j is excepted as before (P7.5).

## 10. Timing

Stamps are from the progress log (UTC). One session, no resume.

| Phase | Finished (UTC) | Duration |
|-------|----------------|----------|
| P0 | 12:17:16 | 0:10:43 from Started 12:06:33 |
| P1 | 12:40:05 | 0:22:49 |
| P2 | 13:00:26 | 0:20:21 |
| P3 | 13:16:34 | 0:16:08 |
| P4 | 13:24:10 | 0:07:36 |
| P5 (and P6, which had nothing to merge; no stamp of its own) | 14:51:36 | 1:27:26, of which the prod-like acceptance job was 1846 s and the lab job 345 s |
| P7 | 16:17:08 | 1:25:32, of which the build on the host was about 22 min and the two server suite runs 21 and 33 min |
| P8 to the PR | in the progress log | |

Work up to P8: about 4 h 13 min. The wait for the PR's checks (`WAITING-CI:`), the merge commit and the post-merge run are recorded in the progress log's merge note on `docs/plan-47-merge-note`, since they happen after this report is pushed.

`scripts/plan-timing.ps1 -From 25 -To 47` (local time, UTC+3, before the PR) gives PLAN-47 9 commits, first commit 15:06, span 4:12; PLAN-46 15:31 and PLAN-45 6:15 for comparison. The script's Merged column is as it prints it.

## 11. Readiness note for PLAN-48 and PLAN-49

**What they build on:**
- `PolicyRuleValidator` and `PolicyRuleJson`.
- `IPolicyDomainCapabilities`: register a real one in place of `AllowAllPolicyDomainCapabilities`.
- `PolicySeedChangeSets`, append-only. A fix of a stored rule is a new set with a new id, for example `RetireRule` for the PLAN-46 triggers.
- `PolicySeedChangeSetApplier`. It applies to national templates today; agency templates need a tenant-scoped variant.
- `PolicyPublishService` (`PublishAsync`, `PublishSystemSeedAsync`) and `PolicyDraftService`.
- The `PLAN-47 validator inventory` log.

**Files they share with this plan:**
- `HR.Core.Infrastructure/Services/Policy/*` (validation service, draft and publish services, orchestrator, cache);
- `HR.Core.Infrastructure/Data/PolicySeedChangeSets.cs`, `PolicySeedChangeSetApplier.cs`, `PolicyEngineSeeder.cs`, `PolicyEngineDomainSeeds.cs`;
- `HR.Core.Domain/Services/Policy/PolicyRuleValidator.cs`, `IPolicyDomainCapabilities.cs`;
- `HRCoreDbContext.cs` (policy entities);
- `GatewayPathAuthorizationMiddleware.cs`;
- `PolicyEngineRoleAuthorization.cs`.

**What to watch:**
- The one-draft index.
- Seeders must create a draft and then publish.
- The triggers refuse any write to a non-draft version.
