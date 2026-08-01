# REL-002 — مصفوفة رحلات Pilot / أدلة / قرار

المصدر: [`rel-pilot/scope-matrix.json`](rel-pilot/scope-matrix.json) — انظر [ADR-REL-002](ADR-REL-002-controlled-pilot.md).

## PilotScope

| خطوة | إلزامي | مالك | دليل |
|---|---|---|---|
| Charter | نعم | program | agency `PILOT-AGENCY-001` |
| Ceilings | نعم | program | maxEmployees / maxNamedUsers |
| InScopeJourneys | نعم | qa | FUN-001..006 UAT packs |
| OutOfScopeNational | نعم | program | NAT / 11-NATIONAL مستبعد |

## EvidenceGates

| خطوة | إلزامي | مالك | دليل |
|---|---|---|---|
| DailyRecon | نعم | ops | `REL_002_DAILY_RECONCILIATION.md` |
| SloWindow | نعم | sre | `REL_002_SLO_WINDOW.md` |
| DefectLeakage | نعم | qa | `REL_002_DEFECT_LEAKAGE.md` |
| PayrollParallel3 | نعم | payroll | harness 3 دورات |
| DrSecurityBinder | نعم | security | OPS + assessor/waiver |
| SupportContacts | نعم | support | `REL_002_SUPPORT_CONTACTS.md` |
| MigrationStub | لا | migration | يعتمد MIG-006 |

## GoNoGoDecision

| خطوة | إلزامي | مالك | دليل |
|---|---|---|---|
| BlockerReview | نعم | program | سجل blockers |
| MultiRoleSignOff | نعم | program | `REL_002_PILOT_GO_NO_GO.md` |
| PilotOnlyClause | نعم | program | ليس NAT-008 |

اعتمادية مغلقة: REL-001. وطني = Blocked حتى Satisfied/Waived.
