1. Real registry docs/qa/dated-obligations.json on today's date → exit 0, nearest open obligation printed.
2. testdata/qa/dated-obligations/fail-5.json (due in 5 days) → exit 1 (::error::).
3. warn-20.json (due in 20 days) → ::warning::, exit 0.
4. expired.json (Declared, past due) → exit 1.
Deviation from the plan text: the nearest obligation is the ObjectStorage CI exclusion (2026-12-14, PLAN-38), which the
plan itself registers, not STR-002 (2027-01-31).