# STR-002 — Release gate (legal controls)

## Must pass

1. `scripts/str002-control-catalog-validate.ps1` → PASS.
2. Every `allowedDomains` entry has ≥1 control.
3. No control with `status=Implemented` whose `evidencePath` or `testRef` contains `LEGAL_TBD` (case-insensitive).
4. Payroll path: `scripts/check-no-legal-tbd.py` remains green (GOV-002).
5. Confidence `D` ⇒ `status=Blocked-Legal` **or** non-null `exceptionId`.
6. Active exceptions listed in CONFLICT register with expiry.

## Allowed on release (documented)

| Item | Rule |
|---|---|
| Blocked-Legal rows | Permitted; must appear on readiness `blockedOn` |
| Partial + exception | Permitted until expiry |
| Claiming SYR-001 / MoF final / live SOV-001 ATO Done | Forbidden |

## Sign-off roles

Legal, Privacy, CISO, HR/Finance (see UAT pack).
