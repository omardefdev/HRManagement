# STR-006 — Bus-factor exercise

## Goal

Prove no critical component relies on a single human (busFactor ≥ 2, ≥1 backup).

## Steps

1. Select each `critical: true` row in ownership-matrix.
2. Confirm `busFactor >= criticalMinBusFactor` (2) and `backupRoles.length >= 1`.
3. Run pass fixture (`bus-factor-pass.json`) — aligns with payroll row.
4. Run fail fixture (`bus-factor-fail.json`) — critical + busFactor=1 + empty backups → harness treats as expected FAIL.
5. Cross-check Tier-0 skills have ≥2 qualified roles.

## Result

Documented in VERIFICATION; enforced by `str006-ownership-validate.ps1`.
