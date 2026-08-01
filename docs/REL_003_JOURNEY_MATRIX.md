# REL-003 — Journey matrix (ops / support SM)

Source: [`rel-ops/service-catalog.json`](rel-ops/service-catalog.json) — see [ADR-REL-003](ADR-REL-003-ops-support-sm.md).

## ServiceCatalog

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| InventoryMap | Yes | sre | service-catalog.json |
| OwnerRunbookEscalation | Yes | sre | per-service fields |
| SeveritySlaBind | Yes | sre | REL_003_SEVERITY_SLA.md |

## IncidentChange

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| IncidentProcess | Yes | sre | REL_003_INCIDENT_PROCESS.md |
| ProblemProcess | Yes | sre | REL_003_PROBLEM_PROCESS.md |
| ChangeProcess | Yes | platform | REL_003_CHANGE_PROCESS.md + DEP-001 |
| StatusMaintenance | Yes | ops | REL_003_STATUS_MAINTENANCE.md |

## OnCallGameDay

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| OnCallRoster | Yes | sre | REL_003_ONCALL_ROSTER.md |
| IncidentGameDay | Yes | sre | rel003-incident-game-day.ps1 |
| MttaMttrLedger | Yes | sre | testdata/rel003/game-day |

## AccessPatchPostmortem

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AccessReview | Yes | security | REL_003_ACCESS_REVIEW.md |
| PatchCadence | Yes | platform | REL_003_PATCH_CADENCE.md |
| PostmortemTracker | Yes | sre | postmortem-actions CSV |
| TrainingVendorStub | No | program | blockedOn TRN-001 / SUP-004 |

Closed deps: OBS-002, OPS-004. National desk/ORR remain out of depth.
