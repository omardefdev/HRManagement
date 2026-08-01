# HARD-001 — Exception Register (Template)

**Control:** HARD-001 | **Owner:** Platform Security + System Owner

Temporary deviations from hardening baselines require a registered exception with **mandatory expiry** (default max 90 days).

## Active exceptions

| ID | Asset | Baseline control | Reason | Risk | Compensating control | Requestor | Approver | Created (UTC) | Expires (UTC) | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| _none_ | — | — | — | — | — | — | — | — | — | — |

## Register entry template

Copy this block for each new exception:

```markdown
### HARD-001-EX-<YYYY>-<NNN>

| Field | Value |
|---|---|
| **ID** | HARD-001-EX-2026-001 |
| **Asset** | e.g. keycloak / hr-core-web |
| **Baseline control** | e.g. cap_drop ALL, read-only rootfs |
| **Reason** | Technical blocker with ticket link |
| **Risk** | Low / Medium / High |
| **Compensating control** | e.g. network policy deny-all, WAF rule |
| **Requestor** | name / role |
| **Approver** | Platform Security + System Owner |
| **Created** | 2026-01-15T00:00:00Z |
| **Expires** | 2026-04-15T00:00:00Z (≤ 90 days) |
| **Status** | Active / Expired / Revoked |
| **Evidence** | PR / change ticket URL |
```

## Rules

1. **No silent exceptions** — if not in this register, the control is required.
2. **Expiry is enforced** — `scripts/hard001-compliance-scan.ps1` fails on expired Active rows.
3. **Renewal** requires new approval; extend by editing expiry only with signed approval.
4. **Critical drift** (missing baseline with no exception) blocks release (HARD-001 acceptance).

## Machine-readable export (optional)

For automation, mirror active rows to `docs/hardening/hard001-exceptions.json`:

```json
{
  "card": "HARD-001",
  "exceptions": []
}
```

## Verification

See [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md).
