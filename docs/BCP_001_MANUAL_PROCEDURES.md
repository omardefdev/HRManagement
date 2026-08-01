# BCP-001 — Manual procedures (Tier-0 / Tier-1)

**Card:** BCP-001 (P1-National)  
**Scope:** Business continuity when the platform, identity, or network is unavailable.

## Activation criteria

See [`docs/bcp/degraded-mode-matrix.json`](bcp/degraded-mode-matrix.json) for Tier-0/1 journey → mode mapping and [`docs/bcp/degraded-mode-triggers.json`](bcp/degraded-mode-triggers.json) for activation triggers. Incident Commander (IC) declares degraded or manual mode per [OPS_DR_COMMUNICATION_TREE.md](OPS_DR_COMMUNICATION_TREE.md).

## Tier-0 journeys (must continue within 4 hours)

| Journey | Manual procedure | Fraud / limit controls |
|---------|------------------|------------------------|
| **Payroll disbursement** | Use pre-printed payroll run sheets (Appendix A). HR Finance lead signs batch totals; treasury executes bank file from last approved export. | Dual signature; max 1 emergency run per pay period; no new hires paid until system restored |
| **Identity / access (critical roles)** | Local branch roster verified against signed paper list; temporary PINs via out-of-band phone to IC-approved list only. | Max 24h temp credentials; audit log on paper; no bulk export of PII |
| **Emergency leave (Tier-0 units)** | Paper Form L-01 (manager + HR stamp); photo scan to secure drop folder when link returns. | Manager daily cap 5 approvals; duplicate national ID check on re-entry |

## Tier-1 journeys (must continue within 8 hours)

| Journey | Manual procedure | Notes |
|---------|------------------|-------|
| **Routine leave approval** | Form L-02 chain; weekly batch to HR inbox. | Does not block Tier-0 payroll |
| **Attendance correction** | Form A-01 with witness signature. | Reconcile to biometric export when online |
| **Contract amendments (non-pay)** | Paper amendment + legal stamp; no salary change without payroll IC approval. | |

## Materials stored offline

- Printed forms L-01, L-02, A-01, payroll run sheet templates
- Branch manager contact tree (laminated)
- Last approved org roster (monthly PDF on USB in branch safe)

## Communication

1. IC notifies agency heads within 30 minutes.
2. ESS/MSS banner text (when Gateway partial): *"النظام في وضع محدود — استخدم الإجراء الورقي المعتمد"*
3. All-clear only after re-entry checklist in [BCP_001_REENTRY_RECONCILIATION.md](BCP_001_REENTRY_RECONCILIATION.md).

## What must NOT be done manually

- Bulk salary changes without dual control
- Permanent account creation without identity verification
- Deletion of employee records (mark suspended on paper only)

## Related

- [BCP_001_REENTRY_RECONCILIATION.md](BCP_001_REENTRY_RECONCILIATION.md)
- [BCP_001_DRILL_PLAYBOOK.md](BCP_001_DRILL_PLAYBOOK.md)
- ARC-001 criticality binding; live drills deferred
