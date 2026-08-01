# قائمة مصالحة رحلات الحركة (FUN-003-C)

استخدم بعد timeout / fault / cancel قبل Resume أو إغلاق التدخل على نقل أو انتداب.

| # | فحص | أين |
|---|---|---|
| 1 | Transfer/Secondment/Loan: IsCancelled؟ EffectiveDate/StartDate؟ Status | HR.Movements |
| 2 | MovementChecklist: خطوات إلزامية + EvidenceRef + OwnerRole | Movements DB |
| 3 | TransferSagaState / SecondmentSagaState CurrentState | Movements DB |
| 4 | ProcessIntervention مفتوح؟ | api/process-interventions |
| 5 | OrgUnitSnapshot للمصدر والهدف | Movements |
| 6 | Employee org assignment | HR.Core |
| 7 | أثر مالي معلّق / markers | Payroll |
| 8 | نطاق org في الدليل | Auth.Directory |
| 9 | تذكرة أصول (إن وُجدت) | Admin.Logistics |
| 10 | ILoanAssignmentEnded نُشر عند إغلاق إعارة؟ | bus / outbox |

## تعويض عند Cancel

`ICancelTransferSaga` / `ICancelSecondmentSaga` يعيدان إرسال `IExecute*Party` بـ`Compensate=true` للأطراف المكتملة (عكس الترتيب):

| طرف | تعويض v1 |
|---|---|
| Payroll | وسم evidence تعويض؛ **ليس** reverse لمحاسبة مرحّلة |
| Auth.Directory | ACK تعويض خفيف (نطاق org) |
| HR.Core / Admin.Logistics | ACK تعويض خفيف |

Resume يعيد Dispatch للأطراف المعلّقة فقط.

Loan = مسار حدث: soft-cancel قبل Active؛ بعد Active → `ILoanAssignmentEnded`.
