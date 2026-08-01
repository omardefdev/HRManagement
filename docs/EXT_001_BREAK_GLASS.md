# EXT-001 — Break-glass

| Control | Required |
|---|---|
| dualControl | true |
| timeBoundMinutes | 60 |
| auditRequired | true |

Break-glass sessions require two distinct approvers, expire automatically, and leave an immutable audit record. Persona `break-glass-operator` requires AAL3.
