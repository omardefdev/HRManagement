# STR-007 — Journey matrix

Source: [`str-value/kpi-matrix.json`](str-value/kpi-matrix.json) — [ADR-STR-007](ADR-STR-007-value-competition-baseline.md).

## KpiCatalog

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RequiredFields | Yes | product | kpis[] |
| OwnerNotAuditor | Yes | audit | owner ≠ auditor |

## BaselineFreeze

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| PrePilotFreeze | Yes | program | baselines.prePilotFreeze |
| LegacySample | Yes | qa | legacy-sample.json |

## IndependentMeasure

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AssessorIndependent | Yes | audit | independent-measurement |

## SampleReconcile

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ReconcilePassFail | Yes | qa | sample-reconciliation pass/fail |

## QuarterlyReview

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AgendaNoScreenCount | Yes | product | quarterly-benefit-review |

## PrivacyGate

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| TenantScoped | Yes | security | privacyRules |
| CrossTenantFails | Yes | qa | cross-tenant-leak |

Soft blockers: FIN-001=NotStarted; PROD-003=Blocked. Hard deps Satisfied.
