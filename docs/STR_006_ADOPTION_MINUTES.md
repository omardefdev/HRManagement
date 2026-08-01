# STR-006 — Program adoption minutes (ownership production binding)

| Field | Value |
|---|---|
| Card | STR-006 (card 64) |
| Date | 2026-07-30 |
| Decision | Bind ownership-matrix into host startup and CI |
| Default National 24×7 | `National24x7Claim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/str-ownership/ownership-matrix.json` (copied to `Ownership/`) |
| Negative fixtures | `testdata/str006/scenarios/*-fail*`, `vendors/missing-exit-clause.json` |
| Positive fixtures | bus-factor-pass, tco-ops-security-backup, staffing-oncall |

Promotion to national 24×7 claim = close SRE-001 first. Full TCO finance review = FIN-001. SOV-007 platform vendor-exit binding is Done; live exit drill = VEN-001.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Sponsor | _pending wet-ink_ | |
| Finance | _pending wet-ink_ | |
| Security | _pending wet-ink_ | |
| Platform / SRE | _pending wet-ink_ | |

## Independent review

See [STR_006_INDEPENDENT_REVIEW_WAIVER.md](STR_006_INDEPENDENT_REVIEW_WAIVER.md).
