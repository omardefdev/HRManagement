# ADR-STR-003 — Service / module product catalog

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-003 (card 61) |
| Date | 2026-07-30 |
| Dependencies | STR-001 Done; REL-003 Done |
| Inputs | GatewayModuleCatalog 14 slugs; REL_001_FEATURE_FLAGS; rel-ops/service-catalog.json |

## Context

REL-003 provides ops inventory (owner/runbook/SLO). REL-001 documents EnabledModules. There is no product catalog with mandatory/optional tiers, agency get/don’t-get contract, or readiness gate that forbids calling a module “available” before UAT+runbook+SLO, and that binds feature-flag exposure to Ready modules.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/str-modules/module-catalog.json` — ModuleInventory / ReadinessGate / AgencyContract / FlagExposureAudit |
| Depth | **Module catalog pack** — docs+harness; no GatewayModuleCatalog.cs changes |
| Slugs | Same 14 app modules as Gateway + postgres/rabbitmq as platform (not agency UI tiles) |
| Ready rule | Ready only if uatPackRef + runbookRef + sloRef non-empty and files exist |
| Flags | Explicit EnabledModules ⊆ Ready (or signed exception); empty=all-on only if all published Ready or waived |
| Join | opsCatalogId matches REL-003 service id |
| Closure | Done = pack + validate + Str003*; TRACEABILITY `STR-003 ✓ (module catalog pack)` |
| Out of scope | License marketplace, ONB automation, PROD-001, Aspire, runtime flag enforcement code |

## Consequences

- Agencies use STR_003_AGENCY_CONTRACT as the product surface.
- REL-001 flag policy cites STR-003 for readiness binding.
- Validate fails if Ready evidence missing or Pilot slug NotReady without exception.
