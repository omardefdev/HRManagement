# STR-004 — Repository no-fork rule

## Policy

1. No long-lived product branches named for an agency (`agency/*`, `tenant-branch`, ministry-named product forks).
2. No source conditionals that hard-code agency identity for product behavior (`if (agency == …)`, `AgencySpecificFork`, etc.).
3. Variation belongs in config packs / entitlements / documented extension points.
4. CODEOWNERS / PR review must reject fork PRs; automate via `scripts/str004-nofork-validate.ps1`.

## Scan scope

Validate harness scans selected `src/` trees for `forbiddenCodePatterns` from [`nofork-matrix.json`](str-product/nofork-matrix.json). Matches fail the gate (false positives require EXCEPTION waiver with ADR note).

## Allowed

- TenantId / tenant context (isolation, not product fork).
- Feature flags and STR-003 entitlements.
- Organization branding options.
