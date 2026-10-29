# Cross-plan consistency review, share A

**Scope:** PLAN-34..PLAN-49, plus study back-links in PRG_001, PRG_002, COD_ORG, POL_001, WF_001, FUN_006, FUN_003.
**Reviewer date:** 2026-10-30.
**Method:**
- read all 16 plans in full;
- read the cross-share plans where a contract crosses: PLAN-50, 51, 60 §0.1, 61, 62, 65;
- ran scripted checks: sections, estimates, AC-ID uniqueness across 34..65, relative links, gap-to-plan mapping;
- spot-checked 32 file:line citations against `/home/claude/hrm`.

## Summary of counts

| Check | Found | Fixed | Open |
|-------|-------|-------|------|
| 1. Ownership | 10 | 10 | 1 (PLAN-51, other share) |
| 2. Order | 9 | 9 | 0 |
| 3. Contracts | 14 | 14 | 2 (other share) |
| 4. Format and size | 0 | — | 0 |
| 5. Links | 0 | — | 0 |
| 6. Accuracy | 1 wording issue (the 32 citations were correct) | 1 | 0 |
| 7. Study back-links | 10 gaps missing or incomplete | 10 | 0 |

## 1. Ownership

| # | Plan:line (before fix) | Issue | Fix |
|---|------------------------|-------|-----|
| 1 | PLAN-37 Out of scope | G-MOV-01 pointer said «انظر PLAN-50» inside «خارج هذه الخطة». The wave-1 table says PLAN-37 owns «G-MOV-01», but the packaging gap is G-MOV-02, as the study and the review brief both say. | Pointer set to PLAN-50. The plan correctly covers G-MOV-02. The PLAN_BRIEF table typo is left unedited (see open items). |
| 2 | PLAN-38 §4 and Out of scope | HR.Contracts reference seeds were sent to PLAN-37, which does not cover them. | Moved into PLAN-38, because the change is small: `ContractsDataSeeder` runs mandatory outside `None`. A DoD line was added. |
| 3 | PLAN-34 D-QA-7 / PLAN-38 | PLAN-34 makes PLAN-38 the owner of the dated obligation «ObjectStorage excluded from CI». PLAN-38 never mentioned it. | Added to PLAN-38 D-38-7 and to its DoD: close or renew the obligation by decision. |
| 4 | PLAN-45 Out of scope | Pointed «WorkflowTriggerService migration → PLAN-46». PLAN-46 explicitly sends this to wave 2. | Now «الموجة 2». |
| 5 | PLAN-43 §1 rule 5 and Out of scope | Pointed «PolicyWorkflowBindings management → PLAN-47/49». Neither plan manages bindings. | Now «الموجة 2 (POL)». The note says PLAN-47 only validates that a binding exists. |
| 6 | PLAN-39 Out of scope | Sent bulk Keycloak accounts to PLAN-62, which covers per-hire provisioning only. | Split: per-hire provisioning → PLAN-62; bulk accounts and directory sync → wave 2 (OPS-G). |
| 7 | PLAN-42 §4 | A new `ConfigurationPlaceholderGuard` duplicated PLAN-36's `ProductionConfigurationGuard`. | PLAN-42 now extends the existing guard and creates no second class. Deliverables and risks were updated. |
| 8 | PLAN-44 §3 | Built a second transaction around `ApproveAsync`/`RejectAsync`. PLAN-40 already wraps both in `OutboxUnitOfWork`. | PLAN-44 builds on PLAN-40's wrapper and adds the conflict catch and the fault-injection test. |
| 9 | PLAN-46 §2 | Re-wired scopes and URLs for svc-idcard and svc-compliance directly in Keycloak templates and compose files. PLAN-37's matrix already owns that wiring. | PLAN-46 adds only `calls[]` entries to `service-wiring.json`. |
| 10 | PLAN-49 §1 | Removed the HR.Core enforce-client registration, which PLAN-37 §1 rule 4 already removes. | Now verify-only. Removals also update `service-wiring.json`. |

