# STR-003 — Independent ONB / licensing review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | STR-003 |
| Expires | When ONB-001 is Accepted, or 2027-01-31 — whichever first |
| Owner | Product + Program Board |

## Waiver statement

Automated agency onboarding (ONB-001) and a license marketplace are **deferred**. Engineering adopts catalog readiness + `EnabledModules` as the operational entitlement gate. This waiver does **not** authorize:

- Exposing `NotReady` modules in Staging/Production without Active exception
- Skipping Ready evidence (UAT/runbook/SLO) for published modules
- Treating compose entitlement lists as certified multi-agency contracts

## Conditions

1. `ModuleCatalog:AllowNotReadyExposure` remains false in Production.
2. Gateway `EnforcePathEntitlement` remains true in Staging/Production.
3. ONB-001 / license marketplace remain soft blockers on the STR-003 backlog.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Product | Engineering waiver accepted pending ONB-001 |
| Platform | Module binding + path entitlement implemented |
| Program Board | Manual EnabledModules acceptable for pilot |
