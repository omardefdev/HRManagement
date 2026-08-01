# SUP-004 — Vendor escalation drill

**Card:** SUP-004  
**Frequency:** Quarterly (tabletop) + annual live vendor bridge test for Tier-0

## Objective

Prove that every Tier-0/Tier-1 vendor in [`sup-contracts/vendor-contract-registry.json`](sup-contracts/vendor-contract-registry.json) has a working escalation path within contracted SLA windows.

## Drill script (90 minutes)

| Time | Activity | Owner |
|---|---|---|
| T+0 | Inject synthetic P1: PostgreSQL primary failover delay | Drill lead |
| T+15 | L1 opens ticket; verify P1 response ≤15m for postgres/keycloak/hosting | Service desk |
| T+30 | L2 engages CNPG runbook + vendor bridge | Platform DBA |
| T+45 | Parallel track: RabbitMQ queue depth + MinIO license check | Messaging + Storage |
| T+60 | Legal/procurement confirms MSA security disclosure clause | Procurement |
| T+75 | Scorecard: SLA met? escalation contacts current? license expiry >90d? | QA recorder |
| T+90 | Close drill; file actions in risk register | Program governance |

## Pass criteria

- All Tier-0 vendors reached L2 within SLA `p1ResponseMinutes`
- No license expiry within 90 days without approved exception
- Escalation contacts match registry `escalation.l1/l2/l3`
- Evidence stored under `testdata/sup004/` (optional harness ledger)

## Verification

- `scripts/sup004-license-audit.ps1`
- `Sup004ContractTests`
