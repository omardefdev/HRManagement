# STR-004 — Independent PROD-001 / ONB-001 review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | STR-004 |
| Expires | When PROD-001 and ONB-001 are Accepted, or 2027-01-31 — whichever first |
| Owner | Architecture + Program Board |

## Waiver statement

Versioned policy/workflow platforms (PROD-001) and crypto-signed provision packs (ONB-001) are **deferred**. Engineering adopts unsigned interim agency packs bound by `releaseDigest`. This waiver does **not** authorize:

- Per-agency source forks or `if (agency == …)` conditionals
- Shipping packs with mismatched product digests
- Claiming signed four-eyes publication without ONB-001

## Conditions

1. Matrix blockers PROD-001 and ONB-001 remain `Blocked`.
2. `NoFork:EnforceOnStartup` remains true in Staging/Production.
3. Forbidden code patterns stay clean in CI.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Architecture | Engineering waiver accepted |
| Product | Dual-agency fixtures adopted for pilot |
| Program Board | Manual pack files acceptable pending ONB-001 |
