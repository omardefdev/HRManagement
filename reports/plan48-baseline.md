# PLAN-48 — baseline (P0)

Plan: [PLAN-48-ten-national-agency-scope.md](../docs/acceptance-plans/PLAN-48-ten-national-agency-scope.md). Prompt: [PLAN-48-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-48-AGENT-PROMPT.md). Progress: [plan48-progress.md](plan48-progress.md).

Branch `feature/plan-48-ten-national-agency-scope` from `origin/main` 61816ed7 (PLAN-47, PR 45). The full inventories are in
`artifacts/plan48/` (not committed): `inventory-national-readers-writers.md`, `inventory-codegen-overlays-jobs.md`,
`baseline-acceptance-*.txt`, `server-p0*.txt`.

## 0.2 Build and tests

| Step | Result | Time |
|------|--------|------|
| `dotnet build HRManagement.slnx -warnaserror` | 0 errors, 0 warnings | 61 s |
| `dotnet test src/Tests/HRManagement.UnitTests --filter "CodeGeneration\|CodSequence\|Policy\|Prod001\|Ten00\|Mdm001\|Est00"` | 437 / 437 passed | 127 s |
| `dotnet test src/Tests/HRManagement.IntegrationTests --filter "CrossTenantRls\|CodeGenerationConcurrency"` (`RUN_TESTCONTAINERS=true`) | 19 / 19 passed | 101 s |

## 0.3 Names this plan uses (from PLAN-37, PLAN-42, PLAN-47)

- **PLAN-37:** the prod-like stack is `scripts/acceptance-up.ps1 -Profile ProdLike` (not `-ProdLike`), and the job runner is
  `scripts/ci/acceptance-job.ps1 -Profile <ProdLike|Lab> -Classes <list> [-Stage <name>]`.
- **PLAN-42:** the service path is `POST api/internal/codes/generate` (`CodeGenerationInternalController`, policy
  `ServicePolicies.CodeGenerate`), which takes the tenant from the request (header), ignores `TenantId` in the body and calls
  `ICodeGenerator.GenerateForServiceAsync`; the kinds a client may ask for are in `CodeKindOwners`.
- **PLAN-47:** `PolicyRuleValidator` (`HR.Core.Domain/Services/Policy`) with `PolicyRuleValidationContext`;
  `PolicyRuleValidationService.LoadContextAsync` (the national read past the EF filter, not past row security);
  `PolicyPublishService.PublishAsync` / `PublishSystemSeedAsync` (the only publish path); `PolicyDraftService`;
  `PolicySeedVersionWriter`; `PolicySeedChangeSetApplier`; the roles `policy-author` / `policy-approver`; the triggers of
  `POL047_PolicyRuleLifecycle` (SQLSTATE P0047, `FOR SHARE` lookups); `AcceptanceHarness.CreatePolicyLifecycleClientAsync`;
  the start-up log "PLAN-47 validator inventory".

## 0.4 The tenant-filter bypasses of the national tables

`SystemTenantBypassPurpose.PolicyResolution` and `NationalReferenceLookup`: **20 uses in 13 files** (the plan counted 14 in 10;
PLAN-43..47 added `ExternalOutcomePublisher`, `ExternalApprovalService`, `PolicyRuleValidationService` ×4 and
`PublishedPolicyImmutabilityInterceptor`; `WorkflowInstancesController:58` is gone). All twenty read one of the ten tables.

