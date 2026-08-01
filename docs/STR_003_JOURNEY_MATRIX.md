# STR-003 — Journey matrix

Source: [`str-modules/module-catalog.json`](str-modules/module-catalog.json) — [ADR-STR-003](ADR-STR-003-service-module-catalog.md).

## ModuleInventory

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| FourteenAppModules | Yes | product | module-catalog.json |
| PlatformRows | Yes | platform | postgres + rabbitmq |
| JoinOpsCatalog | Yes | sre | opsCatalogId ↔ REL-003 |

## ReadinessGate

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| UatRunbookSlo | Yes | qa | MODULE_READINESS |
| ReadyOnlyWhenEvidence | Yes | product | validate script |

## AgencyContract

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| IncludedExcluded | Yes | product | AGENCY_CONTRACT |
| Walkthrough | Yes | program | UAT pack |

## FlagExposureAudit

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| FlagGateDoc | Yes | platform | FEATURE_FLAG_GATE |
| PilotSlugsReady | Yes | qa | validate vs REL_001 list |
