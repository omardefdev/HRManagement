# FUN-003 — تحقق الإغلاق

| تحقق | دليل |
|---|---|
| ADR + journey matrix | `docs/ADR-FUN-003-movements-journey-closure.md`, `docs/fun-movements/journey-matrix.json`, `docs/FUN_003_JOURNEY_MATRIX.md` |
| MovementChecklist + AND authority + SoD | Controllers + `MovementChecklistDefaults` + SodMatrixDefaults Secondment/Loan |
| soft-cancel + Compensate | `ICancelTransferSaga` / `ICancelSecondmentSaga` + state machines + interventions Cancel |
| future / conflict / end | `MovementWorkflowRules` + Close/`ILoanAssignmentEnded` + `FUN_003_EXCEPTION_PATHS.md` |
| Tests | `Fun003*` unit + transfer/secondment saga integration |
| UAT | `FUN_003_UAT_PACK.md` ReadyForSign-off |
| Reconciliation | `FUN_003_RECONCILIATION_CHECKLIST.md` |
| Backlog | FUN-003 Done في `04-P3`؛ TRACEABILITY محدّث |

**الحالة:** Done (جاهز للتوقيع التشغيلي عبر UAT-001).