| # | Site | Entity | Replacement |
|---|------|--------|-------------|
| 1-3 | `WorkflowStarter.cs:101, 112, 122` | binding, definition (by id), definition (by code) | `OwnOrNational` + own first |
| 4 | `WorkflowInstanceActionService.cs:289` | definition (pinned) | `OwnOrNational(instance tenant)` |
| 5 | `ExternalOutcomePublisher.cs:79` | definition | `OwnOrNational(instance tenant)` |
| 6 | `ExternalApprovalService.cs:92` | definition | `OwnOrNational` + own first |
| 7 | `ApprovalStartCoordinator.cs:201` | definition (by id) | `OwnOrNational` |
| 8 | `UnifiedIntakeService.cs:223` | reference (qualifications) | `OwnOrNational`, the own set still replaces the national one (COD-F keeps either/or) |
| 9 | `RecruitmentMetricsService.cs:583` | definitions `RECRUITMENT-*` | `OwnOrNational` |
| 10-13 | `PolicyRuleValidationService.cs:28, 34, 50, 56` | schema, definitions, bindings, code keys | `NationalOnly` (schema), `OwnOrNational` (the rest; national code templates with `Scope = National`) |
| 14 | `PublishedPolicyImmutabilityInterceptor.cs:63` | version status | **not a national read**: the session's own versions only (the POL047 trigger takes `FOR SHARE`, so an agency rule on a national draft stays refused) |
| 15 | `WorkflowPanelBuilder.cs:45` | definition | `OwnOrNational(instance tenant)` |
| 16 | `WorkflowInboxQueryService.cs:175` | definitions | `OwnOrNational` in SQL |
| 17 | `ReferenceDataProvider.cs:96` | reference | `OwnOrNational`, either/or kept |
| 18-20 | `ReferenceCatalogAdminService.cs:104, 272, 299` | reference | `OwnOrNational` + own first |

Other reads of the ten that need the national rows and go through the EF filter today (so an agency sees nothing national):
`PolicyOrchestrator.LoadPublishedAsync` (templates and the facts schema), `CodeGeneratorService.ResolveSnapshotAsync`, the
policy pages and API (`PolicyTemplatesController.Index/Details/Simulate`, `PoliciesController.GetVersion/ListTemplates/
GetTemplate/Simulate`), `CodePoliciesController.Index`, `CodeGenerationController.ListPolicies`, `WorkflowDefinitionsController
List/Get`, `ReferenceDataController` reads, `ReferenceCatalogAdminService.AnalyzeImpactAsync`,
`EstablishmentPayloadSummaryBuilder`. Three places key «national» on `Scope = National` alone (`PolicyOrchestrator.cs:209`,
`CodeGeneratorService.cs:92,108`, `PolicyTemplatesController.cs:135-136`), and `CreateDraft` creates a `Scope = National`
template inside an agency.

## 0.5 Prefixless codes (acceptance data)

The stack left by PLAN-47's last prod-like run (up since 2026-11-08T14:45Z) was read before it was removed:

| Kind | Tenant | Count | Code |
|------|--------|-------|------|
| CitizenReference | PLATFORM | 1 | `-2026-00001` |
| OfferLetter | AGENCY-A | 1 | `-2026-00001` |
| RecruitmentRequisition | AGENCY-A | 1 | `-2026-00001` |
| ServiceTransaction | AGENCY-A | 1 | `-2026-00001` |

The anonymous `/careers` issuance lands in PLATFORM with no prefix: the request carries no tenant, and the EF filter hides the
national policy from the agency sessions (G-COD-02). After a run on the new code this must show zero new codes starting with `-`.

## 0.6 Published code policies of the national tenant

Acceptance stack and demo server alike: 18 national templates (17 kinds plus `AssetTag` / `ASSET-PRIVILEGED`), each with v1
Published, no `CodePolicyRules`. `ForensicCase` has no policy. The defects of §3:

| Kind | v1 segments | Reset | Defect |
|------|-------------|-------|--------|
| CostCenter | `{OrgUnitCode}` | Never | no sequence, but generated with a counter per position: the second position of a unit breaks the issuance index |
| FundedPosition | `{OrgUnitCode}-{GradeCode}-{seq3 [OrgUnitCode, GradeCode, BudgetYear]}` | PerScope | the budget year is in the scope but not in the number |
| RecruitmentRequisition | `REQ-{YYYY}-{seq5 [BudgetYear]}` | Yearly | the number shows the calendar year, the scope is the budget year |
| AssetTag / ASSET-PRIVILEGED | `AH-{YYYY}-{seq6}` / `VIP-{YYYY}-{seq6}` | Yearly | one shared counter (the key is not in the scope key); a request without a key may pick either |
| (bootstrap) | `CodSequenceBootstrapService` | — | its own regexes and scope keys, not the generator's |

## Row security of the ten tables

All ten have a NOT NULL `TenantId`, ENABLE + FORCE row security and one policy, `tenant_isolation` (permissive, all commands,
`"TenantId"::text = app.current_tenant`), on the server and on the stacks. No `national_shared_read` anywhere. National rows
carry PLATFORM. The boot hardening recreates `tenant_isolation` on every start, so the national read must be its own policy.

