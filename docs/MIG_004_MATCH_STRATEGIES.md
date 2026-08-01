# MIG-004 — Match strategies

Source: [`mig-match/match-matrix.json`](mig-match/match-matrix.json)#matchStrategies.

| Strategy | Keys / features | Default outcome | Auto-merge |
|---|---|---|---|
| DeterministicNationalId | tenantId + NationalId | HumanReview | Only risk≤Low and no exception |
| DeterministicComposite | tenantId + sourceId + sourceRecordKey | AutoMerge | risk≤Low |
| ProbabilisticNameDob | normalizedName, DOB, gender | HumanReview | Never |
| ExceptionQueue | exceptionCode | ExceptionQueue | Never |

National-ID exceptions (`missing`, `invalid-checksum`, `twin-shared`, `foreign-id`) always forbid auto-merge.

Name-variant stubs (`arabic-hamza`, `spacing`, `nickname`) are lab-only until **L10N-002**.

This pack does not implement a live scorer; harness fixtures prove policy outcomes.
