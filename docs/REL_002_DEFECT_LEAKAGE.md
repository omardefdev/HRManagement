# REL-002 — Defect leakage (Pilot window)

Track defects found after Pilot start that should have been caught in UAT/staging.

| Field | Value |
|---|---|
| Window | |
| Agency | PILOT-AGENCY-001 |
| Threshold (P0 escaped) | 0 |
| Threshold (P1 escaped) | 2 |

## Summary

| Severity | Count | Escaped from staging? |
|---|---|---|
| P0 | | |
| P1 | | |
| P2 | | |
| P3 | | |

## Detail CSV

Use [`rel-pilot/defect-leakage-template.csv`](rel-pilot/defect-leakage-template.csv).

## Gate rule

- Any unexplained P0 = **No-Go** unless Waived with security+program signatures.
- P1 above threshold = Conditional Go at most.