## 2. Order

All `Depends on` lines list only lower-numbered plans. The issues below were artifacts a plan consumed from a lower-numbered plan without listing it, or later changes that broke ACs a lower-numbered plan had already delivered.

| # | Plan | Artifact | Fix |
|---|------|----------|-----|
| 1 | PLAN-38 | Builds on PLAN-37's `docker-compose.acceptance-prodlike.yml`, but did not list PLAN-37. | PLAN-37 added to Depends on. |
| 2 | PLAN-39 | Uses `service-wiring.json` and the prod-like profile from PLAN-37. | PLAN-37 added to Depends on. |
| 3 | PLAN-44 | Builds on PLAN-40's change to `ApproveAsync`. | PLAN-40 added to Depends on. «من يُدمج ثانياً» removed. |
| 4 | PLAN-45 | Risk row said «PLAN-44 إن سبق». | PLAN-44 always comes first; wording fixed. |
| 5 | PLAN-42 | Modifies PLAN-36's guard, PLAN-38's `init-secrets`, PLAN-39's manifest and PLAN-37's matrix. | Stated in Depends on. |
| 6 | PLAN-43 ↔ PLAN-42 | `PolicyOrchestrator` no longer starts instances for external types. Re-running AC-11 (PLAN-42 version) and the AC-S2S-42c positive would fail. | PLAN-43 now adjusts both, plus `PolicyWorkflowTriggerAllowlist`. Deliverables updated. |
| 7 | PLAN-42 ↔ PLAN-40 | PLAN-42 removes IDCard's `IPolicyPublished` consumer, the only consumer AC-MSG-40c checks. | PLAN-42 retargets AC-MSG-40c to delivery of the outbox row until PLAN-47's consumer exists. |
| 8 | PLAN-42 ↔ PLAN-36/38 | The ECDSA key pair replaces `GATEWAY_CALLER_PROOF_SECRET`, which the PLAN-36 guard, AC-OPS-36f, PLAN-38's `init-secrets` and AC-OPS-38g all use. | PLAN-42 now carries the cross-plan update row. |
| 9 | PLAN-41 → 43/46/48 | `prepare-keycloak-import.ps1:137` hard-codes 61 personas. The persona additions in PLAN-43, 46 and 48 would throw. | PLAN-41 removes the constant. The count is now derived from the catalog. PLAN-43, 46 and 48 persona rows also name `AcceptancePersonaCatalog`. |

No order violation is left that would need renumbering.

## 3. Contracts

