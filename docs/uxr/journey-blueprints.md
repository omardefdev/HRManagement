# UXR-001 — Journey blueprints (critical flows)

**Card:** UXR-001  
**As of:** 2026-07-31  
**Purpose:** Service-design blueprints for moderated field studies. Each journey maps stages, touchpoints, backstage systems, and research tasks.

---

## Journey: Hire (`journey-hire`)

**Personas:** HR Officer (primary), Department Head, Employee (candidate→hiree)  
**Archetypes:** All five PRD-001 archetypes  
**Modules:** HR.Core, HR.Contracts, EST-001, WF-001, DOC-001

| Stage | User action | System touchpoint | Research task |
|-------|-------------|-------------------|---------------|
| 1. Requisition | HR opens funded position | EST-001 position catalog | Can user find vacant funded position in ≤ 3 clicks? |
| 2. Posting | Publish internal/external vacancy | HCM-001 requisition | Task success: create requisition linked to position |
| 3. Screening | Review applications, shortlist | Application case | Error rate: wrong position linkage |
| 4. Committee | Schedule interview, record scores | WF-001 committee workflow | SoD / conflict visibility understood? |
| 5. Offer | Generate offer letter | DOC-001 + SIG-001 | Preview accuracy; signature intent clear? |
| 6. Onboarding | Convert candidate → employee | HR.Core hire transaction | Idempotent conversion; no duplicate entry |
| 7. First login | Employee ESS profile | ESS-001 | Self-service profile completion without helpdesk |

**Pain hypotheses (validate in field):** position funding unclear; committee workflow opaque; offer letter language (AR/EN); conversion failure shows false success.

**Metrics keys:** `hire.requisition.success`, `hire.conversion.time`, `hire.ess-first-login.success`

---

## Journey: Payroll (`journey-payroll`)

**Personas:** Payroll Clerk (primary), Employee, Internal Auditor  
**Archetypes:** central-ministry, governorate, autonomous-institution  
**Modules:** Payroll, HR.Core, HCM-002, INT-004

| Stage | User action | System touchpoint | Research task |
|-------|-------------|-------------------|---------------|
| 1. Cycle open | Open payroll period | Payroll period config | Understand cut-off dates and retro rules |
| 2. Validate | Run validation report | Payroll engine | Interpret validation errors; fix at source |
| 3. Attendance import | Reconcile time data | HCM-002 interface | Idempotent import; duplicate detection UX |
| 4. Adjustments | Apply one-off adjustments | Payroll adjustments | SoD: who can approve adjustments? |
| 5. Approval | Manager/finance sign-off | WF-001 approval chain | Bulk approval blocked where required |
| 6. Treasury export | Submit payment batch | INT-004 treasury connector | Failure recovery; no silent drop |
| 7. Payslip | Employee views payslip | ESS-001 | Low-bandwidth PDF/view; explain deductions |

**Pain hypotheses:** validation report jargon; retro correction after snapshot; treasury failure messaging; payslip deduction clarity.

**Metrics keys:** `payroll.validation.success`, `payroll.cycle.time`, `payroll.payslip.view.success`

---

## Journey: Leave (`journey-leave`)

**Personas:** Employee (primary), Manager, HR Officer  
**Archetypes:** All; include remote branch-office sample  
**Modules:** ESS-001, MSS-001, WF-001, HR.Core

| Stage | User action | System touchpoint | Research task |
|-------|-------------|-------------------|---------------|
| 1. Check balance | View leave balance | ESS-001 profile | Balance matches policy; calendar clear |
| 2. Submit request | Choose dates, type, reason | ESS-001 leave form | Date picker (Hijri/Gregorian); validation messages |
| 3. Evidence | Attach medical cert if required | FILE-001 upload | Resumable upload on low bandwidth |
| 4. Manager review | Approve/reject/delegate | MSS-001 inbox | Acting manager; delegation expiry |
| 5. HR confirm | HR validates policy | WF-001 HR step | Retroactive leave rules understood |
| 6. Balance update | System updates entitlement | HR.Core | Employee sees updated balance in timeline |
| 7. Calendar sync | Team calendar reflects absence | MSS-001 team view | Manager sees team coverage |

**Pain hypotheses:** Hijri/Gregorian date confusion; attachment retry on mobile; manager inbox overload; status not synced with workflow.

**Metrics keys:** `leave.submit.success`, `leave.approval.time`, `leave.balance-accuracy`

---

## Journey: Transfer (`journey-transfer`)

**Personas:** HR Officer (primary), Manager, Employee  
**Archetypes:** cross-agency transfer must include governorate ↔ central sample  
**Modules:** HR.Movements, ORG-001, EST-001, WF-001, MDM-002

| Stage | User action | System touchpoint | Research task |
|-------|-------------|-------------------|---------------|
| 1. Initiate | Start transfer request | HR.Movements | Select target org/position; funding check |
| 2. Impact preview | Review org/role/pay impact | ORG-001 as-of | Understand future-effective changes |
| 3. Source approval | Source agency manager/HR | WF-001 source chain | Scoped to source tenant only |
| 4. Target acceptance | Target agency accepts | WF-001 target chain | Cross-tenant handoff visibility |
| 5. Position assign | Link to target establishment | EST-001 | No assign to unfunded position |
| 6. Effective date | Set transfer effective date | HR.Movements | Historical reporting after transfer |
| 7. Employee notify | Employee receives decision | COM-001 notification | Deep link re-authorizes correctly |

**Pain hypotheses:** cross-agency status invisible; effective-date impact on payroll not explained; prior agency access revocation timing.

**Metrics keys:** `transfer.initiate.success`, `transfer.cross-agency.time`, `transfer.employee-notify.success`

---

## Cross-journey research standards

- **Session length:** 45–60 minutes moderated; max 3 tasks per session.
- **Environment:** Participant's actual device/network where possible (field condition).
- **Recording:** Screen + audio with consent; no national ID or bank details on screen.
- **Severity tagging:** Blocker / Major / Minor / Observation → maps to backlog priority.
- **Traceability:** Each finding references `journeyId`, `stage`, and `metricsKey` from baseline JSON.
