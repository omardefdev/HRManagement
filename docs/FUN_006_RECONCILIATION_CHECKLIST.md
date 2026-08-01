# قائمة مصالحة FUN-006 (أرشيف / تحليلات / تقارير)

| # | فحص | أين |
|---|---|---|
| 1 | ArchiveChecklist Classified→Disposed | Records.Archive |
| 2 | LegalHold يمنع Request/Approve Dispose | ArchiveRetentionRules |
| 3 | SoD: ApprovedBy ≠ DisposeRequestedBy | ApproveDisposal |
| 4 | Digital checksum + CompletenessAttestation | DigitalFileRecords + CustodyEvent |
| 5 | AnalyticsEvent.OrgUnitId من IEmployeeCreated | EmployeeCreatedConsumer |
| 6 | Rebuild fingerprint ثابت لترتيب OccurredAt+Id | ReadModelsController.Rebuild |
| 7 | gap-analysis فرع ≠ إجمالي tenant | WorkforcePlanningController |
| 8 | ExportJob workforce / archive inventory | PERF-004 templates |
| 9 | أرقام gap vs عدّ أحداث EmployeeCreated/Separation | هذا المستند + Fun006* |

## حدود

لا saga؛ لا ANA-001/REP-001؛ لا DW خارجي؛ مصالحة حية مع HR.Core اختيارية خارج الوحدة.
