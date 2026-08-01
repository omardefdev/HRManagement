# REL-004 — Rollback policy

**Card:** REL-004  
**Pairs with:** [REL_005](completion-backlog/08-NATIONAL-ASSURANCE-OPERATIONS.md) progressive delivery (≤15m service rollback target)

---

## 1. Principles

1. **Rollback must not corrupt payroll or master employee data** (Tier-0 invariants).
2. **N-1 code MUST run against N database schema** within the rollback window when migrations are expand-only.
3. **Event contracts:** producers on rolled-back pods MUST NOT emit schema versions that N-1 consumers cannot skip.
4. **External partners:** rollback MUST NOT break certified consumers still on N-1 without incident comms.

---

## 2. Rollback window

| Layer | Default window | Owner |
|-------|----------------|-------|
| Application pods | 15 minutes (REL-005 SLO) | Release Engineering |
| Gateway config | Same deploy artifact — revert with app | Platform |
| Feature flags | Immediate — default safe (off) | Product |
| Database schema | 24 hours if expand-only; **no rollback** if contract migration without waiver | Data Governance |

---

## 3. Migration classes and rollback

| Migration class | Examples | Rollback allowed? |
|-----------------|----------|-------------------|
| **Expand-only** | Add nullable column; new table; new optional JSON field | Yes — revert app to N-1 within 24h |
| **Migrate** | Dual column periodCode + periodId | Yes — if dual-read still active |
| **Contract** | Drop column; NOT NULL without default; tighten FK | **No** without approved waiver + forward-fix plan |

Contract migrations require:

- Pre-approved waiver in change record (DEP-001)
- Entry in compatibility matrix
- Payroll/legal freeze check (REL-006)

---

## 4. API rollback

| Scenario | Action |
|----------|--------|
| Breaking API shipped by mistake | Abort canary; revert deploy; if exposed >1h, notify external consumers |
| Deprecated field removed early | Forward-fix only — restore field in hotfix minor |
| OpenAPI diff FAIL missed in CI | Hold train; root-cause; add baseline if expand was intended |

---

## 5. Event rollback

| Scenario | Action |
|----------|--------|
| New required event field | Roll back producer pods; consumers remain on EventSchemaVersion skip |
| SchemaVersion bump with breaking shape | Stop publisher; replay from outbox after fix |
| MassTransit poison messages | DLQ procedure — [RABBITMQ_OPERATIONS.md](RABBITMQ_OPERATIONS.md) |

---

## 6. Data limits (what rollback cannot undo)

- Payroll run **posted** to treasury export (`IPayrollDisbursementGateway`) — compensating financial transaction required
- Employee **NationalId** correction already propagated (GOV-003) — forward correction only
- Audit/WORM exports — append-only; rollback is logical (flag + compensating entry), not delete

---

## 7. Evidence

Every rollback MUST record:

- Release tag reverted from → to
- Compatibility matrix reference
- Migration IDs involved
- Partner notification ticket (if external)
- Post-implementation review within 24h (REL-006 hotfix path)

---

## 8. Drill mapping (REL-004 acceptance)

Lab drill **rollback after migration** is satisfied by:

- `rel004-compat-validate.ps1` documenting policy artifacts
- REL-005 bad-release injection (future) — this policy is the binding contract until live drills
