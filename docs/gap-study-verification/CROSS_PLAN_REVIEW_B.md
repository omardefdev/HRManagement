# Cross-plan consistency review — share B (PLAN-50..65)

Reviewer B · 2026-10-30 · Scope: PLAN-50..PLAN-65, and study back-links in IAM_001, PAY_001, HCM_002, FUN_002, FUN_005, HCM_006, HCM_TALENT_BENEFITS_HEALTH.

Code base used for citation checks: `/home/claude/hrm` (main @ `f9eab1a`). Relative links were resolved with the brief's mapping (`docs/` and `docs/acceptance-plans/`).

I did not edit PLAN-34..49. During the review the other reviewer also edited three files in my share: PLAN-50:173 (`Workflow:StepRole:ExemptEntityTypes`), PLAN-61:53 (`ServiceRegimePack` counts), and the PLAN-65 AC-11/AC-13 owners. Those edits are kept as they are and are not counted below.

## Summary of counts

| Check | Found | Fixed | Open |
|-------|------:|------:|-----:|
| 1. Ownership (duplicates and wrong «→ PLAN-xx» pointers) | 9 | 8 | 1 |
| 2. Order (an artifact consumed before it is produced, or a cross-cutting rule ignored) | 7 groups | 7 in this share | 1 (PLAN-42..49) |
| 3. Contracts (names and shapes) | 16 | 16 | 0 |
| 4. Format and size | 1 | 1 | 0 |
| 5. Links | 0 | — | 0 |
| 6. Accuracy (file:line spot checks, about 45) | 0 | — | 0 |
| 7. Study back-links (missing `الخطة` lines) | 19 | 19 | 0 |
| Extra: test-data isolation across AC-SEP-60..64 | 1 | 1 (design added) | needs the owner's attention |

---

## 1. Ownership

All gaps assigned in the wave-1 table appear in each plan's Scope and In scope: 50–65 ✓.

| # | Issue | Fix |
|---|-------|-----|
| O1 | PLAN-51 §2 and §6 re-implemented work PLAN-42 already does. PLAN-42 moves the internal controllers to `ServicePolicies.HrCoreInternalS2S` and adds `S2s042InternalRoutesAreServiceOnlyTests`. | PLAN-51:81 now deletes the unused `HrCoreLookupS2S`. PLAN-51:163 moves PLAN-42's test into the new project instead of writing a second one. |
| O2 | PLAN-51 §5 created `HrCore.WorkflowStart`, `HrCore.CodesGenerate` and `HrCore.PolicyEnforce`. These duplicate `WorkflowExternalStart` (PLAN-43), `ServicePolicies.CodeGenerate` and `ServicePolicies.PolicyEnforceS2S` (PLAN-42), on endpoints PLAN-42 already moved. | PLAN-51:140-142 now consume those policies, and only strip the service-account branch from `HrCore.Write` and `HrCore.BranchOperation`. |
| O3 | PLAN-52 added `WorkflowInstances.ScopeOrgUnitId`, a column PLAN-43 already adds. | PLAN-52:79-80 now backfill and stamp the PLAN-43 column. |
| O4 | PLAN-50:198 pointed «employee_id fix, leaver delegations» to PLAN-62, which does neither. | The fix moved into PLAN-50 (see order item R1). Leaver delegations now point to wave 2 (G-SEP-17), as PLAN-63 states (PLAN-50:199). |
| O5 | PLAN-51:179 pointed `forceSameActorForLab` to wave 2, but PLAN-47 removes it. | Now points to PLAN-47. |
| O6 | PLAN-52:153 pointed the `employee_id` fix and AC-06 to PLAN-62. | `employee_id` now points to PLAN-50, AC-06 to PLAN-65. |
| O7 | PLAN-58 said PLAN-51 owns «admin removed from money actions, Keycloak conflict report, role sync» and that afterwards «the check doesn't see admin». | PLAN-58:134 and :182: PLAN-51 only excludes `hr-admin` from `Payroll.Post`/`Reverse` (D-IAM-4). The rest is now wave 2 (IAM-G). |
| O8 | PLAN-55 duplicated PLAN-53's versioning helper and writers test (`EmployeePayProfileRules.CreateNextVersion`, `PayProfileCreationArchitectureTests`). | Counted under contract item C4. |
| **Open** | **PLAN-45:145** says G-IDA-05 (silent custody close) is wave 2. PLAN-62 §3 fixes the separation part of it. | Back-link added as partial. PLAN-45 text is left to the other reviewer. |

