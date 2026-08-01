# EVD-001 — Verification

## Automated

| Check | Location |
|---|---|
| Hash seal + tamper detect | `Evd001EvidenceModelTests` |
| Append-only interceptor | `Evd001EvidenceModelTests` |
| Schema/ADR present | `Evd001EvidenceModelTests` |

## Manual reconstruction drill

1. Execute a hire (or fixture) that emits evidence with one CorrelationId.
2. Independent reviewer rebuilds timeline from `EvidenceRecords` only.
3. Attempt UPDATE/DELETE → must fail.
4. Crash mid-journey + restart → no gap in Sequence for completed decisions (compensation evidence allowed).

## Acceptance mapping

| Criterion | Evidence |
|---|---|
| Rebuild hire/transfer/payroll/role change | Schema + collector + reconstruction recipe |
| No gap on faults | Sequence + CausationId compensation rows |
| App admin cannot edit | Interceptor + DB REVOKE runbook |
| Clock/timezone clear | TimeSource + TimeZoneId |
| Bound to release/config | ReleaseVersion / ConfigVersion fields |
