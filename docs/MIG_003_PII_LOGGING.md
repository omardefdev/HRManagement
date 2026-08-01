# MIG-003 — PII logging

Forbidden in factory logs (structure-only samples allowed):

- NationalId, FullName, BASE_SALARY, EmpNo, NAT_ID, password, email

| Rule | Value |
|---|---|
| redactionMode | hash-or-omit |
| logSamplePolicy | structure-only |

Fixture `log-redaction-sample.json` must contain no forbidden field values in plaintext. Aligns with OBS-001 scrub patterns without claiming OBS runtime Done here.