## 2. Order

`Depends on` lists only lower-numbered plans in all of 50–65 ✓.

| # | Violation | Fix |
|---|-----------|-----|
| R1 | PLAN-50 D-50-2 relied on PLAN-62 to fix the Keycloak attribute name: Auth.Directory writes `employeeId`, the realm mapper reads `employee_id` (`KeycloakUserProvisioningService.cs:74`). PLAN-62 never did this. | The one-line fix moved into PLAN-50: D-50-2 (:28), §5 row (:144), rule 5, risks and deliverables. PLAN-62:77 consumes it. |
| R2 | PLAN-57 §6 edited `hrcore-post-policy-allowlist.txt` and `HrCorePostActionsHaveExplicitPolicyTests`, both of which PLAN-51 deletes. | PLAN-57:260-264 and its DoD now use PLAN-51's `WriteActionsHaveExplicitPolicyTests` and `RolesPolicy`. The architecture project was added to Verification. |
| R3 | PLAN-41's rule (an acceptance class missing from `tests/acceptance/ci-scope.json` fails the CI job) was ignored by all later plans. | 50–64 now list their class in `prodLike[]` with `required: true` in their Deliverables. **PLAN-42..49 are still missing it (open).** |
| R4 | PLAN-40 R4 (every published event type needs a row in `EVENT_PUBLISHER_INVENTORY.md`) was not consumed by the plans that add events. | Rows added: PLAN-53:231, PLAN-56:198 (plus the guard in Verification and a corrected risk row), PLAN-62 (Contracts row), PLAN-63:223. PLAN-57 already had them. |
| R5 | PLAN-52's `OrgScopeCoverageTests` (every HR.Core/HR.Contracts/Admin.Logistics controller declares a scope stance) was not honoured by later new controllers. | PLAN-57:261 (two controllers), PLAN-63:61 (admin endpoint `[OrgScopeCentralOnly]`). |
| R6 | PLAN-53's `PayProfileWritersTests` (allowed writers of `EmployeePayProfiles`) was not honoured by later writers. | PLAN-61:107 (restore), PLAN-63:110 (compensation reactivation). |
| R7 | PLAN-37's `service-wiring.json` (one matrix, with a test that flags any S2S call missing from it) was not updated by new S2S calls. | PLAN-50 (delegation lookup), PLAN-51:142 (`serverPolicy` updates), PLAN-60 (workflow client), PLAN-64:58 (service-record). |

Forward references that consume nothing and so are not violations:
- PLAN-56 §1/§2 mention PLAN-57 fields.
- PLAN-52 §5 defers the live Keycloak effect to PLAN-62.
- PLAN-58 reserves a `PaymentBatchIssuer` source for PLAN-64.

## 3. Contracts

