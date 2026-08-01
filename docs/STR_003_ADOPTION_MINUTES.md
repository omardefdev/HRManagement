# STR-003 — Program adoption minutes (module entitlement)

| Field | Value |
|---|---|
| Card | STR-003 (card 61) |
| Date | 2026-07-30 |
| Decision | Adopt runtime `ModuleCatalog` binding with `EnforcePathEntitlement` on Gateway Staging/Production |
| Agency surface | All 14 `pilotApprovedSlugs` currently **Ready** (agency-visible = core\|extended Ready set) |

## Operational contract

| Item | Value |
|---|---|
| Entitlement source | `module-catalog.json` readiness + `Gateway:EnabledModules` |
| Empty EnabledModules | All-on only while every pilot slug is Ready |
| Kill-switch | Remove slug from EnabledModules |
| Per-agency ONB | Deferred (ONB-001) — compose/env lists remain manual |

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Product | _pending wet-ink_ | |
| Platform | _pending wet-ink_ | |
| Program Board | _pending wet-ink_ | |

Engineering binding is live. Wet-ink slots must be completed before national multi-agency go-live.

## Independent review

See [STR_003_INDEPENDENT_REVIEW_WAIVER.md](STR_003_INDEPENDENT_REVIEW_WAIVER.md).
