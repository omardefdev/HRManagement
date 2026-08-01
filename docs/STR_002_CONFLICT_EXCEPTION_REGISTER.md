# STR-002 — Conflict / exception register

Signed ambiguities and active exceptions. Machine mirror: `exceptions[]` in [`str-legal/control-catalog.json`](str-legal/control-catalog.json).

| exceptionId | Conflict / ambiguity | Resolution | Signed roles | Expires | Linked controls |
|---|---|---|---|---|---|
| EX-STR2-TAX-INTERIM | MoF brackets not gazetted; news ≠ law | Use GOV-002 LD-TAX-* ApprovedInterim; no hard-code from press | legal, payroll | 2027-01-31 | CTRL-TAX-BRACKETS-INTERIM |
| EX-STR2-LAW50-UNCERTIFIED | Law 50 text not certified unified corpus | Operate on file-12 baseline + legal_regime_id; production pin waits SYD-001/SYR-001 | legal, program | 2027-01-31 | CTRL-CS-LAW50-REGIME |

## Payroll-specific conflicts

Reuse [GOV_002_CONFLICT_RETRO_ROUNDING.md](GOV_002_CONFLICT_RETRO_ROUNDING.md) for retro/rounding/conflict rules. Do not duplicate LD rows here.

## Rules

1. New ambiguity → open exception row **or** Blocked-Legal control before release claim.
2. Expired exception without renewal → validate/readiness fail for linked Implemented/Partial claims needing it.
3. Confidence D controls without Blocked-Legal or exception are invalid.