| # | Pair | Issue → fix |
|---|------|-------------|
| C1 | 46↔60 | `IWorkflowApprovalClient`, the request/result types, `IWorkflowOutcomeDecided`, `WorkflowOutcomeGuard`, `WorkflowApprovalSweepJob<TStore>` and `FakeWorkflowApprovalClient` are identical ✓. The sweep config key differed: PLAN-60 used `Separation:Approval:SweepAfterMinutes`. Now PLAN-46's `WorkflowApproval:Sweep:StaleAfterMinutes` (PLAN-60:106, AC-SEP-60i). |
| C2 | 46↔60 | A risk row sent a follow-up to PLAN-50, a lower plan. Now a gap logged against PLAN-46 guarantee 2 (PLAN-60:363). |
| C3 | 53↔55 | PLAN-53 reserved `PayProfileChangeKind.BankAccount` for PLAN-55, but PLAN-55 builds a separate `EmployeeBankAccount` entity. The reservation is removed (PLAN-53:78) and the out-of-scope wording aligned. |
| C4 | 53↔55 | PLAN-55 bypassed the PLAN-53 contract. It now extends `EmployeePayProfileVersioning.CreateNextVersion`, routes `HandleContractActivatedHandler` through `ApplySystemChangeAsync(Source=ContractActivated)`, and updates `PayProfileWritersTests` (PLAN-55:92-93, rule 1, deliverables). |
| C5 | 53↔54 | «the write policy from PLAN-53» was vague. Now `Payroll.MasterData` (PLAN-54:63). |
| C6 | 57↔59 | `IAttendanceSummaryPublished`, `close`, `receipts?periodStart=` and `attendance-status.missingAttendance` match ✓. The close persona was ambiguous: closing the whole agency needs central HR, so it is now `central-a` (PLAN-59:105). |
| C7 | 57↔59 | The cycle month could collide with PLAN-57's reserved months, whose days are locked. D-PAY-22 now skips them (PLAN-59:29). |
| C8 | 43/45/46↔50 | PLAN-50 created `HrCore.WorkflowAction` on the same endpoints PLAN-43 puts under `WorkflowStepAction`. Its gateway rule `HrCore.ReadOnly` would also block the admin-logistics, idcard and compliance approvers added by PLAN-45/46. PLAN-50 now makes `WorkflowStepAction` delegation-aware and uses `Gateway.ApprovalReach`, as D-50-3 already said (PLAN-50:118, :167). |
| C9 | 47↔50 | PLAN-47 moves `PublishFromUi` and `Policies.Publish` to `PolicyApprover`. The PLAN-50 table and test now use it (PLAN-50:94, :102, :104). |
| C10 | 42↔50 | PLAN-50's S2S client «signs the gateway proof», which PLAN-42 removes. Fixed (PLAN-50:143). The new internal policy is now a `ServicePolicies` constant, so it passes PLAN-42's guard. |
| C11 | 42↔51 | PLAN-51 redefined `CreateServiceClientAsync(clientId)` with a different signature. It now reuses PLAN-42's `(clientId, scope)` (PLAN-51:185). |
| C12 | 42↔64 | The service-record endpoint was put under `HrCoreLookupS2S`. Now `ServicePolicies.HrCoreInternalS2S`, with its callers named (PLAN-64:58). |
| C13 | 58↔64 | The payment hook was vague (`PaymentBatchKind`). Now `PaymentBatchIssuer`, the `Pay058SinglePaymentPathArchitectureTests` allow-list and `SettlePaymentBatchHandler` (PLAN-64:40, :119, :121). |
| C14 | 54↔64 | `EosRule` now extends PLAN-54's pack schema and hash (`HashSchemaVersion=3`) and adds a `law53-interim.json` pack (PLAN-64:80-81). The `FinalSettlement` run key that PLAN-56 left to PLAN-64 is closed as «no such run» (PLAN-64:32). |
| C15 | 46↔61 | After PLAN-46, `IDisciplinaryActionTaken` is published only after engine approval. The PLAN-61 problem text, the D-row and AC-SEP-61e now include the approval steps (`a-compliance-2`, `a-management-approver`). `SourceFinalityConfirmation` was added to the SEP061 migration fields. |
| C16 | 50/51↔58 | `PayrollAuditRecords` used the controller-name exception list that PLAN-51 deletes. Now `[ModulePolicyOverride(Payroll.AuditReview)]` (PLAN-58:118). |

Personas: every new persona is defined once, by the first plan that needs it:
- `a-ess-*` (51), `a-payroll-approver-2` (54), `a-payroll-multirole` (58);
- `a-leaver` (60), `a-leaver-53` (61), `a-leaver-sec` (62), `a-leaver-2` and `a-leaver-disc` (63), `a-leaver-debt` (64).

The persona count chain was also fixed. PLAN-54 said «update count 61», which would be wrong by then. Each plan now says «+N on the value at execution time» and names `AcceptancePersonaCatalog.cs`. PLAN-58 adds a note on the PLAN-39 realm generator.

Other contract pairs named in the brief:
- PLAN-43 removing `EnforceStepRoleEntityTypes`: PLAN-60 already aligned, and PLAN-50 was fixed by the other reviewer.
- PLAN-40 publish guard vs PLAN-56/63: see R4.
- PLAN-37 matrix vs PLAN-50/51/61: see R7.

## 4. Format and size

- All 16 plans have every required section, with safe defaults in «قرارات».
- Estimates range from 5 to 10 days.
- The 106 AC IDs in this share are unique, and 0 duplicates exist across PLAN-34..65.
- One broken table row was fixed: PLAN-54:138 had unescaped `|` characters inside a code span.

## 5. Links

0 broken relative links in PLAN-34..65 and the 14 studies.

## 6. Accuracy

