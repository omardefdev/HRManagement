# ADR-STR-004 — Single product, no per-agency fork

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-004 (card 62) |
| Date | 2026-07-30 |
| Dependencies | STR-002 Done; TEN-003 Done; PROD-001 Blocked |

## Context

STR-003 defines module entitlements without forks. TEN-003 isolates tenants. There is no machine taxonomy of what may vary by agency (data/policy/template/workflow/branding/integration vs code), no dual-agency same-release fixtures, no repo rule against agency-named conditionals/branches, and no promotion/rollback schema for agency config packs. PROD-001 (versioned policy/workflow platform) remains open.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/str-product/nofork-matrix.json` — ConfigTaxonomy / ExtensionPoints / DualAgencySameRelease / PromotionRollback / RepoNoForkRule |
| Depth | **Nofork policy pack** — docs + fixtures + validate harness; not PROD-001 engine |
| Variation | Config/entitlement/extension points only; forbidden: source fork, `if (agency==…)` |
| Dual agency | Two fixtures, same `releaseDigest`, conflicting optional modules/branding |
| Signed config | Schema + review roles; crypto four-eyes **Blocked on PROD-001/ONB-001** |
| Upgrade preserve | Stub checklist → PROD-001 |
| Blockers | PROD-001, ONB-001 = Blocked; STR-002, TEN-003 = Satisfied |
| Closure | Done (nofork policy pack) + documented blockers |
| Out of scope | Build PROD-001; real agency forks; Aspire; TEN runtime changes |

## Blocker register

| Id | Status | Note |
|---|---|---|
| STR-002 | Satisfied | Legal control catalog Done |
| TEN-003 | Satisfied | Tenant isolation Done |
| PROD-001 | Blocked | Versioned policy/workflow/forms, upgrade preserves overrides |
| ONB-001 | Blocked | Signed provision pack factory |

## Consequences

- MIG-007 / ONB / PROD-002 consume this policy; they do not re-open forks.
- Validate fails without PROD-001 in blockers or without dual-agency same digest.