## The demo server (read-only, 2026-11-09 ~15:40Z)

- Running tag `61816ed7dda2` (previous `ae95ec2fec82`): PLAN-47's last fix is already deployed.
- Tenants: **PLATFORM only.** No agency, so no agency overlay for the ceiling to change.
- National tables: 20 policy templates (22 versions, 39 rules; `Analytics.ETL` v1-v3 from PLAN-47's tour), 20 facts schemas,
  13 bindings, 21 workflow definitions, 18 code templates and versions, 66 reference entries. No agency rows.
- Prefixless codes: **none**.
- Counters and the highest issued code per scope (all PLATFORM):

| Kind | Scope key (after the tenant) | Counter | Highest issued |
|------|------------------------------|---------|----------------|
| ArchiveReference | `ArchiveReference\|LEAVE\|2026` | 12 | ARC-LEAVE-2026-00012 |
| AssetTag | `AssetTag\|2026` | 12 | AH-2026-000012 |
| CitizenReference | `CitizenReference\|2026` | 2 | CIT-2026-000002 |
| IdCardSerial | `IdCardSerial\|2026` | 10 | ID-2026-0000010 |
| RecruitmentRequisition | `RecruitmentRequisition\|2026\|2026` | 15 | REQ-2026-00015 |
| ServiceTransaction | `ServiceTransaction\|LEAV\|2026` | 12 | SRV-LEAV-2026-00012 |
| ServiceTransaction | `ServiceTransaction\|SEP\|2026` | 1 | SRV-SEP-2026-00001 |

- Numbers held by entities: 15 requisitions `REQ-2026-00001..00015` (all issued through the generator); 500 funded positions
  `FP-0001..FP-0500` (demo seed, not the policy format: the new format cannot produce them, so they are reported, not
  parsed); 51 citizen applications `CA-2026-0001..0051` (demo seed, a prefix no policy uses) and 2 `CIT-2026-00000x`; no offer
  letters; 30 org units.
- Seed packs applied: `NATIONAL-MANDATORY-v1` only.

## Code generation calls (inventory B, §2)

- **In HR.Core (`ICodeGenerator`):** OrgUnit (`OrgUnitsController` Create, `ParentCode` or `00`), RecruitmentRequisition
  (`RecruitmentRequisitionsController` Create, `BudgetYear` of the position), CitizenReference (`PublicCareersController`
  Apply and ApplyCompetition, anonymous, no tenant), OfferLetter (`RecruitmentOfferService`, six callers including the
  offer-expiry job and the anonymous candidate portal), RecruitmentCompetition, EstablishmentChangeRequest, JobCatalogEntry and
  FundedPosition plus CostCenter (both from `EstablishmentChangeApplier` through `JobCatalogCodeService` /
  `FundedPositionCodeService`), and the previews of `CodePoliciesController` and `api/codes`.
- **Through `api/internal/codes/generate`:** AssetTag (Admin.Logistics; the key `ASSET-PRIVILEGED` comes from the policy
  decision `SelectCodePolicy`), InventorySku, ServiceTransaction (HR.Contracts, interactive and consumers), AdministrativeDecision,
  LegalCase, IdCardSerial, ItAssetTag, ArchiveReference (Records.Archive consumers).
- `ForensicCase` has no call site and no policy.
- **Numbers written without the generator:** the demo seed's requisitions `REQ-{year}-{n:D5}` (the generator's shape, the
  PLAN-43 500), and the demo and acceptance seeds' org units, positions (`FP-`, `POS-`), offers, citizen references (`CA-`)
  and competitions. The acceptance numbers carry an agency letter or an `ACxx` marker, so they cannot equal a generated number.
- **Call-site defects:**
  - `FundedPositionCodeService` passes `draft.TenantId` = `Guid.Empty` for both kinds (a counter of tenant `000…000`, and
    under row security a refused insert);
  - `CitizenApplications.ReferenceNumber` is unique **across tenants** and the portal finds an application by it alone, so
    citizen references must stay one national sequence (see P2);
  - `AssetsController` asks for `ASSET-PRIVILEGED`, but an agency gets the fallback `AH-`;
  - the `FIN-ORG-UNIT` overlay sets no reset, so it is `Yearly` with no year in the number (it would collide each January).

