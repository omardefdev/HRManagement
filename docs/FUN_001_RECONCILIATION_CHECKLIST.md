# قائمة مصالحة رحلة التعيين (FUN-001-C)

استخدم بعد timeout / fault / cancel قبل Resume أو إغلاق التدخل.

| # | فحص | أين |
|---|---|---|
| 1 | ServiceTransaction Appointment state | HR.Contracts |
| 2 | HireChecklist: خطوات إلزامية + EvidenceRef | HR.Contracts |
| 3 | Employee موجود وActive | HR.Core |
| 4 | Contract / Commencement | HR.Contracts |
| 5 | EmployeePayProfile | Payroll |
| 6 | AuthUser مرتبط بـEmployeeId ونشط | Auth.Directory |
| 7 | IT ticket / provisioning marker | IT.Infra |
| 8 | بطاقة (إن صدرت) | IDCard |
| 9 | HireCaseSagaState CurrentState | Contracts DB |
| 10 | ProcessIntervention مفتوح؟ | api/process-interventions |

## تعويض عند Cancel

`ICancelHireSaga` يعيد إرسال `IExecuteHireParty` بـ`Compensate=true` للأطراف المكتملة (عكس الترتيب):

| طرف | تعويض v1 |
|---|---|
| Auth.Directory | تعطيل AuthUser المرتبط |
| Payroll / HR.Core / IDCard / Archive / IT | ACK تعويض خفيف (وسم evidence) — ليس reverse كامل كالفصل |

Resume يعيد Dispatch للأطراف المعلّقة فقط.
