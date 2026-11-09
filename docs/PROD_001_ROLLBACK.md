# PROD-001 — Rollback

Published packs require a rollback reference.

## PLAN-47: rollback of a policy version

- A rollback never republishes the old version. The author creates a rollback draft (`POST api/policies/templates/{id}/rollback-drafts`
  or «إنشاء مسودة تراجع إلى هذا الإصدار» on the template page): a copy of the target's rules, with `RollbackOfVersionId` and a
  written reason (required).
- An approver who did not edit it publishes it, like any draft. The new version gets a new number; its publication is
  `Kind = Rollback` with `RolledBackFromVersionId = target`, and `ValidateRollback(true)` is called.
- The validator checks the target's rules again: a rule that names a workflow retired since (PLAN-46) stops the publication
  with the reason and the JSON path.
- **Proof:** `Pol047RollbackTests`, AC-POL-47d.

## The server

The rollback of a release that runs beside the PLAN-47 schema is in `docs/OPS_VPS_SINGLE_HOST.md` (the immutability triggers
and how to drop them).
