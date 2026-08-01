# STR-007 — Independent measurement design

Verify item #1 from the card.

| Field | Requirement |
|---|---|
| assessorRole | Must differ from KPI `ownerRole` (typically audit) |
| method | Sample design + formula citation from kpi-matrix |
| independence | Product owner does not score their own KPI |
| evidence | `testdata/str007/scenarios/independent-measurement.json` |

Assessor may use GOV-003 DQ extracts and OBS probes as sources but cannot redefine formulas without audit waiver.
