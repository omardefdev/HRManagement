# MIG-005 — Dual control

| Field | Value |
|---|---|
| minSigners | 2 |
| requireDistinctRoles | true |
| roles | migration-qa, internal-audit |

Wave evidence and agency sign-off require two distinct roles. Same-role double signature or single signer → FAIL (`dual-control-missing-fail`).
