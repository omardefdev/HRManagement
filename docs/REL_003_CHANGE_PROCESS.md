# REL-003 — Change process

Deploy mechanics remain **DEP-001** (immutable SHA, gate, `action=rollback`). This doc adds SM classification and approval — it does not rebuild the pipeline.

## Classification

| Type | When | Approval | Rollback plan |
|---|---|---|---|
| Standard | Pre-approved low risk (docs, config within allowlist) | Owner | Documented revert |
| Normal | Feature/config with user impact risk | Owner + peer (CAB-lite) | DEP-001 rollback or reverse PR |
| Emergency | Sev1/Sev2 mitigation | On-call commander + post-facto review within 2 business days | Immediate rollback path required |

## Critical change checklist

1. Change ticket id + serviceId(s) from catalog.
2. Risk / blast radius / maintenance window.
3. Approver names and timestamps.
4. Rollback: cite DEP-001 release gate / deploy workflow `action=rollback` (or documented reverse).
5. Communication per [REL_003_STATUS_MAINTENANCE.md](REL_003_STATUS_MAINTENANCE.md) if user-visible.
6. Post-change verification (health, smoke, alert silence lifted).

## Forbidden

- Production change without rollback plan for Normal/Emergency.
- Claiming REL-003 Done by changing deploy.yml alone — use existing DEP-001 evidence.
