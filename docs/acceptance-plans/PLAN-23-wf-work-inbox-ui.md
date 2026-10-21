# PLAN-23 — صندوق عمل موحد (PLAN-WF-A / WF-002 UI)

**Scope:** WF-002 — تجميع اعتمادات workflow (PLAN-21/22) + legacy `ApprovalRequests` في صندوق عمل واحد  
**Status:** Implemented  
**Depends on:** [PLAN-21](PLAN-21-hcm-requisition-approval-ui.md) + [PLAN-22](PLAN-22-hcm-recruitment-workflows-ui.md) (Implemented)  
**Related cards:** WF-002, WF-001, CAP-HCM-01  
**Reference:** [WF_002_WORK_INBOX.md](../WF_002_WORK_INBOX.md)  
**Out of scope (PLAN-24):** `EstablishmentChanges` workflow UI، persistence كامل لـ `WorkInboxItem`، delegation/escalation، team inbox، bulk approve، Enforce الكامل

## Goal

استبدال تجربة «طلبات الموافقة» legacy ب**صندوق عمل موحّد** يعرض للمستخدم كل اعتمادات workflow المعلّقة التي يمكنه تنفيذها (حسب الدور + SoD)، مع **deep link** إلى صفحة Details المعتمدة في PLAN-21/22.

## Design

- **Virtual projection (lab-scale):** لا جدول EF `WorkInboxItems` — القراءة من `WorkflowInstances` + `ApprovalRequests`.
- `IWorkInboxService` مُسجَّل في DI؛ مسارات الكتابة (`Create`/`Delegate`/`Escalate`) → `NotSupportedException`.
- الاعتماد من الصندوق **v1:** عبر deep link فقط → Details workflow panel (لا inline approve).

## Deliverables

| Artifact | Path |
|----------|------|
| Query + eligibility | `WorkflowInboxQueryService`, `WorkflowInboxEligibilityService` |
| Deep links | `WorkflowDeepLinkResolver`, `WorkInboxItemMapper` |
| Service | `HrCoreWorkInboxService` : `IWorkInboxService` |
| UI | `WorkInboxController`, `Views/WorkInbox/Index.cshtml` |
| Nav | `_Layout` → «صندوق العمل» |
| Stats | `StatsApiController` → `{ count, workflowCount, legacyCount }` |
| Tests | `Wf023WorkInboxUiTests`, `AcWf23WorkInboxUiTests` |
| Script | `scripts/wf023-validate.ps1` |

## Entity types aggregated

| EntityType | Workflow | Deep link |
|------------|----------|-----------|
| `RecruitmentRequisition` | `RECRUITMENT-REQUISITION-APPROVAL` | `RecruitmentRequisitions/Details/{id}` |
| `OfferLetter` | `RECRUITMENT-OFFER-APPROVAL` | `OfferLetters/Details/{id}` |
| `SelectionCommitteeSession` | `RECRUITMENT-SELECTION-COMMITTEE` | `SelectionCommittees/Details/{id}` |
| `RecruitmentAppeal` | `RECRUITMENT-APPEAL-REVIEW` | `RecruitmentApplications/Details/{appId}` |

Legacy: `Approvals/Review/{id}`

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-WF-23a | `acc-br1-mgr` inbox lists pending requisition + offer with deep links | — |
| AC-WF-23b | `acc-a-management-approver` sees mgmt step items only | Does not see hr-core-only steps |
| AC-WF-23c | — | `acc-central-a` does not see own requisition/offer as actionable |
| AC-WF-23d | `acc-a-compliance` sees appeal review item | Non-reviewer excluded |
| AC-WF-23e | `api/stats/pending-approvals` count matches inbox actionable rows | — |
| AC-WF-23f | Legacy `ApprovalRequest` still appears in inbox | — |

**Personas:** `acc-central-a`, `acc-br1-mgr`, `acc-a-management-approver`, `acc-a-compliance`

## Verification

```powershell
powershell -File scripts/wf023-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Wf023
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcWf23
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm021|Hcm022"
```

## Definition of done

- [x] `WorkInbox/Index` shows actionable workflow items for PLAN-21/22 entity types
- [x] Legacy approvals visible in unified view (role-filtered)
- [x] Nav points to WorkInbox; stats API counts workflow + legacy
- [x] `IWorkInboxService` registered with read-path implementation
- [x] AC-WF-23 unit coverage + `Hcm021`/`Hcm022` regression PASS
