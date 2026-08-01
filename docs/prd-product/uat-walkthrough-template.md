# PRD-001 — UAT Walkthrough Template

Scenario scripts per agency archetype. Execute each journey with representative data; capture evidence IDs per UAT-001.

**Archetypes:** `central-ministry` | `governorate` | `autonomous-institution` | `branch-office` | `specialized-body`

---

## 1. Onboarding

**Personas:** hr-officer, department-head, citizen (idcard where enabled)

| Step | Action | Module | Expected |
|------|--------|--------|----------|
| 1 | Create position from approved establishment | hr-core | Position linked to org unit |
| 2 | Initiate hire case with contract template | hr-contracts | Versioned template applied |
| 3 | Medical clearance (if mandatory for archetype) | hr-health | Status gates hire progression |
| 4 | Issue employee ID / credentials | idcard, auth-directory | Credential active |
| 5 | Archive hire packet | records-archive, file-storage | Immutable evidence record |

**Archetype notes:**

- **branch-office:** hr-health optional; skip step 3 or use waiver with audit.
- **specialized-body:** hr-training enrollment triggered on day 1.
- **autonomous-institution:** verify configurable modules enabled in tenant pack.

---

## 2. Payroll Cycle

**Personas:** payroll-clerk, minister (analytics), auditor

| Step | Action | Module | Expected |
|------|--------|--------|----------|
| 1 | Close time/absence inputs | hr-core | Period locked for payroll |
| 2 | Run payroll calculation | payroll | Deterministic totals |
| 3 | Compliance checks (statutory deductions) | compliance | No blocking violations |
| 4 | Executive dashboard / export | dev-analytics | Only if not disabled |
| 5 | Auditor sample reconciliation | compliance, payroll | Totals match ledger |

**Archetype notes:**

- **branch-office:** payroll may be `configurable` — central run with branch inputs.
- **governorate:** full local run with treasury handoff.
- **central-ministry:** national rollup after governorate submissions.

---

## 3. Transfer

**Personas:** hr-officer, department-head

| Step | Action | Module | Expected |
|------|--------|--------|----------|
| 1 | Initiate transfer request | hr-movements | Source/target org validated |
| 2 | Manager approvals | hr-core | Scoped to historical org |
| 3 | Update employment record | hr-core | As-of dating preserved |
| 4 | Move personnel file | records-archive | Lineage retained |
| 5 | Re-issue ID if org change requires | idcard | Old credential revoked |

**Archetype notes:**

- **central-ministry ↔ governorate:** cross-archetype transfer with restricted view.
- **branch-office:** transfer may be routed to parent governorate/ministry.

---

## 4. Separation

**Personas:** hr-officer, auditor

| Step | Action | Module | Expected |
|------|--------|--------|----------|
| 1 | Initiate separation (retirement/resign/termination) | hr-core | Reason coded |
| 2 | Final payroll & benefits settlement | payroll | Final pay calculated |
| 3 | Contract closure | hr-contracts | End date effective |
| 4 | Compliance / clearance checklist | compliance | All items signed |
| 5 | Archive separation dossier | records-archive | Retention policy applied |
| 6 | Revoke access | auth-directory | Sessions terminated |

**Archetype notes:**

- **specialized-body:** training certificates archived with separation packet.
- **autonomous-institution:** verify institution-specific clearance extensions.

---

## Sign-off Block

| Field | Value |
|-------|-------|
| Archetype | |
| Release digest | |
| Walkthrough date | |
| HR representative | |
| IT representative | |
| Legal/compliance | |
| Defects / waivers | |
| Result | Pass / Fail |
