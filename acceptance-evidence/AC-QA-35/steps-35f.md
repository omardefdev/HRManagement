1. Gate on run.sarif with a baseline that lists its only high result (Open, review 2027-01-01) → exit 0.
2. Negative: new-high.sarif (cs/sql-injection, security-severity 8.1, a path not in the baseline) → exit 1.
3. Negative: the same baseline entry with reviewBy 2026-10-01 → exit 1 (expired).
4. The live baseline docs/qa/codeql-baseline.json lists the pre-existing alerts one by one (rule, path, fingerprint, owner, reviewBy).