I spot-checked 2–7 citations per plan, about 45 in all. 0 were wrong. Examples:
- `HrModuleAuthorizationFilter.cs:27-46`, `GatewayPathAuthorizationMiddleware.cs:240-258`, `ServiceAuthorizationExtensions.cs:74-82`
- `PayrollRunJobHandlers.cs:90-95`, `PayrollRules.cs:305-308`, `PayrollRunHandlers.cs:238-241`
- `AttendanceIntegrationService.cs:75-124`, `PaymentHoldsController.cs:65-69`
- `SeparationRecord.cs:36,47`, `InboundEventHandlers.cs:610-614`, `ISeparationCompleted.cs:22`
- `EmploymentStatusRules.cs:42-45`, `EndOfServiceCalculation.cs:15-22`, `Ac10EnforcePolicyTests.cs:31`

## 7. Study back-links added

All lines were inserted under الإصلاح المقترح, before التحقق المطلوب.

- **IAM_001:**
  - G-IAM-07 → PLAN-47 (partial)
  - G-IAM-12 → PLAN-39 (partial)
  - G-IAM-14 → PLAN-45/46/51/58 (partial)
- **PAY_001:** G-PAY-11 → PLAN-54/61 (partial)
- **HCM_002:** G-LVA-17 → PLAN-57 (partial)
- **FUN_005:**
  - G-IDA-01 → 62
  - G-IDA-02 → 45
  - G-IDA-05 → 62 (partial)
  - G-IDA-06 → 63 + 62
  - G-IDA-07 → 62 (partial)
  - G-IDA-08 → 52 (partial)
  - G-IDA-09 → 62 (partial)
  - G-IDA-14 → 51 + 46 (partial)
- **HCM_006:**
  - G-ER-01 → 46 (+51) (partial)
  - G-ER-02 → 61 (partial)
  - G-ER-03 → 61 (partial)
  - G-ER-06 → 46 (partial)
- **HCM_TALENT:**
  - G-TBH-02 → 61 (partial)
  - G-TBH-03 → 52 (partial)

FUN_002 was already complete. The brief's cross-area cases in my studies are now present: G-IDA-01/02, G-ER-01/02/03, G-LVA-18, G-SEP-12, G-IAM-03 and G-IAM-13.

## Extra: test-data isolation across the SEP acceptance tests

AC-SEP-60, 61, 62, 63 and 64 all separate, or open a Draft for, the same shared persona `a-leaver`. PLAN-65 also reruns every wave-1 class on the same stack without a reset (AC-V1-65c).

As written, every class after the first would hit «open separation record» or «terminal status».

Fix: PLAN-60:290 defines `AcceptanceLeaverFixture.CreateAsync(template)`, which creates a fresh copy of the template employee for each scenario. PLAN-61..64 reference it and extend it with their own fixtures. PLAN-62 provisions the copy's Keycloak account and sets its password through the admin API.

## Open issues for the owner

1. **PLAN-42..49 do not register their acceptance classes in `tests/acceptance/ci-scope.json`.** PLAN-41's rule fails the CI job for any unregistered class. Each Deliverables «Acceptance» row needs it. This belongs to the other share.
2. **PLAN-45:145** states that G-IDA-05 is wave 2. PLAN-62 §3 already fixes the separation part.
3. **PLAN-43:77, PLAN-46:191 and PLAN-48:176** add personas without `AcceptancePersonaCatalog.cs` or the count update. PLAN-41's match test will turn red.
4. **Product decision, PLAN-61:141.** A single doctor's `Retirement` recommendation opens a Draft medical separation. The TBH study (G-TBH-02, D-TBH-1) says an individual recommendation should have no employment effect, and only an approved committee decision should. The owner should choose. The safe alternative is to drop `MedicalRecommendationRetirementConsumer` and keep only the `MEDICAL_RETIREMENT` committee path.
5. **PLAN-60:290.** `AcceptanceLeaverFixture` is new test infrastructure, about 0.5 day, in a 9–10 day plan. If PLAN-60 overruns, the fallback is a per-class reset endpoint in the Acceptance profile.
6. **PLAN-43:114 (informational).** It names PLAN-60 §0.1, a later plan, as the naming source of `WorkflowApprovalStartRequest`. The contract itself is produced by PLAN-43 and PLAN-46, and the names match.

No order violation remains open in PLAN-50..65.