| # | Pair | Result |
|---|------|--------|
| 1 | PLAN-46 ↔ PLAN-60 | `IWorkflowApprovalClient`, `WorkflowApprovalStartRequest` (fields and order), the result types, `IWorkflowOutcomeDecided`, `WorkflowStepDecisionDto`, error codes and folders all match. PLAN-46 §5 differences 3–5 are already resolved in PLAN-60; PLAN-46 now says so. |
| 2 | PLAN-43 `EnforceStepRoleEntityTypes` removal | PLAN-50:172 still used the key. Aligned to `Workflow:StepRole:ExemptEntityTypes` (edit in the other share). PLAN-60:95 was already correct. |
| 3 | PLAN-40 guard ↔ PLAN-45 | PLAN-45 0.2 named `Wf040`/`Ops008`, which do not exist. Now `OutboxPublishOrderingArchitectureTests`, `Msg040` and the allowlist. |
| 4 | PLAN-40 guard ↔ PLAN-46 | Rule 8 now names the consumer outbox (`consumerOutbox: true`). Inventory rows for `IWorkflowOutcomeDecided` and `IDisciplinaryActionTaken` added (R4). |
| 5 | PLAN-40 guard ↔ PLAN-47 | `IPolicyPublished` would have been published twice, in `PolicyVersionPublisher` (PLAN-40) and `PolicyPublishService`. Now it is published once, where PLAN-40 put it. |
| 6 | PLAN-40 guard ↔ PLAN-56/63 | Consistent. |
| 7 | PLAN-40 ↔ later plans | Added PLAN-40 §5 rule 3: every new event adds its `EVENT_PUBLISHER_INVENTORY.md` row (R4). |
| 8 | PLAN-47 per-instance cache consumer ↔ PLAN-40 inbox | Receipt key `(MessageId, ConsumerType, TenantId)` is shared in the HR.Core DB, so the second replica would skip invalidation. PLAN-47 now excludes this endpoint from the inbox filter and the consumer outbox, and tests two buses. |
| 9 | PLAN-37 matrix ↔ PLAN-39 | `realm-manifest.json` duplicated the `svc-*` clients, scopes and secrets. Now svc clients come from `service-wiring.json` and the manifest holds roles, audiences and security settings only. The parity test is the "PLAN-39 check" that PLAN-37 rule 5 expects. Client and scope counts are no longer hard-coded; `svc-file-storage` stays only if the matrix keeps it. |
| 10 | PLAN-37 matrix ↔ PLAN-42 | DSAR erase scope goes into the matrix and manifest, not `realm-hr-services.json`, which PLAN-39 deprecates. New `serverPolicy` names go into `calls[]`. k8s `secretKeyRef` moves to `<svc>-secrets`. Matrix test rule 3 follows. `HrCoreInternalS2S` now explicitly allows `svc-payroll`, which calls `payroll-blockers` and would otherwise break payroll. |
| 11 | PLAN-37 matrix ↔ later plans | Added PLAN-37 §1 rule 5: any plan adding or removing S2S calls, scopes or policies updates `service-wiring.json` in the same commit. |
| 12 | PLAN-37 ↔ PLAN-61 | PLAN-61:53 now reads through `ServiceRegimePack` and keeps its `separationRules` counter valid for v2 (edit in the other share). |
| 13 | PLAN-37 ↔ PLAN-41 | PLAN-41 0.1 aligned to `acceptance-up.ps1 -ProdLike`. AC-INT-37g pinned to kind `ServiceTransaction` + `X-Tenant-ID`, so it still passes after PLAN-42. |
| 14 | Roles, validator names and AC lineage | `workflow-admin` (PLAN-43) and `policy-author`/`policy-approver` (PLAN-47) now go to `realm-manifest.json`. PLAN-48 names PLAN-47's `PolicyRuleValidator`/`PolicyPublishService`. AC-11/AC-13 lineage aligned in PLAN-41 and PLAN-65:47/71/81/166 (PLAN-65 is the other share). |

**Personas.** Plans 34..49 add 4 new personas, each defined once and used only in its own plan:
- `p-workflow-admin` and `p-workflow-admin-2` (PLAN-43);
- `a-idcard-2` (PLAN-46);
- `platform-central` (PLAN-48).

Every other persona used in 34..49 already exists in `personas.json`.

## 4. Format and size

All 16 plans have:
- the header;
- Goal and decisions with safe defaults;
- المرحلة 0, In and Out of scope;
- AC tables with Positive and Negative columns;
- Verification, DoD, Deliverables, Risks and Effort.

Estimates range from 5–6 to 10 days; none is above 10. AC IDs are unique across PLAN-34..65 (0 duplicates), and the code matches the table for every plan.

## 5. Links

0 broken relative links in PLAN-34..65 and the 7 studies, after the edits, under the `out/docs` → `hrm/docs` mapping.

## 6. Accuracy

