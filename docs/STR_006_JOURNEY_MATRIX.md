# STR-006 — Journey matrix

Source: [`str-ownership/ownership-matrix.json`](str-ownership/ownership-matrix.json) — [ADR-STR-006](ADR-STR-006-permanent-ownership-funding-skills-vendors.md).

## OwnershipQuad

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| QuadRoles | Yes | program | components[] |
| JoinRel003Str003 | Yes | platform | joins |

## BusFactor

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CriticalMinTwo | Yes | program | busFactorRule |
| ExerciseFixtures | Yes | qa | bus-factor scenarios |

## StaffingOnCall

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RosterTemplate | Yes | sre | STAFFING_ONCALL |
| NoNational24x7Claim | Yes | sre | national24x7Claim=false |

## BudgetTco

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| OpsSecBackupFunded | Yes | finance | budgetLines |
| TcoStubApproval | Yes | sponsor | TCO_APPROVAL |

## VendorSla

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RequiredClauses | Yes | procurement | vendors[] |
| MissingExitFails | Yes | qa | missing-exit-clause fixture |

Soft blockers: SOV-007=Satisfied (vendor-exit binding; live drill = VEN-001); FIN-001=NotStarted; SRE-001=Blocked. STR-005=Satisfied.
