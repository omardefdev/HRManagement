# MIG-007 — Journey matrix

Source: [`mig-onboarding/onboarding-matrix.json`](mig-onboarding/onboarding-matrix.json) — [ADR-MIG-007](ADR-MIG-007-agency-onboarding-factory.md).

## ReadinessChecklist

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| GatesDefined | Yes | onboarding-office | readinessGates |
| ChecklistPass | Yes | qa | readiness-checklist-pass |

## NoForkPlaybook

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| PlaybookNofork | Yes | product | playbook.noforkRequired |
| ForkFail | Yes | qa | fork-attempt-fail |

## WaveSignOffGate

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| WavePolicy | Yes | onboarding-office | waveCriteria |
| WaveWithoutSignoffFail | Yes | qa | wave-without-signoff-fail |

## DualAgencyE2E

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AgencyA | Yes | qa | agency-a-e2e |
| AgencyB | Yes | qa | agency-b-e2e |

## LeadTimeDefects

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| LeadTime | Yes | onboarding-office | lead-time-pass |
| Defects | Yes | qa | defects-telemetry |

## ExitRollback

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ExitPolicy | Yes | onboarding-office | exitRollback |
| ExitPass | Yes | qa | exit-rollback-pass |

Blockers: EXT-006/PROD-002 = NotStarted؛ ONB-001 / agency-extract-access / live-dual-agency-onboard = Blocked.