Two citations per plan were checked, 32 in all; all were correct:
- PLAN-34: ProgramGovernanceEvaluator:42, BolaArchitectureTests:21-35, ContractsController:28
- PLAN-35: e2e-journeys.yml:149, sync-dependabot:12
- PLAN-36: compose profiles 100/147/183, gateway Program 102/155-168
- PLAN-37: HrCoreActiveRegimeClient:44-45, ServiceAuthorizationExtensions:74-93
- PLAN-38: MassTransitExtensions:79, init-rabbitmq:30
- PLAN-39: realm template :5/:415, DataProtectionExtensions:24-43
- PLAN-40: MovementsExtendedControllers:1032-1048, PartyAckConsumerBase:30-51
- PLAN-41: AcceptanceGate:76-79, EvidenceRecorder:96-100
- PLAN-42: PolicyEnforceClient:101-107, DsarInternalController:20-21/368-369
- PLAN-43: WorkflowDefinitionsController:14, WorkflowStageGuard:14-34
- PLAN-44: HRCoreDbContext:875-882, ApprovalsController:184-203
- PLAN-45: WorkflowPackSeeder:23-28, AdminLogisticsDbContext:66-68
- PLAN-46: CardRequestsController:123-139, RetiredCodes:81-84
- PLAN-47: AddRule:151-169, WorkflowCondition:28-34
- PLAN-48: CodeGeneratorService:82-89, PublicCareersController:187/315
- PLAN-49: R-LEAVE-MAX:179-182, hasOrgScope:109

One wording fix: PLAN-48 said every acceptance persona belongs to an agency. In fact `platform-admin` and `platform-auditor` sit in PLATFORM; what is missing there is an `hr-core` persona. Corrected.

## 7. Study back-links added

| Study | Gap | Line added or extended |
|-------|-----|------------------------|
| FUN_003 | G-MOV-02 | PLAN-37 |
| FUN_003 | G-MOV-05 | PLAN-40 (partial: publish/flush, guard, replay) |
| FUN_006 | G-ARC-01 | PLAN-37 (partial: wiring, scope, fail-and-retry) |
| FUN_006 | G-ARC-22 | PLAN-40 (partial: Records.Archive outbox) |
| POL_001 | G-POL-15 | PLAN-47 / PLAN-40 / PLAN-42 (partial) |
| WF_001 | G-WF-13 | + PLAN-45 (partial) |
| WF_001 | G-WF-17 | + PLAN-49 and PLAN-60 (partial) |
| PRG_001 | G-QA-01 | + PLAN-34 (partial) |
| PRG_002 | G-OPS-05 | PLAN-38 / PLAN-48 (partial) |
| PRG_002 | G-OPS-12 | PLAN-48 (partial) |

COD_ORG was already complete.

The brief's cross-area cases that fall in these studies are covered: G-MOV-02, G-MOV-05, G-ARC-01, G-POL-01, G-POL-05 and G-COD-05. The IAM, SEP, IDA, ER and LVA cases belong to studies outside this share.

## Edits outside PLAN-34..49 (trivial alignment)

- **PLAN-50:172:** key name changed to `Workflow:StepRole:ExemptEntityTypes`.
- **PLAN-61:53:** the loader now goes through `ServiceRegimePack`.
- **PLAN-65:47, 71, 81, 166:** AC-11 is credited to PLAN-42/45 and AC-13 to PLAN-42/48.

## Open items

1. **PLAN-51:177.** It says «جمهور الرمز لكل خدمة ← PLAN-42 (G-IAM-13)». PLAN-42 binds the gateway proof to the user token; it does not add a per-service audience. Either PLAN-51 takes the audience work or points it to «الموجة 2».
2. **PLAN-62 deliverables (:202).** It adds three new events (`ISeparationClearance*`) without `EVENT_PUBLISHER_INVENTORY.md` rows. PLAN-40's guard (R4) would fail on them. PLAN-63 should be checked for the same.
3. **PLAN_BRIEF wave-1 table, PLAN-37 row.** It lists «G-MOV-01»; it should be G-MOV-02. Not edited.
4. **`svc-file-storage`.** It sits in the dev realm but has no outbound calls. Whether the matrix keeps it is decided at PLAN-37 0.3. PLAN-39 now follows the matrix either way. This is a minor product choice.
