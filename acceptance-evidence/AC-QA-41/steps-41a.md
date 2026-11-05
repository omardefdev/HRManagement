1. Read metadata.json of every folder this run wrote (its code tree).
2. Each names lab or prodlike, and the source of the run: ci with the run id, or local.
3. ACCEPTANCE_REQUIRE_ALL=true, so a blocked required scenario fails instead of skipping.
Negative: the dispatched run with ACCEPTANCE_SIMULATE_MISSING is red and names the scenario (reports/plan41-final-report.md).