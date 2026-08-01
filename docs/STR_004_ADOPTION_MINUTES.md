# STR-004 — Program adoption minutes (nofork production binding)

| Field | Value |
|---|---|
| Card | STR-004 (card 62) |
| Date | 2026-07-30 |
| Decision | Adopt single-product release digest with dual agency packs A/B |
| Release digest | `sha256:str004-demo-digest-20260730` |

## Packs

| PackId | Agency | TenantId | Notes |
|---|---|---|---|
| pack-agency-a-v1 | AGENCY-A | aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa | Finance-heavy optional set |
| pack-agency-b-v1 | AGENCY-B | bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb | Field-heavy optional set |

Promotion = replace pack file with same product `releaseDigest`. Changing digest = new product release.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Architecture | _pending wet-ink_ | |
| Product | _pending wet-ink_ | |
| Program Board | _pending wet-ink_ | |

## Independent review

See [STR_004_INDEPENDENT_REVIEW_WAIVER.md](STR_004_INDEPENDENT_REVIEW_WAIVER.md).