## Agency overlays and the ceiling (inventory B, §3.5 and §4)

| Source | Rule / policy | Domain | Effect | Under D-TEN-8 |
|--------|---------------|--------|--------|---------------|
| `policy-ministry-finance-v1` | `R-FIN-LEAVE-NOTE` (stored as `policy-ministry-finance-v1:R-FIN-LEAVE-NOTE`, which fails PLAN-47's key pattern) | Contracts.Leave | RequireEvidence | allowed; the key is renamed |
| acceptance seed, every agency, v2 | `ACC-R-PRIV-EVIDENCE` | Assets.Custody | RequireEvidence | allowed |
| acceptance seed, every agency, v2 (and s2s42 v4) | `ACC-R-PRIV-WORKFLOW` | Assets.Custody | TriggerWorkflow `ASSET-ASSIGN-APPROVAL` | refused at publish; dropped at evaluation when the national `R-PRIV-WF` / `R-HIGH-APPROVAL` fires |
| acceptance seed, v3 draft (A published) | `ACC-R-PRIV-DENY` | Assets.Custody | Deny | allowed |
| s2s42 patch v4 | `ACC-S2S42-DENY` | Assets.Custody | Deny | allowed |
| `code-ministry-finance-v1` | `FIN-ORG-UNIT` (OrgUnit) | — | code overlay | becomes the agency's default OrgUnit policy (`"default": true`), with `PerScope` |

`AgencyPolicySeeder` adds only the first rule of each domain (no `else` after `if (template is null)`), and never runs the
validator. The demo server has no agency, so the ceiling changes nothing there.

## Jobs and anonymous paths with no tenant (inventory B, §5; for OPS-D)

Tenant-less reads (EF filter lifted or `Guid.Empty`), so under row security they read nothing: `LeaveScheduleReconcilerJob`
(a no-op even on superuser stacks), `RecruitmentIntakeCloseJob`, `RecruitmentOfferExpiryJob` (the expiry read),
`RecruitmentReserveExpiryJob`, `CandidateNotificationJob`, `CandidateRetentionJob` (its tenant list comes from tenant tables),
`FileReferenceOutboxDispatcherJob`, `InboxRetentionHostedService` (a no-op everywhere), the start-up passes after the seeds
(`WorkflowDefinitionHealthReport`, `OrgUnitPathService.RecomputeAllAsync`, `OrgUnitIntegrationService` reads), HR.Contracts
`HireCommencementWatcher`, File.Storage `FileScanProcessorJob`; and every anonymous `/careers` path (postings, apply, track,
competitions, results, the OTP lookups). `WorkflowOutcomeReconciliationJob` (PLAN-44) is the pattern: tenants from `Tenants`,
`TenantIdFlow` per tenant, a new scope per tenant.

## Acceptance classes (inventory B, §6)

- P-10 placements: AcPol47 prodLike/isolated; AcHcm25, AcWf43 prodLike/main; AcWf44-46 prodLike/isolated; AcS2s42
  prodLike/s2s42; Ac13 lab/main **not required**; Ac10 and Ac32 lab/main not required; Ac11, Ac33, AcEst10-18, AcEst24,
  AcHcm21, AcHcm30, AcWf23 lab/main required. `AcTen48NationalScopeTests` does not exist yet.
- Also generate codes or read national tables: AcInt37 (prodLike/isolated), AcHcm22, AcHcm28, AcHcm29, AcHcm31, AcHcm32
  (lab/main), AcHcm26, AcHcm27 (lab/isolated), AcMsg40 (prodLike/isolated), AcOps38 (prodLike/leastpriv, the only stack with
  row security), Ac31, Ac07, Ac09 (lab/main, not required), both recruitment journeys, and the excluded AcOps36 server checks.
- AC-13 today: one tenant (A), one kind (AssetTag) through `svc-admin-logistics`, 120 requests at 30 in parallel; its codes come
  from the AssetTag fallback (same shape); `db-checks.json` is computed in memory, not from the database. `AC-EST-14` (owner
  PLAN-48) relies on the agency fallback today.
