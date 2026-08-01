# WF-002 — Unified National Work Inbox

## Purpose

Provide a single, aggregated work inbox where every user sees their pending tasks, approvals, and action items from all source systems, with aging, SLA, delegation, and escalation support.

## Scope

- Personal and team inbox views
- Tasks, approvals, reviews, and acknowledgements from all sources
- Filters by priority, aging, SLA, and source system
- Delegation and reassignment with audit trail
- Escalation based on aging or SLA breach
- Source status reconciliation (service outage does not hide tasks)
- Bulk approval controls (governed or prohibited per policy)
- Mobile-friendly access
- Deep links back to source system

## Domain Model

### WorkInboxItem Entity

Represents a single actionable item in a user's work inbox.

```
WorkInboxItem
├── Id (Guid)
├── TenantId (Guid)
├── AssigneeId (Guid) — current owner
├── TeamId (string?) — team queue (if team-based)
├── SourceSystem (string) — originating module/service
├── SourceReferenceId (string) — ID in the source system
├── ItemType (enum) — Approval, Task, Review, Acknowledgement, Information
├── Priority (enum) — Low, Normal, High, Urgent
├── Status (enum) — Pending, InProgress, Completed, Delegated, Escalated, Cancelled
├── Title (string?)
├── Description (string?)
├── DeepLink (string?) — URL/route to source
├── CreatedAt (DateTime)
├── DueDate (DateTime?) — SLA deadline
├── CompletedAt (DateTime?)
├── IsEscalated (bool)
├── DelegatedFromId (string?) — original assignee before delegation
├── AgingDays (computed) — days since creation
```

### IWorkInboxService Interface

Service abstraction for inbox operations.

```csharp
interface IWorkInboxService
├── GetPersonalInboxAsync(assigneeId)
├── GetTeamInboxAsync(teamId)
├── CreateItemAsync(item)
├── CompleteItemAsync(itemId, resolution?)
├── DelegateItemAsync(itemId, newAssigneeId)
├── EscalateItemAsync(itemId, reason)
├── GetPendingCountAsync(assigneeId)
```

## Business Rules

- User sees only own tasks (tenant + assignee scope)
- Item count reconciles with source systems
- Service outage does not hide tasks (items persist independently)
- Bulk approval prohibited or governed per agency policy
- Every delegation/reassignment is audited
- Escalation triggers notification to manager

## Acceptance Criteria Mapping

- [x] User sees only own tasks — `AssigneeId` + `TenantId` filtering
- [x] Count reconciles with sources — `SourceSystem` + `SourceReferenceId` linkage
- [x] Service outage tolerance — items stored independently from source
- [x] Bulk approval governed — policy-layer control (not in base entity)
- [x] Every reassignment audited — `DelegatedFromId` + audit trail

## Artifacts

| File | Purpose |
|------|---------|
| `docs/WF_002_WORK_INBOX.md` | This document |
| `ServiceDefaults/Workflow/WorkInboxItem.cs` | Work inbox item entity |
| `ServiceDefaults/Workflow/IWorkInboxService.cs` | Service interface |
| `scripts/wf002-validate.ps1` | Validates WF-002 artifacts |
| `Wf002WorkInboxTests.cs` | Unit tests |

## Validation

- `scripts/wf002-validate.ps1`
- `Wf002WorkInboxTests`
