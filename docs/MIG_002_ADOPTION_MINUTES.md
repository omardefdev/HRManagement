# MIG-002 — Program adoption minutes (code-map production binding)

| Field | Value |
|---|---|
| Card | MIG-002 (card 67) |
| Date | 2026-07-30 |
| Decision | Bind code-map-matrix into host startup and CI |
| Default live engine | `LiveCodeMapEngineClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/mig-canonical/code-map-matrix.json` (copied to `MigCanonical/`) |
| Negative fixtures | mass-other-fail, unmapped-no-quarantine-fail, legacy-id-as-auth-key-fail |
| Positive fixtures | mapping-coverage, status-org-roundtrip, legal-business-signoff |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveCodeMapEngineClaim=true` | MIG-003 transform worker Accepted |
| Live value coverage (agency extract) | agency-extract-access Satisfied |
| Full legal/business regimes | SYR-003 Satisfied |
| Job catalog as SoT | MDM-001 Satisfied |

Until then: lab universes only; live engine claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Data Architecture | _pending wet-ink_ | |
| Legal / HR Finance | _pending wet-ink_ | |
| Migration Office | _pending wet-ink_ | |

## Independent review

See [MIG_002_INDEPENDENT_REVIEW_WAIVER.md](MIG_002_INDEPENDENT_REVIEW_WAIVER.md).
