# ADR-SOV-004 — Zero Trust admin privilege contracts (pack)

| Field | Value |
|---|---|
| Status | Accepted (pack) |
| Card | SOV-004 |
| Date | 2026-07-30 |

## Decision

Source of truth is `docs/sov-zt/zero-trust-matrix.json`. Lab contracts encode no standing privilege, PAM/JIT/JEA with reason+approval, phishing-resistant MFA, admin workstations, session record, break-glass alert+review, quarterly recertification, and privileged targets (database/cloud/k8s/keycloak). Live PAM/workstation/SOC/recert workers remain Blocked; `LivePamClaim=false`.
