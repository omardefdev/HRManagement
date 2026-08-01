# MIG-006 — Journey matrix

Source: [`mig-cutover/cutover-matrix.json`](mig-cutover/cutover-matrix.json) — [ADR-MIG-006](ADR-MIG-006-cutover-rehearsal-rollback.md).

## TimedRehearsals

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| MinThree | Yes | cutover-command | rehearsals ≥3 |
| TimedPass | Yes | qa | timed-rehearsal-pass |

## DeltaIntegrity

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| DeltaPolicy | Yes | migration-engineering | deltaCapture |
| DeltaPass | Yes | qa | delta-capture-pass |
| DeltaLossFail | Yes | qa | delta-loss-fail |

## SurpriseRollback

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RollbackPolicy | Yes | cutover-command | rollback |
| SurprisePass | Yes | qa | surprise-rollback-pass |
| PaperFail | Yes | qa | paper-rollback-fail |

## GoNoGoGate

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| Criteria | Yes | cutover-command | goNoGo |
| Decision | Yes | cutover-command | go-nogo-decision |

## LegacySecured

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| LegacyPolicy | Yes | security | legacyAccess |
| ReadonlyPass | Yes | qa | legacy-readonly-pass |

## PostCutoverRecon

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| T0 | Yes | migration-qa | recon-t0 |
| T1 | Yes | migration-qa | recon-t1 |
| T30 | Yes | migration-qa | recon-t30 |

Soft blockers: agency-extract-access, live-cutover-perf, live-agency-recon, live-rehearsal-at-scale = Blocked.
